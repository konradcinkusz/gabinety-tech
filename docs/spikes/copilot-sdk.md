# Spike T-01.02: the GitHub Copilot SDK in this solution

Date: 2026-11-16. Method: compile-and-read only. No sign-in, no token, no runtime started, no prompt sent to a model.
Everything below marked **measured** was produced by a command in a scratch project outside this repository
(.NET SDK 10.0.404, linux-x64). Everything marked **conclusion** is an inference from it. Anything that needs a
login is **unknown, needs live run (T-02.06)**.

## 1. Versions

| What | Value | Source |
|---|---|---|
| Latest stable on NuGet | **1.0.34** (`dotnet add package GitHub.Copilot.SDK` in the scratch project resolved it) | measured |
| Pinned by this repository | 1.0.28 (`Directory.Packages.props`, not changed here) | measured |
| Pinned by the chess project | 1.0.28 (`llm-multi-agent-chess/Directory.Packages.props`). Its `src/Chess.Llm/README.md` still says "1.0.26" in its heading; the props file is the authority | measured |
| Runtime bundled by SDK 1.0.28 | Copilot CLI **1.0.105** (`CopilotCliVersion` in `build/GitHub.Copilot.SDK.props`) | measured |
| Runtime bundled by SDK 1.0.34 | Copilot CLI **1.0.112** | measured |

## 2. Member table (COPILOT-SDK §3)

Probe: one compile-only `Probe.Touch` method that references every member below (never executed).
Result column: present = compiles at the version. Built against **1.0.34 and 1.0.28**, `net10.0`, with the
`GHCP001` pragma around the probe: **0 errors, 0 warnings in both**.

| # | Member (namespace `GitHub.Copilot`) | 1.0.34 | 1.0.28 | Marker / note |
|---|---|---|---|---|
| 1 | `new CopilotClient(CopilotClientOptions)` | present | present | |
| 2 | `CopilotClient.StartAsync(ct)` | present | present | |
| 3 | `CopilotClient.DisposeAsync()` (`await using`) | present | present | |
| 4 | `CopilotClientOptions.Connection` | present | present | |
| 5 | `CopilotClientOptions.GitHubToken` | present | present | Only referenced as a property, never given a value. |
| 6 | `CopilotClientOptions.UseLoggedInUser` (left at default) | present | present | README: default true, false when `GitHubToken` is given; cannot be used with `ForUri`. |
| 7 | `RuntimeConnection.ForStdio(path, args)` | present | present | |
| 8 | `RuntimeConnection.ForUri(url, connectionToken)` | present | present | README also lists `ForTcp(...)` (not in §3). |
| 9 | `Connection = null` (bundled runtime) | present | present | Property is nullable; compiles. Behaviour is a runtime matter, see §4. |
| 10 | `GetAuthStatusAsync(ct)` -> `IsAuthenticated`, `Login`, `StatusMessage` | present | present | Compiled only; not called. |
| 11 | `ListModelsAsync(ct)` -> `ModelInfo.Id`, `.Name`, `.Policy?.State`, `.Billing?.TokenPrices`, `.SupportedReasoningEfforts`, `.DefaultReasoningEffort` | present | present | All six members compile. `ModelPolicy.State` and `ModelPolicyState` see row 22. |
| 12 | `CreateSessionAsync(SessionConfig, ct)` -> `CopilotSession` | present | present | |
| 13 | `SessionConfig.Model`, `.ReasoningEffort` | present | present | |
| 14 | `SessionConfig.Provider` | present | present | Type is `GitHub.Copilot.ProviderConfig`. A second type `GitHub.Copilot.Rpc.ProviderConfig` also exists; with both namespaces imported the name is ambiguous (CS0104). Qualify it, or import only `GitHub.Copilot`. |
| 15 | `SessionConfig.SystemMessage = new SystemMessageConfig { Mode = SystemMessageMode.Replace, Content }` | present | present | |
| 16 | `SessionConfig.AvailableTools = []` | present | present | Compiles. Whether `[]` means "no tools" is not stated in the XML docs (the SDK has a `ValidateToolFilterList` guard against "silently-empty" filters): unknown, needs live run. |
| 17 | `SessionConfig.Streaming = false` | present | present | |
| 18 | `SessionConfig.OnPermissionRequest` | present | present | Needs `Rpc.PermissionDecision`, row 22. |
| 19 | `session.SendAndWaitAsync(new MessageOptions { Prompt }, timeout, ct)` -> `AssistantMessageEvent?`, `.Data.Content` | present | present | |
| 20 | `session.AbortAsync(ct)` | present | present | |
| 21 | `session.On<AssistantUsageEvent>(h)` and `session.On<AssistantReasoningEvent>(h)`, each returns `IDisposable` | present | present | Both return `IDisposable`; assigned to `IDisposable` variables. |
| 22 | Experimental types `Rpc.PermissionDecision`, `Rpc.ModelPolicyState`, `Rpc.TokenPriceUnit` | present | present | Without the pragma (1.0.34): each use is **error GHCP001** "is for evaluation purposes only and is subject to change or removal". Pragma scoped to one method each, as §3 says. |
| 23 | `ModelInfo.Billing.TokenPrices { BatchSize, InputPrice, OutputPrice, CacheReadPrice }` | present | present | |
| 24 | `TokenPrices.CachePrice` (deprecated) | present | present | Compiler warning at 1.0.34: `'ModelBillingTokenPrices.CachePrice' is obsolete: 'This member is deprecated and will be removed in a future version.'` It is reported under the id `GHCP001`, so the pragma also silences it. |
| 25 | `client.Rpc.Models.ListTokenPriceUnitsAsync(ct)`; `TokenPriceUnit.AiCredits`, `.NanoAiu` | present | present | The unit enum is the `GHCP001` type of row 22. |
| 26 | `session.DisposeAsync()` (`await using`) | present | present | |
| 27 | `ProviderConfig { Type, ModelProvider, WireApi, BaseUrl, ApiKey, WireModel }` assigned to `SessionConfig.Provider` | present | present | The six properties compile on `GitHub.Copilot.ProviderConfig`. The XML says `WireApi` e.g. `"chat-completions"` on this type, and `"completions"` default on the Rpc type; the value needs a live check. |

