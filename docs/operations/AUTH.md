# AUTH: the authservice and PostgreSQL apps on Fly (T-09.04)

Config lives in `deploy/fly/`. Nothing here is secret; `<placeholders>` are filled by the owner. Nothing in this note was run against Fly (no account yet, T-09.01); see section 8.

## 1. Apps

| App | Config | Image | Public | Health (Fly check) |
|---|---|---|---|---|
| `gabinety-authservice-staging` | `deploy/fly/authservice.fly.toml` | `ghcr.io/konradcinkusz/authservice@sha256:d0309a94...` (v0.3.4, digest in the spike) | yes | HTTP `GET /health/ready` on 8080 |
| `gabinety-postgres-staging` | `deploy/fly/postgres.fly.toml` + `init-databases.sql` | `postgres:17-alpine` | no | TCP 5432 |

Order of first deploy: PostgreSQL, then authservice, then the services (T-09.03). Services reach the database at `gabinety-postgres-staging.internal:5432`.

## 2. PostgreSQL

1. `fly apps create gabinety-postgres-staging --org <org>`
2. `fly volumes create pgdata --app gabinety-postgres-staging --region fra --size 5`
3. `fly secrets set --app gabinety-postgres-staging POSTGRES_PASSWORD="$(openssl rand -base64 33)"` (keep the value in the owner's password manager; it also goes into each connection string below)
4. `fly deploy --config deploy/fly/postgres.fly.toml`

`init-databases.sql` creates `authdb`, `directorydb`, `bookingdb`, `reputationdb`, `assistantdb`. The image runs it only when the data directory is empty (first start). It is idempotent, so on an existing volume run it by hand:
`fly proxy 15432:5432 --app gabinety-postgres-staging` then `psql -h localhost -p 15432 -U postgres -f deploy/fly/init-databases.sql`. Schemas are created by the services themselves (migrations on start / authservice `EnsureCreated`). One superuser serves all five databases on staging; per-service roles are a later hardening, not done here.

Connection string shape (a secret on each app, never in a file): `Host=gabinety-postgres-staging.internal;Port=5432;Database=<db>;Username=postgres;Password=<POSTGRES_PASSWORD>`.

## 3. authservice secrets

```
scripts/gen-authservice-key.sh ~/gabinety-staging-jwt.pem --fly gabinety-authservice-staging   # section 4
fly secrets set --app gabinety-authservice-staging --stage \
  "ConnectionStrings__DefaultConnection=Host=gabinety-postgres-staging.internal;Port=5432;Database=authdb;Username=postgres;Password=<POSTGRES_PASSWORD>"
fly deploy --config deploy/fly/authservice.fly.toml
```

Other apps need `Identity__Authority=https://<authservice-public-host>` and `Identity__Audience=gabinety` (the value of `Jwt__Audience`). Only authservice ever holds the signing key (P5).

## 4. Signing key

`scripts/gen-authservice-key.sh <out-file> [--fly <app>]` writes an RSA 2048 PKCS#8 PEM, mode 0600, refuses to overwrite, refuses a path inside a git work tree, and prints only the public key's SHA-256. `*.pem` is git-ignored as a second guard. Only the private key is configured (`Jwt__PrivateKeyPem`); authservice derives and publishes the public key at `/.well-known/jwks.json`. Rotation: set the new key as `Jwt__PrivateKeyPem` and the old one as `Jwt__PreviousPublicKeyPem` (spike section 5); the JWKS then has two keys for the overlap, so the assertion below needs `EXPECTED_KEYS=2` during a rotation (not implemented; edit the script or run it by hand).

## 5. `Jwt__PublicBaseUrl` and the OAuth callback

- `Jwt__PublicBaseUrl` = the public origin of authservice (`https://<authservice-public-host>`, `<app>.fly.dev` until a domain exists). It becomes `jwks_uri` in `/.well-known/openid-configuration`; without it authservice uses the request origin, which behind Fly's proxy is usually right but not guaranteed.
- GitHub sign-in is **optional** and off unless both `OAuth__GitHub__ClientId` and `OAuth__GitHub__ClientSecret` are set. Note DECISIONS 2026-11-16 (GitHub verification by bio code, no OAuth app); the OAuth app of T-09.01 item 5 is only for sign-in.
- If enabled: register the callback `https://<authservice-public-host>/api/external-auth/callback` (unversioned path, spike section 3) at GitHub, set `OAuth__CallbackBaseUrl` to `https://<authservice-public-host>`, and `OAuth__PostLoginRedirectBaseUrl` plus `__PostLoginRedirectAllowedBaseUrls__0` to the web origin. Then `fly secrets set --app gabinety-authservice-staging OAuth__GitHub__ClientId=<id> OAuth__GitHub__ClientSecret=<secret>`.

## 6. Deploy assertion: exactly one key

`scripts/assert-jwks-one-key.sh https://<authservice-public-host>` exits 0 only if `/.well-known/jwks.json` has exactly one key. An empty set (HS256, or a missing `Jwt__PrivateKeyPem`) fails. T-09.03 calls it after the authservice deploy. Local demonstration against stub servers (not against authservice): an empty `{"keys":[]}` gave `FAIL: JWKS has 0 keys, expected exactly 1` (exit 1), one key gave `ok` (exit 0), an unreachable host `FAIL: JWKS not reachable` (exit 1). The demonstration against the real staging authservice with the key removed is pending T-09.01.

## 7. Health checks: Fly checks for Fly (decision on the open question of PR #90)

The backend Dockerfiles carry a bash `/dev/tcp` `HEALTHCHECK` on `/alive` for plain Docker (CI `docker-build`). **Fly does not read a Dockerfile `HEALTHCHECK`**, so for Fly every app declares its own check in its `fly.toml`: authservice `GET /health/ready`; PostgreSQL TCP 5432; the backends `GET /alive` (liveness) and the web app `GET /api/health`, in T-09.02's per-app `fly.toml` files. The Dockerfile `HEALTHCHECK` stays for the CI image test and is not duplicated in Fly. The authservice image is upstream's, so it gets no `docker-build` row here.

## 8. Blocked and unverified

- Needs T-09.01 (Fly organisation, deploy token): creating the apps, volume, secrets, first deploy, the staging JWKS evidence, the token-accepted-by-Directory check and the assertion failing on the real app.
- `fly.toml` syntax (the `[[files]]` injection and a `[[services]]` block with no public port) was not validated by `fly config validate` (no CLI or account here). Verify on first deploy.
- The domain (hostnames) is undecided; every `<...-public-host>` is a placeholder.
