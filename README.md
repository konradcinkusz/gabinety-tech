# gabinety-tech

Code of Gabinety Tech, a Polish marketplace for fixed-price paid consultations with verified programmers.
Specification and plan: [gabinety-tech-spec](https://github.com/konradcinkusz/gabinety-tech-spec).

**State:** the whole system runs in the fake profile (no Docker, no credentials): Directory, Booking, Reputation and Assistant services, the Next.js web app (public catalogue and profiles, sign-in, practitioner onboarding, booking with a mock payment, reviews, cancellations and refunds, disputes, administrator pages), a development identity provider and a demo data seeder. Journeys J1 to J7 are automated end-to-end tests. Not done: the default profile does not work end to end (the real auth service is not wired into the AppHost), no deployment has been verified here, and real payments, e-mail and the live language model need owner accounts and secrets.

**How to run it, sign in, click through every journey, move the test clock and run each test layer: [docs/GETTING-STARTED.md](docs/GETTING-STARTED.md).**

```bash
dotnet build -warnaserror && dotnet test --filter "tier!=integration"
```

Start the whole system with no Docker and no credentials (needs .NET 10 SDK and pnpm):

```bash
pnpm --dir web install --frozen-lockfile
dotnet run --project src/Gabinety.AppHost --launch-profile fake
```

Then `scripts/check-fake-profile.sh` checks every service. Ports: devidentity 5100, directory 5101, booking 5102, reputation 5103, assistant 5104, web 3000 (Next.js), web-blazor 3001 (the Blazor Web App port, ADR-0010 proposal: `docs/adr-proposals/0010-blazor-web-app-frontend.md`; it runs beside the Next.js app until a cutover).
