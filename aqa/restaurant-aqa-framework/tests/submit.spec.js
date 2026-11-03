import { test, expect, expectError, waitForGuest } from '../support/fixtures.js';
import { login } from '../support/api.js';
import { LoginPage } from '../pages/login.page.js';
import { WaiterPage } from '../pages/waiter.page.js';

test('@L2 @ui Owner consolidates submitted dishes and the Waiter sees the final order', async ({ scenario: s, ownerPage: ui, browser, request }) => {
  await s.addGuest('First Guest', [{ dishId: s.dish.id, quantity: 2 }], true);
  const guest = await s.addGuest('Second Guest', [{ dishId: s.dish.id, quantity: 3 }, { dishId: s.secondDish.id, quantity: 1 }], true);
  await s.addGuest('Draft Guest', [{ dishId: s.secondDish.id, quantity: 9 }]);
  await waitForGuest(s, guest);
  await expect.poll(async () => (await s.snapshot()).participants.length).toBe(4);
  await ui.open(s.reservationId);
  await ui.expectGrandTotal(s.dish.price * 5 + s.secondDish.price);
  await ui.submit();
  await ui.expectSent();
  const order = await s.owner.order(s.reservationId);
  expect(order.status).toBe(3);
  expect(order.orderItems.map(i => ({ id: i.dish.id, quantity: i.quantity })).sort((a,b) => a.id.localeCompare(b.id)))
    .toEqual([{ id: s.dish.id, quantity: 5 + s.seedQuantity }, { id: s.secondDish.id, quantity: 1 }].sort((a,b) => a.id.localeCompare(b.id)));
  expect((await s.owner.groupStatus(s.groupId)).status).toBe('Submitted');
  const waiter = await login(request, 'WAITER');
  expect((await waiter.order(s.reservationId)).orderItems).toEqual(order.orderItems);
  const context = await browser.newContext();
  try {
    const page = await context.newPage();
    await new LoginPage(page).login(waiter.credentials.email, waiter.credentials.password);
    const waiterUI = new WaiterPage(page);
    await waiterUI.open(s);
    await waiterUI.expectDish(s.dish.name, 5 + s.seedQuantity);
    await waiterUI.expectDish(s.secondDish.name, 1);
  } finally { await context.close(); }
});

test('@L2 @ui Submission is disabled while all guests are still choosing', async ({ scenario: s, ownerPage: ui }) => {
  const guest = await s.addGuest('Draft Guest', [{ dishId: s.dish.id, quantity: 1 }]);
  await waitForGuest(s, guest);
  await ui.open(s.reservationId);
  await expect(ui.page.getByRole('button', { name: 'Submit Pre-Order', exact: true })).toBeDisabled();
  await expectError(await s.owner.submit(s.groupId, s.reservationId), 400, 'There are no submitted guest sessions.');
});

test('@L2 @api Repeating submit returns 400 and does not duplicate quantities', async ({ scenario: s }) => {
  const guest = await s.addGuest('Submitted Guest', [{ dishId: s.dish.id, quantity: 2 }], true);
  await waitForGuest(s, guest);
  const submitted = await s.owner.submit(s.groupId, s.reservationId);
  expect(submitted.status()).toBe(200);
  const before = await s.owner.order(s.reservationId);
  await expectError(await s.owner.submit(s.groupId, s.reservationId), 400, 'Only an open group reservation');
  expect(await s.owner.order(s.reservationId)).toEqual(before);
});

test.describe('Missing order', () => {
  test.use({ seedOrder: false });
  test('@L2 @api Submitted carts cannot be sent without an order', async ({ scenario: s }) => {
    const guest = await s.addGuest('Submitted Guest', [{ dishId: s.dish.id, quantity: 1 }], true);
    await waitForGuest(s, guest);
    await expectError(await s.owner.submit(s.groupId, s.reservationId), 400, 'Order was not found for this reservation.');
    expect((await s.owner.groupStatus(s.groupId)).status).toBe('Open');
  });
});
