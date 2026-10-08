// Builds the Club's tables and starting data in a hosted, EMPTY PostgreSQL database (Neon …) from db/init/*.sql:
//   pnpm db:apply                               asks for the connection URL, so it stays out of the shell history
//   DATABASE_URL=postgresql://… pnpm db:apply
// The local docker database does not need this: docker compose applies the same files on its first start.
// A database that already has the tables is left exactly as it is.
import { readdirSync, readFileSync } from 'node:fs';
import { createInterface } from 'node:readline/promises';
import { fileURLToPath } from 'node:url';
import pg from 'pg';

let url = process.env.DATABASE_URL ?? process.argv[2];
if (!url) {
  const rl = createInterface({ input: process.stdin, output: process.stdout });
  url = await rl.question('Connection URL of the empty database (postgresql://…): ');
  rl.close();
}
// Dashboards show it as `psql 'postgresql://…'` or inside quotes: take just the URL.
url = url.trim().replace(/^psql\s+/i, '').replace(/^['"]|['"]$/g, '');
if (!/^postgres(ql)?:\/\//i.test(url)) {
  console.error('That is not a postgresql:// URL. Copy the connection string from the database provider and try again.');
  process.exit(1);
}

let target;
try {
  target = new URL(url);
} catch {
  console.error('That connection string cannot be read. Copy it again from the database provider.');
  process.exit(1);
}
// Selecting the text by hand copies what is on screen, and dashboards show the password as dots or stars.
if (!target.password || /^(\*|%E2%80%A2|%E2%97%8F)+$/i.test(target.password)) {
  console.error('The password in that connection string is hidden or missing. In the database provider\'s dashboard, use the Copy button (or "Show password" first), then try again.');
  process.exit(1);
}
// node-postgres already checks the certificate fully for these modes, and warns that it will stop: say what we mean.
if (['prefer', 'require', 'verify-ca'].includes(target.searchParams.get('sslmode'))) target.searchParams.set('sslmode', 'verify-full');

const client = new pg.Client({ connectionString: target.toString() });
try {
  await client.connect();
} catch (e) {
  console.error(`Could not connect to the database: ${e.message}`);
  if (/password authentication failed/i.test(e.message))
    console.error('The password in the connection string is not the database\'s. Copy the string again with the dashboard\'s Copy button — a hand-selected one can carry a hidden or cut-off password.');
  process.exit(1);
}

let failed = false;
try {
  const where = `database "${client.database}" on ${client.host}`;
  const { rows } = await client.query(`SELECT to_regclass('public.club_settings') IS NOT NULL AS ready`);
  if (rows[0].ready) {
    console.log(`The ${where} already has the Club's tables. Nothing was changed.`);
  } else {
    const initDir = fileURLToPath(new URL('../db/init/', import.meta.url));
    const files = readdirSync(initDir).filter((f) => f.endsWith('.sql')).sort();
    // All or nothing: a failure half-way leaves the database empty, so the command can simply be run again.
    await client.query('BEGIN');
    for (const file of files) {
      await client.query(readFileSync(initDir + file, 'utf8'));
      console.log(`  applied ${file}`);
    }
    await client.query('COMMIT');
    console.log(`The ${where} is ready.`);
  }
} catch (e) {
  failed = true;
  await client.query('ROLLBACK').catch(() => {});
  console.error(`Nothing was changed. The database refused: ${e.message}`);
} finally {
  await client.end();
}
process.exit(failed ? 1 : 0);
