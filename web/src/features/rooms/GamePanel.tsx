import { useRef, useState } from 'react'
import { PromptLibraryPanel } from './PromptLibraryPanel'
import { commandGame, RoomError } from '../../api/rooms'
import type { GameCommand, GameView, RoomSnapshot } from '../../api/rooms'

interface GamePanelProps {
  view?: 'room' | 'game' | 'prompts'
  onViewChange?: (view: 'room' | 'game' | 'prompts') => void
  snapshot: RoomSnapshot
  disabled: boolean
  accept: (snapshot: RoomSnapshot) => void
  refresh: () => Promise<unknown>
}
const views: Record<string, typeof ThisOrThatPanel> = { 'this-or-that': ThisOrThatPanel }
export function GamePanel(props: GamePanelProps) {
  const View = views[props.snapshot.game?.gameId ?? 'this-or-that']
  return View ? (
    <View {...props} />
  ) : (
    <p>This game needs a newer version of Mingle. Refresh to continue.</p>
  )
}
function ThisOrThatPanel({
  snapshot,
  disabled,
  accept,
  refresh,
  view,
  onViewChange,
}: GamePanelProps) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [pending, setPending] = useState<GameCommand | null>(null)
  const sending = useRef(false)
  const game = snapshot.game
  const Heading = game || view === 'game' ? 'h1' : 'h2'
  async function act(
    action: string,
    choice?: string,
    retry?: GameCommand,
    selection?: Partial<GameCommand>,
  ) {
    if (disabled || sending.current) return
    sending.current = true
    setBusy(true)
    setError('')
    const command = retry ?? {
      commandId: crypto.randomUUID(),
      action,
      choice,
      gameId: 'this-or-that',
      expectedVersion: snapshot.version,
      sessionId: game?.sessionId,
      roundId: game?.roundId,
      stageRevision: game?.stageRevision ?? 0,
      responseRevision: game?.responseRevision ?? 0,
      ...selection,
    }
    setPending(command)
    try {
      accept((await commandGame(snapshot.id, command)).snapshot)
      setPending(null)
    } catch (cause) {
      if (cause instanceof RoomError && cause.status >= 400 && cause.status < 500) {
        setPending(null)
        setError(cause.message)
        await refresh().catch(() => {})
      } else setError('We could not confirm that change. Retry it safely below')
    } finally {
      sending.current = false
      setBusy(false)
    }
  }
  const blocked = disabled || busy || pending !== null
  const control = (action: string, label: string) =>
    game?.allowedActions.includes(action) && (
      <button
        className={['skip', 'finish'].includes(action) ? 'text-button' : 'button'}
        disabled={blocked}
        onClick={() => {
          void act(action)
        }}
      >
        {label}
      </button>
    )
  const controls = game?.allowedActions.some((action) => action !== 'answer') ? (
    <div className="room-buttons game-controls" role="group" aria-label="Facilitator controls">
      {control('reveal', 'Reveal answers')}
      {control('skip', 'Skip round')}
      {control('next', 'Next round')}
      {control('reset', 'Reset pool and draw')}
      {control('finish', 'Finish game')}
      {control('lobby', 'Return to lobby')}
    </div>
  ) : null
  return (
    <>
      <section
        hidden={view === 'prompts' || (view === 'room' && !!game)}
        className={`game-panel ${game ? 'is-playing' : ''}`}
        aria-labelledby="game-heading"
      >
        {game && (
          <p className="game-notice" role="status">
            {game.stage === 'responding'
              ? game.roundNumber === 1
                ? 'This or That has started'
                : `Round ${game.roundNumber} has started`
              : game.stage === 'revealed'
                ? 'Answers revealed · Time to talk'
                : 'Game finished'}
          </p>
        )}
        {!game && <p className="eyebrow">A LITTLE PLAY, TOGETHER</p>}
        <Heading id="game-heading" tabIndex={-1}>
          This or That
        </Heading>
        <div className={game?.stage === 'responding' ? 'focused-game-layout' : ''}>
          <div className="game-main">
            {!game ? (
              <>
                <p>
                  Pick a side, discover what you share, and talk about the differences. Passing is
                  always welcome.
                </p>
                <p className="room-note">
                  200 built-in pairs across eight categories · Play solo or with your team
                </p>
                {snapshot.youId === snapshot.facilitatorId ? (
                  <button
                    className="button"
                    disabled={blocked}
                    onClick={() => {
                      void act('start')
                    }}
                  >
                    Start This or That
                  </button>
                ) : (
                  <p>Waiting for your facilitator to start a game</p>
                )}
              </>
            ) : (
              <>
                <p className="round-progress">
                  Round {game.roundNumber} ·{' '}
                  {game.stage === 'responding'
                    ? 'Make your choice'
                    : game.stage === 'revealed'
                      ? 'Time to compare'
                      : 'All wrapped up'}
                </p>
                {game.stage !== 'responding' && controls}
                {game.stage === 'responding' && (
                  <>
                    <h2 className="game-prompt">{game.prompt}</h2>
                    {!game.eligible && (
                      <p className="room-note">
                        You joined during this round. You can play from the next round.
                      </p>
                    )}
                    <div className="game-choices" role="group" aria-label="Your choice">
                      {(
                        [
                          ['a', game.optionA],
                          ['b', game.optionB],
                        ] as const
                      ).map(([choice, label]) => (
                        <button
                          key={choice}
                          className={`choice-button ${game.ownChoice === choice ? 'is-selected' : ''}`}
                          aria-pressed={game.ownChoice === choice}
                          disabled={blocked || !game.allowedActions.includes('answer')}
                          onClick={() => {
                            void act('answer', choice)
                          }}
                        >
                          {label}
                          {game.ownChoice === choice && <span aria-hidden="true"> ✓</span>}
                        </button>
                      ))}
                    </div>
                    <p className="answer-feedback" role="status">
                      {game.ownChoice === 'pass'
                        ? 'You passed. You can change it until reveal.'
                        : game.ownChoice
                          ? 'Your answer is saved. You can change it until reveal.'
                          : 'Your choice stays private until reveal.'}
                    </p>
                    <button
                      className="text-button pass-button"
                      aria-pressed={game.ownChoice === 'pass'}
                      disabled={blocked || !game.allowedActions.includes('answer')}
                      onClick={() => {
                        void act('answer', 'pass')
                      }}
                    >
                      Pass this round
                      {game.ownChoice === 'pass' && <span aria-hidden="true"> ✓</span>}
                    </button>
                  </>
                )}
                {game.stage === 'finished' && <h2>Thanks for playing</h2>}
                {game.stage !== 'responding' && (
                  <RoundResults
                    results={game.stage === 'revealed' ? game.results.slice(-1) : game.results}
                  />
                )}
                {game.stage === 'responding' && controls}
                {game.allowedActions.includes('reveal') && (
                  <p className="room-note">
                    You can reveal or move on at any time, even with no answers.
                  </p>
                )}
                {game.stage === 'revealed' && snapshot.youId !== snapshot.facilitatorId && (
                  <p>Waiting for your facilitator to continue</p>
                )}
                {game.stage === 'revealed' && game.results.length > 1 && (
                  <details className="earlier-rounds">
                    <summary>Earlier rounds ({game.results.length - 1})</summary>
                    <RoundResults results={game.results.slice(0, -1)} />
                  </details>
                )}
              </>
            )}
            {snapshot.youId === snapshot.facilitatorId && onViewChange && (
              <button
                className="text-button browse-prompts"
                onClick={() => onViewChange('prompts')}
              >
                Browse prompts
              </button>
            )}
          </div>
          {game?.stage === 'responding' && (
            <aside className="response-sidebar" aria-labelledby="responses-heading">
              <h2 id="responses-heading">Who has responded</h2>
              <p role="status">
                {game.responseCount} of {game.eligibleCount} responded
              </p>
              <ul className="response-list" aria-label="Participant responses">
                {(game.responses ?? []).map((response) => {
                  const participant = snapshot.participants.find(
                    (p) => p.id === response.participantId,
                  )
                  if (!participant) return null
                  return (
                    <li key={participant.id}>
                      <span>
                        {participant.name}
                        {participant.id === snapshot.youId ? ' (you)' : ''}
                        {!participant.isOnline && <small> · Away</small>}
                      </span>
                      <span
                        className={`response-status ${response.hasResponded ? 'has-responded' : ''}`}
                      >
                        {response.hasResponded ? 'Answered' : 'Waiting'}
                      </span>
                    </li>
                  )
                })}
              </ul>
              {game.responses &&
                snapshot.participants.some(
                  (p) => !game.responses!.some((r) => r.participantId === p.id),
                ) && (
                  <p className="room-note">
                    Joining next round:{' '}
                    {snapshot.participants
                      .filter((p) => !game.responses!.some((r) => r.participantId === p.id))
                      .map((p) => p.name)
                      .join(', ')}
                  </p>
                )}
              <p className="room-note">Choices stay private until reveal.</p>
            </aside>
          )}
        </div>
        {game?.stage === 'revealed' && game.remainingPrompts === 0 && (
          <p className="room-note">
            {game.poolSize === 0
              ? 'No enabled prompts match this pool. Browse another category or add a prompt.'
              : 'The selected prompt pool is exhausted. Reset it or browse another category.'}
          </p>
        )}
        {error && (
          <p className="room-error" role="alert">
            {error}
          </p>
        )}
        {pending && !busy && (
          <button
            className="button"
            disabled={disabled}
            onClick={() => {
              void act(pending.action, pending.choice ?? undefined, pending)
            }}
          >
            Retry game change
          </button>
        )}
      </section>
      {snapshot.youId === snapshot.facilitatorId && (
        <div hidden={view !== undefined && view !== 'prompts'} className="workspace-prompts">
          {game && onViewChange && (
            <div className="current-game-strip">
              <span>
                This or That · Round {game.roundNumber} ·{' '}
                {game.stage === 'responding'
                  ? 'Live'
                  : game.stage === 'revealed'
                    ? 'Revealed'
                    : 'Finished'}
              </span>
              <button className="text-button" onClick={() => onViewChange('game')}>
                Return to game →
              </button>
            </div>
          )}
          <PromptLibraryPanel
            snapshot={snapshot}
            disabled={blocked}
            accept={accept}
            refresh={refresh}
            expanded={view === 'prompts'}
            choose={(selection) => act(game ? 'next' : 'start', undefined, undefined, selection)}
          />
          {view === 'prompts' && error && (
            <p className="room-error" role="alert">
              {error}
            </p>
          )}
          {view === 'prompts' && pending && !busy && (
            <button
              className="button"
              disabled={disabled}
              onClick={() => {
                void act(pending.action, pending.choice ?? undefined, pending)
              }}
            >
              Retry game change
            </button>
          )}
        </div>
      )}
    </>
  )
}

