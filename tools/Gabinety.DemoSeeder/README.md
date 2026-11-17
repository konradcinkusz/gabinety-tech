# Gabinety.DemoSeeder

Fills a running **fake-profile** system with demo data through its HTTP APIs (never a database). T-08.01a is the skeleton and the Directory data; Booking (T-08.01b) and Reputation (T-08.01c) add their own steps.

```
GABINETY_FAKE=1 dotnet run --project tools/Gabinety.DemoSeeder -- \
  --Backend:Directory=http://localhost:5101 --Internal:Keys:Directory=<dev key> --Identity:Authority=http://localhost:5100
```

Booking (optional: without `Backend:Booking` the step is skipped, with a log line) takes `--Backend:Booking=http://localhost:5102 --Internal:Keys:Booking=<dev key>` as well. Keys may also come from the environment (`Backend__Directory`, `Internal__Keys__Directory`, `Backend__Booking`, `Internal__Keys__Booking`, `Identity__Authority`). Exit codes: 0 done, 1 a step failed, 2 refused or misconfigured.

**Refusal rule.** It runs only if `GABINETY_FAKE=1` or `Environment:Kind=Development` is set *and* every endpoint is on this machine; a remote target needs `--i-know-this-is-demo` as well (the variable describes the shell, not the target).

**Adding a step.** Create one class implementing `ISeedStep` (`Name`, `Order`, `RunAsync(SeedContext, ct)`) in its own file, add one line to `SeedSteps.All`. Shared state (practitioners and clients by demo key) lives in `SeedContext`. Make HTTP clients with `context.Http.For(SeedService.X)`. Pushes of the Directory step use version 1 (`DirectorySeedStep.BaselineVersion`); later steps that push real figures must use a higher version.

**Data.** Ten fictional practitioners (6 Approved in four problem categories, 1 Submitted, 1 Draft, 1 Suspended, 1 Rejected) and four clients, all in the `demo-` namespace. Idempotent: a second run makes no write except the monotonic internal pushes, which the Directory ignores.

**Booking step (T-08.01b, order 200).** Weekday availability (Europe/Warsaw, :00/:30) for each Approved demo practitioner, then twelve bookings: 2 Confirmed, 1 PendingPayment (created last, after the clock is back, so its hold is live), 1 Expired (a failed payment), 1 CancelledByClient, 1 LateCancelledByClient, 3 Completed, 1 ClientNoShow, 1 Disputed, 1 Resolved (admin `release`). Each booking is named `demo-booking-...`; the name is the start of its brief (`[demo-booking-...] ...`), which is how a second run finds it, and the Idempotency-Key is derived from it (first 16 bytes of SHA-256 of `gabinety-demo-booking:<key>`). The states that need time to pass use `POST /internal/v1/test/clock`: the clock is set to `BaselineOffsetMinutes` (0) at the start, moved forward in time order only for what is still due, and set back to the baseline in a `finally`. Completed needs the service's auto-complete job (T-04.08a): the step waits for it up to 75 s and, if it never answers, logs that and goes on. Booking's stats push overtakes the Directory baseline by itself: the seeder pushes nothing. `BookingSeedStep.ResetAsync` calls `POST /internal/v1/demo/reset`; see the PR for why it finds no seeder booking yet.
