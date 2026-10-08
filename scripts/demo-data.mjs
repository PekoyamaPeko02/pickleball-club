// Fills a database with a few demo Bookings through the real API, so the pages have something to show.
//   Local:      pnpm db:up && pnpm api          (in another terminal)
//               pnpm demo:data
//   Demo site:  API_BASE=https://<the api address> pnpm demo:data      (asks for the Admin's email and password,
//               or takes them from ADMIN_EMAIL / ADMIN_PASSWORD)
// Development and demo sites only: it needs the simulate-payment endpoint. Locally it signs in with the dev Admin
// account from appsettings.Development.json.
import { readFileSync } from 'node:fs';
import { createInterface } from 'node:readline/promises';

const API_BASE = (process.env.API_BASE ?? 'http://localhost:5090').replace(/\/+$/, '');
const API = `${API_BASE}/api/v1`;
const local = /^https?:\/\/(localhost|127\.0\.0\.1)(:|$)/.test(API_BASE);

async function adminAccount() {
  if (process.env.ADMIN_EMAIL && process.env.ADMIN_PASSWORD) return { email: process.env.ADMIN_EMAIL, password: process.env.ADMIN_PASSWORD };
  if (local) {
    const dev = JSON.parse(readFileSync(new URL('../backend/src/PickleballClub.Api/appsettings.Development.json', import.meta.url), 'utf8'));
    return { email: dev.Admin.Email, password: dev.Admin.Password };
  }
  const rl = createInterface({ input: process.stdin });
  const answers = rl[Symbol.asyncIterator]();
  const ask = async (question) => {
    process.stdout.write(question);
    return (await answers.next()).value ?? '';
  };
  const email = (await ask(`Admin email at ${API_BASE}: `)).trim();
  const password = await ask('Admin password (shown as you type): ');
  rl.close();
  return { email, password };
}

function fail(message) {
  console.error(message);
  process.exit(1);
}

async function call(method, path, body, token) {
  const res = await fetch(API + path, {
    method,
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const data = res.status === 204 ? null : await res.json().catch(() => null);
  return { ok: res.ok, status: res.status, data };
}

// A host that sleeps when idle needs a minute or two to answer its first request.
async function getClub() {
  for (let attempt = 1; attempt <= (local ? 1 : 24); attempt++) {
    const club = (await call('GET', '/club').catch(() => null))?.data;
    if (club?.courts) return club;
    if (!local) {
      if (attempt === 1) console.log('Waiting for the server to wake up (a minute or two)…');
      await new Promise((resolve) => setTimeout(resolve, 8000));
    }
  }
  return fail(`The API is not answering at ${API}.${local ? ' Start it with: pnpm api' : ''}`);
}
const club = await getClub();
const day = (offset) => {
  const d = new Date(`${club.today}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + offset);
  return d.toISOString().slice(0, 10);
};
const [c1, c2, c3, c4] = club.courts.map((c) => c.id);

const admin = (await call('POST', '/auth/admin/login', await adminAccount())).data?.accessToken;
if (!admin) fail('Could not sign in as the Admin: check the email and password.');

// A demo Customer (created on the first run, signed in on later runs).
const customer = { email: 'demo.customer@example.test', password: 'demo-customer-2026', displayName: 'Demo Customer', phone: '0812345678' };
const session = (await call('POST', '/auth/register', customer)).data?.accessToken
  ?? (await call('POST', '/auth/login', { email: customer.email, password: customer.password })).data?.accessToken;

let made = 0;
const walkIns = [
  [c1, 0, 20, 2, 'Khun Somsak', 'cash'], [c2, 1, 18, 2, 'Khun Ploy', 'transfer'], [c3, 1, 9, 1, 'Mr. Tanaka', 'cash'],
  [c4, 2, 17, 3, 'Khun Arthit', 'transfer'], [c1, 3, 7, 2, 'Ms. Garcia', 'cash'], [c2, 4, 19, 2, 'Khun Mali', 'cash'],
  [c3, 5, 10, 2, 'Khun Niran', 'transfer'], [c1, 6, 18, 2, 'Khun Dao', 'cash'],
];
for (const [courtId, offset, startHour, hours, guestName, payment] of walkIns) {
  const r = await call('POST', '/admin/bookings/walk-in', { courtId, date: day(offset), startHour, hours, guestName, guestPhone: '0890001111', payment }, admin);
  if (r.ok) made++;
}

if (session) {
  for (const [courtId, offset, startHour, hours] of [[c2, 2, 18, 2], [c4, 3, 10, 1], [c1, 5, 19, 2]]) {
    const held = await call('POST', '/bookings', { courtId, date: day(offset), startHour, hours, method: 'promptpay' }, session);
    if (held.ok && (await call('POST', `/dev/bookings/${held.data.id}/simulate-payment`)).ok) made++;
  }
}

await call('POST', '/admin/blocks', { courtId: c3, date: day(2), startHour: 6, hours: 3, reason: 'Resurfacing' }, admin);
console.log(`Demo data: ${made} bookings created (bookings that already exist are skipped).`);
console.log(`Demo customer: ${customer.email} — the password is in scripts/demo-data.mjs`);
