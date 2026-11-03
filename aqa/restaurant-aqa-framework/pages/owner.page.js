import { expect } from '@playwright/test';
import { required } from '../support/env.js';

function escapeRegex(value) {
  return String(value).replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function amountFromText(text) {
  const numeric = text.normalize('NFKC').replace(/[^\d.,-]/g, '');
  if (!/\d/.test(numeric)) throw new Error('Expected a numeric amount in the UI');
  const lastComma = numeric.lastIndexOf(',');
  const lastDot = numeric.lastIndexOf('.');
  let normalized = numeric;
  if (lastComma !== -1 && lastDot !== -1) {
    normalized = lastComma > lastDot
      ? numeric.replace(/\./g, '').replace(',', '.')
      : numeric.replace(/,/g, '');
  } else if (lastComma !== -1) {
    normalized = numeric.length - lastComma - 1 === 2
      ? numeric.replace(',', '.')
      : numeric.replace(/,/g, '');
  }
  const result = Number(normalized);
  if (!Number.isFinite(result)) throw new Error('Could not parse the displayed amount');
  return result;
}

export class OwnerPage {
  constructor(page) {
    this.page = page;
    this.heading = page.getByRole('heading', { name: 'Group Pre-Order', exact: true });
    this.refreshButton = page.getByRole('button', { name: 'Refresh', exact: true });
    this.grandTotal = page.locator('footer .total strong');
  }

  async open(reservationId) {
    await this.page.goto(new URL(`/reservations/${encodeURIComponent(reservationId)}/group-order`, required('BASE_URL')).href);
    await expect(this.heading).toBeVisible();
    await expect(this.refreshButton).toBeVisible();
  }

  card(name) {
    const title = name === 'Owner' || name === 'My Pre-Order'
      ? /^My Pre-Order$/
      : new RegExp(`^(?:Guest|Owner): ${escapeRegex(name)}$`);
    return this.page.locator('app-participant-card').filter({
      has: this.page.getByRole('heading', { name: title }),
    });
  }

  async refresh() {
    const refreshed = this.page.waitForResponse((response) =>
      response.request().method() === 'GET' &&
      /\/guest\/groups\/[^/]+\/sessions$/.test(new URL(response.url()).pathname));
    await this.refreshButton.click();
    expect((await refreshed).ok(), 'Owner snapshot refresh should succeed').toBe(true);
    await expect(this.refreshButton).toBeEnabled();
  }

  async remove(name) {
    const card = this.card(name);
    await expect(card).toHaveCount(1);
    await card.getByRole('button', { name: `Remove ${name}`, exact: true }).click();
    const dialog = this.page.getByRole('dialog', { name: 'Remove Guest', exact: true });
    await expect(dialog).toBeVisible();
    await dialog.getByRole('button', { name: 'Remove Guest', exact: true }).click();
    await expect(dialog).toBeHidden();
    await expect(card).toHaveCount(0);
  }

  async submit() {
    await this.page.getByRole('button', { name: 'Submit Pre-Order', exact: true }).click();
    const dialog = this.page.getByRole('dialog', { name: 'Submit Pre-Order', exact: true });
    await expect(dialog).toBeVisible();
    await dialog.getByRole('button', { name: 'Submit', exact: true }).click();
    await expect(dialog).toBeHidden();
    await this.expectSent();
  }

  async expectParticipant(name, { dishName, quantity, total, submitted } = {}) {
    const card = this.card(name);
    await expect(card).toHaveCount(1);
    await expect(card).toBeVisible();
    if (submitted !== undefined) {
      await expect(card.locator('.chip')).toHaveText(submitted ? 'Submitted' : 'Still choosing');
    }
    if (total !== undefined) {
      await expect.poll(async () => amountFromText(await card.locator('.subtotal span').last().innerText()))
        .toBeCloseTo(Number(total), 2);
    }
    if (dishName !== undefined) {
      const row = card.locator('.items li').filter({
        has: this.page.locator('.name').filter({ hasText: new RegExp(`^${escapeRegex(dishName)}$`) }),
      });
      await expect(row).toHaveCount(1);
      await expect(row.locator('.name')).toHaveText(dishName);
      if (quantity !== undefined) {
        await expect(row.locator('.quantity')).toHaveText(new RegExp(`^\\s*×\\s*${escapeRegex(quantity)}\\s*$`));
      }
    }
  }

  async expectGrandTotal(amount) {
    await expect.poll(async () => amountFromText(await this.grandTotal.innerText()))
      .toBeCloseTo(Number(amount), 2);
  }

  async expectSent() {
    await expect(this.page.locator('header.page-header .status')).toHaveText('Sent to Restaurant');
    await expect(this.page.locator('footer .finished'))
      .toHaveText('Your group pre-order has been sent to the restaurant.');
    await expect(this.page.getByRole('button', { name: 'Submit Pre-Order', exact: true })).toHaveCount(0);
  }
}