function RoundResults({ results }: { results: GameView['results'] }) {
  return (
    <div className="game-results" aria-label="Round results">
      {results.map((result) => (
        <article key={result.roundId}>
          <h2>
            Round {result.number}
            {result.skipped ? ' · Skipped' : ''}
          </h2>
          <p>
            {result.optionA} / {result.optionB}
          </p>
          {!result.skipped && (
            <>
              <p>
                {result.optionA}: {result.a} {result.a === 1 ? 'vote' : 'votes'} ({result.percentA}
                %)
              </p>
              <p>
                {result.optionB}: {result.b} {result.b === 1 ? 'vote' : 'votes'} ({result.percentB}
                %)
              </p>
              <p>
                {result.passes} passed · {result.missing} unanswered
              </p>
              {(result.choices?.length ?? 0) > 0 ? (
                <ul
                  className="revealed-choices"
                  aria-label={`Player choices for round ${result.number}`}
                >
                  {result.choices!.map((choice) => (
                    <li key={choice.participantId}>
                      <strong>{choice.name}</strong>
                      <span>
                        {choice.choice === 'a'
                          ? result.optionA
                          : choice.choice === 'b'
                            ? result.optionB
                            : choice.choice === 'pass'
                              ? 'Passed'
                              : 'No answer'}
                      </span>
                    </li>
                  ))}
                </ul>
              ) : (
                <p className="room-note">
                  Individual choices are unavailable for this earlier round
                </p>
              )}
            </>
          )}
        </article>
      ))}
    </div>
  )
}
