# Spike T-01.01: the authservice integration surface

Date: 2026-11-16. Task: T-01.01 (M0).

**Source read.** `https://github.com/konradcinkusz/authservice`, shallow clone, commit `d748af53544a5a2416011745de910be9d5b40cfe`, read-only. Latest tag in that clone: `v0.3.4`.
Nothing was copied into this repository. Paths below are relative to the authservice repository root. `S/` stands for `src/AuthService/`.

**Convention.** "Read" means the cited line says it. "Conclusion" means I inferred it from what I read. "Unknown" says what would settle it.

**What I could not read.** `CONTRACTS` section 3.3 (`github-link`) and `SPEC` section 6.1 live in `konradcinkusz/gabinety-tech-spec`, which is not attached to this session (GitHub access is scoped to `gabinety-tech` and the task authorises attaching only authservice). The only contract text I had is the CONTRACTS section 1 and A-01/A-02 text quoted in the task. Differences against 3.3 and 6.1 are therefore listed as **unknown** in "Proposed amendments", except where an authservice fact bears on the `github-link` idea directly (see D4).

## 1. Access token

Read:

| Item | Wire claim name | Where | Value |
|---|---|---|---|
| Subject | `sub` | `S/Services/TokenService.cs:275` | `user.Id` (ASP.NET Identity string id) |
| E-mail | `email` | `S/Services/TokenService.cs:276` | `user.Email` |
| E-mail verified | `email_verified` | `S/Services/TokenService.cs:227` | JSON boolean; `tests/AuthService.Tests/TokenContractCharacterizationTests.cs:152` asserts `JsonValueKind.True` |
| Roles | `http://schemas.microsoft.com/ws/2008/06/identity/claims/role` | `S/Services/TokenService.cs:284` (`ClaimTypes.Role`); wire name asserted at `tests/.../TokenContractCharacterizationTests.cs:26,146` | one claim per role. The test asserts a single role reads as a JSON string (`:162-163`) |
| Also present | `jti`, `exp`, `iss`, `aud`, the `nameidentifier` and `name` URI claims, `organization`, `organization:<id>:role` | `S/Services/TokenService.cs:271-297`; full list at `tests/.../TokenContractCharacterizationTests.cs:144-150` | the full claim list is asserted as exact |
| Not present | `iat`, `nbf` | `tests/.../TokenContractCharacterizationTests.cs:165` | "No `iat` or `nbf`" |

Read: lifetime is `Jwt:ExpirationMinutes`, default 60 (`S/Services/TokenService.cs:338-339`); refresh lifetime default 7 days (`:30`, `:341-342`). Refresh tokens are opaque, 64 random bytes base64 (`:316-319`), rotated on use, replay revokes the family (`:39-98`).
Read: validation requires issuer, audience, lifetime and signing key, `ClockSkew = 0` (`S/Program.cs:185-200`). The defaults for issuer and audience are both the string `AuthService` (`S/Program.cs:116-117`).

Conclusions:
- Role claim name. A product service using ASP.NET Core `JwtBearer` with default inbound claim mapping will see roles as `ClaimTypes.Role` already. A service that sets `MapInboundClaims = false` must read the long URI name, not `role`. This differs from the usual assumption (D1).
- A user with two platform roles gets the same claim name twice, which serialises as a JSON array (`JwtSecurityTokenHandler` behaviour, not read in this repo). Test once with a real token before relying on it.
- Under RS256 the JWT header gains `kid` (`S/Services/JwtSigningKeys.cs:113`). The test at `:137-139` is HS256 and shows only `alg` and `typ`.
- The two-factor challenge token has audience `<Jwt:Audience>:2fa` and is rejected by the bearer pipeline (`S/Services/TokenService.cs:335-336`, `S/Program.cs:194-196`).

## 2. Platform role names

