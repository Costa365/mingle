import { describe, expect, it, vi } from 'vitest'
import { newerSnapshot, parseSnapshot } from './rooms'
import { room } from '../test/room'

describe('room transport and ordering', () => {
  it('accepts the explicit server snapshot contract', () =>
    expect(parseSnapshot(room)).toEqual(room))
  it.each([
    null,
    {},
    { ...room, protocolVersion: 2 },
    { ...room, code: 234567 },
    { ...room, version: -1 },
    { ...room, participants: [room.participants[0], room.participants[0]] },
    { ...room, youId: 'bad' },
    { ...room, allowedActions: ['admin'] },
  ])('rejects incompatible room views %j', (value) => {
    expect(() => parseSnapshot(value)).toThrow('could not be read')
  })
  it('never rolls back durable state and orders transient presence separately', () => {
    expect(newerSnapshot(room, { ...room, version: 1, presenceRevision: 100 })).toBe(room)
    expect(newerSnapshot(room, { ...room, presenceRevision: 0 })).toBe(room)
    expect(newerSnapshot(room, { ...room, version: 3, presenceRevision: 0 }).version).toBe(3)
    expect(newerSnapshot(null, room)).toBe(room)
  })
  it('accepts a new server presence epoch only after authoritative resynchronization', () => {
    const restarted = { ...room, presenceEpoch: crypto.randomUUID(), presenceRevision: 0 }
    expect(newerSnapshot(room, restarted)).toBe(room)
    expect(newerSnapshot(room, restarted, true)).toBe(restarted)
    expect(newerSnapshot(room, { ...restarted, version: 1 }, true)).toBe(room)
  })
  it('refreshes antiforgery without changing a retried command', async () => {
    vi.resetModules()
    const { commandRoom } = await import('./rooms')
    const fetchMock = vi.fn()
    for (const response of [
      Response.json({ requestToken: 'anonymous' }),
      Response.json({ requestToken: 'guest' }),
      Response.json({ code: 'csrf_invalid', message: 'Refresh' }, { status: 400 }),
      Response.json({ requestToken: 'anonymous-new' }),
      Response.json({ requestToken: 'guest-new' }),
      Response.json({ snapshot: room, appliedVersion: room.version, replayed: true }),
    ])
      fetchMock.mockResolvedValueOnce(response)
    vi.stubGlobal('fetch', fetchMock)
    const command = {
      commandId: crypto.randomUUID(),
      expectedVersion: room.version,
      action: 'lock',
      locked: true,
    }
    expect((await commandRoom(room.id, command)).replayed).toBe(true)
    expect(fetchMock.mock.calls[2][1].body).toBe(fetchMock.mock.calls[5][1].body)
    expect(fetchMock.mock.calls[5][1].headers['X-CSRF-TOKEN']).toBe('guest-new')
  })
})

describe('game snapshot boundary', () => {
  const game = {
    sessionId: '50000000-0000-0000-0000-000000000001',
    gameId: 'this-or-that',
    roundId: '50000000-0000-0000-0000-000000000002',
    roundNumber: 1,
    stageRevision: 1,
    stage: 'responding',
    prompt: 'Choose',
    optionA: 'Tea',
    optionB: 'Coffee',
    eligibleCount: 2,
    responseCount: 0,
    eligible: true,
    ownChoice: null,
    responseRevision: 0,
    allowedActions: ['answer'],
    results: [],
  }
  it('validates M4 options, private statements and cleared choices after a stage closes', () => {
    const statements = { statements: ['First', 'Second', 'Third'], lieIndex: 1 }
    const extended = {
      ...game,
      gameId: 'two-truths-and-a-lie',
      stage: 'collection',
      options: [],
      ownSubmission: statements,
      allowedActions: ['submit', 'answer', 'close-collection'],
    }
    expect(
      parseSnapshot({ ...room, status: 'playing', game: extended }).game?.ownSubmission,
    ).toEqual(statements)
    for (const invalid of [
      { ...extended, ownSubmission: { ...statements, lieIndex: 3 } },
      { ...extended, ownSubmission: { ...statements, statements: ['Only one'] } },
      {
        ...extended,
        presenter: { participantId: 'bad', name: 'Player', statements: statements.statements },
      },
      { ...extended, options: [{ value: '1', label: '' }] },
      { ...extended, stage: 'finished', ownChoice: '3' },
    ])
      expect(() => parseSnapshot({ ...room, status: 'playing', game: invalid })).toThrow()
    expect(
      parseSnapshot({
        ...room,
        status: 'playing',
        game: { ...extended, stage: 'finished', ownChoice: null },
      }).game?.ownChoice,
    ).toBeNull()
  })
  it('accepts a private game view and rejects missing or malformed state', () => {
    expect(parseSnapshot({ ...room, status: 'playing', game }).game).toEqual(game)
    for (const invalid of [
      undefined,
      { ...game, stage: 'unknown' },
      { ...game, ownChoice: 'secret' },
      { ...game, responseCount: 3 },
      { ...game, allowedActions: ['admin'] },
      { ...game, results: [null] },
      { ...game, sessionId: 'bad' },
    ])
      expect(() => parseSnapshot({ ...room, status: 'playing', game: invalid })).toThrow()
  })
  it('validates named response status against counts, unique IDs and room membership', () => {
    const responses = room.participants.map((p) => ({ participantId: p.id, hasResponded: false }))
    expect(
      parseSnapshot({ ...room, status: 'playing', game: { ...game, responses } }).game?.responses,
    ).toEqual(responses)
    for (const invalid of [
      'not a list',
      [null],
      responses.slice(0, 1),
      [responses[0], responses[0]],
      [responses[0], { ...responses[1], participantId: crypto.randomUUID() }],
      [responses[0], { ...responses[1], hasResponded: 'yes' }],
      [responses[0], { ...responses[1], hasResponded: true }],
    ])
      expect(() =>
        parseSnapshot({ ...room, status: 'playing', game: { ...game, responses: invalid } }),
      ).toThrow()
  })
})

it('validates named choices in revealed result payloads', () => {
  const game = {
    sessionId: '50000000-0000-0000-0000-000000000001',
    gameId: 'this-or-that',
    roundId: '50000000-0000-0000-0000-000000000002',
    roundNumber: 1,
    stageRevision: 2,
    stage: 'revealed',
    prompt: 'Choose',
    optionA: 'Tea',
    optionB: 'Coffee',
    eligibleCount: 1,
    responseCount: 1,
    eligible: true,
    ownChoice: 'a',
    responseRevision: 1,
    allowedActions: [],
    results: [] as unknown[],
  }
  const choice = { participantId: room.youId, name: 'Alex', choice: 'a' }
  const result = {
    roundId: game.roundId,
    number: 1,
    prompt: 'Choose',
    optionA: 'Tea',
    optionB: 'Coffee',
    skipped: false,
    a: 1,
    b: 0,
    passes: 0,
    missing: 0,
    percentA: 100,
    percentB: 0,
    choices: [choice],
  }
  expect(
    parseSnapshot({ ...room, status: 'playing', game: { ...game, results: [result] } }).game
      ?.results[0].choices,
  ).toEqual([choice])
  for (const invalid of [
    { ...choice, choice: 'secret' },
    { ...choice, participantId: 'bad' },
    { ...choice, name: '' },
  ])
    expect(() =>
      parseSnapshot({
        ...room,
        status: 'playing',
        game: { ...game, results: [{ ...result, choices: [invalid] }] },
      }),
    ).toThrow()
})
