import { useState } from 'react'
import type { PromptInput, PromptView } from '../../api/rooms'
import { gameNames } from '../rooms/gameNames'

export function PromptEditor({
  prompt,
  busy,
  save,
  cancel,
}: {
  prompt?: PromptView
  busy: boolean
  save: (input: PromptInput) => void
  cancel: () => void
}) {
  const [gameId, setGame] = useState(prompt?.gameId ?? 'this-or-that')
  const [category, setCategory] = useState(prompt?.category ?? '')
  const [tags, setTags] = useState(prompt?.tags.join(', ') ?? '')
  const [text, setText] = useState(prompt?.text ?? '')
  const [a, setA] = useState(prompt?.optionA ?? '')
  const [b, setB] = useState(prompt?.optionB ?? '')
  const [enabled, setEnabled] = useState(prompt?.enabled ?? true)
  const binary = ['this-or-that', 'would-you-rather'].includes(gameId)
  return (
    <form
      className="admin-card admin-editor"
      aria-label={prompt ? 'Edit shared prompt' : 'Create shared prompt'}
      onSubmit={(e) => {
        e.preventDefault()
        save({
          gameId,
          category,
          tags: tags
            .split(',')
            .map((t) => t.trim())
            .filter(Boolean),
          text,
          optionA: binary ? a : '',
          optionB: binary ? b : '',
          enabled,
        })
      }}
    >
      <h2>{prompt ? 'Edit shared prompt' : 'Create shared prompt'}</h2>
      <label>
        Game
        <select value={gameId} onChange={(e) => setGame(e.target.value)}>
          {Object.entries(gameNames)
            .filter(([id]) => id !== 'two-truths-and-a-lie')
            .map(([id, name]) => (
              <option key={id} value={id}>
                {name}
              </option>
            ))}
        </select>
      </label>
      <label>
        Category
        <input
          required
          maxLength={48}
          value={category}
          onChange={(e) => setCategory(e.target.value)}
        />
      </label>
      <label>
        Tags, separated by commas
        <input value={tags} onChange={(e) => setTags(e.target.value)} maxLength={270} />
      </label>
      <label>
        Prompt text
        <textarea required maxLength={240} value={text} onChange={(e) => setText(e.target.value)} />
      </label>
      {binary && (
        <>
          <label>
            Option A
            <input required maxLength={120} value={a} onChange={(e) => setA(e.target.value)} />
          </label>
          <label>
            Option B
            <input required maxLength={120} value={b} onChange={(e) => setB(e.target.value)} />
          </label>
        </>
      )}
      <label className="admin-check">
        <input type="checkbox" checked={enabled} onChange={(e) => setEnabled(e.target.checked)} />
        Enabled
      </label>
      <div className="admin-actions">
        <button className="button" disabled={busy}>
          Save shared prompt
        </button>
        <button type="button" className="text-button" disabled={busy} onClick={cancel}>
          Cancel
        </button>
      </div>
    </form>
  )
}
