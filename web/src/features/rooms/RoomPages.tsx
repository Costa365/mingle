import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { commandRoom, enterRoom, RoomError } from '../../api/rooms'
import type { EntryRequest, RoomCommand } from '../../api/rooms'
import { GamePanel } from './GamePanel'
import { gameName } from './gameNames'
import { useRoom } from '../../realtime/useRoom'

export function RoomEntry({ create }: { create: boolean }) {
  const navigate = useNavigate()
  const [search] = useSearchParams()
  const [name, setName] = useState('')
  const [code, setCode] = useState(search.get('code') ?? '')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const request = useRef<EntryRequest | null>(null)
  async function submit(event: FormEvent) {
    event.preventDefault()
    if (busy) return
    setBusy(true)
    setError('')
    const displayName = name.trim()
    const invite = create ? undefined : code.trim().toUpperCase()
    // Keep the ID after an ambiguous network failure, including a page reload.
    let saved: { create: boolean; request: EntryRequest } | null = null
    try {
      saved = JSON.parse(sessionStorage.getItem('mingle-entry') ?? 'null') as {
        create: boolean
        request: EntryRequest
      } | null
    } catch {
      /* A stale browser draft can be discarded. */
    }
    const previous = request.current ?? (saved?.create === create ? saved.request : null)
    const next =
      previous?.displayName === displayName && previous?.code === invite
        ? previous
        : { commandId: crypto.randomUUID(), displayName, code: invite }
    request.current = next
    try {
      sessionStorage.setItem('mingle-entry', JSON.stringify({ create, request: next }))
    } catch {
      /* The in-memory retry ID still works when browser storage is unavailable. */
    }
    try {
      const result = await enterRoom(next, create)
      try {
        sessionStorage.removeItem('mingle-entry')
      } catch {
        /* Navigation still succeeds without browser storage. */
      }
      navigate(`/rooms/${result.snapshot.id}`)
    } catch (cause) {
      setError(
        cause instanceof RoomError
          ? cause.message
          : 'We could not reach your room; try again to safely retry',
      )
    } finally {
      setBusy(false)
    }
  }
  return (
    <section className="page-content room-entry">
      <title>{create ? 'Host a room — Mingle' : 'Join a room — Mingle'}</title>
      <p className="eyebrow">GOOD COMPANY STARTS HERE</p>
      <h1>{create ? 'Make room for your people' : 'Pull up a chair'}</h1>
      <p>
        {create
          ? 'Create a space for your team, then share the invitation.'
          : 'Enter your invitation code and the name your team knows you by.'}
      </p>
      <form
        onSubmit={(event) => {
          void submit(event)
        }}
        className="room-form"
      >
        <label htmlFor="display-name">Your name</label>
        <input
          id="display-name"
          name="displayName"
          autoComplete="nickname"
          maxLength={32}
          required
          value={name}
          onChange={(event) => setName(event.target.value)}
        />
        {!create && (
          <>
            <label htmlFor="room-code">Room code</label>
            <input
              id="room-code"
              name="code"
              autoComplete="off"
              autoCapitalize="characters"
              spellCheck={false}
              minLength={6}
              maxLength={6}
              required
              value={code}
              onChange={(event) => setCode(event.target.value.toUpperCase())}
            />
          </>
        )}
        {error && (
          <p className="room-error" role="alert">
            {error}
          </p>
        )}
        <button className="button" disabled={busy}>
          {busy ? 'Finding your place…' : create ? 'Create room' : 'Join room'}
        </button>
      </form>
      <p className="room-note">
        Your place is saved in this browser. Clearing cookies or switching devices loses it;
        transfer the facilitator role before leaving if needed.
      </p>
      <Link to={create ? '/join' : '/host'}>
        {create ? 'Have an invitation? Join a room' : 'Want to host? Create a room'}
      </Link>
    </section>
  )
}

