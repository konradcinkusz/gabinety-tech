<p align="center">
  <img src="docs/assets/logo.svg" alt="Gabinety Tech" width="360">
</p>

<p align="center">
  <a href="https://github.com/konradcinkusz/gabinety-tech/actions/workflows/ci.yml"><img alt="CI" src="https://github.com/konradcinkusz/gabinety-tech/actions/workflows/ci.yml/badge.svg"></a>
  <a href="https://github.com/konradcinkusz/gabinety-tech/actions/workflows/pages.yml"><img alt="Pages" src="https://github.com/konradcinkusz/gabinety-tech/actions/workflows/pages.yml/badge.svg"></a>
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10-512bd4">
  <img alt="Next.js" src="https://img.shields.io/badge/web-Next.js-000000">
  <img alt="PostgreSQL" src="https://img.shields.io/badge/db-PostgreSQL-336791">
  <img alt="Aspire" src="https://img.shields.io/badge/orchestration-Aspire-512bd4">
</p>

**Gabinety Tech** to polski marketplace płatnych konsultacji w stałej cenie ze zweryfikowanymi programistami: klient znajduje specjalistę, rezerwuje termin, płaci, a po spotkaniu wystawia opinię.

## Co działa

Cały system uruchamia się lokalnie w profilu `fake`, bez Dockera i bez kluczy:

- katalog i profile praktyków, rejestracja i logowanie, onboarding praktyka,
- rezerwacja terminu z płatnością próbną, anulowanie i zwroty, spory,
- opinie, panel administratora, asystent oparty o model językowy,
- dane demonstracyjne i zamiennik dostawcy tożsamości do prac lokalnych,
- siedem automatycznych ścieżek end-to-end (J1 do J7).

**Jeszcze nie:** profil domyślny z prawdziwym [authservice](https://github.com/konradcinkusz/authservice), prawdziwe płatności, e-mail i model językowy (wymagają kont i sekretów właściciela) oraz zweryfikowane wdrożenie.

## Szybki start

Potrzebne: .NET 10 SDK i pnpm.

```bash
pnpm --dir web install --frozen-lockfile
dotnet run --project src/Gabinety.AppHost --launch-profile fake
scripts/check-fake-profile.sh   # sprawdza wszystkie usługi
```

Aplikacja: <http://localhost:3000>. Pełny przewodnik (logowanie, ścieżki, zegar testowy, warstwy testów): [docs/GETTING-STARTED.md](docs/GETTING-STARTED.md).

## Architektura

| Element | Rola | Port (fake) |
|---|---|---|
| `Gabinety.Directory` | katalog, profile, weryfikacja | 5101 |
| `Gabinety.Booking` | rezerwacje, płatności, spory | 5102 |
| `Gabinety.Reputation` | opinie i oceny | 5103 |
| `Gabinety.Assistant` | asystent (model językowy) | 5104 |
| `Gabinety.DevIdentity` | zamiennik dostawcy tożsamości | 5100 |
| `web/` | aplikacja webowa z BFF | 3000 |

Usługi .NET 10 (minimal API, EF Core, PostgreSQL) uruchamia Aspire AppHost. Autoryzacja: tokeny RS256 z [authservice](https://github.com/konradcinkusz/authservice). Pieniądze to `long` w groszach, czas pochodzi z `TimeProvider`.

## Weryfikacja

```bash
dotnet build -warnaserror && dotnet test --filter "tier!=integration"
```

Testy integracyjne (Docker) i E2E (Playwright) działają w CI.

## Dokumentacja

- Specyfikacja, kontrakty i decyzje: [gabinety-tech-spec](https://github.com/konradcinkusz/gabinety-tech-spec)
- Wdrożenie i operacje: [docs/operations](docs/operations)
- Zasady pracy w repozytorium: [AGENTS.md](AGENTS.md)
