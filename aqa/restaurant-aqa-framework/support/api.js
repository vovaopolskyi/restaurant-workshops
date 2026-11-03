import { expect } from '@playwright/test';
import { environment, required } from './env.js';

const segment = (value) => encodeURIComponent(value);

function operationName(path) {
  // Invitation tokens are secrets, including when they occur inside a URL.
  return path.replace(/\/invites\/[^/?]+/, '/invites/[redacted]');
}

export class RestaurantApi {
  constructor(request, token = null) {
    this.request = request;
    this.token = token;
    this.settings = environment();
  }

  async call(method, path, data) {
    if (!path.startsWith('/') || path.startsWith('//')) {
      throw new Error('API paths must be relative to the configured API target.');
    }
    const headers = this.token ? { Authorization: `Bearer ${this.token}` } : {};
    return this.request.fetch(`${this.settings.apiURL}${path}`, {
      method,
      headers,
      ...(data === undefined ? {} : { data }),
    });
  }

  async json(method, path, data) {
    const response = await this.call(method, path, data);
    expect(response.ok(), `${method} ${operationName(path)} returned HTTP ${response.status()}`).toBe(true);
    return response.json();
  }

  me() {
    return this.json('GET', '/me');
  }

  dishes() {
    return this.json('GET', '/dishes');
  }

  createReservation(slot, guestCount = 6) {
    return this.json('POST', '/reservations', {
      tableId: this.settings.tableId,
      startTime: slot.startTime,
      endTime: slot.endTime,
      guestCount,
    });
  }

  async invite(reservationId) {
    let token;
    await expect.poll(async () => {
      const response = await this.call('GET', `/reservations/${segment(reservationId)}/invite-token`);
      if (response.status() !== 200) return false;
      const body = await response.json();
      token = body.token;
      return typeof token === 'string' && token.length > 0;
    }, { message: 'The new reservation should expose its invitation token', timeout: 20_000 }).toBe(true);
    const group = await this.json('GET', `/guest/invites/${segment(token)}`);
    // The original guest invitation handler serializes its positional record as PascalCase.
    const groupId = group.GroupReservationId ?? group.groupReservationId;
    if (!groupId) throw new Error('Invitation response did not contain a group reservation ID.');
    return { groupId, token };
  }

  async guest(groupId, name) {
    const body = await this.json('POST', '/guest/sessions', {
      groupReservationId: groupId,
      guestName: name,
    });
    const id = body.SessionId ?? body.sessionId;
    const token = body.SessionToken ?? body.sessionToken;
    if (!id || !token) throw new Error('Guest creation did not return a session ID and token.');
    return { id, token, name, api: new RestaurantApi(this.request, token) };
  }

  addGuestDish(guest, dishId, quantity) {
    return guest.api.json('POST', `/guest/sessions/${segment(guest.id)}/dishes`, { dishId, quantity });
  }

  removeGuestDish(guest, dishId, quantity) {
    return guest.api.json('DELETE', `/guest/sessions/${segment(guest.id)}/dishes`, { dishId, quantity });
  }

  submitGuest(guest) {
    return guest.api.json('POST', `/guest/sessions/${segment(guest.id)}/submit`, {});
  }

  guestDetails(guest) {
    return guest.api.json('GET', `/guest/sessions/${segment(guest.id)}`);
  }

  sessions(groupId) {
    return this.json('GET', `/guest/groups/${segment(groupId)}/sessions`);
  }

  createOrder(reservationId, dishId, quantity = 1) {
    return this.json('POST', '/orders', {
      reservationId,
      orderItems: [{ dishId, quantity }],
    });
  }

  order(reservationId) {
    return this.json('GET', `/orders/${segment(reservationId)}`);
  }

  submit(groupId, reservationId) {
    return this.call('POST', `/group-reservations/${segment(groupId)}/submit`, { reservationId });
  }

  deleteSession(groupId, guestSessionId) {
    return this.call('DELETE', `/guest/groups/${segment(groupId)}/sessions/${segment(guestSessionId)}`);
  }

  groupStatus(groupId) {
    return this.json('GET', `/guest/groups/${segment(groupId)}/status`);
  }

  cancelReservation(reservationId) {
    return this.json('DELETE', '/reservations', { id: reservationId });
  }
}

export async function login(request, role = 'OWNER') {
  const roleName = role.toUpperCase();
  const email = required(`${roleName}_EMAIL`);
  const password = required(`${roleName}_PASSWORD`);
  const anonymous = new RestaurantApi(request);
  const result = await anonymous.json('POST', '/signin', { email, password });
  if (!result.idToken) throw new Error(`Sign-in for ${roleName} did not return an ID token.`);
  const api = new RestaurantApi(request, result.idToken);
  api.credentials = { email, password };
  return api;
}
