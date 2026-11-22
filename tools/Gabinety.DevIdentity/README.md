# Gabinety.DevIdentity

A development stand-in for the authservice (ADR-0003). It serves the subset of the authservice HTTP surface the web app uses, on an in-memory user list, and signs RS256 access tokens whose claim names are those of `docs/spikes/authservice.md`.

Rules: it lives under `tools/`, no project under `src/` is referenced by it (the AppHost starts it, only when the fake profile is on), it is in no Dockerfile, and **it refuses to start unless `GABINETY_FAKE=1`** (exit code 1, the reason on stderr). Everything is lost on restart. The RSA key is generated in memory at start.

The stand-in must mimic the real service, so every difference found is fixed here: the contract suite (`tests/Gabinety.Integration.Tests/AuthContract`) holds the observable behaviour and runs on both. Known differences that remain: no two-factor, no GitHub, no e-mail confirmation (`202`), no e-mail at all (a reset token is read from the dev route), no rate limit, no admin API, `userName` is the e-mail, and demo users get `demo-` subjects.

The real surface is proven separately: `tests/Gabinety.Integration.Tests/AuthContract` runs one suite against this tool (unit tier) and against the real authservice image (integration tier, when `AUTH_BASE_URL` is set), so the stand-in cannot drift.

## Run

```
GABINETY_FAKE=1 ASPNETCORE_URLS=http://localhost:5100 dotnet run --project tools/Gabinety.DevIdentity
```

## Surface

`GET /health` and:

| Method and path (also under `/api/auth/...`) | Notes |
|---|---|
| `GET /api/v1/auth/consents/versions` | `{terms, privacy, cookies}`, `2026-01-01` for all three, authservice's default |
| `POST /api/v1/auth/register` | `{email, password (8-100), acceptedTermsVersion, acceptedPrivacyVersion, locale?}`. `200` with a token response (no e-mail verification step, so never `202`); `400 {errors:[...]}` for an unaccepted consent version, a password without a digit, a lower-case letter, an upper-case letter and a special character, or an address already taken |
| `POST /api/v1/auth/login` | `{email, password}`; `200` token response, `401 {error}` otherwise (one answer for an unknown address and a wrong password). Five wrong passwords lock the account for five minutes: then the right password gets `401 {error, lockedOut:true, lockoutEnd}`. Never the two-factor shape, never `403` |
| `POST /api/v1/auth/refresh` | `{refreshToken}`; rotates; a used token is `401` and revokes its family |
| `POST /api/v1/auth/logout` | Bearer; revokes every refresh token of the user |
| `GET /api/v1/auth/me` | Bearer; the fields the spike lists |
| `GET /api/v1/auth/consents`, `POST /api/v1/auth/consents` | Bearer; required and accepted versions (`/konto`) |
| `POST /api/v1/auth/forgot-password` | `{email}`; `200 {message, isOAuthOnly:false}` for every well-formed address (known or not: byte-identical, no enumeration); a malformed address is the `400` problem document of `[ApiController]` |
| `POST /api/v1/auth/reset-password` | `{email, token, newPassword}`; `200 {message}`; a wrong, used or expired token or an unknown address is `400 {errors:[...]}`, so is a password that breaks the rules (the token stays usable then). Success consumes the token, revokes every refresh token and lifts a lockout. Tokens live 10 minutes |
| `POST /api/v1/auth/change-password` | Bearer, `{currentPassword, newPassword}`; a wrong current password is `400 {errors}`; success is `200 {message, sessionsRevoked:true, tokens}` with a fresh token pair, every other refresh token revoked |
| `GET /dev/last-reset-token?email=` | **Development only**, no `/api` prefix: `200 {token}` with the last reset token issued for the address (the fake profile sends no e-mail), `404` when none. Mapped only when `GABINETY_FAKE=1`; the real authservice has no such route |
| `GET /api/v1/auth/export` | Bearer; profile, consents, empty lists |
| `DELETE /api/v1/auth/account` | Bearer, body `{password, confirmation:"DELETE"}`; soft delete: the account can no longer sign in or refresh |
| `GET /api/v1/external-auth/providers` | `{providers:[]}`: no provider is configured |
| `GET /.well-known/jwks.json` | one key |
| `GET /.well-known/openid-configuration` | `issuer` and `jwks_uri` |

Token response: `{accessToken, refreshToken, expiresIn, tokenType:"Bearer"}`. Access token: 60 minutes, `iss` and `aud` both `AuthService` (override `Jwt__Issuer`, `Jwt__Audience`; `Jwt__PublicBaseUrl` sets the origin of `jwks_uri`), exactly the claims measured on the real authservice v0.3.4 (CI run 43933246622): `sub`, `email`, `jti`, `exp`, `iss`, `aud` and the `nameidentifier` and `name` URI claims. No `email_verified` (this corrects the spike), no `iat`, no `nbf`. Only `demo-admin` also carries the role claim `http://schemas.microsoft.com/ws/2008/06/identity/claims/role` = `Admin`. Not issued: `organization` claims.

## Seeded synthetic users (SPEC 6.7)

All seeded users share one fixed **development** password. It is not a secret: it protects nothing and exists only in this stand-in.

| Password | `Demo-Dev-Passw0rd!` |
|---|---|

| User (e-mail is `<name>@demo.example`) | Platform role |
|---|---|
| `demo-client` | none |
| `demo-practitioner-1` to `demo-practitioner-8` | none |
| `demo-admin` | `Admin` |

`sub` of a demo user (address `demo-<name>@demo.example`: the seeded ones and any later registered one) is its user name, e.g. `demo-practitioner-3`, the same on every start: the Booking and Reputation demo reset removes rows whose user id starts with `demo-` (CONTRACTS 10.4). Any other user gets a random opaque id.
