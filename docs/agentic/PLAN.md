# Gabinety Agents: plan przebudowy na marketplace agentów AI

Ten dokument jest instrukcją wykonawczą. Każde zadanie (A-01, A-02, ...) jest samodzielne: jedno zadanie = jedna sesja = jeden commit.
Wykonuj zadania **w podanej kolejności** i nie łącz ich.

## 0. Zasady dla wykonawcy (czytaj przed każdym zadaniem)

1. Przeczytaj `AGENTS.md` w katalogu głównym. Obowiązuje w całości.
2. Przed edycją przeczytaj **w całości** każdy plik wymieniony w polu „Pliki" zadania. Nie zgaduj sygnatur.
3. Po zadaniu uruchom: `dotnet build -warnaserror && dotnet test --filter "tier!=integration"`. Zadanie nie jest gotowe, dopóki oba polecenia nie przejdą.
4. Nie wolno wyłączać, pomijać ani usuwać testu tylko po to, żeby przeszedł build. Test usuwasz wyłącznie wtedy, gdy zadanie wprost usuwa testowaną funkcję; wtedy napisz to w opisie commita.
5. Pieniądze to `long` w groszach. Czas wyłącznie z `TimeProvider`. Wersje pakietów wyłącznie w `Directory.Packages.props`.
6. Tekst wpisany przez użytkownika (brief, treść zadania, wiadomości czatu, odpowiedź agenta) **nigdy** nie trafia do logów, metryk, komunikatów wyjątków ani odpowiedzi błędów.
7. Klucz API nigdy nie jest zapisywany ani logowany jawnie. W bazie tylko skrót SHA-256 i 8-znakowy prefiks.
8. Dane demonstracyjne mają prefiks `demo-`. Żadnych prawdziwych sekretów w repozytorium.
9. Nowy kod układaj tak jak istniejący: jedna funkcja = jedna klasa `IServiceFeature` (`src/Gabinety.ServiceDefaults/Extensions.cs`) z metodami `Add` i `Map`; wzoruj się na pliku wskazanym w zadaniu.
10. Zmiana schematu bazy = nowa migracja EF Core w tym samym projekcie (znajdź istniejący katalog migracji poleceniem `git ls-files | grep Migrations`), nigdy edycja starej.
11. Jeśli coś w zadaniu nie zgadza się z kodem (plik nie istnieje, sygnatura inna), **zatrzymaj się i opisz rozbieżność**. Nie improwizuj obejścia.
12. Nazwy w kodzie zostają: `Practitioner`, `Offering`, `Booking`. Zmieniają się teksty dla użytkownika i zachowanie. Zmiana nazw w kodzie to osobny, opcjonalny etap E.

## 1. Słownik: stare pojęcie → nowe znaczenie

