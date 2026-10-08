// Starts everything for local development with one command and keeps it running until Ctrl-C:
//   the database (docker compose), the API (:5090), the customer site (:9010) and the back office (:9011).
// Ctrl-C stops the three servers; the database container keeps running (stop it with `docker compose stop db`).
import { spawn, spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('..', import.meta.url));
const API = 'http://localhost:5090';
const apps = { API_MOCK: 'false', API_BASE: API };

const db = spawnSync('docker', ['compose', 'up', '-d', '--wait', 'db'], { cwd: root, stdio: 'inherit' });
if (db.status !== 0) {
  console.error('\nCould not start the database. Is Docker running?');
  process.exit(1);
}

const servers = [
  { name: 'api', color: 36, url: `${API}/health`, cmd: 'dotnet', args: ['run', '--project', 'backend/src/PickleballClub.Api'] },
  { name: 'web', color: 33, url: 'http://localhost:9010', cmd: 'pnpm', args: ['--filter', '@pbc/web', 'exec', 'quasar', 'dev'], env: apps },
  { name: 'admin', color: 35, url: 'http://localhost:9011', cmd: 'pnpm', args: ['--filter', '@pbc/admin', 'exec', 'quasar', 'dev'], env: apps },
];

let stopping = false;
function stop(code) {
  if (stopping) return;
  stopping = true;
  for (const s of servers) s.child?.kill('SIGTERM');
  setTimeout(() => process.exit(code), 1500);
}

for (const s of servers) {
  const tag = `\x1b[${s.color}m${s.name.padEnd(5)}\x1b[0m │ `;
  s.child = spawn(s.cmd, s.args, { cwd: root, env: { ...process.env, ...s.env } });
  for (const stream of [s.child.stdout, s.child.stderr]) {
    let rest = '';
    stream.on('data', (chunk) => {
      const lines = (rest + chunk).split('\n');
      rest = lines.pop();
      for (const line of lines) if (line.trim()) console.log(tag + line);
    });
  }
  s.child.on('exit', (code) => {
    if (stopping) return;
    console.error(`\n${s.name} stopped (exit ${code ?? 'signal'}); stopping the others.`);
    stop(code ?? 1);
  });
}
process.on('SIGINT', () => stop(0));
process.on('SIGTERM', () => stop(0));

// Say when everything answers.
async function ready(url) {
  for (let i = 0; i < 180 && !stopping; i++) {
    try {
      if ((await fetch(url)).ok) return true;
    } catch { /* not up yet */ }
    await new Promise((r) => setTimeout(r, 1000));
  }
  return false;
}
if ((await Promise.all(servers.map((s) => ready(s.url)))).every(Boolean)) {
  console.log('\n  Customer site   http://localhost:9010');
  console.log('  Back office     http://localhost:9011');
  console.log('  API             http://localhost:5090/swagger');
  console.log('  Press Ctrl-C to stop.\n');
}
