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

**Open:** the address of a user is resolved by `IContactResolver`; until authservice offers an address-by-user-id lookup the default `NullContactResolver`
returns none, so no real message is sent yet (see the PR of T-09.06).