New compiler warnings at 1.0.34 with the pragma: none. Without it: the `GHCP001` errors above and the `CachePrice` obsolete warning.
No other experimental or obsolete marker was met on the probed members.

Not probed because not in §3 (the chess README lists it): `StopAsync()`. Not part of this spike.

## 3. Runtime bundle at build time (A-12)

Measured by building the probe. The SDK's `build/GitHub.Copilot.SDK.targets` (target `_DownloadCopilotCli`, runs before `BeforeBuild`):

- **Yes, the first `dotnet build` downloads a runtime bundle** (not `dotnet restore`; the NuGet package itself is 19.5 MB and holds no runtime).
- Host: `https://github.com/github/copilot-cli/releases/download/v<CopilotCliVersion>/` (GitHub releases). Override with `CopilotCliReleaseBaseUrl` or the `COPILOT_CLI_DOWNLOAD_BASE_URL` environment variable (mirror).
  `CopilotCliBinaryPath` supplies your own binary and skips the download; `CopilotSkipCliDownload=true` skips it entirely (the build then does not copy a runtime).
- Archive per RID, `github-copilot-<ver>-<platform>.tgz`. Measured for linux-x64:

| SDK | Bundled CLI | Archive size (from MSBuild's own download log) | Extracted in cache |
|---|---|---|---|
| 1.0.28 (repo pin) | 1.0.105 | **89,395,448 bytes** (about 89 MB) | 321 MB |
| 1.0.34 (latest) | 1.0.112 | **109,157,659 bytes** (about 109 MB) | 282 MB |

- Checksum: the targets first download `SHA256SUMS.txt` from the same release (1,748 bytes), require exactly one line for the asset, compute SHA-256 of the archive, and **fail the build on mismatch** (`Checksum mismatch ... expected ..., got ...`; the archive is deleted). Measured published SHA-256: 1.0.28 -> `be20f4a2a68fc24da149f6328aa4993309b8d0d13faeea9e22bd065d23ef03b2`; 1.0.34 -> `c9dcab9dd34dd53663cee004e628a799539155850fcd4db6aa94e2615b0ce157` (the `github-copilot-<ver>-linux-x64.tgz` lines). Conclusion: the checksum comes from the same host as the archive, so it detects corruption, not a hostile release host.
- Cache folder: `$(IntermediateOutputPath)copilot-cli/<CopilotCliVersion>/<platform>/`, i.e. `obj/<Configuration>/<tfm>/copilot-cli/1.0.105/linux-x64/` per project. Completion marker `.copilot-runtime-complete`; the `.tgz` is deleted after extraction. An incomplete cache is deleted and re-downloaded. Then copied to `bin/.../runtimes/<rid>/native/`.
- Download timeout: `CopilotCliDownloadTimeout`, default 600 s.
- Conclusions: the chess project's "about 89 MB, checked against a published SHA-256, cached under `obj`" is exactly what 1.0.28 does. Cache is under `obj`, per project and per configuration, so a clean checkout or a cold CI runner downloads again (cache `obj/**/copilot-cli` or set `CopilotCliBinaryPath`/a mirror). Moving to 1.0.34 raises the download to about 109 MB. The scratch build ran through this environment's proxy and succeeded; time not recorded as a benchmark.

**A-12: confirmed** (differs only in size by SDK version: about 89 MB at the pinned 1.0.28, about 109 MB at 1.0.34). Source: SDK `build/GitHub.Copilot.SDK.targets`, and the two measured builds above.

## 4. Custom provider, runtime and sign-in (A-05)

What the documentation says (SDK README "Bring Your Own Key (BYOK)" and the bundled XML docs):

- `SessionConfig.Provider` takes a `ProviderConfig` ("Configuration for a custom model provider"): `Type` ("openai", "azure"), `WireApi`, `BaseUrl`, `ApiKey`, plus `Transport`, `ModelProvider` (telemetry only), `ApiKeyCommand`, `BearerToken`, a bearer-token callback, `WireModel`. The README example sets only `Type`, `BaseUrl`, `ApiKey`.
- The Rpc-side docs describe `WireModel` as the model id sent to the provider, "as opposed to modelId which is the well-known base".
- The docs speak of "named BYOK provider connections, additive to CAPI auth", and for a BYOK registry that `session.model.list` returns an empty list (no Copilot catalog).
- The session is created through `CopilotClient`, which talks to the runtime (bundled, `ForStdio`, `ForUri` or `ForTcp`). The provider settings are handed to that runtime, which makes the provider requests ("the runtime issues (for both CAPI and BYOK providers)", `CopilotRequestHandler` doc).

What the documentation does **not** say: whether a session with `Provider` set still requires a Copilot sign-in (`GetAuthStatusAsync` authenticated) to be created or to answer. The chess README records the same gap ("Whether a provider session also needs a Copilot sign-in is unknown").

- Runtime needed: **supported by documentation** (inference, not run): that the runtime is the process that makes the provider calls and the client starts/talks to it; there is no pure in-process BYOK path in the SDK docs. Conclusion: the image needs a runtime (the bundled one, or a path/URL). Source: README and XML docs for 1.0.34. Not run.
- Sign-in needed: **unknown, needs live run (T-02.06)**.

**A-05: unknown** as a whole. The "needs a runtime in the image" half is supported by the documentation (so far not `differs`); the "with or without a sign-in" half is open. Source: SDK README and XML docs 1.0.34; chess `src/Chess.Llm/README.md`. Nothing here was executed.

## 5. Recommendation: version to pin

**Keep 1.0.28 for M0 now; plan a deliberate bump to 1.0.34 as its own change after T-02.06.**

Reasons (conclusion):
- Every §3 member is present and compiles warning-free (with the pragma) at both versions, so 1.0.34 offers nothing this solution needs yet.
- 1.0.28 is the version the chess project pins and builds against; its README records that no live run has happened there either, so no version has live evidence yet (A-05, `AvailableTools = []`, 1 AI unit = 1 credit). Staying put avoids changing two things at once.
- 1.0.34 bundles a larger runtime (about 109 MB vs about 89 MB, every cold build).
- A bump is a one-line change in `Directory.Packages.props`; do it after the live run, with the same probe, and re-run the build to confirm the `CachePrice` warning is still covered by the pragma. Not done here (out of scope for this task).

If the owner prefers the latest stable now, 1.0.34 is the one to pin: the probe passes there too.

## 6. Reproduce

```
mkdir probe && cd probe && dotnet new console && dotnet add package GitHub.Copilot.SDK
# paste Probe.Touch (all §3 members, never run), wrap in #pragma warning disable GHCP001
dotnet build        # downloads the runtime bundle on the first build
```
Not run, by design: `StartAsync`, `GetAuthStatusAsync`, `ListModelsAsync`, `CreateSessionAsync`, `SendAndWaitAsync`.
