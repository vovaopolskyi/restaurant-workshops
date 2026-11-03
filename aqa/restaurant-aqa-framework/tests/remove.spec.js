import { randomUUID } from 'node:crypto';
import { test, expect, expectError, waitForGuest } from '../support/fixtures.js';
import { createScenario } from '../support/scenario.js';
import { LoginPage } from '../pages/login.page.js';
import { WaiterPage } from '../pages/waiter.page.js';

for (const submitted of [false, true]) {
  test(`@L3 @ui Removing a ${submitted ? 'submitted' : 'draft'} guest updates the list and total`, async ({ scenario: s, ownerPage: ui }) => {
    const removed = await s.addGuest('Removed Guest', [{ dishId: s.dish.id, quantity: 2 }], submitted);
    await s.addGuest('Remaining Guest', [{ dishId: s.secondDish.id, quantity: 1 }], true);
    await waitForGuest(s, removed);
    await ui.open(s.reservationId);
    await ui.remove('Removed Guest');
    await expect(ui.card('Removed Guest')).toHaveCount(0);
    await ui.expectParticipant('Remaining Guest', { dishName: s.secondDish.name, quantity: 1, total: s.secondDish.price, submitted: true });
    await ui.expectGrandTotal(s.secondDish.price);
    const snapshot = await s.snapshot();
    expect(snapshot.participants.map(p => p.id)).not.toContain(removed.id);
    expect(snapshot.total).toBeCloseTo(s.secondDish.price, 2);
  });
}

test('@L3 @ui Removing the last guest leaves the protected Owner and a zero total', async ({ scenario: s, ownerPage: ui }) => {
  await s.addGuest('Last Guest', [{ dishId: s.dish.id, quantity: 1 }], true);
  await ui.open(s.reservationId);
  await ui.remove('Last Guest');
  await expect(ui.card('Last Guest')).toHaveCount(0);
  await ui.expectGrandTotal(0);
  const snapshot = await s.snapshot();
  expect(snapshot.participants).toHaveLength(1);
  expect(snapshot.participants[0].type).toBe('Owner');
  expect(snapshot.total).toBe(0);
});

test('@L3 @api A guest belonging to another group cannot be removed', async ({ scenario: s, request }, testInfo) => {
  const otherGroup = await createScenario(request, testInfo, { order: false });
  try {
    const foreign = await otherGroup.addGuest('Foreign Guest', [{ dishId: otherGroup.dish.id, quantity: 1 }]);
    const before = await otherGroup.snapshot();
    await expectError(await s.owner.deleteSession(s.groupId, foreign.id), 400, 'Guest session does not belong to this group reservation.');
    expect(await otherGroup.snapshot()).toEqual(before);
  } finally { await otherGroup.dispose(); }
});

test('@L3 @api A nonexistent guest returns 404 without changing the group', async ({ scenario: s }) => {
  const before = await s.snapshot();
  await expectError(await s.owner.deleteSession(s.groupId, randomUUID()), 404, 'was not found');
  expect(await s.snapshot()).toEqual(before);
});

test('@L3 @ui Owner removes a guest, submits, and Waiter sees only retained dishes', async ({ scenario: s, ownerPage: ui, browser }) => {
  test.setTimeout(180_000);
  await s.addGuest('Removed Guest', [{ dishId: s.secondDish.id, quantity: 5 }], true);
  await s.addGuest('Kept Guest', [{ dishId: s.dish.id, quantity: 2 }], true);
  await ui.open(s.reservationId);
  await ui.remove('Removed Guest');
  await ui.submit();
  await ui.expectSent();
  await expect.poll(async () => (await s.owner.order(s.reservationId)).status).toBe(3);
  const order = await s.owner.order(s.reservationId);
  expect(order.orderItems).toHaveLength(1);
  expect(order.orderItems[0].dish.id).toBe(s.dish.id);
  expect(order.orderItems[0].quantity).toBe(2 + s.seedQuantity);
  const waiter = await s.login('WAITER');
  const context = await browser.newContext();
  try {
    const page = await context.newPage();
    await new LoginPage(page).login(waiter.credentials.email, waiter.credentials.password);
    const waiterUI = new WaiterPage(page);
    await waiterUI.open(s);
    await waiterUI.expectDish(s.dish.name, 2 + s.seedQuantity);
    await expect(page.locator('.preorder-list app-dish-card').filter({ hasText: s.secondDish.name })).toHaveCount(0);
  } finally { await context.close(); }
});
