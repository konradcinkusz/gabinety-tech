# E-mail (Booking notifications)

`Notifications:Provider` (CONTRACTS 10.3): `Log` (default; fake and default profiles) or `Smtp` (standard SMTP, any vendor, T-09.06).
Environment variables use `__`. The password is a platform secret (SPEC 11), set with `fly secrets set`, never committed.

| Key | Default | Meaning |
|---|---|---|
| `Notifications:Smtp:Host` | none, required | SMTP server |
| `Notifications:Smtp:Port` | `587` | `25`, `587` (STARTTLS) or `465` (implicit TLS) |
| `Notifications:Smtp:TlsMode` | `StartTls` | `None` (loopback or test only), `StartTls` (required, no silent downgrade), `Implicit` |
| `Notifications:Smtp:User` / `Notifications:Smtp:Password` | none | both or neither; the password is the secret |
| `Notifications:Smtp:FromAddress` | none, required | envelope and header sender, a bare `local@domain` |
| `Notifications:Smtp:FromName` | `Gabinety Tech` | display name |
| `Notifications:Smtp:TimeoutSeconds` | `10` | connect, each command and the whole send (SPEC 5.1) |

A missing or invalid key does not stop the service (P8): Booking uses the **log sender**, logs one warning at startup naming the key (never a value) and
`/health/details` shows `email: Degraded`. The log sender marks notifications as sent without delivering them, so a `Degraded` e-mail check on a real
environment means no one is being e-mailed. Fix the configuration and restart.

The sender logs the user id, the kind and the outcome class only, never an address, subject, body, host or credential. A refused or timed-out message is
retried by the notification job (up to 5 attempts over 24 h).

## Deploy (staging)

Set these GitHub environment secrets (`staging`, names only here; `DEPLOY.md` section 2), then push a tag that touches Booking or re-run its job:

| GitHub secret | Fly secret on Booking | Notes |
|---|---|---|
| `NOTIFICATIONS_SMTP_HOST` | `Notifications__Smtp__Host` | with FROM it switches the provider on |
| `NOTIFICATIONS_SMTP_FROM` | `Notifications__Smtp__FromAddress` | a bare `local@domain` on a domain the SMTP vendor lets you send from |
| `NOTIFICATIONS_SMTP_PORT`, `NOTIFICATIONS_SMTP_TLS_MODE` | `...Port`, `...TlsMode` | optional, 587 and `StartTls` by default |
| `NOTIFICATIONS_SMTP_USER`, `NOTIFICATIONS_SMTP_PASSWORD` | `...User`, `...Password` | both or neither |

`Notifications__Provider` is **not** in `booking.fly.toml`: the deploy stages `Smtp` only when HOST and FROM are both set; otherwise it is unset and the code default `Log` applies. Outside the fake profile
a `Log` provider is never silent: Booking writes one startup warning (`Dependency email is Degraded: Notifications:Provider is Log: e-mail is NOT delivered, it is only logged ...`) and `/health/details`
shows `email: Degraded`. `Degraded` on staging therefore means "no one is being e-mailed", whichever way it arose. Healthy means the SMTP sender is in use (not that the vendor accepted a message: send one test booking).

## Where the address comes from (T-04.09c)

authservice has no "address by user id" endpoint, so no address is ever asked from it. Both addresses are facts a service already holds:

| Recipient | Source | Written when | Erased by |
|---|---|---|---|
| the client | `bookings.client_email` (Booking), the `email` claim of the validated access token | the booking is created (`POST /api/v1/bookings`); a token without a usable `email` stores null | Booking erasure hook (RUNBOOK 8.3): set to null |
| the practitioner | `practitioners.contact_email` (Directory), the `email` claim of the token they used on `POST`, `GET`, `PUT` or `POST .../submit` of `/api/v1/me/practitioner` | each of those calls with a usable claim (a token with none leaves the stored value alone) | Directory erasure hook (RUNBOOK 8.2): set to null |

Booking reads the practitioner's address with `GET /internal/v1/practitioners/{userId}/contact` (Booking to Directory, the key Booking already holds as
`Internal__Keys__Directory`; `404` when Directory holds none). `ContactResolver` caches an answer for 5 minutes and treats every Directory failure as "no address":
the dispatcher then retries the notification as for any failed attempt (up to 5 over 24 h). It is registered when `Backend:Directory` is set (deployed and fake
profiles); without it `NullContactResolver` is used and nothing is delivered. In the fake profile the sender is still the log sender.

The snapshots are never returned by any endpoint, never in a DTO or an export, never logged (log-capture tests). The client's address is the one at booking time:
a change made later in authservice does not reach existing bookings. A practitioner who has not touched their profile since T-03.09 has no address yet: their
notifications are retried until they open their dashboard (a `GET` refreshes it) or the five attempts run out.

The SMTP path is proven against a real server (Mailpit, `axllent/mailpit:v1.29.7`, Testcontainers) by `SmtpMailpitIntegrationTests` in the CI `integration` job.
