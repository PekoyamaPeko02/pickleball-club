# A free demo site

How to put a **demo** of the site on the internet without paying for hosting, to show it to people. It is not the Club's
real site: payments are simulated, no email is sent, and the host sleeps when nobody visits.

| Part | Where | Free plan limits that matter |
|---|---|---|
| API (`backend/`) | [Render](https://render.com) web service, Docker, Singapore | Sleeps after 15 minutes without a visitor; the next visit waits a minute or two. 0.1 CPU, 750 running hours a month. |
| Customer site, back office | Render static sites | 500 build minutes a month for the whole account. |
| Database | [Neon](https://neon.com) Postgres, Singapore | 100 compute hours a month — about 13 hours a day of the API being awake. |

Neither service asks for a card. Render's own free Postgres is not used because Render deletes it 30 days after it is created.

This was checked on a developer machine — the Docker image builds and starts the way `render.yaml` starts it, both apps
build with the commands in `render.yaml`, and a booking was made end to end — but it has **not been deployed to Render or
Neon** yet. Expect to adjust a detail on the first deploy.

## 1. The database (Neon)

1. Sign up at neon.com and create a project. Region: **AWS Asia Pacific (Singapore)**. Postgres version: 16.
2. Open **Connect** and copy the connection string. Turn **connection pooling off** first, so the host name has no
   `-pooler` in it. It looks like `postgresql://neondb_owner:…@ep-….ap-southeast-1.aws.neon.tech/neondb?sslmode=require`.
   It contains the database password: do not paste it into chats or commit it.
3. On your machine, in the project folder, build the tables and the starting data:

   ```bash
   pnpm install
   pnpm db:apply
   ```

   Paste the connection string when asked. It runs `db/init/*.sql` once; run again, it changes nothing.

## 2. The three services (Render)

1. Sign up at render.com with your GitHub account.
2. **New → Blueprint**, choose this repository (allow Render's GitHub app to see it — the repository is private).
   Render reads `render.yaml` from the `main` branch.
3. Render asks for three values:
   - `ConnectionStrings__Default` — the same Neon connection string as above, exactly as Neon shows it.
   - `Admin__Email` — the address the first Admin signs in with.
   - `Admin__Password` — that Admin's password; use 12 characters or more. It only counts on the first start: later,
     change the password inside the back office.
4. Apply. The first build takes several minutes. When all three are live:
   - customer site — https://pickleball-club-demo.onrender.com
   - back office — https://pickleball-club-demo-admin.onrender.com
   - API health — https://pickleball-club-demo-api.onrender.com/health should answer `{"status":"ok","db":"up"}`

### If Render changed an address

Render uses the service's name as its address. If a name was already taken it adds a few random letters (the dashboard
shows the real address at the top of each service). The apps and the API must know each other's addresses, so fix them
under **Environment** of each service:

| Service | Variable | Must be |
|---|---|---|
| `pickleball-club-demo-api` | `App__WebUrl`, `Cors__Origins__0` | the customer site's address |
| `pickleball-club-demo-api` | `App__AdminUrl`, `Cors__Origins__1` | the back office's address |
| `pickleball-club-demo`, `pickleball-club-demo-admin` | `API_BASE` | the API's address |

No `/` at the end. Saving redeploys the service. `API_BASE` is built into the apps, so those two need that redeploy
(**Manual Deploy → Deploy latest commit**) before the change shows.

## 3. Sample bookings (optional)

An empty schedule is a poor demo. This adds a demo customer and a week of bookings through the API:

```bash
API_BASE=https://pickleball-club-demo-api.onrender.com pnpm demo:data
```

It asks for the Admin email and password from step 2. Run it again whenever the week has passed.

## Living with the free plan

- **Open the site two minutes before showing it.** A sleeping API takes a minute or two to start (Render says about a
  minute to wake a service; on 0.1 CPU this API then needs roughly another half minute to boot). Meanwhile the pages say
  "Connecting to the server…" and continue by themselves.
- **It is slow even when awake**: a tenth of a CPU. Pages answer in under a second, signing in takes a second or two.
- **Do not add an uptime pinger** to keep it awake. An awake API keeps the database awake, and a month of that is about
  180 compute hours against the 100 Neon gives; the database would stop until the next month.
- **Timed work only happens while the API is awake**: unpaid Holds are released as soon as it wakes; reminders are late or skipped.
- **No email is sent.** Confirmations, reminders and password-reset links are only written, in full, to the API's log in
  the Render dashboard (**Logs**) — that is where to find the link if the Admin forgets the password. Render's free plan
  also blocks SMTP ports, so a real sender must use an HTTP API.
- **Deploys**: a push to `main` redeploys only the services whose files changed, after the GitHub checks pass.

## If something does not work

- **The API deploy fails and the log mentions SSL or channel binding**: remove `&channel_binding=require` from the end of
  `ConnectionStrings__Default` (keep `?sslmode=require`) and save.
- **The pages load but say "Cannot reach the server"**: an address is wrong — see "If Render changed an address" — or the
  API is still starting.
- **The API's health address answers `"db":"down"`**: the connection string is wrong, or Neon's monthly compute hours ran out.

## What demo mode is

`Demo__Enabled=true` (see `backend/src/PickleballClub.Api/Services/Demo.cs`) is the only reason this works outside a
developer machine:

- The `mock` payment provider is allowed, and the booking page shows **Simulate payment (demo only)**. Anyone can mark a
  Booking as paid — which is the point of a demo and the reason it must never be on for a real Club.
- The QR on the payment page is deliberately not a PromptPay QR, so nobody can pay it by accident.
- Both apps show a "Demo site" banner.
- Everything else is as in production: no Swagger, no `/api/v1/dev/emails`, a generated signing key, the sign-in rate limit.
- The API refuses to start with `Demo__Enabled=true` and a real payment provider together.

## From demo to the real site

Do not switch this deployment over. Start from the list in [`BACKLOG.md`](BACKLOG.md) under "Before going live" — real
payments (Beam), a real email sender, a host that does not sleep, the Club's own courts, prices and texts — and leave
`Demo__Enabled` off there.
