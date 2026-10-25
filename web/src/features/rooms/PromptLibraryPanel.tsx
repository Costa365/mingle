import { useEffect, useId, useRef, useState } from 'react'
import { commandPrompt, getPrompts, RoomError } from '../../api/rooms'
import type {
  GameCommand,
  PromptCommand,
  PromptInput,
  PromptView,
  RoomSnapshot,
} from '../../api/rooms'

const games = [
  ['this-or-that', 'This or That'],
  ['would-you-rather', 'Would You Rather?'],
  ['never-have-i-ever', 'Never Have I Ever'],
  ['most-likely-to', 'Most Likely To'],
]
const blank: PromptInput = {
  gameId: 'this-or-that',
  category: 'Team favourites',
  tags: [],
  text: 'Which would you choose?',
  optionA: '',
  optionB: '',
  enabled: true,
}
export function PromptLibraryPanel({
  snapshot,
  disabled,
  accept,
  refresh,
  choose,
  expanded = false,
}: {
  snapshot: RoomSnapshot
  disabled: boolean
  accept: (s: RoomSnapshot) => void
  refresh: () => Promise<unknown>
  expanded?: boolean
  choose: (selection: Partial<GameCommand>) => Promise<void>
}) {
  const Heading = expanded ? 'h1' : 'h3'
  const Subheading = expanded ? 'h2' : 'h4'
  const listId = useId()
  const headingId = `${listId}-heading`
  const scrollHelpId = `${listId}-scroll-help`
  const [toggledOpen, setOpen] = useState(false)
  const open = expanded || toggledOpen
  const [prompts, setPrompts] = useState<PromptView[]>([])
  const [loaded, setLoaded] = useState(false)
  const [error, setError] = useState('')
  const [loadError, setLoadError] = useState('')
  const [gameId, setGame] = useState('this-or-that')
  const [category, setCategory] = useState('')
  const [tag, setTag] = useState('')
  const [search, setSearch] = useState('')
  const [editing, setEditing] = useState<PromptView | null>(null)
  const [form, setForm] = useState<PromptInput | null>(null)
  const [pending, setPending] = useState<PromptCommand | null>(null)
  const [busy, setBusy] = useState(false)
  const sending = useRef(false)
  const [confirmDelete, setConfirmDelete] = useState<string | null>(null)
  useEffect(() => {
    if (!open || disabled) return
    const controller = new AbortController()
    getPrompts(snapshot.id, controller.signal)
      .then((page) => {
        if (!controller.signal.aborted) {
          setPrompts(page.prompts)
          setLoaded(true)
          setLoadError('')
        }
      })
      .catch(() => {
        if (!controller.signal.aborted)
          setLoadError(
            expanded
              ? 'The prompt library could not be loaded; leave Prompts and return to retry'
              : 'The prompt library could not be loaded; close and reopen it to retry',
          )
      })
    return () => controller.abort()
  }, [open, snapshot.id, snapshot.version, disabled, expanded])
  const pool = prompts.filter((p) => p.gameId === gameId)
  const normalized = (text: string) =>
    text.normalize('NFKC').trim().toUpperCase().split(/\s+/u).join(' ')
  const filtered = pool.filter(
    (p) =>
      (!category || p.category === category) &&
      (!tag || p.tags.includes(tag)) &&
      normalized(`${p.text} ${p.optionA} ${p.optionB} ${p.category} ${p.tags.join(' ')}`).includes(
        normalized(search),
      ),
  )
  const usable = filtered.filter((p) => p.enabled && !p.used)
  const playable =
    gameId === 'this-or-that' && (!snapshot.game || snapshot.game.stage === 'revealed')
  const blocked = disabled || busy || pending !== null
  async function save(action: string, target?: PromptView, retry?: PromptCommand) {
    if ((blocked && !retry) || sending.current) return
    sending.current = true
    setBusy(true)
    setError('')
    const command: PromptCommand = retry ?? {
      commandId: crypto.randomUUID(),
      expectedVersion: snapshot.version,
      action,
      promptId: target?.id,
      promptRevision: target?.revision ?? 0,
      input:
        action === 'delete'
          ? undefined
          : form
            ? { ...form, tags: form.tags.map((t) => t.trim()).filter(Boolean) }
            : null,
    }
    setPending(command)
    try {
      accept((await commandPrompt(snapshot.id, command)).snapshot)
      setPending(null)
      setForm(null)
      setEditing(null)
      setConfirmDelete(null)
      await getPrompts(snapshot.id)
        .then((page) => setPrompts(page.prompts))
        .catch(() =>
          setLoadError(
            expanded
              ? 'Your prompt change is saved; leave Prompts and return to reload it'
              : 'Your prompt change is saved; close and reopen the library to reload it',
          ),
        )
    } catch (cause) {
      if (cause instanceof RoomError && cause.status >= 400 && cause.status < 500) {
        setPending(null)
        setError(cause.message)
        await refresh().catch(() => {})
      } else setError('We could not confirm this prompt change. Retry it safely below')
    } finally {
      sending.current = false
      setBusy(false)
    }
  }
  return (
    <section className="prompt-library" aria-label="Prompt library">
      {!expanded && (
        <button className="text-button" aria-expanded={open} onClick={() => setOpen(!open)}>
          Browse prompts
        </button>
      )}
      {open && (
        <>
          <Heading id="prompts-heading" tabIndex={-1}>
            Your prompt library
          </Heading>
          <div className="prompt-filters">
            <label>
              Game
              <select
                value={gameId}
                onChange={(e) => {
                  setGame(e.target.value)
                  setCategory('')
                  setTag('')
                }}
              >
                {games.map(([id, name]) => (
                  <option key={id} value={id}>
                    {name}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Category
              <select value={category} onChange={(e) => setCategory(e.target.value)}>
                <option value="">All categories</option>
                {[...new Set([...pool.map((p) => p.category), ...(category ? [category] : [])])]
                  .sort()
                  .map((c) => (
                    <option key={c}>{c}</option>
                  ))}
              </select>
            </label>
            <label>
              Tag
              <select value={tag} onChange={(e) => setTag(e.target.value)}>
                <option value="">All tags</option>
                {[...new Set([...pool.flatMap((p) => p.tags), ...(tag ? [tag] : [])])]
                  .sort()
                  .map((t) => (
                    <option key={t}>{t}</option>
                  ))}
              </select>
            </label>
            <label>
              Search prompts
              <input
                type="search"
                maxLength={120}
                value={search}
                onChange={(e) => setSearch(e.target.value)}
              />
            </label>
          </div>
          {!loaded && <p role="status">Loading prompts…</p>}
          {loaded && (
            <p role="status">
              {filtered.length} {filtered.length === 1 ? 'prompt' : 'prompts'} · {usable.length}{' '}
              unused and enabled
            </p>
          )}
          {gameId !== 'this-or-that' && (
            <p className="room-note">
              You can browse and prepare prompts for this game. Gameplay for this game is coming
              soon.
            </p>
          )}
          {snapshot.game?.stage === 'responding' && (
            <p className="room-note">
              Reveal or skip this round before choosing the next prompt. Editing a prompt will not
              change the round already in play.
            </p>
          )}
          <div className="room-buttons">
            <button
              className="button"
              disabled={blocked || !loaded || !playable || usable.length === 0}
              onClick={() => {
                void choose({ category, tag, search })
              }}
            >
              Draw random prompt
            </button>
            {playable && usable.length === 0 && filtered.some((p) => p.enabled) && (
              <button
                className="button"
                disabled={blocked}
                onClick={() => {
                  void choose({ category, tag, search, resetPool: true })
                }}
              >
                Reset pool and draw
              </button>
            )}
            <button
              className="text-button"
              disabled={blocked}
              onClick={() => {
                setEditing(null)
                setForm({
                  ...blank,
                  gameId,
                  text:
                    gameId === 'would-you-rather'
                      ? 'Would you rather?'
                      : gameId === 'this-or-that'
                        ? blank.text
                        : '',
                })
                setError('')
              }}
            >
              Add room prompt
            </button>
          </div>
          {form && (
            <form
              className="prompt-form"
              onSubmit={(e) => {
                e.preventDefault()
                void save(editing ? 'edit' : 'create', editing ?? undefined)
              }}
            >
              <Subheading>{editing ? 'Edit room prompt' : 'New room prompt'}</Subheading>
              <label>
                Prompt text
                <input
                  required
                  maxLength={240}
                  value={form.text}
                  onChange={(e) => setForm({ ...form, text: e.target.value })}
                />
              </label>
              {['this-or-that', 'would-you-rather'].includes(form.gameId) && (
                <>
                  <label>
                    Option A
                    <input
                      required
                      maxLength={120}
                      value={form.optionA}
                      onChange={(e) => setForm({ ...form, optionA: e.target.value })}
                    />
                  </label>
                  <label>
                    Option B
                    <input
                      required
                      maxLength={120}
                      value={form.optionB}
                      onChange={(e) => setForm({ ...form, optionB: e.target.value })}
                    />
                  </label>
                </>
              )}
              <label>
                Prompt category
                <input
                  required
                  maxLength={48}
                  value={form.category}
                  onChange={(e) => setForm({ ...form, category: e.target.value })}
                />
              </label>
              <label>
                Tags separated by commas
                <input
                  maxLength={263}
                  value={form.tags.join(',')}
                  onChange={(e) => setForm({ ...form, tags: e.target.value.split(',') })}
                />
              </label>
              <label className="prompt-enabled">
                <input
                  type="checkbox"
                  checked={form.enabled}
                  onChange={(e) => setForm({ ...form, enabled: e.target.checked })}
                />
                Enabled for drawing
              </label>
              <div className="room-buttons">
                <button className="button" disabled={blocked}>
                  Save prompt
                </button>
                <button
                  type="button"
                  className="text-button"
                  disabled={blocked}
                  onClick={() => setForm(null)}
                >
                  Cancel editing
                </button>
              </div>
            </form>
          )}
          <section className="prompt-results-frame" aria-labelledby={headingId}>
            <div className="prompt-results-toolbar">
              <Subheading id={headingId}>Available prompts</Subheading>
              <span>Scroll for more ↓</span>
            </div>
            <p id={scrollHelpId} className="prompt-scroll-help">
              Scroll within the list, or focus it and use Page Up / Page Down
            </p>
            <div className="prompt-results-columns" aria-hidden="true">
              <span>Prompt</span>
              <span>Category &amp; tags</span>
              <span>Action</span>
            </div>
            <ul
              className="prompt-results"
              aria-label="Available prompts"
              aria-describedby={scrollHelpId}
              tabIndex={0}
            >
              {filtered.map((p) => (
                <li key={p.id} className="prompt-result-row">
                  <p className="prompt-result-title">
                    <strong>{p.optionA ? `${p.optionA} / ${p.optionB}` : p.text}</strong>
                  </p>
                  <div className="prompt-result-meta">
                    <span className="prompt-category">{p.category}</span>
                    {p.tags.length > 0 && <span className="prompt-tags">{p.tags.join(' · ')}</span>}
                    <span className="prompt-source">
                      {p.source === 'room' ? 'Room prompt' : 'Built-in'}
                      {p.used ? ' · Used' : ''}
                      {!p.enabled ? ' · Disabled' : ''}
                    </span>
                  </div>
                  <div className="room-buttons prompt-result-actions">
                    <button
                      className="text-button"
                      disabled={blocked || !playable || p.used || !p.enabled}
                      onClick={() => {
                        void choose({ promptId: p.id, category, tag, search })
                      }}
                    >
                      Use this prompt
                    </button>
                    {p.source === 'room' && (
                      <>
                        <button
                          className="text-button"
                          disabled={blocked}
                          onClick={() => {
                            setEditing(p)
                            setForm({
                              gameId: p.gameId,
                              category: p.category,
                              tags: [...p.tags],
                              text: p.text,
                              optionA: p.optionA,
                              optionB: p.optionB,
                              enabled: p.enabled,
                            })
                            setError('')
                          }}
                        >
                          Edit prompt
                        </button>
                        <button
                          className="text-button"
                          disabled={blocked}
                          onClick={() => setConfirmDelete(p.id)}
                        >
                          Delete prompt
                        </button>
                        {confirmDelete === p.id && (
                          <div role="group" aria-label="Confirm prompt deletion">
                            <p>The current and historical rounds keep their saved text</p>
                            <button
                              className="text-button"
                              disabled={blocked}
                              onClick={() => {
                                void save('delete', p)
                              }}
                            >
                              Confirm delete
                            </button>
                            <button
                              className="text-button"
                              disabled={blocked}
                              onClick={() => setConfirmDelete(null)}
                            >
                              Keep prompt
                            </button>
                          </div>
                        )}
                      </>
                    )}
                  </div>
                </li>
              ))}
            </ul>
          </section>
          {loaded && filtered.length === 0 && <p>No prompts match these filters</p>}
          {loadError && (
            <p role="alert" className="room-error">
              {loadError}
            </p>
          )}
          {error && (
            <p role="alert" className="room-error">
              {error}
            </p>
          )}
          {pending && !busy && (
            <button
              className="button"
              disabled={disabled}
              onClick={() => {
                void save(pending.action, undefined, pending)
              }}
            >
              Retry prompt change
            </button>
          )}
        </>
      )}
    </section>
  )
}
