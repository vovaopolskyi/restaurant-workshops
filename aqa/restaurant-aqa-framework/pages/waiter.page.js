import { expect } from '@playwright/test';
import { required } from '../support/env.js';

function escapeRegex(value) {
  return String(value).replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

export class WaiterPage {
  constructor(page) {
    this.page = page;
  }

  async open(reservationId, details = {}) {
    const input = typeof reservationId === 'object'
      ? reservationId
      : { ...details, reservationId };
    const reservation = { ...(input.reservation ?? {}), ...input };
    const id = reservation.reservationId || reservation.id;
    if (!id || !reservation.startTime) {
      throw new Error('WaiterPage.open needs reservationId and unique startTime to select the actual reservation card');
    }
    await this.page.goto(new URL('/waiter-reservations', required('BASE_URL')).href);
    // DatePipe uses the browser timezone; derive labels in that same browser context.
    const labels = await this.page.evaluate((startTime) => {
      const date = new Date(startTime);
      return {
        date: new Intl.DateTimeFormat('en-US', { year: 'numeric', month: 'short', day: 'numeric' }).format(date),
        time: new Intl.DateTimeFormat('en-US', { hour: 'numeric', minute: '2-digit', hour12: true }).format(date),
      };
    }, reservation.startTime);
    const timePattern = new RegExp(escapeRegex(labels.time).replace(/\s+/g, '\\s*'));
    let card = this.page.locator('app-card-reservation')
      .filter({ hasText: labels.date })
      .filter({ hasText: timePattern });
    if (reservation.locationAddress) card = card.filter({ hasText: reservation.locationAddress });
    if (reservation.tableName) {
      const tablePattern = new RegExp(`^\\s*${escapeRegex(reservation.tableName)}\\s*$`);
      card = card.filter({
        has: this.page.locator('app-custom-select.table-select .selected-value').filter({ hasText: tablePattern }),
      });
    }
    if (reservation.ownerName) card = card.filter({ hasText: reservation.ownerName });
    await expect(card, 'Select one reservation by its unique fixture date/time and table').toHaveCount(1);
    const orderLoaded = this.page.waitForResponse((response) =>
      response.request().method() === 'GET' &&
      new URL(response.url()).pathname.endsWith(`/orders/${encodeURIComponent(id)}`));
    await card.getByRole('button', { name: 'Pre-order', exact: true }).click();
    expect((await orderLoaded).ok(), 'Waiter should load this reservation order').toBe(true);
    await expect(this.page).toHaveURL(/\/waiter-pre-order(?:[?#].*)?$/);
    await expect(this.page.locator('h2.title')).toContainText('Pre-order of');
  }

  async expectDish(name, quantity) {
    const card = this.page.locator('.preorder-list app-dish-card').filter({
      has: this.page.locator('.dish-name').filter({ hasText: new RegExp(`^${escapeRegex(name)}$`) }),
    });
    await expect(card).toHaveCount(1);
    await expect(card.locator('.dish-name')).toHaveText(name);
    await expect(card.locator('.pre-order-quantity')).toHaveText(`Quantity: ${quantity}`);
  }
}
