import { readdir } from 'node:fs/promises';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
const root = fileURLToPath(new URL('../', import.meta.url));
let count = 0;
async function check(dir) {
  for (const entry of await readdir(dir, { withFileTypes:true })) {
    const file = path.join(dir,entry.name);
    if (entry.isDirectory()) await check(file);
    else if (/\.(m?js)$/.test(file)) {
      const result = spawnSync(process.execPath,['--check',file],{encoding:'utf8'});
      if (result.status !== 0) { console.error(result.stderr); process.exitCode = 1; }
      count++;
    }
  }
}
for (const dir of ['src','demo','integrations','tests','scripts']) await check(path.join(root,dir));
console.log(`Checked ${count} JavaScript modules.`);
