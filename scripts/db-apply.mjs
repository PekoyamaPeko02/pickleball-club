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
url = url.trim().replace(/^['"]|['"]$/g, ''); // pasted with its quotes
if (!/^postgres(ql)?:\/\//i.test(url)) {
  console.error('That is not a postgresql:// URL. Copy the connection string from the database provider and try again.');
  process.exit(1);
}

const client = new pg.Client({ connectionString: url });
try {
  await client.connect();
} catch (e) {
  console.error(`Could not connect to the database: ${e.message}`);
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
