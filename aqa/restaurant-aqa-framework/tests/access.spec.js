import { randomUUID } from 'node:crypto';
import { test, expect, expectError } from '../support/fixtures.js';
import { RestaurantApi } from '../support/api.js';

// Each role/action combination owns a separate reservation, so test order and
// failures in another case cannot authorize a later request accidentally.
for (const role of ['ANONYMOUS', 'OTHER', 'GUEST', 'WAITER']) {
  for (const action of ['list', 'owner-session', 'submit', 'remove']) {
    const level = action === 'remove' ? '@L3' : action === 'submit' || action === 'owner-session' ? '@L2' : '@L1';
    test(`${level} @api ${role} cannot ${action} another customer's group`, async ({ scenario: s, request }) => {
      const guest = await s.addGuest('Access Guest');
      const api = role === 'ANONYMOUS' ? new RestaurantApi(request)
        : role === 'GUEST' ? guest.api : await s.login(role);
      const responses = {
        list: () => api.call('GET', `/guest/groups/${s.groupId}/sessions`),
        'owner-session': () => api.call('GET', `/reservations/${s.reservationId}/guest-session-id`),
        submit: () => api.call('POST', `/group-reservations/${s.groupId}/submit`, { reservationId: s.reservationId }),
        remove: () => api.call('DELETE', `/guest/groups/${s.groupId}/sessions/${guest.id}`),
      };
      const response = await responses[action]();
      // Cognito rejects guest/anonymous tokens before Lambda. Signed-in users
      // with the wrong ownership reach the domain's explicit 403 response.
      expect(response.status()).toBe(['ANONYMOUS', 'GUEST'].includes(role) ? 401 : 403);
      expect((await s.snapshot()).participants.some(p => p.id === guest.id)).toBe(true);
      expect((await s.owner.groupStatus(s.groupId)).status).toBe('Open');
    });
  }
}

test('@L1 @api A nonexistent group returns 404', async ({ scenario: s }) => {
  const response = await s.owner.call('GET', `/guest/groups/${randomUUID()}/sessions`);
  expect(response.status()).toBe(404);
});

test('@L2 @api A nonexistent reservation has no Owner session', async ({ scenario: s }) => {
  const response = await s.owner.call('GET', `/reservations/${randomUUID()}/guest-session-id`);
  expect(response.status()).toBe(404);
});

test('@L2 @api A nonexistent group cannot be submitted', async ({ scenario: s }) => {
  const response = await s.owner.submit(randomUUID(), s.reservationId);
  expect(response.status()).toBe(404);
});

test('@L2 @api A nonexistent reservation cannot be submitted', async ({ scenario: s }) => {
  const response = await s.owner.submit(s.groupId, randomUUID());
  expect(response.status()).toBe(404);
});

test('@L2 @api Owner session is identified and protected from removal', async ({ scenario: s }) => {
  const { guestSessionId } = await s.owner.json('GET', `/reservations/${s.reservationId}/guest-session-id`);
  expect((await s.snapshot()).participants.find(p => p.id === guestSessionId)?.type).toBe('Owner');
  await expectError(await s.owner.deleteSession(s.groupId, guestSessionId), 400, 'The owner session cannot be removed');
});
