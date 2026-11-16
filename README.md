# gabinety-tech

Code of Gabinety Tech, a Polish marketplace for fixed-price paid consultations with verified programmers.
Specification and plan: [gabinety-tech-spec](https://github.com/konradcinkusz/gabinety-tech-spec).

**State:** scaffold in progress (task T-00.03). Present: the .NET solution shells of SPEC 3.2, the kernel (`IServiceFeature`, `/health`, `/alive`),
architecture guards and a CI workflow. Not present yet: AppHost, web app, telemetry, authentication, any feature.

```bash
dotnet build -warnaserror && dotnet test --filter "tier!=integration"
```
