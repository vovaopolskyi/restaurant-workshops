import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { mkdtemp, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { required } from './env.js';

const execute = promisify(execFile);

async function aws(service, operation, input) {
  const directory = await mkdtemp(join(tmpdir(), 'restaurant-aqa-'));
  try {
    const path = join(directory, 'input.json');
    await writeFile(path, JSON.stringify(input), { encoding: 'utf8', mode: 0o600 });
    const { stdout } = await execute('aws', [service, operation, '--cli-input-json', `file://${path}`,
      '--region', required('AWS_REGION'), '--output', 'json', '--no-cli-pager'], { timeout: 30_000 });
    return stdout.trim() ? JSON.parse(stdout) : {};
  } finally { await rm(directory, { recursive: true, force: true }); }
}

// Privileged setup is confined to an identity created for this scenario. The
// state transition under test still goes through the deployed application API.
export async function withSessionState(scenario, id, transform, assertion) {
  if (required('ENABLE_ADMIN_FIXTURES') !== 'true') throw new Error('Controlled AWS fixtures are disabled.');
  const identity = await aws('sts', 'get-caller-identity', {});
  if (identity.Account !== required('AWS_ACCOUNT_ID')) throw new Error('AWS fixture account mismatch.');
  const table = required('AWS_GUEST_SESSIONS_TABLE');
  const key = { guestSessionId: { S: id } };
  const { Item: original } = await aws('dynamodb', 'get-item', { TableName: table, Key: key, ConsistentRead: true });
  if (!original || original.groupReservationId?.S !== scenario.groupId) throw new Error('Refusing to change a session outside this scenario.');
  const condition = {
    ConditionExpression: 'groupReservationId = :group',
    ExpressionAttributeValues: { ':group': { S: scenario.groupId } },
  };
  const changed = transform(structuredClone(original));
  try {
    if (changed === null) await aws('dynamodb', 'delete-item', { TableName: table, Key: key, ...condition });
    else await aws('dynamodb', 'put-item', { TableName: table, Item: changed, ...condition });
    await assertion();
  } finally {
    // Restore the exact original document even if the tested endpoint fails.
    await aws('dynamodb', 'put-item', { TableName: table, Item: original });
  }
}