Read: `DbSeeder.DefaultRoles = { "SuperAdmin", "Admin", "User" }` (`S/Data/DbSeeder.cs:12`). `docs/roles.md:1-10` says the platform roles are `SuperAdmin`, `Admin`, `User`, kept separate from organization roles `Owner`, `Admin`, `Member`. Admin endpoints use `[Authorize(Roles = "Admin,SuperAdmin")]` (`S/Controllers/AdminController.cs:21`); role assignment is `SuperAdmin` only (`:186`, `:224`). The first `SuperAdmin` is seeded from `InitialAdmin:Email` / `InitialAdmin:Password` (`S/Data/DbSeeder.cs:25-27`, `docs/roles.md` "The first `SuperAdmin`").

Read: organization membership appears in the token as `organization` and `organization:<id>:role` (`S/Services/TokenService.cs:294-295`). ADR-0002 says marketplace roles are Directory facts, so the web app should ignore these claims.

## 3. Endpoints the web app needs

Base path `/api/v1` (an unversioned `/api/...` alias also exists: `S/Controllers/AuthController.cs:20-21`, `S/Controllers/ExternalAuthController.cs:18-19`). JSON property names are camelCase (ASP.NET default; the test at `tests/.../TokenContractCharacterizationTests.cs:44` posts `refreshToken`). That casing is a conclusion from the framework default.

