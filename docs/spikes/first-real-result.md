# First real result: a token from the real authservice image (DECISIONS D-20 a)

Measured 2026-11-16 by the `integration` job, https://github.com/konradcinkusz/gabinety-tech/actions/runs/43933246622 (commit of PR #10; the same tests were green in
run 43932882039). Real thing: `ghcr.io/konradcinkusz/authservice@sha256:d0309a94f6b6300d4b8091381786c160ae7717a022416debaaa6d8d19a244645` (tag `v0.3.4`) on PostgreSQL 17,
RS256 key generated in memory for the run, `Jwt__Issuer=gabinety-thinslice`, `Jwt__Audience=gabinety`, `Auth__RequireConfirmedEmail=false`. Directory validated the token with
`Identity:Authority` set to the container's URL and nothing else.

## Measured

- `ThinSlice.Real_authservice_token_is_accepted`: register answered **200** (a token response, no e-mail step), login answered 200, `GET /api/v1/me` on Directory answered 200 and
  `userId` equals the token's `sub`.
- `ThinSlice.Altered_token_is_rejected`: the unaltered token passed (control), the same token with one signature character changed was answered **401**.
- Claims of an ordinary user's access token (names and JSON kinds): `sub` string, `email` string, `jti` string,
  `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier` string, `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name` string, `exp` number, `iss` string, `aud` string.
- Header: `alg` RS256, `typ` JWT, `kid` present. Discovery `issuer` is the configured `Jwt:Issuer` string; `jwks_uri` path `/.well-known/jwks.json`. The JWKS has **1** key.

## What the real image revealed (differs from what the spike concluded)

- **`email_verified` is not in the token** of an ordinary user here. The spike (D2) read `TokenService.cs:227` and concluded it is always present; the measured token says otherwise
  (with e-mail confirmation not required). CONTRACTS §1 is amended: `email` is always present, `email_verified` is not relied on; the verified state comes from `GET /api/v1/auth/me`
  (`emailConfirmed`) when Directory needs it. Conclusion, not measured: the claim may be added only in some configurations (for example when confirmation is required).
- **No `iat` and no `nbf`** (spike D7 confirmed).
- No role claim for an ordinary user (the spike's role claim name is still unobserved for an administrator; it needs a seeded `InitialAdmin` run).

## What the stand-in (DevIdentity) will not show

- Anything about authservice itself: lockout, 2FA challenge at 200, `202` on register when confirmation is required, GitHub sign-in, consent versions, soft delete, rate limits.
- The real discovery and JWKS caching headers, key rotation (`Jwt__PreviousPublicKeyPem`).
- The multi-role claim shape.

## Not shown

- A token of an administrator (the role claim on the wire) and of a user with several roles.
- Behaviour with e-mail confirmation required.
- The image on `linux/arm64` (the job ran on `linux/amd64`).

## Observed since (real-authservice change; details in `docs/operations/AUTH.md` section 9)

The "Not shown" items above were closed by the Testcontainers tests in `tests/Gabinety.Integration.Tests/RealAuth` (CI `integration` job; each prints its `OBSERVED ...` line):

- **Administrator token**: `InitialAdmin` is seeded as `SuperAdmin` (not `Admin`); its role claim is the long URI name with a JSON **string** value `SuperAdmin`. A user given `Admin` carries the string `Admin`; a user with two roles (`Admin`, `User`) carries a JSON **array** of both. The spike's conclusion about the array is confirmed by observation (on a build of the v0.3.4 source, run as a process: the image's own CI output is the authority).
- **E-mail confirmation required** (`Auth__RequireConfirmedEmail=true`): register answers `202 {userId, email, message, emailVerificationRequired}` without tokens; sign-in before confirming is the generic `401 {error}`, **not** the `403 {emailVerificationRequired}` the spike read in the controller (Identity refuses first).
- The GitHub start redirects to GitHub with `redirect_uri=<authservice>/signin-github`; the spike's `/api/external-auth/callback` is the step after.
- Still not shown: the `linux/arm64` image; a real GitHub round trip; e-mail delivery.
