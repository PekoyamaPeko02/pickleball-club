// Gives the end-to-end run its own, empty database built from db/init/*.sql — never the developer's `pickleball` database.
// Connection: E2E_DB_HOST / E2E_DB_PORT (default 5434, the docker compose port) / E2E_DB_USER / E2E_DB_PASSWORD.
import { readdirSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import pg from 'pg';

export const DATABASE = 'pickleball_e2e';
const base = {
  host: process.env.E2E_DB_HOST ?? 'localhost',
  port: Number(process.env.E2E_DB_PORT ?? 5434),
  user: process.env.E2E_DB_USER ?? 'pickleball',
  password: process.env.E2E_DB_PASSWORD ?? 'pickleball',
};

async function run(database, work) {
  const client = new pg.Client({ ...base, database });
  await client.connect();
  try {
    await work(client);
  } finally {
    await client.end();
  }
}

await run('postgres', async (c) => {
  await c.query(`DROP DATABASE IF EXISTS ${DATABASE} WITH (FORCE)`);
  await c.query(`CREATE DATABASE ${DATABASE}`);
});

const initDir = fileURLToPath(new URL('../../db/init/', import.meta.url));
await run(DATABASE, async (c) => {
  for (const file of readdirSync(initDir).filter((f) => f.endsWith('.sql')).sort()) await c.query(readFileSync(initDir + file, 'utf8'));
});
console.log(`Database ${DATABASE} is ready on ${base.host}:${base.port}`);
