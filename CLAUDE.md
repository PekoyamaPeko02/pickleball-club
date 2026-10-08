# Pickleball Club — notes for Claude Code

Website of ONE pickleball club (not a marketplace): public site + online Court Rental booking + back office.
Domain vocabulary is in `CONTEXT.md` — use those words in code, UI and docs.
The customer site is English only. The admin UI language is not decided yet (English for now).

## Stack & layout
- pnpm 9 monorepo (workspaces: `apps/*`, `packages/*`). TypeScript ~5.9.
  - `apps/web` — customer site, Quasar 2 + Vue 3 + Pinia, port 9010.
  - `apps/admin` — back office, Quasar 2 + Vue 3 + Pinia, port 9011.
  - `packages/api` — the API contract: TS types, HTTP client, and an in-browser mock (`src/mock.ts`) that covers the public read-only calls only (everything else answers 501 `demo_mode`).
  - `packages/ui` — design tokens (`src/styles/tokens.css`) + shared components (`AvailabilityGrid`, `CourtPlan`).
- `backend/` — .NET 8 Minimal API, solution `backend/PickleballClub.sln`:
  `src/PickleballClub.Api` (EF Core, JWT, BCrypt.Net-Next, Swashbuckle; `Features/{Auth,Club,Bookings,Payments,Admin,Dev}`, `Jobs/BookingJobs`),
  `tests/PickleballClub.Tests` (unit), `tests/PickleballClub.IntegrationTests` (real Postgres).
  `Directory.Build.props` sets RollForward=Major so a newer local runtime works.
- DB: PostgreSQL 16 via docker compose on **host port 5434** (database/user/password `pickleball`).
  Schema is owned by SQL in `db/init/*.sql` (NOT EF migrations). Keep `Data/AppDbContext.cs` and `Domain/Entities.cs` in sync by hand.
  EF uses `EFCore.NamingConventions` (snake_case).
- `e2e/` — Playwright tests against the real API on its own database (`pnpm e2e`, needs `pnpm db:up`).
- Not configured: ESLint, Prettier, a JS unit-test runner. Formatting follows `.editorconfig`.

## Run
```bash
pnpm install
pnpm db:up          # first run applies db/init/*.sql; reset with: pnpm db:reset
pnpm api            # http://localhost:5090/swagger
pnpm --filter @pbc/web dev:api     # http://localhost:9010 against the real API
pnpm dev:web        # alternative: mock API only
```

## Checks before committing
```bash
dotnet build backend/PickleballClub.sln && dotnet test backend/PickleballClub.sln
pnpm -r run typecheck && pnpm -r run build
```
The integration tests need the dev DB up. They create their own database; they read `TEST_DB_HOST` / `TEST_DB_PORT` (default 5434) / `TEST_DB_USER` / `TEST_DB_PASSWORD`.

## Tests
- Integration tests share one API + database per collection. `ApiCollection` ("api") relies on the seeded Club: take Courts/hours from
  `SlotPicker` so tests never collide. `SettingsCollection` ("settings") is a second database for tests that change settings or move the
  clock by days; do not use `SlotPicker` there (make a Court of your own).
- The clock is a `FakeTimeProvider` that only moves forward. Hold expiry, completion and reminders are run by calling the service, not the job.
- Emails are captured in `api.Emails`; Google is `FakeGoogle`; the Admin signs in with the account from `appsettings.Development.json`.
- `pnpm e2e` builds its own database `pickleball_e2e` and starts the API (:5190) and both apps (:9110, :9111).

## Invariants — don't break these
- Only the payment webhook (`BookingService.ConfirmPaymentAsync`) confirms an online Booking. A payment that cannot buy its Booking is flagged `refund_due`; the system never refunds.
- One open Hold per Customer is a partial unique index (`ux_bookings_one_hold_per_user`), not a check in code.
- The Customer's Reschedule is one conditional UPDATE (`rescheduled_at IS NULL`); an Admin Move never sets `rescheduled_at`.
- App-generated timestamps on Bookings come from `TimeProvider`, not the database's `now()`.
- Validation (`.WithValidation()`) goes AFTER authorization on an endpoint, so strangers get 401/403, not field errors.
- A wrong password is 400 `invalid_credentials`, never 401: the apps sign out on any 401.
- Emails are sent after the change is saved and never fail it (`BookingMailer.SafelyAsync`).
- CSV cells go through `ReportEndpoints.Cell` (quotes + no leading formula character).
- No double booking is enforced by the DB: the `no_overlapping_bookings` EXCLUDE constraint on `bookings (court_id, time_range)` for status held|confirmed|checked_in. `BookingStatus.Live` must list the same statuses. Map Postgres `23P01` → HTTP 409.
- A Booking is one Court and consecutive Slots. A Reschedule or an Admin Move is one UPDATE of the same row.
- Hours are whole hours of the Club's local day (0–24; 24 = midnight). There are no half-hour Slots.
- Prices are computed server-side (`Services/Pricing.cs`): Court rule > Club-wide rule; Day Type is `weekday` or `holiday`.
- Times stored UTC (timestamptz); Club local time via `ClubClock` and `club_settings.timezone`.
- The API forces InvariantCulture (Program.cs): the dev Mac is th-TH and would otherwise emit Buddhist-era years.
- API errors are always JSON `{ code, message }`: domain rules throw `ApiException`; bodiless 4xx are filled in by `UseStatusCodePages`.
- Quasar's tsconfig uses `exactOptionalPropertyTypes` — never pass `undefined` for an optional property.
- No refunds through the system: a Customer can only Reschedule; an Admin Cancellation records that the Club refunded outside it.

## Status (2026-10-08)
First version built: accounts, online booking with Hold + payment (mock provider; Beam adapter written but never run against Beam),
Reschedule, back office (schedule, walk-in, block, move, cancel, check-in, no-show, refunds to make, settings, revenue report), emails
through a dev sender, Playwright e2e. `docs/BACKLOG.md` lists what was done, the choices made without asking, and what is needed before
going live. Nothing has been committed yet.