| W kodzie (zostaje) | Znaczenie w wersji agentowej | Tekst dla użytkownika |
|---|---|---|
| `Practitioner` | Agent AI z personą (np. „Bartosz Testowy") | „Agent" |
| konto właściciela profilu | konto techniczne dostawcy, jedno na agenta (jak dziś `demo-practitioner-N`) | „Dostawca" |
| `Offering` | pakiet dostępu do agenta | „Pakiet" |
| `Offering.DurationMinutes` | typowy czas wykonania jednego zadania (30 lub 60, bez zmiany walidacji) | „Typowy czas: ok. 30 min" |
| `Booking` | zakupiony pakiet | „Dostęp" |
| `Booking.StartUtc` / `EndUtc` | początek i koniec ważności pakietu | „Ważny do" |
| slot, dostępność, link do spotkania | usunięte | brak |
| spór (`Dispute`) | reklamacja | „Reklamacja" |

Model identyfikowany w API to **slug agenta** (`Practitioner.Slug`, np. `demo-bartosz-testowy`).

## 2. Docelowy produkt

### 2.1 Dwa rodzaje pakietów (`OfferingKind`)

| Kind | Co kupuje klient | Jednostka (`Units`) | Jak zużywa |
|---|---|---|---|
| `Tasks` | N wykonań zadania opisanego w tytule pakietu | liczba zadań | `POST /v1/tasks` (asynchronicznie) |
| `Chat` | pulę tokenów na zwykłe API czatu | tysiące tokenów | `POST /v1/chat/completions` (np. GitHub Copilot BYOK) |

Pakiet jest ważny `Access:ValidityDays` dni (domyślnie 30) od zakupu. Wartości domyślne w tym planie to propozycje; ostateczne stałe ustala właściciel (AGENTS.md: stałe sekcji 9 kontraktów).

### 2.2 Klucz API

Jeden klucz na konto klienta (można mieć do 5 aktywnych), działa dla wszystkich agentów, do których klient ma aktywny pakiet. Agent jest wybierany polem `model`.
Format: `gab_` + 40 znaków base62 z `RandomNumberGenerator`. Pokazywany raz, przy utworzeniu.

### 2.3 Publiczne API (serwuje `Gabinety.Booking`)

Autoryzacja: `Authorization: Bearer gab_...`. Błędy w kształcie OpenAI: `{"error":{"message":"...","type":"...","code":"..."}}` (nie `problem+json`).

| Metoda i ścieżka | Działanie |
|---|---|
| `GET /v1/models` | lista agentów, do których klucz ma aktywny pakiet `Chat`; kształt OpenAI: `{"object":"list","data":[{"id":"<slug>","object":"model","owned_by":"gabinety"}]}` |
| `POST /v1/chat/completions` | zgodne z OpenAI; `stream` true lub false; pola `tools`, `tool_choice` i pozostałe przekazywane do dostawcy bez zmian |
| `POST /v1/tasks` | ciało `{"model":"<slug>","offering_id":"<guid>","input":"<tekst 20..20000 znaków>"}`; odpowiedź 202 `{"id":"<guid>","status":"queued"}` |
| `GET /v1/tasks/{id}` | `{"id","status":"queued|running|succeeded|failed","output":"<tekst lub null>","created_at","completed_at"}` |

Kody błędów: 401 `invalid_api_key`; 404 `model_not_found` (brak agenta lub brak aktywnego pakietu właściwego rodzaju); 429 `insufficient_quota` (pakiet wyczerpany); 429 `rate_limit_exceeded`; 502 `upstream_error`.

### 2.4 Dostawca agentów (inna aplikacja)

Ta aplikacja nie uruchamia agentów. Woła zewnętrzną platformę przez jeden interfejs `IAgentUpstream`:

- `POST {Agents:Upstream:BaseUrl}/v1/chat/completions` z nagłówkiem `Authorization: Bearer {Agents:Upstream:ApiKey}`, `model` = slug agenta.
- Zadanie `Tasks` to to samo wywołanie z `stream:false`, jedną wiadomością `user` (treść zadania) i polem `metadata: {"gabinety_offering_id":"<guid>","gabinety_task_id":"<guid>"}`.
- Odpowiedź musi zawierać `usage.total_tokens` (dla strumienia: ostatni fragment, bo wysyłamy `stream_options: {"include_usage": true}`).

W profilu `fake` działa `FakeAgentUpstream` (bez sieci, deterministyczny), więc cały system uruchamia się bez drugiej aplikacji.

### 2.5 Cykl życia zakupionego pakietu (`Booking`)

| Stan | Znaczenie | Przejście |
|---|---|---|
| `PendingPayment` | czeka na płatność (blokada 10 min jak dziś) | płatność → `Confirmed`; brak → `Expired` |
| `Confirmed` | pakiet aktywny | wyczerpany lub po `EndUtc` → `Completed`; rezygnacja → `CancelledByClient`; reklamacja → `Disputed` |
| `CancelledByClient` | rezygnacja z pełnym zwrotem | tylko gdy `UnitsUsed == 0` |
| `Completed` | zakończony | reklamacja do `EndUtc + 72 h` → `Disputed` |
| `Disputed` → `Resolved` | reklamacja rozstrzygnięta przez admina | bez zmian względem dziś |

Stany `LateCancelledByClient`, `CancelledByPractitioner`, `ClientNoShow` zostają w enumie (stare dane, mniej zmian), ale nowy kod do nich nie prowadzi poza wycofaniem agenta przez admina (`CancelledByPractitioner`, istniejący mechanizm).

## 3. Etapy i zadania

Etap A: model danych i zakup. Etap B: klucze i API. Etap C: interfejs. Etap D: dane demo, testy end-to-end, dokumentacja. Etap E (opcjonalny): porządki.

---

### A-01 Pakiet zamiast konsultacji w Directory

**Cel:** oferta ma rodzaj i liczbę jednostek.

**Pliki:** `src/Gabinety.Contracts/Directory/Directory.cs`, `src/Gabinety.Directory/Features/Offerings/Offering.cs`, `OfferingsFeature.cs`, `OfferingFacts.cs`, `src/Gabinety.Directory/Features/Internal/InternalFeature.cs`, `src/Gabinety.Directory/Features/Search/SearchFeature.cs`, `PublicJson.cs`, `src/Gabinety.Booking/Clients/DirectoryClient.cs`, testy w `tests/Gabinety.Directory.Tests`, `tests/Gabinety.Contracts.Tests`.

**Kroki:**
1. W `Directory.cs` dodaj enum `OfferingKind { Tasks, Chat }` z `[JsonConverter(typeof(JsonStringEnumConverter))]`.
2. Do rekordów `Offering`, `OfferingPrivate`, `OfferingRequest` dodaj na końcu pola `OfferingKind Kind = OfferingKind.Tasks, int Units = 1` (wartości domyślne, żeby istniejące wywołania się kompilowały).
3. W encji `Offering` dodaj `Kind` (kolumna `kind`, zapis jako tekst, max 10, domyślnie `Tasks`) i `Units` (kolumna `units`, `int`, domyślnie 1). Dodaj migrację.
4. Walidacja w `OfferingsFeature`: `Units` od 1 do 1000 dla `Tasks`; od 100 do 100000 dla `Chat` (czyli 100 tys. do 100 mln tokenów). Błąd w polu `units`. Walidacji `DurationMinutes` nie zmieniaj.
5. `InternalFeature.GetOffering` i rekord `DirectoryOffering` w Booking: dodaj `Kind`, `Units` oraz `PractitionerSlug`.
6. Pole `MeetingUrl` zostaje w schemacie, nic z nim nie rób.

**Testy:** utworzenie oferty `Chat` z `Units=500` zwraca te pola; `Units=0` daje 422; oferta bez `kind` w JSON ma `Tasks` i 1.

**Gotowe gdy:** build i testy przechodzą; `GET /api/v1/practitioners/{slug}` zwraca `kind` i `units` w każdej ofercie.

---

### A-02 Zakup pakietu bez terminu

**Cel:** `POST /api/v1/bookings` tworzy zakup pakietu; nie ma slotów.

**Pliki:** `src/Gabinety.Contracts/Booking/Booking.cs`, `src/Gabinety.Booking/Features/Bookings/Create/CreateBookingFeature.cs`, `Features/Bookings/Entities/Booking.cs`, `Features/Slots/BookingSlotGuard.cs`, `Features/Bookings/View/BookingViewFeature.cs`, `MyBookingsFeature.cs`, migracje Booking, `tests/Gabinety.Booking.Tests/Create`, `tests/Gabinety.Booking.Tests/Slots`.

**Kroki:**
1. Encja `Booking`: dodaj `Kind` (tekst, max 10), `UnitsTotal` (`long`), `UnitsUsed` (`long`, domyślnie 0), `PractitionerSlug` (max 60). Kolumny `kind`, `units_total`, `units_used`, `practitioner_slug`.
2. Migracja: dodaj kolumny i **usuń ograniczenie `bookings_no_overlap`** (znajdź migrację, która je tworzy: `grep -rn bookings_no_overlap src`). Wiele pakietów jednego agenta musi móc istnieć równocześnie.
3. `CreateBookingRequest`: pole `StartUtc` staje się opcjonalne i ignorowane (`DateTimeOffset? StartUtc = null`). Usuń jego walidację i usuń je z funkcji `Hash`.
4. W `HandleAsync`: `StartUtc = now`, `EndUtc = now + Access:ValidityDays` (domyślnie 30 dni), `UnitsTotal = offering.Units` (dla `Chat` pomnóż przez 1000: przechowujemy tokeny), `Kind`, `PractitionerSlug` z oferty.
5. Usuń z `HandleAsync` sprawdzanie slotu (`SlotCalculator`, `offered.Contains`, kody `slot_taken`, `slot_unavailable`) oraz blokadę praktyka. Zostaw transakcję, idempotencję (oba sprawdzenia rekordu) i obsługę naruszenia unikalności.
6. Zostaw warunki: 404 brak oferty, `own_offering`, `practitioner_not_bookable`, `offering_inactive`, `practitioner_payments_not_ready`.
7. `BookingView`: dodaj na końcu `string Kind = "Tasks", long UnitsTotal = 0, long UnitsUsed = 0`. Wypełnij w obu widokach.

**Testy:** dwa zakupy tej samej oferty przez dwóch klientów w tej samej sekundzie oba dają 201; ten sam `Idempotency-Key` dwa razy daje tę samą odpowiedź; `EndUtc - StartUtc` = 30 dni przy zegarze testowym. Testy slotów przy tworzeniu usuń (funkcja usunięta).

**Gotowe gdy:** build i testy przechodzą; zakup nie wymaga `startUtc`.

---

### A-03 Reguły cyklu życia pakietu

**Cel:** rezygnacja, zakończenie, reklamacja i opinia działają według tabeli 2.5.

**Pliki:** `src/Gabinety.Billing/BookingStateMachine.cs`, `RefundPolicy.cs`, `src/Gabinety.Booking/Features/Lifecycle/Cancel/CancelPlanner.cs`, `CancelFeature.cs`, `Lifecycle/Dispute/DisputeFeature.cs`, `Features/Bookings/View/BookingPermissions.cs`, `Features/Internal/Reviewability/ReviewabilityFeature.cs`, `Jobs/AutoComplete/AutoCompleteJob.cs`, `Notifications/Scheduling/NotificationSchedulingHook.cs`, `Jobs/StatsPush/StatsCalculator.cs`, testy w `tests/Gabinety.Billing.Tests`, `tests/Gabinety.Booking.Tests/Lifecycle`, `Jobs`, `View`.

**Kroki:**
1. Rezygnacja klienta: dozwolona tylko w stanie `Confirmed` i gdy `UnitsUsed == 0`; zawsze pełny zwrot i stan `CancelledByClient`. Regułę 24 h (`RefundPolicy.ForClientCancellation`) przestań wywoływać z kodu Booking; samej klasy `RefundPolicy` nie usuwaj. W `BookingStateMachine.Next` dla `ClientCancels` zwracaj zawsze `CancelledByClient`.
2. Zakończenie: `AutoCompleteJob` przenosi `Confirmed` → `Completed`, gdy `EndUtc <= now` **albo** `UnitsUsed >= UnitsTotal`. Okno 48 h (`Booking:CompleteAfterHours`) przestaje obowiązywać dla tego warunku.
3. Reklamacja: dozwolona w `Confirmed` i `Completed`, do `EndUtc + 72 h`, dla każdego rodzaju. Rodzaj `practitioner_no_show` zostaje w enumie, UI go nie pokaże (zadanie C-03).
4. Opinia (`ReviewabilityFeature` i `BookingPermissions.CanReview`, oba miejsca identycznie): dozwolona, gdy `UnitsUsed > 0` i stan to `Confirmed`, `Completed` albo `Resolved` z `release`, do `EndUtc + 14 dni`.
5. `CanReportNoShow` zawsze `false`. Punkt końcowy `report-client-no-show` zostaw.
6. Przypomnienia o terminie: w `NotificationSchedulingHook` nie planuj przypomnień przed `StartUtc`. Powiadomienia o potwierdzeniu, rezygnacji i reklamacji zostają.
7. `StatsCalculator`: `OpenSlots` wysyłaj jako stałą równą `RankingConstants.SlotsForFullAvailability` (agent jest zawsze dostępny). Przeczytaj plik, żeby znaleźć, skąd wziąć tę wartość; jeśli nie jest dostępna w Booking, dodaj ustawienie `Access:ReportedOpenSlots` z wartością domyślną 10.

**Testy:** rezygnacja przy `UnitsUsed=0` zwraca pełną kwotę; przy `UnitsUsed=1` daje 409 `invalid_transition`; pakiet z `UnitsUsed == UnitsTotal` kończy się w następnym przebiegu zadania; opinia niedozwolona przy `UnitsUsed=0`.

**Gotowe gdy:** build i testy przechodzą.

---

### B-01 Klucze API

**Cel:** klient tworzy, listuje i unieważnia klucze.

**Pliki (nowe):** `src/Gabinety.Booking/Features/ApiKeys/ApiKey.cs`, `ApiKeysFeature.cs`, `ApiKeyHasher.cs`; zmiana: `Persistence/BookingDbContext.cs`, `Features/Internal/Erasure/ErasureFeature.cs`, `Features/Internal/DemoReset/DemoResetFeature.cs`, `src/Gabinety.Contracts/Booking/Booking.cs`. Wzór funkcji: `Features/Lifecycle/Cancel/CancelFeature.cs`.

**Kroki:**
1. Encja `ApiKey` (tabela `api_keys`): `Id`, `ClientUserId` (max 64), `Name` (max 60), `Prefix` (8 znaków po `gab_`), `Hash` (64 znaki hex, indeks unikalny), `CreatedAt`, `LastUsedAt?`, `RevokedAt?`. Migracja.
2. `ApiKeyHasher`: `Generate()` zwraca jawny klucz (2.2); `Hash(string)` zwraca SHA-256 hex małymi literami.
3. Punkty końcowe (polityka `Authenticated`): `POST /api/v1/me/api-keys` ciało `{"name"}` → 201 `{"id","name","prefix","key","createdAt"}` (jedyny raz z polem `key`); `GET /api/v1/me/api-keys` → lista bez `key`; `DELETE /api/v1/me/api-keys/{id}` → 204, ustawia `RevokedAt`. Limit 5 aktywnych: szósty daje 422 `validation_failed`.
4. Usuwanie danych (`ErasureFeature`): usuń klucze użytkownika. Reset demo: usuń klucze użytkowników `demo-`.

**Testy:** klucz zaczyna się od `gab_`; w bazie nie ma jawnego klucza; cudzy klucz przy `DELETE` daje 404; szósty klucz daje 422.

**Gotowe gdy:** build i testy przechodzą.

---

### B-02 Uwierzytelnianie kluczem i zużycie

**Cel:** wspólna podstawa dla punktów `/v1`.

**Pliki (nowe):** `src/Gabinety.Booking/Features/AgentApi/ApiKeyAuthentication.cs`, `OpenAiError.cs`, `EntitlementStore.cs`, `UsageRecord.cs`; zmiana: `src/Gabinety.Booking/Program.cs`, `PublicEdge.cs`, `Persistence/BookingDbContext.cs`. Wzór schematu uwierzytelniania: `AddInternalKeyAuthentication` w `src/Gabinety.ServiceDefaults/Security.cs`.

**Kroki:**
1. Schemat uwierzytelniania `ApiKey`: czyta `Authorization: Bearer gab_...`, liczy skrót, szuka nieunieważnionego klucza, ustawia claim `sub` = `ClientUserId` i claim `api_key_id`. Aktualizuj `LastUsedAt` najwyżej raz na minutę.
2. Polityka `ApiKey` w `Program.cs` (tylko ten schemat). Niepowodzenie zwraca 401 w kształcie OpenAI z kodem `invalid_api_key`.
3. `PublicEdge.Serves`: dodaj ścieżki zaczynające się od `/v1`.
4. `EntitlementStore`:
   - `FindAsync(clientUserId, practitionerSlug, kind, offeringId?)` zwraca najstarszy `Booking` w stanie `Confirmed`, z `EndUtc > now` i `UnitsUsed < UnitsTotal`; `null`, gdy brak.
   - `ConsumeAsync(bookingId, units)` zwiększa `UnitsUsed` jednym poleceniem SQL (`ExecuteUpdateAsync`, bez odczytu i zapisu encji), żeby równoległe żądania się nie nadpisywały. Dla dostawcy InMemory użyj zwykłej modyfikacji encji pod `SemaphoreSlim`.
   - `RefundUnitsAsync(bookingId, units)` zmniejsza `UnitsUsed`, nie poniżej 0.
5. `UsageRecord` (tabela `usage_records`): `Id`, `BookingId`, `ApiKeyId?`, `Kind`, `Units`, `CreatedAt`. Tylko liczby, żadnej treści. Migracja.
6. Limit żądań dla `/v1`: użyj istniejącego mechanizmu z `src/Gabinety.ServiceDefaults/RateLimits.cs`, klucz podziału = `api_key_id`. Przekroczenie zwraca 429 `rate_limit_exceeded` w kształcie OpenAI.

**Testy:** brak nagłówka → 401; unieważniony klucz → 401; 20 równoległych `ConsumeAsync(1)` daje `UnitsUsed == 20`.

**Gotowe gdy:** build i testy przechodzą.

---

### B-03 Dostawca agentów: abstrakcja i wersja fake

**Cel:** `IAgentUpstream` z dwiema implementacjami.

**Pliki (nowe):** `src/Gabinety.Booking/Agents/Abstractions/IAgentUpstream.cs`, `Agents/Fake/FakeAgentUpstream.cs`, `Agents/Http/HttpAgentUpstream.cs`, `Agents/AgentsFeature.cs`; zmiana: `src/Gabinety.AppHost/Program.cs`, `deploy/config-keys.map`. Wzór: `Payments/Abstractions/IPaymentProvider.cs`, `Payments/Mock/MockPaymentProvider.cs` i sposób ich rejestracji.

**Kroki:**
1. Interfejs:
   ```csharp
   public sealed record UpstreamResult(int StatusCode, string Json, long TotalTokens);
   public interface IAgentUpstream
   {
       string Name { get; }
       Task<UpstreamResult> CompleteAsync(string agentSlug, JsonObject body, CancellationToken ct);
       /// Zapisuje do output surowe linie SSE dostawcy; zwraca liczbę tokenów z ostatniego fragmentu usage.
       Task<long> StreamAsync(string agentSlug, JsonObject body, Stream output, CancellationToken ct);
   }
   ```
2. `FakeAgentUpstream`: odpowiedź `"[demo <slug>] Otrzymano N znaków."` (N = łączna długość treści wiadomości; samej treści nie powtarzaj). `TotalTokens` = N/4 + 20. Strumień: trzy fragmenty `chat.completion.chunk`, fragment z `usage`, potem `data: [DONE]`.
3. `HttpAgentUpstream`: `HttpClient` z `Agents:Upstream:BaseUrl`, nagłówek z `Agents:Upstream:ApiKey`, podmienia `model` na slug, przy strumieniu wymusza `stream_options.include_usage = true`. Limit czasu `Agents:Upstream:TimeoutSeconds` (domyślnie 600). Przy starcie bez `BaseUrl` lub `ApiKey` rzuć wyjątek konfiguracji.
4. Wybór: `Agents:Provider` = `Fake` albo `Http`. W `AppHost` w profilu `fake` ustaw `Agents__Provider=Fake`.
5. Dopisz nowe klucze konfiguracji tam, gdzie wymaga tego `scripts/check-config-coverage.sh` (przeczytaj skrypt).

**Testy:** fake zwraca poprawny JSON `chat.completion`; strumień kończy się `data: [DONE]`; `TotalTokens` deterministyczne.

**Gotowe gdy:** build i testy przechodzą, `scripts/check-config-coverage.sh` przechodzi.

---

### B-04 Zwykłe API czatu (GitHub Copilot BYOK)

**Cel:** `GET /v1/models` i `POST /v1/chat/completions`.

**Pliki (nowe):** `src/Gabinety.Booking/Features/AgentApi/ModelsFeature.cs`, `ChatCompletionsFeature.cs`; testy `tests/Gabinety.Booking.Tests/AgentApi`.

**Kroki:**
1. `GET /v1/models` (polityka `ApiKey`): różne `PractitionerSlug` z aktywnych pakietów `Chat` klienta, kształt z 2.3.
2. `POST /v1/chat/completions`:
   a. Wczytaj ciało jako `JsonObject`; limit rozmiaru 2 MB; niepoprawny JSON → 400 `invalid_request_error`.
   b. `model` wymagany. `EntitlementStore.FindAsync(sub, model, "Chat", null)`. Brak pakietu u tego agenta → 404 `model_not_found`. Są pakiety, ale wszystkie wyczerpane → 429 `insufficient_quota`.
   c. `stream != true`: wywołaj `CompleteAsync`, zwróć JSON dostawcy bez zmian z jego kodem statusu; przy 2xx `ConsumeAsync(booking, TotalTokens)` i zapisz `UsageRecord`.
   d. `stream == true`: ustaw `Content-Type: text/event-stream`, `Cache-Control: no-cache`, wyłącz buforowanie odpowiedzi, wywołaj `StreamAsync` z `Response.Body`; po zakończeniu (także po rozłączeniu klienta, w `finally`) zużyj tokeny i zapisz `UsageRecord`.
   e. Wyjątek lub 5xx dostawcy przed pierwszym bajtem → 502 `upstream_error`. Nic nie zużywaj.
3. Zużycie może przekroczyć pulę o jedno żądanie (rozliczamy po fakcie). To celowe: `UnitsUsed` może być większe od `UnitsTotal`; widok pokazuje pozostałe jako `max(0, total - used)`.
4. Loguj wyłącznie: identyfikator pakietu, slug, liczbę tokenów, kod statusu, czas trwania.

**Testy (z `FakeAgentUpstream`):** bez pakietu → 404; z pakietem → 200 i `UnitsUsed` rośnie o `TotalTokens`; strumień zawiera `[DONE]` i zużycie zapisane; pakiet wyczerpany → 429; pole `tools` z żądania dociera do dostawcy bez zmian (test z atrapą `IAgentUpstream`, która zapisuje otrzymane ciało).

**Gotowe gdy:** build i testy przechodzą; w profilu `fake` działa:
```bash
curl -s http://localhost:5102/v1/chat/completions -H "Authorization: Bearer $KEY" -H "Content-Type: application/json" -d '{"model":"demo-bartosz-testowy","messages":[{"role":"user","content":"test"}]}'
```

---

### B-05 API zadań

**Cel:** `POST /v1/tasks`, `GET /v1/tasks/{id}` i wykonawca w tle.

**Pliki (nowe):** `src/Gabinety.Booking/Features/AgentApi/AgentTask.cs`, `TasksFeature.cs`, `src/Gabinety.Booking/Jobs/AgentTasks/AgentTaskJob.cs`; zmiana: `BookingDbContext.cs`, `ErasureFeature.cs`, `DemoResetFeature.cs`. Wzór zadania w tle: `Jobs/HoldExpiry/HoldExpiryJob.cs` i `Jobs/Base/PeriodicJob.cs`.

**Kroki:**
1. Encja `AgentTask` (tabela `agent_tasks`): `Id`, `BookingId`, `ClientUserId`, `PractitionerSlug`, `OfferingId`, `Status` (tekst: `Queued`, `Running`, `Succeeded`, `Failed`), `InputText` (max 20000), `OutputText?`, `Attempts`, `CreatedAt`, `StartedAt?`, `CompletedAt?`. Indeks `(Status, CreatedAt)`. Migracja.
2. `POST /v1/tasks`: walidacja z 2.3; `FindAsync(sub, model, "Tasks", offering_id)`; brak → 404 `model_not_found` lub 429 `insufficient_quota` jak w B-04; `ConsumeAsync(booking, 1)` **od razu**; zapis `Queued`; 202.
3. `GET /v1/tasks/{id}`: tylko właściciel (inaczej 404). To samo udostępnij pod `GET /api/v1/me/tasks/{id}` i listę `GET /api/v1/me/bookings/{bookingId}/tasks` (polityka `Authenticated`) dla panelu web.
4. `POST /api/v1/bookings/{id}/tasks` (polityka `Authenticated`, ciało `{"input"}`): to samo co `POST /v1/tasks`, ale dla zalogowanego klienta z przeglądarki i konkretnego pakietu.
5. `AgentTaskJob` co 5 s: bierze do 5 najstarszych `Queued`, ustawia `Running`, woła `CompleteAsync` (`stream:false`, wiadomość `user` = `InputText`, `metadata` z 2.4), zapisuje treść `choices[0].message.content` jako `OutputText` i `Succeeded`. Błąd: do 2 ponowień (`Attempts`), potem `Failed` i `RefundUnitsAsync(booking, 1)`. Zadania `Running` starsze niż `Agents:TaskTimeoutMinutes` (domyślnie 30) traktuj jak błąd.
6. Usuwanie danych użytkownika: wyczyść `InputText` i `OutputText`. Reset demo: usuń zadania użytkowników `demo-`.

**Testy:** zadanie zużywa 1 jednostkę; po sukcesie `output` niepuste; po trzech błędach `failed` i jednostka wraca; cudze zadanie → 404; treść zadania nie występuje w logach (test z przechwytującym `ILoggerProvider`).

**Gotowe gdy:** build i testy przechodzą.

---

### C-01 Teksty i katalog

**Cel:** interfejs mówi o agentach, nie o programistach i terminach.

**Pliki:** `src/Gabinety.Web/Text/*.cs`, `Components/Pages/Home.razor`, `Pages/Practitioners/*`, `Pages/Gabinet/Profile.razor`, `Pages/Static/*`, `Components/Layout/*`, `Components/Shared/Avatar.razor`, `src/Gabinety.Web/Api/Dtos.cs`, testy `tests/Gabinety.Web.Tests`.

**Kroki:**
1. Zamiany w tekstach: „programista/specjalista" → „agent"; „zarezerwuj termin" → „wykup dostęp"; „konsultacja" → „pakiet"; „rezerwacja" → „dostęp"; „spór" → „reklamacja". Przejdź każdy plik `Text/*.cs` linia po linii; nie używaj ślepego zamieniania.
2. Nowy komponent `Components/Shared/AiBadge.razor`: plakietka „Agent AI". Pokaż ją przy nazwie na karcie (`PractitionerCard.razor`), na profilu i w szczegółach dostępu. Style według `docs/ui/DESIGN-CONTRACT.md` i `docs/ui/tokens.css`.
3. Profil (`Profile.razor`): usuń `SlotPicker`. Każda oferta pokazuje: tytuł, opis, cenę oraz „10 zadań, typowy czas ok. 30 min" (`Tasks`) albo „500 tys. tokenów, API zgodne z OpenAI" (`Chat`), ważność „30 dni" i przycisk „Wykup dostęp" prowadzący do `/rezerwacja/{OfferingId}`.
4. Na profilu dodaj sekcję „Jak się połączyć" z adresem bazowym API, nazwą modelu (slug) i przykładem `curl`.
5. `DTO` w `Api/Dtos.cs`: dodaj `Kind`, `Units`.
6. Adresów stron (`/programisci`, `/gabinet/{Slug}`) w tym zadaniu nie zmieniaj.
7. `Content/Legal/*.md`, „Jak to działa", „Cennik": przepisz pod nowy model; zaznacz na górze regulaminu i polityki `<!-- DO WERYFIKACJI PRAWNEJ -->`.

**Testy:** popraw asercje tekstów w `tests/Gabinety.Web.Tests`; dodaj test, że karta i profil zawierają plakietkę.

**Gotowe gdy:** build i testy przechodzą; w `src/Gabinety.Web` polecenie `grep -rni "zarezerwuj\|termin\b" Components Text` nie zwraca tekstów widocznych dla użytkownika.

---

### C-02 Ścieżka zakupu

**Cel:** formularz zakupu bez wyboru terminu, potwierdzenie z instrukcją.

**Pliki:** `Components/Pages/Booking/BookingFormPage.razor`, `BookingSteps.razor`, `HoldTimer.razor`, `BookingConfirmation.razor`, `MockCheckout.razor`, `StripeReturn.razor`, `src/Gabinety.Web/Api/ClientAreaClients.cs`, `Hosting/CalendarEndpoints.cs`, `Hosting/IcsBuilder.cs`, `Text/BookFlowText.cs`.

**Kroki:**
1. Formularz: podsumowanie pakietu (agent, tytuł, jednostki, ważność, cena) i pole briefu z nową etykietą „Do czego chcesz użyć agenta?" (walidacja 20 do 2000 znaków zostaje, asystent briefu zostaje). Brak pola terminu; żądanie bez `startUtc`.
2. Kroki: „Pakiet" → „Płatność" → „Gotowe".
3. Potwierdzenie: stan pakietu, link „Utwórz klucz API" (`/moje/klucze`), dla `Chat` instrukcja podłączenia (adres bazowy, model), dla `Tasks` przycisk „Zleć pierwsze zadanie" (do szczegółów dostępu).
4. Usuń pobieranie pliku kalendarza: `CalendarEndpoints.cs`, `IcsBuilder.cs`, ich rejestrację i testy.

**Testy:** popraw `tests/Gabinety.Web.Tests`; test, że formularz nie zawiera wyboru terminu.

**Gotowe gdy:** build i testy przechodzą; w profilu `fake` da się przejść: profil → wykup → płatność próbna → potwierdzenie.

---

### C-03 „Moje dostępy" i klucze

**Cel:** panel klienta.

**Pliki:** `Components/Pages/Bookings/MyBookings.razor`, `BookingDetail.razor`, `BookingBadge.razor`, `ConsultationSummary.razor`; nowe: `Components/Pages/Account/ApiKeys.razor` (`/moje/klucze`), `Api/ClientAreaClients.cs` (metody kluczy i zadań), `Text/BookingsText.cs`.

**Kroki:**
1. Lista (`/moje/rezerwacje`, adres zostaje): agent, pakiet, pasek „wykorzystano X z Y", „ważny do", stan.
2. Szczegóły: to samo oraz akcje „Zrezygnuj" (gdy `CanCancel`), „Zgłoś reklamację" (bez rodzaju „niestawienie się"), „Wystaw opinię" (gdy `CanReview`), „Wykup ponownie". Usuń link do spotkania i przycisk niestawienia się.
3. Dla `Tasks`: formularz „Zleć zadanie" (pole tekstowe 20 do 20000 znaków, `POST /api/v1/bookings/{id}/tasks`) i lista zadań ze stanem i wynikiem. Wynik to tekst użytkownika: renderuj jako zwykły tekst, nigdy `MarkupString`.
4. `/moje/klucze`: lista (nazwa, prefiks, utworzono, ostatnio użyto), „Utwórz klucz" (pełny klucz pokazany raz, z przyciskiem kopiowania i ostrzeżeniem), „Unieważnij" z potwierdzeniem. Link w nagłówku konta.
5. Strona wymaga logowania; wzoruj się na `MyBookings.razor`.

**Testy:** bUnit: klucz pokazany po utworzeniu i nieobecny po ponownym wczytaniu listy; lista zadań renderuje wynik bez interpretacji HTML.

**Gotowe gdy:** build i testy przechodzą.

---

### C-04 Panel dostawcy i admina

**Cel:** usunąć kalendarz, dostosować oferty.

**Pliki:** `Components/Pages/PractitionerArea/*`, `Api/PractitionerClients.cs`, `Text/PractitionerArea.cs`, `Components/Pages/Admin/Disputes.razor`, `Text/Admin/PlAdmin.cs`.

**Kroki:**
1. Usuń stronę `Availability.razor` (`/gabinet-programisty/dostepnosc`) i link do niej w `PractitionerLayout.razor`.
2. `Offers.razor`: dodaj pola „Rodzaj pakietu" (Zadania / Czat API) i „Liczba jednostek"; usuń pole linku do spotkania; etykieta czasu: „Typowy czas wykonania".
3. `ProfileEdit.razor`: usuń pole domyślnego linku do spotkania. Pola NIP i GitHub zostają.
4. `PractitionerHome.razor`: zamiast najbliższych terminów pokaż liczbę aktywnych pakietów i ostatnie zakupy (dane z `GET /api/v1/me/bookings`).
5. Admin: „Spory" → „Reklamacje" w tekstach; w szczegółach pokaż `UnitsUsed` / `UnitsTotal`.

**Gotowe gdy:** build i testy przechodzą.

---

### D-01 Dane demonstracyjne

**Pliki:** `tools/Gabinety.DemoSeeder/Directory/DemoCatalog.cs`, `DirectoryApi.cs`, `DirectorySeedStep.cs`, `Booking/*`, `Reputation/ReputationPlan.cs`, `tests/Gabinety.Integration.Tests/Seeder`, `tools/Gabinety.DemoSeeder/README.md`.

**Kroki:**
1. Persony zostają bez zmiany nazw i slugów. Przepisz `Headline` i `Bio` w pierwszej osobie agenta, np. Bartosz: „Agent AI od migracji z .NET Framework do nowoczesnego .NET. Pod spodem zespół wyspecjalizowanych agentów: analityk zależności, planista etapów i recenzent ryzyka." Bez e-maili, telefonów, znaków `@` i długich ciągów cyfr.
2. Każdy zatwierdzony agent ma: jeden lub dwa pakiety `Tasks` (dotychczasowe tytuły, np. „Plan migracji z .NET Framework", `Units` 1 lub 5) oraz jeden pakiet `Chat` („Dostęp do API czatu, 500 tys. tokenów", `Units=500`, cena 9900). Rozszerz `DemoOffering` o `Kind` i `Units`.
3. Usuń kroki ustawiające dostępność. Zakupy demo: bez terminu; kilka z `UnitsUsed > 0` (przez zlecenie zadań przez API, nie przez zapis do bazy), żeby opinie były dozwolone.
4. Dodaj krok tworzący klucz API dla `demo-client` i wypisz w logu seedera tylko jego prefiks.

**Gotowe gdy:** `dotnet run --project src/Gabinety.AppHost --launch-profile fake` i `scripts/check-fake-profile.sh` przechodzą; katalog pokazuje sześciu agentów.

---

### D-02 Ścieżki end-to-end

**Pliki:** `tests/e2e/journeys/*.spec.ts`, `tests/e2e/harness/*`, `tests/e2e/journeys/support/*`.

| Plik | Nowy scenariusz |
|---|---|
| j1 | przeglądanie kategorii i profilu; plakietka „Agent AI"; brak wyboru terminu |
| j2 | dostawca zakłada agenta, dodaje pakiet `Chat`, admin zatwierdza, agent widoczny |
| j3 | klient kupuje pakiet `Tasks`, płaci, zleca zadanie w panelu, widzi wynik, wystawia opinię |
| j4 | asystent wyłączony: zakup nadal działa |
| j5 | zakup i rezygnacja przed użyciem: pełny zwrot; po użyciu przycisk rezygnacji niewidoczny |
| j6 | reklamacja i częściowy zwrot przez admina |
| j7 | obce pochodzenie odrzucone (bez zmian) |
| j8 (nowy) | klient kupuje pakiet `Chat`, tworzy klucz w `/moje/klucze`, test woła `GET /v1/models` i `POST /v1/chat/completions` (zwykłe i strumień), zużycie widoczne w panelu |

**Gotowe gdy:** `pnpm --dir tests/e2e test -- --trace off` przechodzi lokalnie w profilu `fake`.

---

### D-03 Dokumentacja i wdrożenie

1. `README.md`, `docs/GETTING-STARTED.md`: nowy opis produktu, przykład `curl`, lista ścieżek J1 do J8.
2. Nowy `docs/operations/AGENT-API.md`: opis API z 2.3, kontrakt dostawcy z 2.4, konfiguracja `Agents:*` i `Access:*`.
3. Nowy `docs/operations/COPILOT-BYOK.md`: jak podłączyć agenta w GitHub Copilot jako dostawcę zgodnego z OpenAI (adres bazowy `https://<host>/v1`, klucz, model = slug). **Sprawdź aktualną dokumentację GitHub Copilot przed napisaniem kroków; nie pisz z pamięci.** Zaznacz ograniczenia: dotyczy czatu, tryb agenta wymaga obsługi `tools` przez dostawcę.
4. `deploy/fly/booking.fly.toml`, `deploy/secrets.map`, `deploy/config-keys.map`: nowe ustawienia; `Agents:Upstream:ApiKey` jako sekret. Sprawdź `scripts/check-fly-toml.sh` i `scripts/check-private-topology.sh`.
5. Nowa propozycja ADR `docs/adr-proposals/0013-agent-marketplace.md`: decyzje z sekcji 4 tego planu.

---

### Etap E (opcjonalny, dopiero po D): porządki

Każdy punkt to osobne zadanie; wykonuj tylko na polecenie właściciela.

- E-01 Usuń martwy kod terminów: `src/Gabinety.Scheduling`, `Features/Slots`, `Features/Availability`, `ReportClientNoShowFeature`, tabele dostępności (migracja), ich testy.
- E-02 Zmień adresy stron: `/programisci` → `/agenci`, `/gabinet/{Slug}` → `/agent/{Slug}`, `/rezerwacja/...` → `/dostep/...`, `/moje/rezerwacje` → `/moje/dostepy`, `/gabinet-programisty` → `/panel-agenta`; stare adresy przekierowują 301.
- E-03 Płatności rzeczywiste: sprzedawcą jest platforma, więc usuń wypłaty i konta połączone (`Payouts`, `PaymentAccount*`, `PayoutJob`, `FeeHook`, `IConnectedAccountProvider`); wymaga nowej decyzji ADR zastępującej propozycję 0011.
- E-04 Jeden dostawca, wielu agentów: zdejmij ograniczenie „jedno konto = jeden profil" (`practitioner_exists`).
- E-05 Wydziel `/v1` do osobnej usługi, gdy ruch API zacznie obciążać Booking.
- E-06 Zmiana nazw w kodzie (`Practitioner` → `Agent`, `Booking` → `Access`).

## 4. Decyzje do potwierdzenia przez właściciela

| Nr | Decyzja | Przyjęte w planie |
|---|---|---|
| 1 | Ważność pakietu | 30 dni |
| 2 | Rezygnacja | pełny zwrot tylko przed pierwszym użyciem |
| 3 | Rozliczanie czatu | suma tokenów wejścia i wyjścia, jedna stawka |
| 4 | Liczba kluczy | do 5 na konto, wspólne dla wszystkich agentów |
| 5 | Gdzie działa `/v1` | w usłudze Booking (etap E-05 to zmienia) |
| 6 | Konto dostawcy | jedno konto techniczne na agenta (etap E-04 to zmienia) |
| 7 | Wypłaty i prowizja | bez zmian w profilu `fake`; do usunięcia przed prawdziwymi płatnościami (E-03) |
| 8 | Oznaczenie AI | plakietka „Agent AI" wszędzie, gdzie widać nazwę agenta |
