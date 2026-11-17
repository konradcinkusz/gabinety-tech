# Gabinety.DemoSeeder

Fills a running **fake-profile** system with demo data through its HTTP APIs (never a database). T-08.01a is the skeleton and the Directory data; Booking (T-08.01b) and Reputation (T-08.01c) add their own steps.

```
GABINETY_FAKE=1 dotnet run --project tools/Gabinety.DemoSeeder -- \
  --Backend:Directory=http://localhost:5101 --Internal:Keys:Directory=<dev key> --Identity:Authority=http://localhost:5100
```

Keys may also come from the environment (`Backend__Directory`, `Internal__Keys__Directory`, `Identity__Authority`). Exit codes: 0 done, 1 a step failed, 2 refused or misconfigured.

**Refusal rule.** It runs only if `GABINETY_FAKE=1` or `Environment:Kind=Development` is set *and* every endpoint is on this machine; a remote target needs `--i-know-this-is-demo` as well (the variable describes the shell, not the target).

**Adding a step.** Create one class implementing `ISeedStep` (`Name`, `Order`, `RunAsync(SeedContext, ct)`) in its own file, add one line to `SeedSteps.All`. Shared state (practitioners and clients by demo key) lives in `SeedContext`. Make HTTP clients with `context.Http.For(SeedService.X)`. Pushes of the Directory step use version 1 (`DirectorySeedStep.BaselineVersion`); later steps that push real figures must use a higher version.

**Data.** Ten fictional practitioners (6 Approved in four problem categories, 1 Submitted, 1 Draft, 1 Suspended, 1 Rejected) and four clients, all in the `demo-` namespace. Idempotent: a second run makes no write except the monotonic internal pushes, which the Directory ignores.
