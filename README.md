# gabinety-tech

Code of Gabinety Tech, a Polish marketplace for fixed-price paid consultations with verified programmers.
Specification and plan: [gabinety-tech-spec](https://github.com/konradcinkusz/gabinety-tech-spec).

**State:** scaffold in progress (task T-00.03). Present: the .NET solution shells of SPEC 3.2, the AppHost (fake profile), the Next.js shell, the kernel (`IServiceFeature`, `/health`, `/alive`),
architecture guards and a CI workflow. Not present yet: telemetry, authentication, any feature.

```bash
dotnet build -warnaserror && dotnet test --filter "tier!=integration"
```

Start the whole system with no Docker and no credentials (needs .NET 10 SDK and pnpm):

```bash
pnpm --dir web install --frozen-lockfile
dotnet run --project src/Gabinety.AppHost --launch-profile fake
```

Then `scripts/check-fake-profile.sh` checks every service. Ports: devidentity 5100, directory 5101, booking 5102, reputation 5103, assistant 5104, web 3000.
