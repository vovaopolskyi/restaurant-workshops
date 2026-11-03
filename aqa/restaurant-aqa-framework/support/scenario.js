import { createHash, randomUUID } from 'node:crypto';
import { expect } from '@playwright/test';
import { environment } from './env.js';
import { login } from './api.js';

const segment = (value) => encodeURIComponent(value);

function candidateSlots(testInfo) {
  const slots = [];
  const today = new Date();
  // Tomorrow and later keep teardown outside the API's 30-minute cancellation cutoff.
  for (let day = 1; day <= 14; day += 1) {
    for (let offset = 0; offset < 7; offset += 1) {
      const start = new Date(Date.UTC(today.getUTCFullYear(), today.getUTCMonth(), today.getUTCDate() + day, 10));
      start.setUTCMinutes(start.getUTCMinutes() + offset * 105);
      slots.push({ startTime: start.toISOString(), endTime: new Date(start.getTime() + 90 * 60_000).toISOString() });
    }
  }
  const identity = `${testInfo.testId}:${testInfo.workerIndex}:${testInfo.retry}:${randomUUID()}`;
  const offset = createHash('sha256').update(identity).digest().readUInt32BE(0) % slots.length;
  return [...slots.slice(offset), ...slots.slice(0, offset)];
}

async function reserveUniqueSlot(owner, testInfo) {
  const { tableId } = owner.settings;
  // Cancellation in the current backend does not exclude cancelled reservations from
  // overlap checks. A fresh slot and bounded collision retries isolate repeated runs.
  for (const slot of candidateSlots(testInfo).slice(0, 12)) {
    const response = await owner.call('POST', '/reservations', { tableId, ...slot, guestCount: 6 });
    if (response.status() === 201) {
      const reservation = await response.json();
      if (!reservation.id) throw new Error('Reservation setup did not return its ID.');
      return { ...reservation, slot };
    }
    let message = '';
    try {
      const body = await response.json();
      message = body.error ?? body.message ?? '';
    } catch {
      // A non-JSON response remains a setup error, not a reason to guess success.
    }
    if (response.status() === 400 && /overlaps with an existing reservation/i.test(message)) continue;
    throw new Error(`Reservation setup returned HTTP ${response.status()}. Check the dedicated table, waiter assignment and account prerequisites.`);
  }
  throw new Error('No isolated reservation slot found in 12 attempts. Use a fresh dedicated test table or clear expired test fixtures.');
}

export async function createScenario(request, testInfo, { order = true } = {}) {
  const owner = await login(request, 'OWNER');
  const settings = environment();
  const { locationId } = settings;
  const tableIds = (process.env.TEST_TABLE_IDS?.trim() || settings.tableId).split(',').map((value) => value.trim()).filter(Boolean);
  const tableId = tableIds[testInfo.workerIndex % tableIds.length];
  owner.settings = { ...owner.settings, tableId };
  const roles = new Map([['OWNER', owner]]);
  // This management endpoint is waiter-only. Resolve real table metadata instead
  // of inventing the display number needed by the waiter dashboard assertions.
  const waiter = await login(request, 'WAITER');
  roles.set('WAITER', waiter);
  const tables = await waiter.json('GET', `/locations/${segment(locationId)}/tables/all`);
  const table = tables.find((candidate) => candidate.id === tableId);
  if (!table || table.capacity < 6 || !Number.isInteger(table.tableNumber)) {
    throw new Error('The worker needs a dedicated table at the configured location, capacity >= 6 and a valid table number.');
  }
  const location = await owner.json('GET', `/locations/${segment(locationId)}`);
  if (typeof location.address !== 'string' || !location.address) {
    throw new Error('The configured location did not return its address.');
  }
  const catalog = await owner.json('GET', `/locations/${segment(locationId)}/dishes`);
  const available = catalog.filter((dish) => dish.state === 'Available');
  if (available.length < 2) {
    throw new Error('The dedicated test location needs at least two available dishes before scenarios can run.');
  }
  const [dish, secondDish] = available;
  let reservationId;
  let disposed = false;
  const seedQuantity = order ? 1 : 0;

  async function dispose() {
    if (!reservationId || disposed) return;
    disposed = true;
    const response = await owner.call('DELETE', '/reservations', { id: reservationId });
    if (!response.ok()) {
      const description = `Reservation cleanup failed with HTTP ${response.status()}; fixture ${reservationId} needs review.`;
      testInfo.annotations.push({ type: 'cleanup-failure', description });
      await testInfo.attach('cleanup-failure', { body: Buffer.from(description), contentType: 'text/plain' });
      throw new Error(description);
    }
  }

  try {
    const reservation = await reserveUniqueSlot(owner, testInfo);
    reservationId = reservation.id;
    const invitation = await owner.invite(reservationId);
    const { groupId, token: inviteToken } = invitation;
    await expect.poll(async () => {
      const response = await owner.call('GET', `/reservations/${segment(reservationId)}/guest-session-id`);
      return response.status();
    }, { message: 'The owner session should be visible after group creation', timeout: 20_000 }).toBe(200);

    if (order) {
      await owner.createOrder(reservationId, dish.id, seedQuantity);
      await expect.poll(async () => {
        const response = await owner.call('GET', `/orders/${segment(reservationId)}`);
        return response.status();
      }, { message: 'The seed order should be visible through its reservation index', timeout: 20_000 }).toBe(200);
    }

    const scenario = {
      owner,
      reservationId,
      groupId,
      inviteToken,
      reservation,
      startTime: reservation.startTime,
      tableId,
      tableNumber: table.tableNumber,
      tableName: `Table ${table.tableNumber}`,
      locationAddress: location.address,
      dish,
      secondDish,
      seedQuantity,
      async login(role) {
        const name = role.toUpperCase();
        if (!roles.has(name)) roles.set(name, await login(request, name));
        return roles.get(name);
      },
      async addGuest(name, items = [], submitted = false) {
        const guest = await owner.guest(groupId, name);
        for (const item of items) {
          await owner.addGuestDish(guest, item.dishId, item.quantity);
        }
        if (submitted) await owner.submitGuest(guest);
        await expect.poll(async () => {
          const snapshot = await owner.sessions(groupId);
          const participant = snapshot.participants.find((candidate) => candidate.id === guest.id);
          return !!participant && participant.isSubmitted === submitted;
        }, { message: 'The new guest should be visible in the owner snapshot', timeout: 20_000 }).toBe(true);
        return guest;
      },
      snapshot: () => owner.sessions(groupId),
      dispose,
    };
    return scenario;
  } catch (error) {
    try {
      await dispose();
    } catch {
      // dispose records its failure without replacing the original setup error.
    }
    throw error;
  }
}
