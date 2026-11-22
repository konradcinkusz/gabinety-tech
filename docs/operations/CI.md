# CI: measured table

Measured 2026-11-16 on GitHub-hosted `ubuntu-latest` (DECISIONS D-09), commit `5645ebbc913ff03708d59d6c34dd6ec71fe25001` (merge of PR #3), by re-running the same workflow runs three times
(`gh api .../actions/runs/<id>/rerun`). Durations are the job's `completed_at - started_at` from the Actions API. The SPEC 9 "target" column is the budget.

| Job | Attempt 1 | Attempt 2 | Attempt 3 | Target (SPEC 9) | Result |
|---|---|---|---|---|---|
| `build-test` | 44 s | 65 s | 42 s | 6 min | success x3 |
| `web` (the Next.js job, **retired at the cutover**; measured history, no longer exists) | 29 s | 24 s | 26 s | 5 min | success x3 |
| `integration` | 52 s | 53 s | 61 s | 8 min | success x3 |
| `e2e` | 66 s | 92 s | 54 s | 10 min | success x3 |
| `secret-scan` | 9 s | 10 s | 8 s | 2 min | success x3 |

Runs: `ci` https://github.com/konradcinkusz/gabinety-tech/actions/runs/43908188697 (attempts 1 to 3) and `secret-scan` https://github.com/konradcinkusz/gabinety-tech/actions/runs/43908188672 (attempts 1 to 3).

## Required checks (branch protection of `main`)

The check names to require are exactly three: **`e2e`**, **`build-test`** and **`docker-build-backend (web)`** (the matrix leg of `docker-build-backend` that builds the Blazor host, ADR-0010). `e2e-blazor`, `web` and `docker-build-web` no longer exist (they went with the Next.js app); a required check that no job produces blocks every merge forever, so do not keep them in the rule. The `integration` job (Docker, the real authservice image) is deliberately not required: it needs the Docker daemon of the runner and pulls images from two registries; read it before merging a change to auth, but a registry outage must not block the repository.

## What each job needs from the runner (read from the workflow, not measured as bytes)

| Job | Installs or downloads at run time | Needs from the runner image |
|---|---|---|
| `build-test` | .NET SDK from `global.json` (`setup-dotnet`), NuGet packages | internet |
| `integration` | .NET SDK, NuGet, the images `postgres:17-alpine`, `axllent/mailpit:v1.29.7` and `ghcr.io/konradcinkusz/authservice@sha256:...` (Testcontainers; Mailpit is the real SMTP server of the notification test; the Blazor host runs in process against the real authservice, `tests/Gabinety.Integration.Tests/BlazorAuth`) | Docker daemon |
| `e2e` | .NET SDK, Node 22, pnpm, the npm packages of `tests/e2e` only, NuGet (incl. the Aspire orchestration packages) | internet; no Docker (the fake profile starts none) |
| `secret-scan` | the gitleaks action | internet |

## Not measured

- Bytes downloaded per job, cold versus warm cache. The numbers above include whatever cache state the runner had.
- The Copilot runtime bundle download (A-12): the SDK is only referenced by `Gabinety.Llm`, which has no code yet, so nothing exercised it.
- `docker-build` does not exist yet (T-09.02).
- Three runs of one commit show repeatability on that day, not stability over time. They are not a load test.

## Findings from getting here

- The first `integration` run (PR #3, run 43907436061) failed: the booking exclusion constraint of CONTRACTS 4.1 is rejected by PostgreSQL (`42P17`, not IMMUTABLE). The amended form passes (spec PR pending the owner).
- `typescript-eslint` does not support TypeScript 7; TypeScript is pinned to 6.x.
- A branch push without a pull request starts no job: the workflows trigger on `pull_request` and on pushes to `main`.
