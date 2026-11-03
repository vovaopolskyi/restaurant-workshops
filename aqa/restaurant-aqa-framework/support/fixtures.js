import { test as base, expect } from '@playwright/test';
import { createScenario } from './scenario.js';
import { OwnerPage } from '../pages/owner.page.js';
import { LoginPage } from '../pages/login.page.js';

export const test = base.extend({
  seedOrder: [true, { option: true }],
  scenario: async ({ request, seedOrder }, use, testInfo) => {
    const scenario = await createScenario(request, testInfo, { order: seedOrder });
    try { await use(scenario); }
    finally { await scenario.dispose(); }
  },
  ownerPage: async ({ page, scenario }, use) => {
    await new LoginPage(page).login(scenario.owner.credentials.email, scenario.owner.credentials.password);
    const ownerPage = new OwnerPage(page);
    await use(ownerPage);
  },
});
export { expect };

export async function expectError(response, status, message) {
  expect(response.status()).toBe(status);
  const body = await response.json();
  expect(body.error || body.message || '').toContain(message);
}

export async function waitForGuest(scenario, guest) {
  // DynamoDB's group index is eventually consistent. Retry observations only,
  // never repeat a successful mutation (especially consolidated submission).
  await expect.poll(async () => (await scenario.snapshot()).participants.some(p => p.id === guest.id)).toBe(true);
}
