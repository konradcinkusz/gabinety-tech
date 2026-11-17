# Gabinety.DevIdentity

A development stand-in for the authservice (ADR-0003). It serves the subset of the authservice HTTP surface the web app uses, on an in-memory user list, and signs RS256 access tokens whose claim names are those of `docs/spikes/authservice.md`.

Rules: it lives under `tools/`, no project under `src/` is referenced by it (the AppHost starts it, only when the fake profile is on), it is in no Dockerfile, and **it refuses to start unless `GABINETY_FAKE=1`** (exit code 1, the reason on stderr). Everything is lost on restart. The RSA key is generated in memory at start.

The real surface is proven separately: `tests/Gabinety.Integration.Tests/AuthContract` runs one suite against this tool (unit tier) and against the real authservice image (integration tier, when `AUTH_BASE_URL` is set), so the stand-in cannot drift.

## Run

```
GABINETY_FAKE=1 ASPNETCORE_URLS=http://localhost:5100 dotnet run --project tools/Gabinety.DevIdentity
```

## Surface

`GET /health` and:

| Method and path (also under `/api/auth/...`) | Notes |
|---|---|
| `GET /api/v1/auth/consents/versions` | `{terms, privacy, cookies}`, one fixed string `dev-1` |
| `POST /api/v1/auth/register` | `{email, password (8-100), acceptedTermsVersion, acceptedPrivacyVersion, locale?}`. Always `200` with a token response: there is no e-mail verification step, so never `202` |
| `POST /api/v1/auth/login` | `{email, password}`; `200` token response, `401` otherwise. Never the two-factor shape, never `403` |
| `POST /api/v1/auth/refresh` | `{refreshToken}`; rotates; a used token is `401` and revokes its family |
| `POST /api/v1/auth/logout` | Bearer; revokes every refresh token of the user |
| `GET /api/v1/auth/me` | Bearer; the fields the spike lists |
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
