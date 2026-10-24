import { useRef, useState } from 'react'
import { commandGame, RoomError } from '../../api/rooms'
import type { GameCommand, RoomSnapshot } from '../../api/rooms'

interface GamePanelProps {
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
function ThisOrThatPanel({ snapshot, disabled, accept, refresh }: GamePanelProps) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [pending, setPending] = useState<GameCommand | null>(null)
  const sending = useRef(false)
  const game = snapshot.game
  async function act(action: string, choice?: string, retry?: GameCommand) {
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
        className="button"
        disabled={blocked}
        onClick={() => {
          void act(action)
        }}
      >
        {label}
      </button>
    )
  return (
    <section className="game-panel" aria-labelledby="game-heading">
      <p className="eyebrow">A LITTLE PLAY, TOGETHER</p>
      <h2 id="game-heading">This or That</h2>
      {!game ? (
        <>
          <p>
            Pick a side, discover what you share, and talk about the differences. Passing is always
            welcome.
          </p>
          <p className="room-note">A starter deck of 12 pairs · Play solo or with your team</p>
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
          {game.stage === 'responding' && (
            <>
              <h3>{game.prompt}</h3>
              <p role="status">
                {game.responseCount} of {game.eligibleCount} responded
              </p>
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
                    ['pass', 'Pass this round'],
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
              <p className="room-note">
                {game.ownChoice
                  ? 'Your answer is saved. You can change it until reveal.'
                  : 'Your choice stays private until reveal.'}
              </p>
            </>
          )}
          {game.stage === 'finished' && <h3>Thanks for playing</h3>}
          {game.stage !== 'responding' && (
            <div className="game-results" aria-label="Round results">
              {game.results.map((result) => (
                <article key={result.roundId}>
                  <h3>
                    Round {result.number}
                    {result.skipped ? ' · Skipped' : ''}
                  </h3>
                  <p>
                    {result.optionA} / {result.optionB}
                  </p>
                  {!result.skipped && (
                    <>
                      <p>
                        {result.optionA}: {result.a} {result.a === 1 ? 'vote' : 'votes'} (
                        {result.percentA}%)
                      </p>
                      <p>
                        {result.optionB}: {result.b} {result.b === 1 ? 'vote' : 'votes'} (
                        {result.percentB}%)
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
          )}
          <div className="room-buttons game-controls">
            {control('reveal', 'Reveal answers')}
            {control('skip', 'Skip round')}
            {control('next', 'Next round')}
            {control('finish', 'Finish game')}
            {control('lobby', 'Return to lobby')}
          </div>
          {game.allowedActions.includes('reveal') && (
            <p className="room-note">
              You can reveal or move on at any time, even with no answers.
            </p>
          )}
          {game.stage === 'revealed' && snapshot.youId !== snapshot.facilitatorId && (
            <p>Waiting for your facilitator to continue</p>
          )}
        </>
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
  )
}
