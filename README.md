<p align="center">
  <img src="docs/assets/logo.svg" alt="Gabinety Tech" width="360">
</p>

<p align="center">
  <a href="https://github.com/konradcinkusz/gabinety-tech/actions/workflows/ci.yml"><img alt="CI" src="https://github.com/konradcinkusz/gabinety-tech/actions/workflows/ci.yml/badge.svg"></a>
  <a href="https://github.com/konradcinkusz/gabinety-tech/actions/workflows/pages.yml"><img alt="Pages" src="https://github.com/konradcinkusz/gabinety-tech/actions/workflows/pages.yml/badge.svg"></a>
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10-512bd4">
  <img alt="Blazor" src="https://img.shields.io/badge/web-Blazor-512bd4">
  <img alt="PostgreSQL" src="https://img.shields.io/badge/db-PostgreSQL-336791">
  <img alt="Aspire" src="https://img.shields.io/badge/orchestration-Aspire-512bd4">
</p>

**Gabinety Tech** to polski marketplace płatnych konsultacji w stałej cenie ze zweryfikowanymi programistami: klient znajduje specjalistę, rezerwuje termin, płaci, a po spotkaniu wystawia opinię.

## Dane logowania (profil fake)

Po uruchomieniu profilu `fake` zaloguj się na <http://localhost:3000/logowanie> (e-mail i hasło). **Hasło każdego zasianego konta: `Demo-Dev-Passw0rd!`** (hasło deweloperskie, nic nie chroni; działa tylko w zamienniku dostawcy tożsamości).

| Konto | E-mail | Do czego |
|---|---|---|
| klient | `demo-client@demo.example` | rezerwacje, płatność próbna, opinie, anulowanie, spór |
| administrator | `demo-admin@demo.example` | panel `/admin` (weryfikacja, spory, opinie, księga) |
| praktyk (6 zatwierdzonych) | `demo-practitioner-1@demo.example` do `demo-practitioner-6@demo.example` | Anna Demowska, Bartosz Testowy, Celina Przykładowska, Dariusz Fikcyjny, Ewa Wzorcowa, Filip Syntetyczny |
| praktyk (bez zatwierdzenia) | `demo-practitioner-7@demo.example`, `demo-practitioner-8@demo.example` | konta, których nie ma na publicznej liście |

Własne konto założysz w `/rejestracja` (dowolny e-mail, bez weryfikacji). Dane są w pamięci: po restarcie znikają i seeder zakłada je od nowa (około 4,5 minuty od startu; do końca seedowania nie rezerwuj, bo seeder przesuwa zegar Booking). Szczegóły i ścieżki do przeklikania: [docs/GETTING-STARTED.md](docs/GETTING-STARTED.md).

## Co działa

Cały system uruchamia się lokalnie w profilu `fake`, bez Dockera i bez kluczy:

- katalog i profile praktyków, rejestracja i logowanie, onboarding praktyka,
- rezerwacja terminu z płatnością próbną, anulowanie i zwroty, spory,
- opinie, panel administratora, asystent oparty o model językowy,
- dostępność programistów do kontraktu i propozycje współpracy z negocjacją (bez opłat; adresy e-mail dopiero po obustronnej akceptacji),
- dane demonstracyjne i zamiennik dostawcy tożsamości do prac lokalnych,
- dziesięć automatycznych ścieżek end-to-end (J1 do J10).

Prawdziwy [authservice](https://github.com/konradcinkusz/authservice) jest dołączony do profilu domyślnego (potrzebuje Dockera) i sprawdzany w CI na wszystkich przepływach, których potrzebuje web (`docs/operations/AUTH.md`); samego profilu domyślnego nikt jeszcze nie uruchomił.

**Jeszcze nie:** logowanie przez GitHub do końca (potrzebuje prawdziwej aplikacji OAuth), potwierdzanie e-mail, prawdziwe płatności, e-mail i model językowy (wymagają kont i sekretów właściciela) oraz zweryfikowane wdrożenie.

## Szybki start

Potrzebne: .NET 10 SDK (pnpm i Node tylko do testów end-to-end w `tests/e2e`).

```bash
dotnet run --project src/Gabinety.AppHost --launch-profile fake
scripts/check-fake-profile.sh   # sprawdza wszystkie usługi
```

Frontend to **Blazor Web App** (`src/Gabinety.Web`, ADR-0010) na <http://localhost:3000>; Next.js (`web/`) został wycofany (przełączenie: [docs/operations/PARITY.md](docs/operations/PARITY.md), [docs/operations/CUTOVER.md](docs/operations/CUTOVER.md)). Ścieżki użytkownika J1-J7 przechodzą na nim: `pnpm --dir tests/e2e test`. Pełny przewodnik (logowanie, ścieżki, zegar testowy, warstwy testów): [docs/GETTING-STARTED.md](docs/GETTING-STARTED.md).

## Architektura

| Element | Rola | Port (fake) |
|---|---|---|
| `Gabinety.Directory` | katalog, profile, weryfikacja | 5101 |
| `Gabinety.Booking` | rezerwacje, płatności, spory | 5102 |
| `Gabinety.Reputation` | opinie i oceny | 5103 |
| `Gabinety.Assistant` | asystent (model językowy) | 5104 |
| `Gabinety.DevIdentity` | zamiennik dostawcy tożsamości | 5100 |
| `src/Gabinety.Web` | frontend: Blazor Web App (ADR-0010: `docs/adr-proposals/0010-blazor-web-app-frontend.md`), renderowanie statyczne, interaktywne tylko tam, gdzie trzeba | 3000 |

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
