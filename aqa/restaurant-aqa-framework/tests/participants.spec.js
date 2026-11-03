import { test, expect, waitForGuest } from '../support/fixtures.js';

test('@L1 @ui Owner sees submitted, draft and empty guests with individual totals', async ({ scenario: s, ownerPage: ui }) => {
  const submitted = await s.addGuest('Submitted Guest', [{ dishId: s.dish.id, quantity: 2 }], true);
  await s.addGuest('Choosing Guest', [{ dishId: s.secondDish.id, quantity: 1 }]);
  await s.addGuest('Empty Guest');
  await waitForGuest(s, submitted);
  await expect.poll(async () => (await s.snapshot()).participants.length).toBe(4);
  await ui.open(s.reservationId);
  await ui.expectParticipant('Submitted Guest', { dishName: s.dish.name, quantity: 2, total: s.dish.price * 2, submitted: true });
  await ui.expectParticipant('Choosing Guest', { dishName: s.secondDish.name, quantity: 1, total: s.secondDish.price, submitted: false });
  await expect(ui.card('Empty Guest')).toContainText('No dishes yet');
  await expect(ui.card('Empty Guest')).toContainText('Still choosing');
  // The UI's Grand Total is the submitted consolidated order; drafts stay on cards.
  await ui.expectGrandTotal(s.dish.price * 2);
  const snapshot = await s.snapshot();
  expect(snapshot.total).toBeCloseTo(s.dish.price * 2 + s.secondDish.price, 2);
  expect(snapshot.total).toBeCloseTo(snapshot.participants.reduce((sum, p) => sum + p.total, 0), 2);
});

test('@L1 @ui Owner refreshes a changed draft cart without reloading the page', async ({ scenario: s, ownerPage: ui }) => {
  const guest = await s.addGuest('Changing Guest', [{ dishId: s.dish.id, quantity: 1 }]);
  await waitForGuest(s, guest);
  await ui.open(s.reservationId);
  await ui.expectParticipant('Changing Guest', { dishName: s.dish.name, quantity: 1, total: s.dish.price, submitted: false });
  await s.owner.addGuestDish(guest, s.dish.id, 2);
  await ui.refresh();
  await ui.expectParticipant('Changing Guest', { dishName: s.dish.name, quantity: 3, total: s.dish.price * 3, submitted: false });
  expect((await s.snapshot()).total).toBeCloseTo(s.dish.price * 3, 2);
});

test('@L1 @ui Fresh group displays the Owner and an empty pre-order', async ({ scenario: s, ownerPage: ui }) => {
  await ui.open(s.reservationId);
  await expect(ui.page.getByRole('heading', { name: 'My Pre-Order', exact: true })).toBeVisible();
  await expect(ui.page.getByText("Participants haven't added any dishes yet", { exact: true })).toBeVisible();
  await ui.expectGrandTotal(0);
  const snapshot = await s.snapshot();
  expect(snapshot.participants).toHaveLength(1);
  expect(snapshot.participants[0].type).toBe('Owner');
  expect(snapshot.total).toBe(0);
});
