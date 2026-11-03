import { readdirSync } from 'node:fs';
import { join } from 'node:path';
import { spawnSync } from 'node:child_process';

function check(directory) {
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    if (['node_modules', 'playwright-report', 'test-results', '.git'].includes(entry.name)) continue;
    const path = join(directory, entry.name);
    if (entry.isDirectory()) check(path);
    else if (/\.(m?js)$/.test(path)) {
      const result = spawnSync(process.execPath, ['--check', path], { stdio: 'inherit' });
      if (result.status !== 0) process.exit(result.status || 1);
    }
  }
}
check('.');
console.log('JavaScript syntax checks passed.');