| Need | Method and path | Auth | Request | Response | Read at |
|---|---|---|---|---|---|
| Consent versions | `GET /api/v1/auth/consents/versions` | none | none | `{terms, privacy, cookies}` (version strings) | `S/Controllers/AuthController.cs:532-539`; `S/DTOs/AuthDtos.cs:256-263` |
| Register | `POST /api/v1/auth/register` | none | `{email, password (8-100), acceptedTermsVersion, acceptedPrivacyVersion, locale?}` | `200` `TokenResponse`, or `202` `{userId, email, message, emailVerificationRequired}` when e-mail must be verified first; `400` `{errors:[...]}` | `S/Controllers/AuthController.cs:82-193` (`:185` 202, `:193` 200); `S/DTOs/AuthDtos.cs:9-34`, `:111-116` |
| Login | `POST /api/v1/auth/login` | none | `{email, password}` | `200` `TokenResponse`; **or** `200` `{requiresTwoFactor:true, challengeToken, expiresIn:300}`; `401` bad credentials or locked (`lockedOut`, `lockoutEnd`); `403` `{emailVerificationRequired:true}` | `S/Controllers/AuthController.cs:201-254`; `S/DTOs/AuthDtos.cs:39-48`, `:122-126` |
| Refresh | `POST /api/v1/auth/refresh` | none | `{refreshToken}` | `200` `TokenResponse`; `401` | `S/Controllers/AuthController.cs:259-276`; `S/DTOs/AuthDtos.cs:67-71` |
| Logout | `POST /api/v1/auth/logout` | Bearer | none | `200` `{message}`. Revokes **all** the user's refresh tokens | `S/Controllers/AuthController.cs:848-863` |
| Me | `GET /api/v1/auth/me` | Bearer | none | `{id, email, userName, profileImageUrl, createdAt, lastLoginAt, organizations[], hasPassword, requiresConsent, emailConfirmed, twoFactorEnabled}` | `S/Controllers/AuthController.cs:349-394`; `S/DTOs/AuthDtos.cs:76-105` |
| GitHub sign-in start | `GET /api/v1/external-auth/login?provider=GitHub&returnUrl=<url>` | none | query only. This is a browser navigation: it answers with a redirect (`Challenge`) to GitHub; `400` if GitHub is not configured or `returnUrl` is not on an allowed origin | redirect | `S/Controllers/ExternalAuthController.cs:40-97` (`:96` challenge) |
| GitHub callback (authservice-side) | `GET /api/external-auth/callback` (**correction, observed:** the URL registered at GitHub is the OAuth handler's `/signin-github`; authservice then continues to this path itself, see `docs/operations/AUTH.md` section 5) | none | from GitHub | redirects to `<returnUrl>?code=<single-use code>`; on failure to `<ErrorRedirectBaseUrl or PostLoginRedirectBaseUrl>/login?error=<reason>` (`oauth_failed`, `locked_out`, `no_email`, `email_not_verified`, `creation_failed`, `link_failed`, `user_not_found`, `account_deleted`) | `S/Controllers/ExternalAuthController.cs:83-88` (why unversioned), `:104-287` |
| GitHub exchange | `POST /api/v1/external-auth/exchange` | none | `{code}` | `200` `TokenResponse`, or the 2FA challenge shape as for login; `401` for a bad, expired or used code | `S/Controllers/ExternalAuthController.cs:293-330`; `S/DTOs/AuthDtos.cs:131-134` |
| Providers | `GET /api/v1/external-auth/providers` | none | none | `{providers:[{provider, displayName}]}` | `S/Controllers/ExternalAuthController.cs:336-348` |
| Consent status | `GET /api/v1/auth/consents` | Bearer | none | `{terms, privacy, cookies: {requiredVersion, acceptedVersion, acceptedAt, accepted}, requiresConsent}` | `S/Controllers/AuthController.cs:544-575`; `S/DTOs/AuthDtos.cs:269-288` |
| Accept consent | `POST /api/v1/auth/consents` | Bearer | `{acceptedTerms?, acceptedPrivacy?, cookies?:{necessary, preferences, analytics, thirdParty}, locale?}` | `200` consent status | `S/Controllers/AuthController.cs:580-649`; `S/DTOs/AuthDtos.cs:293-310` |
| Export | `GET /api/v1/auth/export` | Bearer | none | `{exportedAt, profile, externalLogins[{provider, providerDisplayName}], consents[], organizations[], invitationsReceived[], invitationsSent[], sessions[]}` | `S/Controllers/AuthController.cs:399-404` (handler starts; body to the next member); `S/DTOs/ExportDtos.cs:14-55` |
| Delete account | `DELETE /api/v1/auth/account` | Bearer | JSON body `{password?, confirmation:"DELETE"}`; password required if the account has one | `200` `{message}`. This is a **soft delete** with a retention period before permanent deletion | `S/Controllers/AuthController.cs:868-918` (`:901-903` soft delete); `S/DTOs/AuthDtos.cs:240-248` |

`TokenResponse` = `{accessToken, refreshToken, expiresIn (seconds), tokenType:"Bearer"}` (`S/DTOs/AuthDtos.cs:53-62`).

Also read, likely needed: `POST /api/v1/auth/verify-email {email, token}` (`S/Controllers/AuthController.cs:283`, DTO `:137-144`), `POST /api/v1/auth/resend-verification {email}` (`:312`, DTO `:147-151`), the 2FA login completion `POST /api/v1/auth/2fa/login` (named at `S/Controllers/AuthController.cs:199`; its body is in `S/Controllers/TwoFactorController.cs`, not read in detail).

Conclusion (D4): the exchange code is redeemed server-side to a `userId` (`S/Controllers/ExternalAuthController.cs:305`) and the issued token is an ordinary session token, so a product service sees a GitHub-signed-in user exactly like a password user.

## 4. Health, image, port

- Health. Read: `GET /health` is static liveness (`S/Program.cs:507`); `GET /alive` is an alias (`:512`); `GET /health/ready` is readiness, 503 until the database answers (`:516`, `docs/DEPLOYMENT.md:46-47`). `docs/DEPLOYMENT.md:47` says platform health checks and load balancers belong on `/health/ready`. A-02 says "exposes `/health`": **confirmed**, with the nuance that readiness is `/health/ready`.
- Port. Read: `EXPOSE 8080` (`src/AuthService/Dockerfile:6`) and `ENV ASPNETCORE_URLS=http://+:8080` (`:33`).
- Image name and tags. Read: pushed as `ghcr.io/konradcinkusz/authservice:<tag>` and `:latest` on every `v*` tag (`.github/workflows/publish-image.yml:21-25`, `:64-66`); `docs/DEPLOYMENT.md:14` names `ghcr.io/konradcinkusz/authservice:v0.3.4`; `README.md:322` says "pin a real tag, don't float :latest". Platforms `linux/amd64,linux/arm64` (`publish-image.yml:61`).
- Digest. **Read from the registry, not from the repository.** On 2026-11-16 an anonymous token for `repository:konradcinkusz/authservice:pull` was accepted by `ghcr.io` (so the package is public), and `HEAD /v2/konradcinkusz/authservice/manifests/v0.3.4` returned `docker-content-digest: sha256:d0309a94f6b6300d4b8091381786c160ae7717a022416debaaa6d8d19a244645`. `latest` returned the same digest. The tag list returned `v0.1.0, latest, v0.1.1, v0.1.2, v0.2.0, v0.3.0, v0.3.1, v0.3.2, v0.3.3, v0.3.4`. That digest is the multi-platform index digest (the request asked for the index media type). Re-check it when a later task pins the image.
- Conclusion: a stable, versioned tag exists, so A-02 is **confirmed**.

## 5. Configuration to run it with RS256 behind Directory

Read (all as environment variables, `__` for `:`):

| Key | Need | Read at |
|---|---|---|
| `ConnectionStrings__DefaultConnection` | required; startup throws if blank | `S/Program.cs:102-107` |
| `DATABASE_PROVIDER` (`DatabaseProvider`) | `PostgreSQL` (default) or `SqlServer` | `docs/DEPLOYMENT.md:27`; `S/appsettings.json:2` |
| `Database__SchemaMode` | default `EnsureCreated`; `Migrate` needs `Database__MigrationsAssembly=AuthService.Migrations.PostgreSQL` | `S/appsettings.json:3-6`; `docs/DEPLOYMENT.md:29` |
| `Jwt__Algorithm=RS256` | optional: if unset it is inferred RS256 when a private key is configured, else HS256 | `S/Services/JwtSigningKeys.cs:187-204` |
| `Jwt__PrivateKeyPem` or `Jwt__PrivateKeyPath` | required for RS256; PKCS#8 PEM, RSA at least 2048 bits; escaped `\n` is repaired | `S/Services/JwtSigningKeys.cs:81-89`, `:103-108`, `:207-224`, `:245-248` |
| `Jwt__Issuer`, `Jwt__Audience` | default `AuthService`; docs say set per product | `S/Program.cs:116-117`; `docs/DEPLOYMENT.md:32` |
| `Jwt__PublicBaseUrl` | public origin used for `jwks_uri` in discovery; falls back to the request origin | `S/Program.cs:119-124`, `:493`; `docs/DEPLOYMENT.md:33` |
| `Jwt__PreviousPublicKeyPem` | keeps old tokens valid across a rotation | `S/Services/JwtSigningKeys.cs:120-143`, `:226-238` |
| `OAuth__GitHub__ClientId` / `__ClientSecret` | GitHub sign-in is enabled only when both are set; scope `user:email` | `S/Program.cs:223-231`; `README.md:169` |
| `OAuth__CallbackBaseUrl` | public base the provider redirects back to; callback is `<base>/api/external-auth/callback` | `S/Controllers/ExternalAuthController.cs:82-88` |
| `OAuth__PostLoginRedirectBaseUrl` (+ `__PostLoginRedirectAllowedBaseUrls`, `__ErrorRedirectBaseUrl`) | required for GitHub sign-in; `returnUrl` must be on an allowed origin | `S/Controllers/ExternalAuthController.cs:55-75`, `:107-112` |
| `Cors__AllowedOrigins` | browser origins allowed | `S/appsettings.json:77-79` |
| `ConsentVersions__Terms/Privacy/Cookies` | defaults `2026-01-01` | `S/appsettings.json:68-72` |
| `InitialAdmin__Email/Password` | seeds the first `SuperAdmin` | `S/appsettings.json:73-76`; `S/Data/DbSeeder.cs:25-27` |
| `Auth__RequireConfirmedEmail` | default: on only when SendGrid is configured | `S/Program.cs:150`, `:170`; `README.md:200` |

Public keys. Read: the JWKS is served at `GET /.well-known/jwks.json` with `Cache-Control: public, max-age=300` (`S/Program.cs:474-478`); it is empty under HS256 (`S/Services/JwtSigningKeys.cs:172-183`). The discovery document at `/.well-known/openid-configuration` carries `issuer` (the configured `Jwt:Issuer`, not the URL) and `jwks_uri` (`S/Program.cs:484-504`). The `kid` is the RFC 7638 thumbprint of the key (`S/Services/JwtSigningKeys.cs:255-266`).
Read: a product service can use `options.Authority = <base url>` and nothing else (`S/Program.cs:480-483`); because `issuer` in discovery is the configured string, set `Jwt__Issuer` per product and give `ValidIssuer` that same string.

Key pair on Linux. Read: `openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out jwt-signing.pem` (`S/Services/JwtSigningKeys.cs:88`; `docs/DEPLOYMENT.md:97`; `README.md:144`). Only the private key is configured; the public key is derived and published through the JWKS, so no public key file is distributed. Conclusion: for a local run, pass the file contents with `Jwt__PrivateKeyPem="$(cat jwt-signing.pem)"` or mount it and set `Jwt__PrivateKeyPath`. The generated file is a secret and must never be committed.

Compose-file note. Read: the shipped `docker-compose.yml` is HS256 with a demo secret (`docker-compose.yml:30-32`) and `S/Program.cs:401-407` warns that HS256 lets any validator mint tokens, so it must not be copied.

## 6. Assumptions

### A-01: **differs** (in one part; the rest confirmed)

A-01 says: "the access token carries the subject, e-mail and role claims, the platform roles are named `Admin` and `SuperAdmin`, `GET /api/v1/auth/me` returns the linked GitHub login, and the login and OAuth exchange endpoints behave as its README lists".

| Part | Verdict | Evidence |
|---|---|---|
| subject claim | confirmed | `S/Services/TokenService.cs:275` (`sub`) |
| e-mail claim | confirmed | `S/Services/TokenService.cs:276` (`email`), plus `email_verified` at `:227` |
| role claim | confirmed, but the wire name is the long URI, not `role` (D1) | `S/Services/TokenService.cs:284`; `tests/.../TokenContractCharacterizationTests.cs:26,146` |
| platform roles `Admin`, `SuperAdmin` | confirmed (plus a third role `User`) | `S/Data/DbSeeder.cs:12`; `S/Controllers/AdminController.cs:21` |
| `me` returns the linked GitHub login | **differs**. `UserInfoResponse` has no field for it, and nothing else in the code stores a GitHub login name | `S/DTOs/AuthDtos.cs:76-105`; `S/Controllers/AuthController.cs:379-391`. The GitHub handler maps only `avatar_url` (`S/Program.cs:232`). The export lists a linked login only as `{provider, providerDisplayName}` (`S/DTOs/ExportDtos.cs:41-44`, `S/Controllers/AuthController.cs:499`) |
| login and exchange "behave as its README lists" | confirmed with extras the README summary may not show: the 2FA challenge variant at the same `200`, a `202` on register, and `403` on an unverified e-mail | `S/Controllers/AuthController.cs:185,193,232-245`; `S/Controllers/ExternalAuthController.cs:318-324`. README endpoint list: `README.md:370` |

Conclusion: the GitHub login (handle) is not obtainable from authservice. What is stored is the provider key, in Identity's login table, and the user name, which is derived from the e-mail local part and domain (`S/Controllers/ExternalAuthController.cs:181`, `:380`; the helper's doc comment is at `S/Controllers/AuthController.cs:920-924`), never from the GitHub login. Whatever needs the GitHub handle must get it from GitHub directly, or from a change to authservice that ADR-0002 forbids here.

