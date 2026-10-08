# Pickleball Club — backlog

Worked top-down. Tick an item with `- [x]` and add a one-line note when it is done and its checks pass
(`dotnet build` + `dotnet test`, `pnpm -r run typecheck`, `pnpm -r run build`). Words follow `CONTEXT.md`.

## Done
- [x] **0 Skeleton** — monorepo, schema + placeholder seed, API (`/health`, `/club`, `/availability`), unit + integration test harness, `apps/web` and `apps/admin` reading the availability grid (real API and mock), CI file.

- [x] **A Customer accounts** — register / sign in (email + password, bcrypt, 5 wrong tries = 15 min lock), JWT that dies on a password change, profile (name + phone), password change / forgot / reset by emailed link, email confirmation link, Google sign-in behind `Auth:GoogleClientId` (verifier written, never run against live Google), first Admin created from `Admin:Email` / `Admin:Password` config, dev email sender + `GET /api/v1/dev/emails`. Web pages: sign in, register, forgot / reset password, confirm email, account. Final count for the whole project: 83 integration + 71 unit + 7 end-to-end tests.
- [x] **B Online booking** — `POST /bookings` holds consecutive Slots of one Court for 10 min (server-priced, inside the Booking Window, one open Hold per Customer by a partial unique index, Idempotency-Key), mock payment provider (PromptPay QR / card) behind `IPaymentProvider`, webhook-only confirmation under row locks, dev simulate-payment, Hold expiry job, release Hold, switch payment method. Money that cannot buy the Booking any more (Hold ran out and the Court was taken / blocked, time passed, paid twice) sets `payments.refund_due` with a reason. Web: selectable grid → checkout → payment page with QR, countdown and polling → confirmation; bookings on the account page.
- [x] **C My bookings + Reschedule** — `POST /me/bookings/{id}/reschedule`: once (atomic), notice hours from settings, same number of Slots, any Court, inside the Booking Window, total not higher, nothing charged or refunded; one UPDATE under the exclusion constraint. Web: "Move this booking" page. 58 integration + 38 unit tests.
- [x] **D Admin sign-in + schedule actions** — `/admin/*` for role `admin` only: day schedule (grid + who has each Court + blocks), look-up by code, Walk-in Booking (no account, past the Booking Window, counter payment), Court Block (409 over a live Booking or another block), Admin Move (keeps the Customer's Reschedule, ignores price / notice / window), Admin Cancellation with a required refund note, Check-in (opens 60 min before), No-show (manual, 15 min after the start, frees the Court), checked-in Bookings complete by a job, list of payments needing a manual refund + "mark as refunded". Admin app: sign in / forgot / reset password, schedule page with dialogs, refunds page. 69 integration tests.
- [x] **E Settings** — `/admin/settings` (one call for the page), club name / Booking Window / reschedule notice, Courts (add, rename, switch off — refused while Bookings lie ahead; never deleted), Operating Hours (whole week replaced, a day left out is closed, up to midnight), Price Rules (409 on overlap within the same Court scope + Day Type; Bookings keep their price), holidays. Admin app: tabbed settings page. Settings tests run on their own database (`SettingsCollection`). 76 integration tests.
- [x] **F Emails** — `BookingMailer` behind `IEmailSender` (dev sender logs + keeps the last 200 for `GET /api/v1/dev/emails`): booking confirmed, rescheduled by the Customer, moved by the Club, cancelled by the Club, reminder 24 h before play (job, once, skipped for last-minute Bookings), and a "new booking" / "booking moved" notice to the Admins. Sent after the change is saved; a failed email never fails the change. 80 integration tests.
- [x] **G Revenue report** — `/admin/reports/revenue` by the day of play (bookings, hours sold, online vs walk-in, hours available → occupancy; paid and kept = confirmed / checked in / completed / no-show), `revenue.csv` and `bookings.csv` (UTF-8 with BOM, cells defused against spreadsheet formulas). Admin app: revenue page with range presets, headline tiles, stacked column chart (colours validated), table view, CSV downloads. `pnpm demo:data` fills a dev database with sample bookings. 82 integration + 49 unit tests.
- [x] **H Beam adapter** — `BeamPaymentProvider`, chosen with `Payments:Provider = beam` (mock stays the default, and is refused outside Development). PromptPay = QR charge expiring with the Hold; card = hosted Payment Link limited to cards; webhook verified by `X-Beam-Signature` (reproduces the example on Beam's docs page) and events `charge.succeeded` / `charge.failed` / `payment_link.paid`. **Written from the docs and never run against Beam** — needs a Playground merchant account to try. 71 unit tests.
- [x] **I End-to-end tests** — `e2e/` (Playwright 1.63, `pnpm e2e`): real API on its own database `pickleball_e2e` (rebuilt from `db/init` each run), both apps on ports 9110 / 9111. 7 tests: public pages, wrong password, the whole customer journey (choose hours → register → pay → move the booking), admin walk-in + cancel, court block, settings / refunds / revenue pages. CI job added (not run yet: the repository has no remote).
- [x] **J Home / Courts / FAQ design** — new tokens (ink, court blue, chalk, one marigold accent; Brygada 1918 + Albert Sans), `CourtPlan` (a court drawn to scale), home page whose hero shows today's Courts lit when free with their next free hour, courts page, opening hours. Copy and brand are placeholders for the Club to replace.

- [x] **K Free demo site** — `Demo:Enabled` lets the mock provider and simulate-payment run outside Development (never with a real provider; `/dev/emails` and Swagger stay closed; the demo QR cannot be paid; both apps show a "Demo site" banner). `render.yaml` (API in Docker + two static sites on Render, Singapore) with Neon for the database, `pnpm db:apply`, `pnpm demo:data` against a hosted API, a `postgresql://` URL as the connection string, "Connecting to the server…" while a sleeping host starts. Steps: `docs/DEMO-HOSTING.md`. Checked locally only — never deployed to Render or Neon.

## To do
Nothing is in progress. Ideas that were deliberately left out of the first version: Open Play, coach booking, equipment rental,
membership tiers (see `CONTEXT.md`), a PWA / mobile app, an admin UI in Thai.

## Before going live
- Run one PromptPay and one card payment through Beam Playground (the adapter has never talked to Beam), then set the production keys.
- Replace `DevEmailSender` with a real provider (the Club has not chosen one); emails are only logged today.
- Set `Auth:GoogleClientId` and try "Sign in with Google" once (the verifier has never met a real Google token).
- Set `Jwt:SigningKey`, `Admin:Email` / `Admin:Password` (first Admin), `App:WebUrl` / `App:AdminUrl`, `Cors:Origins` for the real hosts.
- Behind a reverse proxy, set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` (as `render.yaml` does): the sign-in rate limit is per client IP and would otherwise see one address.
- Keep `Demo:Enabled` off: it lets anyone mark a Booking as paid. The demo site (`docs/DEMO-HOSTING.md`) is a separate deployment, not a stage of this one.
- The public site is a client-rendered app: decide whether search engines need the home / courts / FAQ pages pre-rendered.
- Write the Privacy Policy and Terms pages (placeholders today).

## Waiting on the club
- Beam merchant ID, API key and webhook HMAC key (Playground first), a Google OAuth client ID, an email provider, hosting + domain.
- Real courts, opening hours and prices (set them in the back office), photos, logo, the club's own words, Privacy Policy and Terms text.

## Decisions taken without asking (change if wrong)
- Admin UI is in English.
- Brand: no logo or photos exist, so the look is built from the court's own plan drawing; palette and fonts are a proposal.
- A Customer can book before confirming their email address (the confirmation link is sent, but nothing waits for it).
- Registration on the web asks for the phone number straight away; the API only insists on it at the first Booking (Google accounts have none at first).
- A Customer who pays for a Hold they had released (or that ran out) still gets the Court if it is free.
- Releasing an unpaid Hold sets it to `expired` (the word "cancelled" is kept for an Admin Cancellation).
- On the move page a Customer cannot pick hours that overlap their current time on the same Court (the API allows it; the grid shows those hours as booked).
- No-show is marked by the Admin by hand (never automatically). A confirmed Booking nobody checked in stays "confirmed" after its time.
- Check-in opens 60 minutes before the start (`AdminDesk:CheckInEarlyMinutes`); No-show is allowed 15 minutes after it (`AdminDesk:NoShowGraceMinutes`).
- An Admin Move keeps the number of hours and the amount paid; a Walk-in Booking is priced by the same Price Rules (no manual price).
- A Court cannot be blocked over a live Booking: the Admin moves or cancels the Booking first.
- The Customer is also emailed when the Club moves or cancels their Booking (not asked for, but they must be told).
- "New booking" notices go to every Admin account's address unless `Notifications:AdminEmail` is set.
- The reminder goes out 24 hours before play (`Notifications:ReminderHoursBefore`).
- Revenue is reported by the day of play (not the day of payment); occupancy uses today's Courts and Operating Hours for every day in the range.
- Card payments send the Customer to Beam's hosted payment page (a Payment Link limited to cards) rather than a card form on this site, so card numbers never pass through this server.
- Signing in with Google into an account whose email was never confirmed removes that account's password and signs out its sessions (someone else may have registered the address first).
- The mock API (`pnpm dev:web`) covers the public pages only; anything that needs an account answers "not available in demo mode". End-to-end tests will run against the real API instead.
