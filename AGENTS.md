# AGENTS.md

Code repository of Gabinety Tech. The specification, contracts, tasks and decisions live in
[`konradcinkusz/gabinety-tech-spec`](https://github.com/konradcinkusz/gabinety-tech-spec) (ADR-0009); read the task block you were given and
`docs/spec/SPEC.md` there. Standards: `konradcinkusz/architecture-standards` (P1-P15).

- Package versions only in `Directory.Packages.props`. The Copilot SDK is referenced only by `src/Gabinety.Llm`.
- Money is `long` grosz; time comes from `TimeProvider`; constants of CONTRACTS section 9 are the owner's.
- Fixtures are synthetic (`demo-`); no secrets; no user free text in logs.
- Verify: `dotnet build -warnaserror && dotnet test --filter "tier!=integration"`.
