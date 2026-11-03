import { request } from '@playwright/test';
import { login } from './api.js';

export default async function globalSetup() {
  const context = await request.newContext();
  try {
    for (const role of ['OWNER', 'OTHER', 'WAITER']) {
      const api = await login(context, role);
      const user = await api.me();
      const expected = role === 'WAITER' ? 'Waiter' : 'Customer';
      if (user.role !== expected) throw new Error(`${role} test account must have the ${expected} role.`);
      console.log(`${role} authentication preflight passed.`);
    }
  } finally { await context.dispose(); }
}
