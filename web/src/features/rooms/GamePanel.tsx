import { useRef, useState } from 'react'
import { gameName, gameStageLabel } from './gameNames'
import { StatementForm } from './StatementForm'
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
const views: Record<string, typeof GameSessionPanel> = Object.fromEntries(
  [
    'this-or-that',
    'would-you-rather',
    'never-have-i-ever',
    'most-likely-to',
    'two-truths-and-a-lie',
  ].map((id) => [id, GameSessionPanel]),
)
export function GamePanel(props: GamePanelProps) {
  const View = views[props.snapshot.game?.gameId ?? 'this-or-that']
  return View ? (
    <View {...props} />
  ) : (
    <p>This game needs a newer version of Mingle. Refresh to continue.</p>
  )
}
function GameSessionPanel({
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
  const [selectedGame, setSelectedGame] = useState('this-or-that')
  const gameId = game?.gameId ?? selectedGame
  const title = gameName(gameId)
  const collecting = game?.stage === 'collection'
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
      gameId,
      expectedVersion: snapshot.version,
      sessionId: game?.sessionId,
      roundId: game?.roundId,
      stageRevision: game?.stageRevision ?? 0,
      responseRevision: game?.responseRevision ?? 0,
      ...selection,
    }
    setPending(command)
    try {
      const result = await commandGame(snapshot.id, command)
      accept(result.snapshot)
      setPending(null)
      return result.snapshot
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
  const control = (action: string, label: string) => {
    if (!game?.allowedActions.includes(action)) return null
    return (
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
  }
  const promptGame = gameId !== 'two-truths-and-a-lie'
  const chooseNextPrompt =
    snapshot.youId === snapshot.facilitatorId &&
    onViewChange &&
    promptGame &&
    game?.stage === 'revealed'
  const nextRoundControl = control('next', promptGame ? 'Next random prompt' : 'Next round')
  const controls = game?.allowedActions.some((action) =>
    ['reveal', 'skip', 'next', 'reset', 'finish', 'lobby', 'close-collection', 'reopen'].includes(
      action,
    ),
  ) ? (
    <div className="room-buttons game-controls" role="group" aria-label="Facilitator controls">
      {control('reveal', 'Reveal answers')}
      {control('skip', 'Skip round')}
      {chooseNextPrompt ? (
        <div className="next-round-actions" role="group" aria-label="Next round">
          {nextRoundControl}
          <button className="button secondary-button" onClick={() => onViewChange('prompts')}>
            Choose next prompt
          </button>
        </div>
      ) : (
        nextRoundControl
      )}
      {control('reset', 'Reset pool and draw')}
      {control('finish', 'Finish game')}
      {control('lobby', 'Return to lobby')}
      {control('close-collection', 'Close collection')}
      {control('reopen', 'Reopen collection')}
    </div>
  ) : null
  const browsePrompts = snapshot.youId === snapshot.facilitatorId &&
    onViewChange &&
    gameId !== 'two-truths-and-a-lie' && (
      <button
        className={`${game ? 'text-button' : 'button secondary-button'} browse-prompts`}
        onClick={() => onViewChange('prompts')}
      >
        {game ? 'Browse prompts' : 'Choose first prompt'}
      </button>
    )
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
                ? `${title} has started`
                : `Round ${game.roundNumber} has started`
              : game.stage === 'collection'
                ? 'Private statement collection'
                : game.stage === 'ready'
                  ? 'Collection closed'
                  : game.stage === 'revealed'
                    ? 'Answers revealed · Time to talk'
                    : 'Game finished'}
          </p>
        )}
        {!game && <p className="eyebrow">A LITTLE PLAY, TOGETHER</p>}
        <Heading id="game-heading" tabIndex={-1}>
          {title}
        </Heading>
        <div className={game?.stage === 'responding' || collecting ? 'focused-game-layout' : ''}>
          <div className="game-main">
            {!game ? (
              <>
                {snapshot.youId === snapshot.facilitatorId && (
                  <label className="game-selector">
                    Choose a game
                    <select value={selectedGame} onChange={(e) => setSelectedGame(e.target.value)}>
                      {Object.keys(views).map((id) => (
                        <option key={id} value={id}>
                          {gameName(id)}
                        </option>
                      ))}
                    </select>
                  </label>
                )}
                <p>
                  {gameId === 'two-truths-and-a-lie'
                    ? 'Write two true statements and one lie, then take turns guessing together.'
                    : gameId === 'most-likely-to'
                      ? 'Nominate another teammate and discover the group’s picks. Votes stay anonymous.'
                      : gameId === 'never-have-i-ever'
                        ? 'Find the little adventures you share. Choose I have or I haven’t.'
                        : 'Pick a side, discover what you share, and talk about the differences. Passing is always welcome.'}
                </p>
                <p className="room-note">
                  {gameId === 'this-or-that'
                    ? '200 built-in pairs across eight categories · Play solo or with your team'
                    : gameId === 'two-truths-and-a-lie'
                      ? 'At least two players for guessing · Your lie stays private until reveal'
                      : gameId === 'most-likely-to'
                        ? '50 built-in prompts · At least two players for nominations'
                        : '50 built-in prompts · Play solo or with your team'}
                </p>
                {snapshot.youId === snapshot.facilitatorId ? (
                  <div
                    className="room-buttons game-start-actions"
                    role="group"
                    aria-label="Game setup"
                  >
                    <button
                      className="button"
                      disabled={blocked}
                      onClick={() => {
                        void act('start')
                      }}
                    >
                      {promptGame ? 'Start with random prompt' : `Start ${title}`}
                    </button>
                    {browsePrompts}
                  </div>
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
                      : collecting
                        ? 'Write your statements'
                        : game.stage === 'ready'
                          ? 'Collection closed'
                          : 'All wrapped up'}
                </p>
                {game.stage !== 'responding' && controls}
                {game.notice && (
                  <p role="status" className="room-note">
                    {game.notice}
                  </p>
                )}
                {collecting && (
                  <StatementForm
                    key={`${game.sessionId}:${game.roundId}`}
                    game={game}
                    disabled={blocked}
                    submit={async (submission, responseRevision) =>
                      (await act('submit', undefined, undefined, { submission, responseRevision }))
                        ?.game?.responseRevision
                    }
                    pass={async (responseRevision) =>
                      (await act('answer', 'pass', undefined, { responseRevision }))?.game
                        ?.responseRevision
                    }
                  />
                )}
                {game.stage === 'responding' && (
                  <>
                    <h2 className="game-prompt">{game.prompt}</h2>
                    {game.presenter && (
                      <p className="presenter-name">Presenter: {game.presenter.name}</p>
                    )}
                    {!game.eligible && !game.notice && (
                      <p className="room-note">
                        {game.presenter?.participantId === snapshot.youId
                          ? 'This is your turn. Your teammates are guessing, so you cannot vote.'
                          : (game.notice ??
                            'You joined during this round. You can play from the next round.')}
                      </p>
                    )}
                    <div className="game-choices" role="group" aria-label="Your choice">
                      {(
                        game.options ?? [
                          { value: 'a', label: game.optionA },
                          { value: 'b', label: game.optionB },
                        ]
                      ).map(({ value: choice, label }) => (
                        <button
                          key={choice}
                          className={`choice-button ${game.presenter ? 'statement-choice' : ''} ${game.ownChoice === choice ? 'is-selected' : ''}`}
                          aria-pressed={game.ownChoice === choice}
                          disabled={blocked || !game.allowedActions.includes('answer')}
                          onClick={() => {
                            void act('answer', choice)
                          }}
                        >
                          {game.presenter && (
                            <span className="statement-number" aria-hidden="true">
                              {choice}
                            </span>
                          )}
                          <span>{label}</span>
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
                {['revealed', 'finished'].includes(game.stage) && (
                  <RoundResults
                    named={
                      gameId === 'this-or-that' ||
                      (game.stage === 'finished' && gameId !== 'most-likely-to')
                    }
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
                    <RoundResults
                      named={gameId === 'this-or-that'}
                      results={game.results.slice(0, -1)}
                    />
                  </details>
                )}
              </>
            )}
            {game && !chooseNextPrompt && browsePrompts}
          </div>
          {(game?.stage === 'responding' || collecting) && game && (
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
                        {response.hasResponded
                          ? collecting
                            ? 'Responded'
                            : 'Answered'
                          : 'Waiting'}
                      </span>
                    </li>
                  )
                })}
              </ul>
              {game.responses &&
                snapshot.participants.some(
                  (p) =>
                    p.id !== game.presenter?.participantId &&
                    !game.responses!.some((r) => r.participantId === p.id),
                ) && (
                  <p className="room-note">
                    Joining next round:{' '}
                    {snapshot.participants
                      .filter(
                        (p) =>
                          p.id !== game.presenter?.participantId &&
                          !game.responses!.some((r) => r.participantId === p.id),
                      )
                      .map((p) => p.name)
                      .join(', ')}
                  </p>
                )}
              <p className="room-note">
                {collecting
                  ? 'Statements stay private until each person’s turn.'
                  : 'Choices stay private until reveal.'}
              </p>
            </aside>
          )}
        </div>
        {gameId !== 'two-truths-and-a-lie' &&
          game?.stage === 'revealed' &&
          game.remainingPrompts === 0 && (
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
                {title} · Round {game.roundNumber} · {gameStageLabel(game.stage)}
              </span>
              <button className="text-button" onClick={() => onViewChange('game')}>
                {game.stage === 'revealed' ? 'Back to results' : 'Return to game →'}
              </button>
            </div>
          )}
          <PromptLibraryPanel
            snapshot={snapshot}
            disabled={blocked}
            accept={accept}
            refresh={refresh}
            expanded={view === 'prompts'}
            choose={async (selection) => {
              await act(game ? 'next' : 'start', undefined, undefined, selection)
            }}
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

function RoundResults({
  results,
  named = true,
}: {
  results: GameView['results']
  named?: boolean
}) {
  return (
    <div className="game-results" aria-label="Round results">
      {results.map((result) => (
        <article key={result.roundId}>
          <h2>
            Round {result.number}
            {result.skipped ? ' · Skipped' : ''}
          </h2>
          <p>
            {result.presenterName
              ? `Presenter: ${result.presenterName}`
              : ['Nominate a teammate', 'I have'].includes(result.optionA)
                ? result.prompt
                : `${result.optionA} / ${result.optionB}`}
          </p>
          {!result.skipped && result.totals && (
            <>
              {result.totals.some((t) => t.winner) && (
                <p className="result-winners">
                  {result.totals
                    .filter((t) => t.winner)
                    .map((t) => t.label)
                    .join(' and ')}{' '}
                  {result.totals.filter((t) => t.winner).length > 1
                    ? 'share the lead'
                    : 'leads the nominations'}
                </p>
              )}
              {result.totals.map((total, index) => (
                <div
                  className={`vote-total ${result.lieIndex === index ? 'is-lie' : ''}`}
                  key={total.value}
                >
                  <strong>{total.label}</strong>
                  {result.lieIndex === index && <span> · This was the lie</span>}
                  <p>
                    {total.count} {result.presenterName ? 'guesses' : 'nominations'} (
                    {total.percent}%)
                  </p>
                </div>
              ))}
              <p>
                {result.passes} passed · {result.missing} unanswered
              </p>
            </>
          )}
          {!result.skipped && !result.totals && (
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
            </>
          )}
          {!result.skipped &&
            ((result.choices?.length ?? 0) > 0 ? (
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
                            : choice.choice === 'missing'
                              ? 'No answer'
                              : (result.totals?.find((total) => total.value === choice.choice)
                                  ?.label ?? 'Choice unavailable')}
                    </span>
                  </li>
                ))}
              </ul>
            ) : !named ? null : (
              <p className="room-note">Individual choices are unavailable for this earlier round</p>
            ))}
        </article>
      ))}
    </div>
  )
}
