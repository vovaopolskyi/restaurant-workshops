import { existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

export function loadEnv() {
  const path = fileURLToPath(new URL('../.env', import.meta.url));
  if (existsSync(path)) process.loadEnvFile(path);
}

export function required(name) {
  const value = process.env[name]?.trim();
  if (!value) throw new Error(`Missing ${name}. Copy .env.example to .env and configure your test environment.`);
  return value;
}

export function environment() {
  const baseURL = required('BASE_URL');
  const apiURL = required('API_BASE_URL').replace(/\/$/, '');
  for (const value of [baseURL, apiURL]) {
    const url = new URL(value);
    if (!['https:', 'http:'].includes(url.protocol)) throw new Error('Only HTTP(S) test targets are supported.');
    if (url.username || url.password) throw new Error('Do not embed credentials in target URLs.');
  }
  return { baseURL, apiURL, tableId: required('TEST_TABLE_ID'), locationId: required('TEST_LOCATION_ID') };
}
