import { useState } from 'react'
import type { GameView } from '../../api/rooms'

type Submission = { statements: string[]; lieIndex: number }
export function StatementForm({
  game,
  disabled,
  submit,
  pass,
}: {
  game: GameView
  disabled: boolean
  submit: (submission: Submission, revision: number) => Promise<number | undefined>
  pass: (revision: number) => Promise<number | undefined>
}) {
  const [draft, setDraft] = useState(() => ({
    statements: game.ownSubmission?.statements ?? ['', '', ''],
    lieIndex: game.ownSubmission?.lieIndex ?? -1,
    revision: game.responseRevision,
    dirty: false,
  }))
  const [error, setError] = useState('')
  function loadSaved() {
    setDraft({
      statements: game.ownSubmission?.statements ?? ['', '', ''],
      lieIndex: game.ownSubmission?.lieIndex ?? -1,
      revision: game.responseRevision,
      dirty: false,
    })
  }
  if (!draft.dirty && draft.revision !== game.responseRevision) loadSaved()
  const blocked = disabled || !game.allowedActions.includes('submit')
  async function save(optOut = false) {
    const revision = optOut
      ? await pass(draft.revision)
      : await submit({ statements: draft.statements, lieIndex: draft.lieIndex }, draft.revision)
    if (revision !== undefined) setDraft((current) => ({ ...current, revision, dirty: false }))
  }
  return (
    <form
      className="statement-form"
      onSubmit={(e) => {
        e.preventDefault()
        if (
          draft.lieIndex < 0 ||
          new Set(draft.statements.map((s) => s.trim().toUpperCase())).size !== 3
        ) {
          setError('Write three different statements and select your lie')
          return
        }
        setError('')
        void save()
      }}
    >
      <p className="room-note">
        Your statements and lie selection stay private until your turn. Only you see your lie
        selection before reveal.
      </p>
      {!game.eligible && (
        <p className="room-note">
          You can contribute when collection reopens, or join the next guessing round.
        </p>
      )}
      {draft.dirty && draft.revision !== game.responseRevision && (
        <p role="status">
          Your saved statements changed in another tab. Your local draft is kept.{' '}
          <button className="text-button" type="button" onClick={loadSaved}>
            Use saved statements
          </button>
        </p>
      )}
      <fieldset disabled={blocked}>
        <legend>Choose which statement is your lie</legend>
        {draft.statements.map((value, index) => (
          <div className="statement-entry" key={index}>
            <label htmlFor={`statement-${index}`}>Statement {index + 1}</label>
            <textarea
              id={`statement-${index}`}
              maxLength={200}
              required
              value={value}
              onChange={(e) =>
                setDraft({
                  ...draft,
                  statements: draft.statements.map((s, i) => (i === index ? e.target.value : s)),
                  dirty: true,
                })
              }
            />
            <label className="lie-label">
              <input
                type="radio"
                name="lie"
                value={index}
                required
                checked={draft.lieIndex === index}
                onChange={() => setDraft({ ...draft, lieIndex: index, dirty: true })}
              />
              Statement {index + 1} is my lie
            </label>
          </div>
        ))}
      </fieldset>
      {error && <p role="alert">{error}</p>}
      <div className="room-buttons">
        <button className="button" disabled={blocked}>
          Save statements
        </button>
        <button
          type="button"
          className="text-button"
          disabled={blocked}
          onClick={() => {
            void save(true)
          }}
        >
          Sit this game out
        </button>
      </div>
      <p role="status" className="room-note">
        {game.ownChoice === 'pass'
          ? 'You are sitting this game out. You can change this before collection closes.'
          : game.ownSubmission
            ? 'Your private response is saved. You can edit until collection closes.'
            : 'Three statements · Up to 200 characters each'}
      </p>
    </form>
  )
}