export function RoomLobby() {
  const { id = '' } = useParams()
  const room = useRoom(id)
  const { snapshot, status } = room
  const [busy, setBusy] = useState(false)
  const [actionError, setActionError] = useState('')
  const [copyStatus, setCopyStatus] = useState('')
  const [confirmClose, setConfirmClose] = useState(false)
  const [pending, setPending] = useState<RoomCommand | null>(null)
  const sending = useRef(false)
  const closeButton = useRef<HTMLButtonElement>(null)
  const cancelClose = () => {
    setConfirmClose(false)
    closeButton.current?.focus()
  }
  const synchronized = status === 'live'
  const [workspace, setWorkspace] = useState<{
    roomId: string
    stageKey: string
    view: 'room' | 'game' | 'prompts'
  }>({ roomId: id, stageKey: '', view: 'room' })
  const stageKey = snapshot?.game
    ? `${snapshot.game.sessionId}:${snapshot.game.roundId}:${snapshot.game.stage}`
    : 'lobby'
  if (snapshot && synchronized && (workspace.roomId !== id || workspace.stageKey !== stageKey)) {
    setWorkspace({ roomId: id, stageKey, view: snapshot.game ? 'game' : 'room' })
  }
  const isFacilitator = snapshot?.youId === snapshot?.facilitatorId
  const view = workspace.view === 'prompts' && !isFacilitator ? 'game' : workspace.view
  function selectView(next: 'room' | 'game' | 'prompts') {
    setWorkspace((current) => ({ ...current, view: next }))
  }
  useEffect(() => {
    if (!workspace.stageKey) return
    const heading = document.getElementById(
      view === 'game' ? 'game-heading' : view === 'prompts' ? 'prompts-heading' : 'room-heading',
    )
    heading?.focus({ preventScroll: true })
    heading?.scrollIntoView?.({ block: 'start', behavior: 'instant' })
  }, [workspace.stageKey, view])
  async function act(action: string, targetId?: string, locked?: boolean, retry?: RoomCommand) {
    if (!snapshot || !synchronized || sending.current) return
    sending.current = true
    setBusy(true)
    setActionError('')
    const command = retry ?? {
      commandId: crypto.randomUUID(),
      expectedVersion: snapshot.version,
      action,
      targetId,
      locked,
    }
    setPending(command)
    try {
      const result = await commandRoom(id, command)
      room.accept(result.snapshot)
      setPending(null)
      setConfirmClose(false)
    } catch (cause) {
      if (cause instanceof RoomError && cause.status >= 400 && cause.status < 500) {
        setPending(null)
        setActionError(cause.message)
        await room.refresh().catch(() => {})
      } else
        setActionError('We could not confirm that change. Retry it safely using the button below')
    } finally {
      sending.current = false
      setBusy(false)
    }
  }
  async function copy(invite: boolean) {
    if (!snapshot) return
    const value = invite ? `${window.location.origin}/join?code=${snapshot.code}` : snapshot.code
    try {
      await navigator.clipboard.writeText(value)
      setCopyStatus(invite ? 'Invite link copied' : 'Room code copied')
    } catch {
      setCopyStatus('Copying is unavailable; select the room code and share it with your team')
    }
  }
  if (status === 'ended')
    return (
      <section className="page-content room-ended">
        <title>Room unavailable — Mingle</title>
        <h1>Your room is unavailable</h1>
        <p role="alert">{room.error}</p>
        <Link className="button" to="/join">
          Join a room
        </Link>
      </section>
    )
  if (!snapshot)
    return (
      <section className="page-content">
        <h1>Finding your room</h1>
        <p role="status">{room.error || 'Connecting to your room…'}</p>
        {status === 'offline' && (
          <button className="button" onClick={room.retry}>
            Retry connection
          </button>
        )}
      </section>
    )
  if (snapshot.status === 'closed')
    return (
      <section className="page-content room-ended">
        <title>Room closed — Mingle</title>
        <h1>This room is closed</h1>
        <p>Your facilitator has wrapped things up. Thanks for making time for your people.</p>
        <Link className="button" to="/">
          Back to the games
        </Link>
      </section>
    )
  const can = (action: string) => snapshot.allowedActions.includes(action)
  const blocked = !synchronized || busy || pending !== null
  const facilitator = snapshot.participants.find((participant) => participant.isFacilitator)
  return (
    <section className="page-content lobby-page">
      <title>
        {snapshot.game ? `${gameName(snapshot.game.gameId)} — Mingle` : 'Your room — Mingle'}
      </title>
      <div className="workspace-header">
        <div className="workspace-identity">Room {snapshot.code}</div>
        <span className={`connection-status ${synchronized ? 'is-live' : ''}`} role="status">
          {synchronized ? 'Connected' : status === 'connecting' ? 'Connecting…' : 'Reconnecting…'}
        </span>
        <nav className="workspace-nav" aria-label="Room navigation">
          {(['room', 'game', ...(isFacilitator ? ['prompts'] : [])] as const).map((item) => (
            <button
              key={item}
              className="workspace-tab"
              aria-current={view === item ? 'page' : undefined}
              onClick={() => selectView(item as 'room' | 'game' | 'prompts')}
            >
              {item === 'room'
                ? `Room (${snapshot.participants.length})`
                : item === 'game'
                  ? `Game${snapshot.game ? (snapshot.game.stage === 'finished' ? ' · Finished' : ' · Live') : ''}`
                  : 'Prompts'}
            </button>
          ))}
        </nav>
      </div>
      {!synchronized && (
        <div className="room-recovery">
          <p>
            {room.error || 'Your place is saved. Controls return when your room is synchronized.'}
          </p>
          <button className="button" onClick={room.retry}>
            Retry connection
          </button>
        </div>
      )}
      {room.error && synchronized && <p role="alert">{room.error}</p>}
      <div hidden={view !== 'room'}>
        {snapshot.game && (
          <div className="current-game-strip">
            <span>
              {gameName(snapshot.game.gameId)} · Round {snapshot.game.roundNumber} ·{' '}
              {snapshot.game.stage === 'responding'
                ? 'Live'
                : snapshot.game.stage === 'revealed'
                  ? 'Revealed'
                  : 'Finished'}
            </span>
            <button className="text-button" onClick={() => selectView('game')}>
              Return to game →
            </button>
          </div>
        )}
        <h1 id="room-heading" tabIndex={-1}>
          The gathering place
        </h1>
        <div className="lobby-grid">
          <section className="lobby-roster" aria-labelledby="roster-heading">
            <h2 id="roster-heading">
              Your team{' '}
              <span>
                ({snapshot.participants.length}/{snapshot.capacity})
              </span>
            </h2>
            {!facilitator?.isOnline && (
              <p className="room-note">
                Your facilitator is away. Their place is saved; they can reconnect to continue.
              </p>
            )}
            <ul>
              {snapshot.participants.map((participant) => (
                <li key={participant.id}>
                  <div className="participant-identity">
                    <span className="participant-avatar" aria-hidden="true">
                      {Array.from(participant.name)[0]}
                    </span>
                    <div>
                      <strong>
                        {participant.name}
                        {participant.id === snapshot.youId ? ' (you)' : ''}
                      </strong>
                      <span className="participant-presence">
                        {participant.isFacilitator ? 'Facilitator · ' : ''}
                        {participant.isOnline ? 'Online' : 'Away'}
                      </span>
                    </div>
                  </div>
                  {participant.id !== snapshot.youId && (can('remove') || can('transfer')) && (
                    <div className="participant-actions">
                      {can('transfer') && (
                        <button
                          disabled={blocked}
                          onClick={() => {
                            void act('transfer', participant.id)
                          }}
                          aria-label={`Make ${participant.name} facilitator`}
                        >
                          Make {participant.name} facilitator
                        </button>
                      )}
                      {can('remove') && (
                        <button
                          disabled={blocked}
                          onClick={() => {
                            void act('remove', participant.id)
                          }}
                          aria-label={`Remove ${participant.name}`}
                        >
                          Remove
                        </button>
                      )}
                    </div>
                  )}
                </li>
              ))}
            </ul>
          </section>
          <aside className="lobby-invite" aria-labelledby="invite-heading">
            <h2 id="invite-heading">Bring your people</h2>
            <p>Share this code or send an invite link</p>
            <p className="invite-code" aria-label={`Room code ${snapshot.code}`}>
              {snapshot.code}
            </p>
            <div className="room-buttons">
              <button
                className="button"
                onClick={() => {
                  void copy(true)
                }}
              >
                Copy invite link
              </button>
              <button
                className="text-button"
                onClick={() => {
                  void copy(false)
                }}
              >
                Copy code
              </button>
            </div>
            <p className="copy-status" role="status">
              {copyStatus}
            </p>
            <p className="room-note">
              {snapshot.joinLocked
                ? 'Joining is locked. Existing participants can still reconnect.'
                : 'Joining is open to people with your invitation.'}
            </p>
            {can('lock') && (
              <button
                className="text-button"
                disabled={blocked}
                onClick={() => {
                  void act('lock', undefined, !snapshot.joinLocked)
                }}
              >
                {snapshot.joinLocked ? 'Unlock joining' : 'Lock joining'}
              </button>
            )}
          </aside>
        </div>
        {actionError && (
          <p className="room-error" role="alert">
            {actionError}
          </p>
        )}
        {pending && !busy && (
          <button
            className="button"
            disabled={!synchronized}
            onClick={() => {
              void act(pending.action, pending.targetId, pending.locked, pending)
            }}
          >
            Retry last change
          </button>
        )}
      </div>
      <GamePanel
        view={view}
        onViewChange={selectView}
        snapshot={snapshot}
        disabled={blocked}
        accept={room.accept}
        refresh={room.refresh}
      />
      <div hidden={view !== 'room'} className="lobby-next">
        <div>
          <h2>Make yourself at home</h2>
          <p>Invite your teammates and settle in. Your facilitator sets the pace.</p>
        </div>
        {can('close') && (
          <button
            ref={closeButton}
            className="text-button"
            disabled={blocked}
            onClick={() => setConfirmClose(true)}
          >
            Close room
          </button>
        )}
      </div>
      {confirmClose && (
        <div
          className="room-confirm"
          role="alertdialog"
          aria-labelledby="close-heading"
          aria-describedby="close-description"
          onKeyDown={(event) => {
            if (event.key === 'Escape' && !blocked) cancelClose()
          }}
        >
          <h2 id="close-heading">Close this room</h2>
          <p id="close-description">
            Everyone will leave this gathering. You can create a new room afterward.
          </p>
          <div className="room-buttons">
            <button
              className="button"
              disabled={blocked}
              onClick={() => {
                void act('close')
              }}
            >
              Close room for everyone
            </button>
            <button className="text-button" autoFocus disabled={blocked} onClick={cancelClose}>
              Keep room open
            </button>
          </div>
        </div>
      )}
    </section>
  )
}
