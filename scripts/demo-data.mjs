// Fills a local development database with a few demo Bookings through the real API, so the pages have something to show:
//   pnpm db:up && pnpm api          (in another terminal)
//   pnpm demo:data
// Development only: it uses the dev Admin account from appsettings.Development.json and the dev simulate-payment endpoint.
import { readFileSync } from 'node:fs';

const API = `${process.env.API_BASE ?? 'http://localhost:5090'}/api/v1`;
const dev = JSON.parse(readFileSync(new URL('../backend/src/PickleballClub.Api/appsettings.Development.json', import.meta.url), 'utf8'));

async function call(method, path, body, token) {
  const res = await fetch(API + path, {
    method,
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const data = res.status === 204 ? null : await res.json().catch(() => null);
  return { ok: res.ok, status: res.status, data };
}

const club = (await call('GET', '/club')).data;
if (!club) throw new Error(`The API is not answering at ${API}. Start it with: pnpm api`);
const day = (offset) => {
  const d = new Date(`${club.today}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + offset);
  return d.toISOString().slice(0, 10);
};
const [c1, c2, c3, c4] = club.courts.map((c) => c.id);

const admin = (await call('POST', '/auth/admin/login', { email: dev.Admin.Email, password: dev.Admin.Password })).data?.accessToken;
if (!admin) throw new Error('Could not sign in as the dev Admin.');

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
