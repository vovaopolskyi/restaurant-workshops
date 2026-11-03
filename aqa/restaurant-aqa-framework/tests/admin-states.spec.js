import { test, expect, expectError } from '../support/fixtures.js';
import { withSessionState } from '../support/admin-fixtures.js';

test.describe('Controlled states unavailable through normal guest API', () => {
  test.skip(process.env.ENABLE_ADMIN_FIXTURES !== 'true', 'Requires explicit ENABLE_ADMIN_FIXTURES=true and scoped AWS fixture credentials.');

  test('@L1 @api @admin A genuinely empty group returns an empty snapshot', async ({ scenario: s }) => {
    const { guestSessionId } = await s.owner.json('GET', `/reservations/${s.reservationId}/guest-session-id`);
    await withSessionState(s, guestSessionId, () => null, async () => {
      await expect.poll(async () => await s.snapshot()).toEqual({ participants: [], total: 0 });
    });
  });

  test('@L2 @api @admin Submitted sessions without dishes cannot be consolidated', async ({ scenario: s }) => {
    const guest = await s.addGuest('Empty Submitted Guest');
    await withSessionState(s, guest.id, record => ({ ...record, dishes: { L: [] }, isSubmitted: { BOOL: true } }), async () => {
      await expect.poll(async () => (await s.snapshot()).participants.find(p => p.id === guest.id)?.isSubmitted).toBe(true);
      await expectError(await s.owner.submit(s.groupId, s.reservationId), 400, 'Submitted guest sessions contain no dishes.');
      expect((await s.owner.groupStatus(s.groupId)).status).toBe('Open');
    });
  });
});