### A-02: **confirmed**

Evidence: image `ghcr.io/konradcinkusz/authservice` with versioned tags and `latest` (`.github/workflows/publish-image.yml:64-66`); port 8080 (`src/AuthService/Dockerfile:6,33`); `/health` returns 200 `{status:"Healthy"}` (`S/Program.cs:507`). Tag `v0.3.4` and digest `sha256:d0309a94f6b6300d4b8091381786c160ae7717a022416debaaa6d8d19a244645` come from the registry, not the repository (section 4). Nuance: use `/health/ready` for readiness.

## 7. Proposed amendments

I did not edit CONTRACTS or SPEC. The supervisor decides.

- **D1. CONTRACTS section 1, `IsAdmin`.** Name the role claim by its wire name `http://schemas.microsoft.com/ws/2008/06/identity/claims/role` (and say that default ASP.NET Core inbound mapping presents it as `ClaimTypes.Role`). State that a user with more than one role carries the claim more than once. `IsAdmin` stays "role `Admin` or `SuperAdmin`" (confirmed).
- **D2. CONTRACTS section 1, `Email` and `EmailVerified`.** Replace "token claims if present; otherwise `/me`" with: `email` and `email_verified` are always present in session tokens (`S/Services/TokenService.cs:227,276`), so the `/me` fallback is not needed for them. `email_verified` is a JSON boolean. Caveat: MCP-flow tokens use another claim set (`S/Services/TokenService.cs:224-226` comment), but ADR-0002 excludes that flow.
- **D3. CONTRACTS section 1, `UserId`.** Note `sub` is authservice's string user id (the Identity id, not asserted to be a GUID), so contracts should type it as an opaque string, not `Guid`. Unknown whether the existing contracts type it as a Guid; I could not read them.
- **D4. A-01 and CONTRACTS section 3.3 (`github-link`).** `GET /api/v1/auth/me` does not return a GitHub login, and there is no authservice endpoint that returns one (section 6). Also there is no authenticated "link GitHub to my account" endpoint; linking happens only inside the sign-in callback, by matching the provider e-mail to an existing account (`S/Controllers/ExternalAuthController.cs:177-231`). Any `github-link` contract that reads the handle from `me`, or that lets a signed-in user attach a GitHub identity, cannot be met without changing authservice. Options for the supervisor: (a) drop the handle from the contract; (b) have Directory read the handle from GitHub with the user's own consent in a separate OAuth step. Text of 3.3 itself: **unknown**, spec repo not attached.
- **D5. SPEC section 6.1.** Text **unknown** (spec repo not attached). Facts it must be checked against: login and exchange can return a 2FA challenge at `200`; register can return `202` without tokens; unverified e-mail gives `403`; `DELETE /auth/account` is a soft delete with a retention period and needs a body; `logout` revokes every refresh token of the user, not just the current one (`S/Controllers/AuthController.cs:857`); the GitHub start endpoint is a browser navigation, not a fetch call.
- **D6. Operations.** Readiness check path is `/health/ready` (`/health` is liveness only). Pin the image by digest in the compose or AppHost definition; the digest to start from is in section 4.
- **D7. Token lifetime.** Access token 60 minutes and no `iat`/`nbf` by default; product services should not require those claims. Clock skew on authservice's own validation is zero (`S/Program.cs:200`).

## 8. Checklist against the task's Accept line

- Claim names: section 1. Platform role names: section 2. Endpoints with method, path, request, response: section 3. Health path, image name, tag, digest, port: section 4. Configuration keys and Linux key generation: section 5.
- A-01 and A-02 marked with file and line: section 6.
- "Proposed amendments" exists and is not empty: section 7.
- Open items, each with what settles it: (1) text of CONTRACTS 3.3 and SPEC 6.1, settled by attaching the spec repo; (2) multi-role claim shape and RS256 `kid` header, settled by decoding a real token from a running container; (3) body of `POST /api/v1/auth/2fa/login`, in `S/Controllers/TwoFactorController.cs`, not read in detail; (4) the digest is a registry fact from 2026-11-16 and can move if `latest` is retagged; pin by digest.
