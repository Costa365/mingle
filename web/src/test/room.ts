import type { RoomSnapshot } from '../api/rooms'
export const room: RoomSnapshot = {
  protocolVersion: 1,
  id: '11111111-1111-4111-8111-111111111111',
  code: 'ABC234',
  version: 2,
  presenceRevision: 1,
  presenceEpoch: '44444444-4444-4444-8444-444444444444',
  status: 'lobby',
  joinLocked: false,
  capacity: 20,
  youId: '22222222-2222-4222-8222-222222222222',
  facilitatorId: '22222222-2222-4222-8222-222222222222',
  participants: [
    {
      id: '22222222-2222-4222-8222-222222222222',
      name: 'Host',
      isFacilitator: true,
      isOnline: true,
    },
    {
      id: '33333333-3333-4333-8333-333333333333',
      name: 'Player',
      isFacilitator: false,
      isOnline: true,
    },
  ],
  allowedActions: ['lock', 'remove', 'transfer', 'close'],
}
