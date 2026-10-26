import { useCallback, useEffect, useRef, useState } from 'react'
import { AdminError, adminRead, adminSession } from '../../api/admin'
import type {
  AdminSessionView,
  AdminRoomPage,
  AdminRoomView,
  AdminRoomCommand,
  AdminPromptPage,
  AdminPromptCommand,
  AdminAuditPage,
} from '../../api/admin'
import type { PromptInput, PromptView } from '../../api/rooms'
import { gameName, gameNames } from '../rooms/gameNames'
import { PromptEditor } from './PromptEditor'

type Tab = 'rooms' | 'prompts' | 'audit'
type Pending = { path: string; payload: AdminRoomCommand | AdminPromptCommand }
const pendingKey = 'mingle-admin-pending'
function storePending(pending: Pending | null, expiresTicks?: number | null) {
  try {
    if (pending) sessionStorage.setItem(pendingKey, JSON.stringify({ pending, expiresTicks }))
    else sessionStorage.removeItem(pendingKey)
  } catch {
    /* In-memory retry still works when storage is unavailable. */
  }
}
const date = (ticks: number) => new Date(ticks / 10000 - 62135596800000).toLocaleString()
function Pager({
  page,
  total,
  change,
  disabled,
}: {
  page: number
  total: number
  change: (page: number) => void
  disabled: boolean
}) {
  return (
    <nav className="admin-actions" aria-label="Pagination">
      <button
        className="text-button"
        disabled={disabled || page === 1}
        onClick={() => change(page - 1)}
      >
        Previous page
      </button>
      <span>
        Page {page} · {total} total
      </span>
      <button
        className="text-button"
        disabled={disabled || page * 20 >= total}
        onClick={() => change(page + 1)}
      >
        Next page
      </button>
    </nav>
  )
}
export function AdminPage() {
  const detailHeading = useRef<HTMLHeadingElement>(null)
  const confirmationHeading = useRef<HTMLHeadingElement>(null)
  const [session, setSession] = useState<AdminSessionView | null>(null)
  const [credential, setCredential] = useState('')
  const [tab, setTab] = useState<Tab>('rooms')
  const [page, setPage] = useState(1)
  const [filter, setFilter] = useState('')
  const [search, setSearch] = useState('')
  const [appliedSearch, setAppliedSearch] = useState('')
  const [rooms, setRooms] = useState<AdminRoomPage | null>(null)
  const [room, setRoom] = useState<AdminRoomView | null>(null)
  const [prompts, setPrompts] = useState<AdminPromptPage | null>(null)
  const [audit, setAudit] = useState<AdminAuditPage | null>(null)
  const [editor, setEditor] = useState<PromptView | 'new' | null>(null)
  const [confirm, setConfirm] = useState<'room' | PromptView | null>(null)
  const [confirmation, setConfirmation] = useState('')
  const [pending, setPending] = useState<Pending | null>(null)
  const [busy, setBusy] = useState(false)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [refresh, setRefresh] = useState(0)
  const fail = useCallback((e: unknown) => {
    setError(e instanceof Error ? e.message : 'The request failed')
    if (e instanceof AdminError && e.status === 401) {
      setSession((s) => (s ? { ...s, authenticated: false } : s))
      setRoom(null)
      setRooms(null)
      setPrompts(null)
      setAudit(null)
      setPending(null)
      storePending(null)
    }
  }, [])
  useEffect(() => {
    let live = true
    adminSession().then(
      (s) => {
        if (live) {
          setSession(s)
          try {
            const saved = JSON.parse(sessionStorage.getItem(pendingKey) ?? 'null') as {
              pending: Pending
              expiresTicks: number | null
            } | null
            if (
              s.authenticated &&
              saved?.expiresTicks === s.expiresTicks &&
              saved.pending?.payload?.commandId &&
              (saved.pending.path === '/prompts' ||
                /^\/rooms\/[0-9a-f-]{36}\/commands$/.test(saved.pending.path))
            ) {
              setPending(saved.pending)
              if (saved.pending.path === '/prompts') setTab('prompts')
            } else storePending(null)
          } catch {
            storePending(null)
          }
        }
      },
      (e) => {
        if (live) fail(e)
      },
    )
    return () => {
      live = false
    }
  }, [fail])
  useEffect(() => {
    if (!session?.authenticated || !session.expiresTicks) return
    const timer = setTimeout(
      () => {
        setSession((s) => (s ? { ...s, authenticated: false } : s))
        setRoom(null)
        setRooms(null)
        setPrompts(null)
        setAudit(null)
        setPending(null)
        storePending(null)
        setNotice('Your admin session ended; sign in again')
      },
      Math.max(0, session.expiresTicks / 10000 - 62135596800000 - Date.now()),
    )
    return () => clearTimeout(timer)
  }, [session])
  useEffect(() => {
    if (!session?.authenticated) return
    let live = true
    const query = new URLSearchParams({
      page: String(page),
      pageSize: '20',
      ...(filter ? { [tab === 'rooms' ? 'status' : 'gameId']: filter } : {}),
      ...(appliedSearch ? { search: appliedSearch } : {}),
    })
    // Keep old rows visible while a refresh is in flight.
    Promise.resolve().then(() => {
      if (live) setLoading(true)
    })
    const load =
      tab === 'rooms'
        ? adminRead<AdminRoomPage>(`/rooms?${query}`).then((v) => {
            if (live) setRooms(v)
          })
        : tab === 'prompts'
          ? adminRead<AdminPromptPage>(`/prompts?${query}`).then((v) => {
              if (live) setPrompts(v)
            })
          : adminRead<AdminAuditPage>(`/audit?${query}`).then((v) => {
              if (live) setAudit(v)
            })
    load
      .catch((e) => {
        if (live) fail(e)
      })
      .finally(() => {
        if (live) setLoading(false)
      })
    return () => {
      live = false
    }
  }, [session?.authenticated, tab, page, filter, appliedSearch, refresh, fail])
  async function inspect(id: string) {
    setError('')
    try {
      setRoom(await adminRead<AdminRoomView>(`/rooms/${id}`))
    } catch (e) {
      fail(e)
    }
  }
  async function mutate(action: Pending) {
    setBusy(true)
    setError('')
    setNotice('')
    setPending(action)
    storePending(action, session?.expiresTicks)
    try {
      await adminRead(action.path, action.payload)
      // A confirmed mutation is never offered again if refreshing its view fails.
      setPending(null)
      storePending(null)
      setConfirm(null)
      setConfirmation('')
      setEditor(null)
      setNotice('Change saved')
      setRefresh((v) => v + 1)
      if (action.path.includes('/rooms/') && action.payload.action === 'delete') setRoom(null)
      else if (room && action.path.includes('/rooms/')) await inspect(room.id)
    } catch (e) {
      if (e instanceof AdminError && e.status > 0) {
        setPending(null)
        storePending(null)
        if (e.status === 409 && room && action.path.includes('/rooms/')) await inspect(room.id)
        setRefresh((v) => v + 1)
      }
      fail(e)
    } finally {
      setBusy(false)
    }
  }
  function roomAction(action: string, targetId?: string, locked?: boolean) {
    if (!room) return
    void mutate({
      path: `/rooms/${room.id}/commands`,
      payload: {
        commandId: crypto.randomUUID(),
        expectedVersion: room.version,
        action,
        targetId,
        locked,
        ...(action === 'delete' ? { confirmation } : {}),
      },
    })
  }
  function promptAction(action: string, prompt?: PromptView, input?: PromptInput) {
    void mutate({
      path: '/prompts',
      payload: {
        commandId: crypto.randomUUID(),
        action,
        input,
        promptId: prompt?.id,
        promptRevision: prompt?.revision ?? 0,
      },
    })
  }
  useEffect(() => {
    detailHeading.current?.focus()
  }, [room?.id])
  useEffect(() => {
    confirmationHeading.current?.focus()
  }, [confirm])
  const blocked = busy || pending !== null
  const current = tab === 'rooms' ? rooms : tab === 'prompts' ? prompts : audit
  return (
    <section className="page-content admin-page">
      <title>Administration — Mingle</title>
      <p className="eyebrow">MINGLE ADMINISTRATION</p>
      <h1>Keep good company</h1>
      {error && (
        <div role="alert" className="notice error-notice">
          {error}
        </div>
      )}
      {notice && <p role="status">{notice}</p>}
      {!session ? (
        <>
          <p role="status">Loading administration…</p>
          <button
            className="text-button"
            onClick={() => {
              void adminSession().then(setSession, fail)
            }}
          >
            Retry connection
          </button>
        </>
      ) : !session.enabled ? (
        <p>Administration is not configured on this deployment</p>
      ) : !session.authenticated ? (
        <form
          className="admin-card admin-login"
          onSubmit={(e) => {
            e.preventDefault()
            setBusy(true)
            setError('')
            const value = credential
            setCredential('')
            adminRead<AdminSessionView>('/login', { credential: value })
              .then((s) => {
                setSession(s)
                setNotice('Signed in')
              }, fail)
              .finally(() => setBusy(false))
          }}
        >
          <h2>Administrator sign in</h2>
          <label>
            Administrator credential
            <input
              type="password"
              autoComplete="current-password"
              required
              maxLength={1024}
              value={credential}
              onChange={(e) => setCredential(e.target.value)}
            />
          </label>
          <button className="button" disabled={busy}>
            Sign in
          </button>
        </form>
      ) : (
        <>
          <div className="admin-topline">
            <nav className="admin-actions" aria-label="Administration">
              {(['rooms', 'prompts', 'audit'] as Tab[]).map((t) => (
                <button
                  className="workspace-tab"
                  key={t}
                  aria-current={tab === t ? 'page' : undefined}
                  disabled={blocked}
                  onClick={() => {
                    setTab(t)
                    setPage(1)
                    setFilter('')
                    setSearch('')
                    setAppliedSearch('')
                    setRoom(null)
                    setEditor(null)
                    setConfirm(null)
                    setError('')
                    setNotice('')
                  }}
                >
                  {t === 'prompts' ? 'Shared prompts' : t === 'rooms' ? 'Rooms' : 'Audit'}
                </button>
              ))}
            </nav>
            <button
              className="text-button"
              disabled={blocked}
              onClick={() => {
                setBusy(true)
                adminRead('/logout', {})
                  .then(() => adminSession())
                  .then((s) => {
                    setSession(s)
                    setRooms(null)
                    setPrompts(null)
                    setAudit(null)
                    setRoom(null)
                    setNotice('Signed out')
                  }, fail)
                  .finally(() => setBusy(false))
              }}
            >
              Sign out
            </button>
          </div>
          <p className="small-note">
            Session ends {session.expiresTicks ? date(session.expiresTicks) : 'soon'}
          </p>
          {pending && !busy && (
            <button
              className="button"
              onClick={() => {
                void mutate(pending)
              }}
            >
              Retry same action
            </button>
          )}
          <div className="admin-actions">
            {tab !== 'audit' && (
              <label>
                {tab === 'rooms' ? 'Room status' : 'Prompt game'}
                <select
                  value={filter}
                  disabled={blocked}
                  onChange={(e) => {
                    setFilter(e.target.value)
                    setPage(1)
                  }}
                >
                  <option value="">All</option>
                  {(tab === 'rooms'
                    ? ['lobby', 'playing', 'closed', 'expired']
                    : Object.keys(gameNames).filter((id) => id !== 'two-truths-and-a-lie')
                  ).map((id) => (
                    <option key={id} value={id}>
                      {tab === 'rooms' ? id : gameName(id)}
                    </option>
                  ))}
                </select>
              </label>
            )}
            {tab === 'prompts' && (
              <>
                <form
                  className="admin-actions"
                  onSubmit={(e) => {
                    e.preventDefault()
                    setAppliedSearch(search)
                    setPage(1)
                  }}
                >
                  <label>
                    Search shared prompts
                    <input
                      value={search}
                      maxLength={120}
                      onChange={(e) => setSearch(e.target.value)}
                    />
                  </label>
                  <button className="text-button" disabled={blocked}>
                    Search
                  </button>
                </form>
                <button className="button" disabled={blocked} onClick={() => setEditor('new')}>
                  Create shared prompt
                </button>
              </>
            )}
            <button
              className="text-button"
              disabled={blocked}
              onClick={() => {
                setError('')
                setRefresh((v) => v + 1)
                if (room) void inspect(room.id)
              }}
            >
              Refresh
            </button>
          </div>
          {loading && <p role="status">Refreshing…</p>}
          {tab === 'rooms' && (
            <div className={`admin-workspace ${room ? 'with-detail' : ''}`}>
              <div className="admin-card">
                <h2>Rooms</h2>
                {rooms?.rooms.length === 0 && <p>No rooms match this view</p>}
                <ul className="admin-list">
                  {rooms?.rooms.map((r) => (
                    <li key={r.id}>
                      <div>
                        <strong>{r.code}</strong>
                        {r.games.at(-1) && (
                          <span>
                            {gameName(r.games.at(-1)!.gameId)} · {r.games.at(-1)!.stage}
                          </span>
                        )}
                        <span>
                          {r.status} · {r.participantCount} players
                        </span>
                        <small>Last activity {date(r.lastActivityTicks)}</small>
                      </div>
                      <button
                        className="text-button"
                        disabled={blocked}
                        onClick={() => {
                          void inspect(r.id)
                        }}
                      >
                        Manage {r.code}
                      </button>
                    </li>
                  ))}
                </ul>
              </div>
              {room && (
                <article className="admin-card">
                  <h2 ref={detailHeading} tabIndex={-1}>
                    Room {room.code}
                  </h2>
                  <p>
                    {room.status} · {room.joinLocked ? 'Joining locked' : 'Joining open'}
                  </p>
                  <div className="admin-actions">
                    {['lobby', 'playing'].includes(room.status) && (
                      <>
                        <button
                          className="text-button"
                          disabled={blocked}
                          onClick={() => roomAction('lock', undefined, !room.joinLocked)}
                        >
                          {room.joinLocked ? 'Unlock joining' : 'Lock joining'}
                        </button>
                        {room.status === 'playing' && room.games.at(-1)?.stage !== 'finished' && (
                          <button
                            className="text-button"
                            disabled={blocked}
                            onClick={() => roomAction('end-game')}
                          >
                            End game
                          </button>
                        )}
                        <button
                          className="text-button"
                          disabled={blocked}
                          onClick={() => roomAction('close')}
                        >
                          Close room
                        </button>
                      </>
                    )}
                    <button
                      className="text-button"
                      disabled={blocked}
                      onClick={() => {
                        setConfirm('room')
                        setConfirmation('')
                      }}
                    >
                      Delete room
                    </button>
                  </div>
                  <h3>Participants</h3>
                  <ul className="admin-list">
                    {room.participants.map((p) => (
                      <li key={p.id}>
                        <span>
                          {p.name}
                          {p.isFacilitator ? ' · Facilitator' : ''}
                          {p.isOnline ? ' · Online' : ' · Away'}
                        </span>
                        {['lobby', 'playing'].includes(room.status) && !p.isFacilitator && (
                          <div className="admin-actions">
                            <button
                              className="text-button"
                              disabled={blocked}
                              onClick={() => roomAction('transfer', p.id)}
                            >
                              Make {p.name} facilitator
                            </button>
                            <button
                              className="text-button"
                              disabled={blocked}
                              onClick={() => roomAction('remove', p.id)}
                            >
                              Remove {p.name}
                            </button>
                          </div>
                        )}
                      </li>
                    ))}
                  </ul>
                  <h3>Game sessions</h3>
                  {room.games.length === 0 ? (
                    <p>No games started</p>
                  ) : (
                    <ul className="admin-list">
                      {room.games.map((g) => (
                        <li key={g.sessionId}>
                          <span>
                            {gameName(g.gameId)} · {g.stage} · Round {g.roundNumber}
                          </span>
                        </li>
                      ))}
                    </ul>
                  )}
                </article>
              )}
            </div>
          )}
          {tab === 'prompts' && (
            <>
              {editor !== null && (
                <PromptEditor
                  key={editor === 'new' ? 'new' : editor.id}
                  prompt={editor === 'new' ? undefined : editor}
                  busy={blocked}
                  cancel={() => setEditor(null)}
                  save={(input) =>
                    promptAction(
                      editor === 'new' ? 'create' : 'edit',
                      editor === 'new' ? undefined : editor,
                      input,
                    )
                  }
                />
              )}
              <div className="admin-card">
                <h2>Shared prompts</h2>
                {prompts?.prompts.length === 0 && <p>No prompts match this view</p>}
                <ul className="admin-list">
                  {prompts?.prompts.map((p) => (
                    <li key={p.id}>
                      <div>
                        <strong>{p.text}</strong>
                        <span>
                          {gameName(p.gameId)} · {p.category} · {p.enabled ? 'Enabled' : 'Disabled'}
                        </span>
                        {p.optionA && (
                          <small>
                            {p.optionA} / {p.optionB}
                          </small>
                        )}
                      </div>
                      <div className="admin-actions">
                        <button
                          className="text-button"
                          disabled={blocked}
                          onClick={() => setEditor(p)}
                        >
                          Edit
                        </button>
                        <button
                          className="text-button"
                          disabled={blocked}
                          onClick={() => promptAction(p.enabled ? 'disable' : 'enable', p)}
                        >
                          {p.enabled ? 'Disable' : 'Enable'}
                        </button>
                        <button
                          className="text-button"
                          disabled={blocked}
                          onClick={() => setConfirm(p)}
                        >
                          Delete
                        </button>
                      </div>
                    </li>
                  ))}
                </ul>
              </div>
            </>
          )}
          {tab === 'audit' && (
            <div className="admin-card">
              <h2>Audit trail</h2>
              <p>Actions and identifiers only · Retained for the configured audit period</p>
              <ul className="admin-list">
                {audit?.events.map((a) => (
                  <li key={a.id}>
                    <div>
                      <strong>{a.action}</strong>
                      <span>{date(a.createdTicks)}</span>
                      <small>{a.targetId}</small>
                    </div>
                  </li>
                ))}
              </ul>
              {audit?.events.length === 0 && <p>No audit events yet</p>}
            </div>
          )}
          {current && (
            <Pager
              page={page}
              total={current.total}
              disabled={blocked || loading}
              change={setPage}
            />
          )}
          {confirm && (
            <section
              className="admin-card admin-confirm"
              role="region"
              aria-label="Confirm deletion"
            >
              <h2 ref={confirmationHeading} tabIndex={-1}>
                {confirm === 'room' ? 'Delete this room' : 'Delete this shared prompt'}
              </h2>
              <p>
                {confirm === 'room'
                  ? 'This permanently removes the room, memberships, game data and room prompts. Players lose access.'
                  : 'This removes the prompt from future draws. Started rounds keep their saved text.'}
              </p>
              {confirm === 'room' && (
                <label>
                  Enter room code to confirm
                  <input value={confirmation} onChange={(e) => setConfirmation(e.target.value)} />
                </label>
              )}
              <div className="admin-actions">
                <button
                  className="button"
                  disabled={blocked || (confirm === 'room' && confirmation !== room?.code)}
                  onClick={() =>
                    confirm === 'room' ? roomAction('delete') : promptAction('delete', confirm)
                  }
                >
                  {confirm === 'room' ? 'Permanently delete room' : 'Confirm delete prompt'}
                </button>
                <button className="text-button" disabled={blocked} onClick={() => setConfirm(null)}>
                  Cancel deletion
                </button>
              </div>
            </section>
          )}
        </>
      )}
    </section>
  )
}
