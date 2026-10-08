# Pickleball Club

The website of one pickleball club: a public site (courts, FAQ) with online court booking, and a back office for the club's staff. The words used in the code are defined in [`CONTEXT.md`](CONTEXT.md).

**Status: first version built, not yet live.** Customers can see free courts, register, book, pay (mock provider in development) and move a booking; staff run the day from the back office. What is left before going live is listed in [`docs/BACKLOG.md`](docs/BACKLOG.md).

## Structure

```
apps/
  web/      Vue 3 + Quasar — customer site (English)            :9010
  admin/    Vue 3 + Quasar — back office                        :9011
packages/
  api/      TypeScript types + HTTP client (+ an in-browser mock for the public pages)
  ui/       Design tokens + shared components (availability grid, court plan)
backend/
  src/PickleballClub.Api                .NET 8 Minimal API · EF Core + Npgsql   :5090
  tests/PickleballClub.Tests            xUnit unit tests
  tests/PickleballClub.IntegrationTests xUnit against a real PostgreSQL
db/init/    001_schema.sql · 002_seed.sql (placeholder courts, hours and prices)
e2e/        Playwright tests against the real stack
scripts/    dev.mjs (pnpm dev) · demo-data.mjs (sample bookings) · db-apply.mjs (tables for a hosted database)
render.yaml The free demo site on Render — see docs/DEMO-HOSTING.md
```

## Quick start

Requires Node 22+, pnpm 9, .NET 8 SDK, Docker.

```bash
pnpm install
pnpm dev                         # database + API (:5090) + customer site (:9010) + back office (:9011), until Ctrl-C
pnpm demo:data                   # optional, in another terminal: a demo customer and a week of sample bookings
```

`pnpm dev` is the same as running these one by one:

```bash
pnpm db:up                       # PostgreSQL on localhost:5434; applies db/init/*.sql on first start
pnpm api                         # http://localhost:5090/swagger
pnpm --filter @pbc/web dev:api   # customer site  http://localhost:9010
pnpm --filter @pbc/admin dev:api # back office    http://localhost:9011
```

- The first Admin account is created when the API starts, from `Admin:Email` / `Admin:Password` — the development values are in `backend/src/PickleballClub.Api/appsettings.Development.json`.
- Emails are not sent in development: read them at `GET http://localhost:5090/api/v1/dev/emails` (confirmation and reset links are in there).
- `pnpm dev:web` alone runs the public pages on in-browser demo data, without the API; anything that needs an account needs the API.
- `pnpm db:reset` drops the volume and re-runs the schema and seed. The schema lives in `db/init/*.sql`; after changing it, reset.

## Demo site

To show the site to people without paying for hosting, [`docs/DEMO-HOSTING.md`](docs/DEMO-HOSTING.md) puts a demo on
Render and Neon (both free): `render.yaml` describes the three services, `pnpm db:apply` builds the tables in the hosted
database, and `Demo__Enabled=true` lets payments be simulated there. A demo is never the Club's real site.

## Checks

```bash
dotnet build backend/PickleballClub.sln && dotnet test backend/PickleballClub.sln   # integration tests need `pnpm db:up`
pnpm -r run typecheck && pnpm -r run build
pnpm e2e                                                                           # Playwright; needs `pnpm db:up`
```

## Payments

Development uses the `mock` provider: no money moves, and a booking is "paid" with the *Simulate payment* button (or `POST /api/v1/dev/bookings/{id}/simulate-payment`). Outside Development the API refuses to start with `mock`, unless it is a demo site (`Demo__Enabled=true`).

To take real payments through [Beam](https://docs.beamcheckout.com):

1. Get a merchant ID, an API key and the webhook HMAC key from Beam Lighthouse (start with Playground).
2. Set `Payments__Provider=beam`, `Payments__Beam__MerchantId`, `Payments__Beam__ApiKey`, `Payments__Beam__WebhookHmacKey`, and `Payments__Beam__BaseUrl` (`https://playground.api.beamcheckout.com`, later `https://api.beamcheckout.com`).
3. In Lighthouse, point the webhook at `https://<api host>/api/v1/payments/webhook/beam` for `charge.succeeded`, `charge.failed` and `payment_link.paid`.

The Beam adapter was written from Beam's documentation and has **not been run against Beam yet**. Make one PromptPay and one card payment in Playground before going live.

Money that arrives but cannot buy the booking (the 10-minute hold ran out and the court went to someone else, or it was paid twice) is never sent back by the system: it shows up under *Refunds to make* in the back office.

## Rules the code must keep

- **No double bookings, enforced by the database**: `bookings` has `EXCLUDE USING gist (court_id WITH =, time_range WITH &&)` over the statuses `held | confirmed | checked_in`.
- **Whole hours only**: Operating Hours and Price Rules are stored as hours (0–24), so every Slot starts on the hour.
- **Prices are computed on the server** from `price_rules`: a rule for one Court beats a Club-wide rule; Saturdays, Sundays and the days in `holidays` use the `holiday` rules.
- **Times are stored in UTC**; the Club's timezone is in `club_settings`.
