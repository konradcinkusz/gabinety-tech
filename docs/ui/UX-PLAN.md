# UX review of the Blazor web app and the plan to reach a market-leading experience

Date of the run: 2026-11-22. Target: `src/Gabinety.Web` (Blazor Web App, static server rendering) at commit `0efe0ca`, served by the fake profile.
Earlier documents: [`UI-AUDIT.md`](UI-AUDIT.md) audited the retired Next.js app; [`DESIGN-CONTRACT.md`](DESIGN-CONTRACT.md) is the contract the port was to implement. This document is the audit of the port itself and the task list that follows from it.

## 1. What was run

| Item | Detail |
|---|---|
| Code read | every page, layout and shared component under `src/Gabinety.Web/Components`, `wwwroot/app.css`, `wwwroot/tokens.css` |
| Live run | `tests/e2e/tools/ui-audit/audit.mjs` against the fake profile: 144 page visits (visitor, client, practitioner, administrator; 1440 and 390 px; light and dark) plus the booking, cancel, dispute, registration and onboarding flows. The script in the repository no longer runs (it signs in through `POST /api/auth/login`, which the Blazor host does not have); it was run from a temporary copy with the form sign-in of `tests/e2e/harness/auth.ts`. Nothing of that copy is committed: UX-003 fixes the tool and regenerates `docs/ui/audit/` |
| Not captured | the review form and its thank-you state (the flow found no booking offering `Wystaw opinię`), `/gabinet-programisty/platnosci`, `/logowanie/kod`, the Stripe return page, the resolve step of a dispute. Findings about those pages come from the code only |

### Mechanical result

| Check | Result |
|---|---|
| Horizontal page scroll at 390 px | none on 72 phone visits |
| axe (WCAG 2.1 A/AA + best practice), light and dark | 0 serious or critical. One moderate: `heading-order` on `/gabinet-programisty/oferty` (an `h3` without an `h2` above it) |
| One `h1` per page, labelled controls, focus indicator, layout shift | all pass |
| Dark theme | works on every page |
| Route guards | visitor -> `302 /logowanie?redirect=…`; client on `/admin/*` -> `403` with the forbidden page |
| Tap targets under 44 px at 390 px | on most pages: chips 34 px high (filters, slot days), text inputs and selects 40 to 42 px, the registration consent checkbox 24 px, inline links 17 to 24 px. Worst pages: category list (26), availability editor (25), offers (18) |

**Reading:** the port fixed almost every defect of the old audit (session-aware header, dark theme, form summaries with inline errors, names in booking views, confirmation before disabling an offer, forbidden page). The app is correct and accessible. It is not yet *good*: it looks like an unstyled prototype, the path from landing to payment has avoidable friction and one dead end, and nothing reacts until a full page round trip. The gap to the best booking products (Calendly, Cal.com, Booksy, Znany Lekarz, intro.co) is in sections 2 and 3.

## 2. Top findings (what costs conversion and trust first)

1. **Dead-end primary button on the profile.** `Zarezerwuj` on every offer links to `/rezerwacja/<offer>?gabinet=<slug>` with no `start`, and `BookingFormPage` answers "Nie wybrano terminu albo adres jest niepełny" (`Profile.razor` line 65, `BookingFormPage.razor` `_missing`). The most prominent button of the page leads to an error.
2. **The design foundation was never finished.** `wwwroot/tokens.css` still holds the "NEUTRAL placeholder values"; the frozen block `docs/ui/tokens.css` (different names and values) was never applied. `app.css` is three stacked blocks that redefine the same classes (`.btn--ghost` twice with different colours, `.field__error` three times, `.form`, `.form-actions`, `.btn--secondary` twice). The administrator area carries a parallel vocabulary (`admin-*`, its own badge and button variants). A third of the contract's classes do not exist (`.container`, `.grid-2`, `.table`, `.table--cards`, `.skeleton`, `.spinner`, `.loading`, `.breadcrumb` and `.segmented` as shared classes). Result: white page, white cards, hairline borders, no depth or hierarchy.
3. **No identity.** `wwwroot` has two CSS files: no favicon, no logo (one exists in `docs/assets/logo.svg`), no social image, no avatar of any kind. A marketplace that sells trust in people shows no faces, not even initials.
4. **Practitioner cards are indistinguishable.** All six read `Zweryfikowany` + `Nowy` + `od 120,00 zł 30 min, 60 min`. No avatar, no technologies, no next free time, no sort, only the name is clickable.
5. **Slot picking is slow and unclear.** Day chips carry full names (`poniedziałek, 30 listopada`), sit in a horizontal scroller with no selected-state contrast and no hint that it scrolls; every day is a full page load with an anchor jump; nothing says when the first free time is; `Pokaż kolejne dni` is a full-width button that outweighs the times.
6. **The refund rule is hidden until cancellation.** The booking form and checkout show price and the 10-minute hold, but not "free cancellation until …". The 24-hour rule first appears in the cancel panel, after the money is spent.
7. **Nothing responds without a round trip.** The only interactive component is the checkout clock. Counters (`0 / 2000`) do not move while typing, submit buttons show no pending state and accept a second click, the assistant, "add hours", "pick a day" and the status filter each reload the page. There is no navigation progress indication.
8. **After payment the user is left alone.** Confirmation: a green line, a table, an unexplained `Link do spotkania`. No calendar file, no "what happens next", the `h1` is the generic `Rezerwacja`. `Moje rezerwacje` is a flat list in which a cancelled booking is first and the next consultation is not highlighted.
9. **Navigation hides the product.** The header has one link (`Programiści`); `Jak to działa`, `Cennik` and `Zasady rankingu` exist and are reachable only from the bottom of the home page. Every signed-in client sees `Panel programisty`. The footer has two legal links. There is no password reset, no help or contact page.
10. **Practitioner tools are forms, not tools.** Availability is eight rows of three selects where each "add" is a round trip; a draft dashboard stacks two checklists (11 cards) that say the same; an approved dashboard shows four green `Gotowe` cards and no agenda, no link to the public profile, no numbers.

## 3. Findings per area

Severity: **B** blocks a task, **M** hurts conversion, trust or a clear usability rule, **m** polish. "Shot" names the capture of the run (regenerated by UX-003).

### 3.1 Foundation and shell

| ID | Sev | Finding |
|---|---|---|
| S-1 | M | Placeholder tokens; contract tokens not applied; `--color-surface` is used both as "sunken" and as "button fill" (finding 2) |
| S-2 | M | `app.css` duplicates and conflicts; later blocks win by order, so a change in one place silently does nothing |
| S-3 | M | Contract 3.2 `--tap` 44 px is not met by inputs (`min-height: 2.5rem`), selects, chips, checkbox; focus ring is 2 px (contract: 3 px) |
| S-4 | M | No favicon, logo, `theme-color`, social image |
| S-5 | M | Header: one link; account trigger is the words `Twoje konto` (no e-mail, no initials); `Panel programisty` for every account; phone menu is a small dropdown |
| S-6 | m | Footer: two links; no product, help or practitioner links |
| S-7 | M | No pending state on any submit, no protection against a second click on `Zapłać`, `Zapisz`, `Tak, anuluj rezerwację` |
| S-8 | m | Flash is an unstyled strip pushed against the header; no dismiss; the Blazor reconnect state is a bare bar |
| S-9 | m | `ui-audit` is broken and not in CI, so contract section 10 item 4 is not enforced |

### 3.2 Visitor: finding and choosing

| ID | Sev | Page | Finding |
|---|---|---|---|
| V-1 | M | `/` | Hero is a heading and a paragraph; all eight tiles carry the same sentence (`Zweryfikowani programiści i wolne terminy`); the "trust row" is three plain lines; no practitioners shown, no price range, no questions and answers, nothing for a programmer who wants to join |
| V-2 | M | `/programisci` | Finding 4. Price `od 120,00 zł` is body text; `Wyniki: 6` is plain text; the rating badge and `Nowy` look like the verification badge |
| V-3 | M | `/programisci` filters | Nine problem chips in a 16 rem column; the segmented controls wrap and break their joined shape (`60 min` and `do 500,00 zł` drop to a second row as orphans); technology cannot be filtered before a problem is chosen; on the phone the open filter panel puts the first result about 900 px down |
| V-4 | m | `/programisci?q=…` empty | A warning-coloured box and a detached button; the active filters are not listed |
| V-5 | B | `/gabinet/<slug>` | Finding 1 |
| V-6 | M | `/gabinet/<slug>` | One column: header, text, offers, reviews. No sticky booking panel (contract 5.4), five grey badges in a row, `Portfolio` and `GitHub` look like primary actions, technologies of the offers are not shown although `OfferingDto.TechSlugs` carries them, reviews have no summary |
| V-7 | M | slot picker | Finding 5. Also only Warsaw time is shown; a visitor abroad gets no local time |
| V-8 | M | `/logowanie`, `/rejestracja` | The form is left-aligned at 26 rem on an empty page; arriving from a slot nothing says "sign in to finish the booking of …"; no show-password control; the password rule is a sentence, not a checklist; no "forgot password" (no such flow exists in the host) |
| V-9 | M | `/zasady-rankingu` at 390 px | The example table breaks its first column into one letter per line (`S / kł / a / d / ni / k`): `overflow-wrap: anywhere` on `body` plus a narrow column. The table is unreadable on a phone |
| V-10 | m | `/jak-to-dziala` | The numbered steps render as run-on lines (lists have `list-style: none` globally), so four steps read as one paragraph; no visuals, no call to action at the end |
| V-11 | m | `/cennik` | Correct and clear, but one example only; no calculator, no call to action |
| V-12 | m | 404 | Heading and one sentence; the search and category links of contract 5.10 are missing (the profile 404 has them) |

### 3.3 Client: booking and after

| ID | Sev | Page | Finding |
|---|---|---|---|
| C-1 | M | booking form | No step indication (where am I, what is left); no cancellation terms (finding 6); no way to change the time without the browser's back button; counter is static; the assistant reloads the page and its result (`Sugerowana kategoria`, `Warto jeszcze opisać`) is small grey text |
| C-2 | m | checkout (mock) | Fine for a demonstration page; the summary card is half the width of the notice above it |
| C-3 | M | confirmation | Finding 8 |
| C-4 | M | `/moje/rezerwacje` | Flat list, no "upcoming / past"; the label `Status` wraps to `Statu` / `s`; a `Filtruj` button for one select; only `Szczegóły` is clickable; `Klient / Programista` tabs for users who have no practitioner profile |
| C-5 | M | booking detail | The cancel "dialog" is a card appended below the content after a reload (`?action=cancel`); it states the outcome and then repeats the general rule; the meeting link is a small inline link with no time context; no status history; `Anuluj rezerwację` floats alone under the cards; a completed booking does not lead with `Wystaw opinię` |
| C-6 | m | review form | Rating is five numbered boxes; no stars, no word for the chosen value |
| C-7 | m | `/konto` | E-mail confirmation as a full-width green callout; consents read `wymaga ponownej akceptacji` with no explanation; the delete-account form is always open with a full-width password field; no display name, no password change |

### 3.4 Practitioner

| ID | Sev | Page | Finding |
|---|---|---|---|
| P-1 | M | dashboard (draft) | Two checklists (`Co dalej` 4 cards, `Lista kroków` 7 cards) describe the same work; progress is the text `Postęp: 1 z 7` |
| P-2 | M | dashboard (approved) | Four `Gotowe` cards; no upcoming consultations, no link to the public profile, no completed count or rating |
| P-3 | M | availability | Finding 10. Also a native date field that reads `mm/dd/yyyy`, `Zapisz` at the very bottom, unsaved state said only in a grey sentence; on the phone each rule is two rows and the page is 1900 px |
| P-4 | m | offers | The new-offer form is always open under the list; twelve technology checkboxes with no "2 z 4" count; no preview of the card a client will see; axe `heading-order` |
| P-5 | m | profile edit | Six stacked fields, no preview; counters static |
| P-6 | m | area tabs at 390 px | The fifth tab is cut (`Pł`) with no hint that the strip scrolls |

### 3.5 Administrator

| ID | Sev | Page | Finding |
|---|---|---|---|
| A-1 | m | shell | Own class vocabulary (S-2); section tabs and status switches show no counts, so the size of a queue is known only after opening it |
| A-2 | M | `/admin/ksiega` | The `Rezerwacja` column shows the first 8 characters of a UUIDv7, which is the timestamp part: all eleven rows read `01a2039c`. It identifies nothing. Period text is ISO (`2026-11-01 – 2026-11-22`), no totals |
| A-3 | m | `/admin/spory` | Good content; the booking id is the only reference to the booking and is not a link |

## 4. Decisions the owner must take first

| # | Decision | Recommendation | Tasks that wait for it |
|---|---|---|---|
| D-1 | May the host ship one small first-party script (`wwwroot/enhance.js`, same origin, already allowed by `script-src 'self'`) for progressive enhancement? Contract rule 1 says "no JS". | Yes. Every page keeps working without it; it only adds live counters, pending buttons, native `<dialog>`, show-password, auto-submit of a select. The alternative, an interactive circuit per page, costs a websocket per visitor and reacts with network latency. Amend contract rule 1 and ADR-0010 in the same PR | UX-005 and the "with enhance.js" items of other tasks |
| D-2 | Who sees `Panel programisty` in the header? (open question 1 of the old audit) | Only accounts with a practitioner profile; everyone else gets `Zostań programistą` in the account menu and the footer | UX-010 |
| D-3 | Faces: initials now, photos later? | Initials avatar now (no backend). Photos need a Directory contract change (UX-070) | UX-012, UX-014 |
| D-4 | A user-controlled light/dark switch (the contract excludes it) | Later, after wave 1; the system setting already works | UX-061 |

**Decided by the owner on 2026-11-22: all four recommendations accepted** (D-1 yes, D-2 practitioners only, D-3 initials now, D-4 after wave 1).

**Decided on 2026-11-22 after wave 2 (owner: "decide yourself, merge when green"):**
- The header's `GET /api/v1/me` once per signed-in session per minute (cached 60 s, 10 s on failure, failing soft to "not a practitioner") is **accepted**.
- **UX-061 (theme switch) is dropped.** The system setting already works, and a server-rendered choice needs a cookie-varying output cache on every public page plus a second script file against D-1. Revisit only if users ask.
- **Wave 3 stays open** and each item starts in `gabinety-tech-spec` (contract first); nothing in this repository can start it. Order of value for conversion: UX-071 (sort, next free time and technologies on the list card), UX-072 (password reset), UX-073 (reschedule), then UX-070, 074, 075, 076.
- Before spending on wave 3, measure the funnel (list, profile, slot, form, paid): the product has no analytics yet.

## 5. Rules for every task (so the sessions do not collide)

1. One task, one branch, one PR. A task changes **only the files it owns** (column "Owns"). If it needs a change elsewhere it stops and says so in the PR; it does not edit another task's file.
2. **Shared CSS belongs to UX-002.** A page task writes its rules in its own `Foo.razor.css` with tokens only. It does not add to `app.css` or `tokens.css`. A missing shared class is requested from UX-002's owner, not invented (contract rule 4).
3. Polish strings stay in the text class of the area the task owns (`Text/*.cs`); a task appends inside its own nested class and does not reformat the file.
4. The hooks of [`DESIGN-CONTRACT.md` section 9](DESIGN-CONTRACT.md) are frozen. A task that must change one changes the journey or page object in the same PR and says why. Hooks most at risk are named per task.
5. Every page still works with JavaScript off (links and form posts); enhancement is additive.
6. Done means: `dotnet build -warnaserror && dotnet test --filter "tier!=integration"` green; bUnit test for each new component or state; the task's pages captured at 1440 and 390 px in light and dark and attached to the PR; no new axe serious/critical; no horizontal scroll at 390 px; tap targets of the touched pages at least 44 px.
7. Repository rules of `AGENTS.md` apply (money as `long` grosz, time from `TimeProvider`, no user free text in logs, synthetic fixtures).

## 6. Tasks

Waves: **0** foundation (merge first), **1** pages (all parallel once wave 0 is in), **2** cross-cutting polish, **3** needs a backend or contract change in `gabinety-tech-spec` first. Size: S up to half a day, M about a day, L two days.

### Wave 0: foundation

Status: **UX-001, UX-002, UX-003 and UX-005 are done** (UX-005: `wwwroot/enhance.js`, the contract and ADR-0010 amended, `harness/enhance.spec.ts`; the `<script>` line in `App.razor` is added when UX-004 is merged) (same PR as this document). UX-003: the audit signs in through the host's form post, covers `/rejestracja/sprawdz-skrzynke` and `/gabinet-programisty/platnosci`, runs the review flow before the dispute flow (the dispute consumed the only completed booking), `docs/ui/audit/` holds 99 captures of the Blazor host, and `journeys/ui-gate.spec.ts` asserts no horizontal scroll and no serious axe violation at 390 px in both schemes in the `e2e` job (tap targets are not asserted yet; `/logowanie/kod` needs a pending challenge and is not covered). What UX-002 left to the page tasks, because the rules live in scoped sheets it does not own: the filters' segmented controls still wrap (UX-013), the header sits on the sunken surface (UX-010), the administrator area still carries its `admin-*` copies (UX-050), `.flash` stays in `MainLayout.razor.css` (UX-060). Remaining tap targets under 44 px are inline links and 24 px checkboxes inside 44 px label rows, per page.

| ID | Task | Owns | Depends | Size |
|---|---|---|---|---|
| UX-001 | Apply the real tokens | `wwwroot/tokens.css`, `docs/ui/tokens.css`, token list in `StyleContractTests`, `tests/e2e/tools/ui-audit/contrast.py` | none | S |
| UX-002 | One shared stylesheet | `wwwroot/app.css`, class list in `StyleContractTests` | UX-001 | L |
| UX-003 | Repair the UI audit and put it in CI | `tests/e2e/tools/ui-audit/**`, `docs/ui/audit/**`, the journeys job of `.github/workflows/ci.yml` | none | M |
| UX-004 | Brand assets and document head | new files in `wwwroot/` (favicon, icons, social image, manifest), `Components/App.razor`, `Components/Shared/PageMeta.razor` | none | S |
| UX-005 | Progressive-enhancement script (D-1) | `wwwroot/enhance.js`, its tests, one `<script>` line in `App.razor` (coordinate with UX-004), `Hosting/SecurityHeaders.cs` only if a header must change, contract rule 1 and ADR-0010 text | D-1 | M |

**UX-001.** Replace the placeholder values with the frozen ones of `docs/ui/tokens.css` (page `#f6f7f9`, cards white, sunken `#eceff4`, both themes). Keep the token **names the app uses** (`--color-*`, `--text-*`) so no component changes; add the tokens the contract has and the app lacks (`surface-sunken`, `accent-soft-text`, `danger-contrast`, `scrim`, `--tap`, `--form-width`, `--prose-width`). Make `docs/ui/tokens.css` and the shipped file identical and have `contrast.py` and the contrast test read the shipped one. Done when the 28 pairs of contract 3.3 pass in both themes and the page background differs from the card surface.

**UX-002.** Rewrite `app.css` as one block per class, no duplicate selectors (add a test that fails on a duplicate top-level selector). Provide the full vocabulary of contract section 4 and move the generic parts of the administrator sheet here later (UX-050 removes them there). Apply `--tap` to inputs, selects, chips, buttons, checkbox rows; focus ring 3 px; the type scale of contract 3.4 (`h1` 36 px from 40 rem); restore list markers inside `.prose` and for `ol.steps` (V-10); remove `overflow-wrap: anywhere` from `body` and apply it to user text only (V-9); style `.flash`, `.dialog::backdrop`, `.skeleton`, `.spinner`, `.btn[aria-busy]`. No class is renamed. Done when every page renders without visual regression in the audit captures and the tap-target count at 390 px is zero for shared controls.

**UX-003.** Sign in with the form post of `harness/auth.ts`; add the missing routes (`/gabinet-programisty/platnosci`, `/logowanie/kod`, `/rejestracja/sprawdz-skrzynke`); make the review flow create its own completed booking; regenerate `docs/ui/audit/` (the committed captures show the Next.js app); run in the journeys job with assertions: no horizontal scroll, no axe serious/critical. Tap targets become an assertion after UX-002.

**UX-004.** SVG favicon from `docs/assets/logo.svg`, PNG fallbacks, `apple-touch-icon`, `theme-color` for both schemes, web manifest, a default `og:image` (and `twitter:card`) in `PageMeta`. All same-origin (CSP `img-src 'self' data:`).

**UX-005.** One module, no dependency, under 8 KB, initialised on load and on Blazor's `enhancedload`. Behaviours keyed on existing markup so pages need no change: (a) `.field__counter` next to a `.field__control` updates while typing; (b) a submitting `form[method=post]` sets `aria-busy="true"` on the clicked button and ignores a second submit; (c) `[data-enhance="dialog"]` panels open as a native `<dialog>` with focus return; (d) `[data-enhance="autosubmit"]` selects submit their GET form; (e) `[data-enhance="reveal"]` toggles a password field; (f) `[data-enhance="copy"]` copies a link. Each behaviour has a no-script fallback that is the current behaviour.

### Wave 1: pages (parallel)

| ID | Task | Owns | Depends | Size |
|---|---|---|---|---|
| UX-010 | Header, footer, navigation | `Components/Layout/SiteHeader.razor(.css)`, `SiteFooter.razor(.css)`, `SiteNavigation.cs`, `Pl.Nav`, `Pl.Site`, navigation tests | UX-002, D-2 | M |
| UX-011 | Home page | `Pages/Home.razor(.css)`, `Pl.Home`, `PlPages.HomeExtra` | UX-002 | M |
| UX-012 | Practitioner card and result list | `Pages/Practitioners/PractitionerCard.razor(.css)`, `PractitionerList.razor(.css)`, `ListView.cs`, `Pl.Card`, `Pl.ListPage`, new `Components/Shared/Avatar.razor(.css)` | UX-002, D-3 | M |
| UX-013 | List filters | `Pages/Practitioners/FilterNav.razor(.css)`, `ListState.cs`, `Pl.Filters` | UX-002 | M |
| UX-014 | Public profile page | `Pages/Gabinet/Profile.razor(.css)`, `ProfileNotFoundBlock.razor`, `PlPages.Profile` | UX-002; uses `Avatar` of UX-012 (inline initials until it lands) | L |
| UX-015 | Slot picker | `Pages/Gabinet/SlotPicker.razor(.css)`, `SlotPickerModel.cs`, `OfferSlots.cs`, `PlPages.Slots` | UX-002 | L |
| UX-016 | Sign-in and registration pages | `Pages/Account/Login*`, `Register*`, `TwoFactor*`, `CheckInbox.razor`, `Logout.razor`, `Pl.Login`, `Pl.Register` | UX-002 | M |
| UX-017 | Static pages, 404, error | `Pages/Static/**`, `Pages/NotFound.razor`, `Pages/Error.razor`, their strings in `PlPages` and `Pl.Errors` | UX-002 | M |
| UX-020 | Booking form | `Pages/Booking/BookingFormPage.razor(.css)`, `Pages/Bookings/ConsultationSummary.razor(.css)`, new `Pages/Booking/BookingSteps.razor(.css)`, `BookFlowText` except `Checkout` and `Confirmation` | UX-002 | L |
| UX-021 | Checkout, confirmation, calendar file | `Pages/Booking/MockCheckout.razor(.css)`, `BookingConfirmation.razor`, `StripeReturn.razor`, `HoldTimer.razor`, new `Hosting/CalendarEndpoints.cs`, `BookFlowText.Checkout`, `BookFlowText.Confirmation` | UX-002; adds `BookingSteps` after UX-020 | M |
| UX-030 | My bookings list | `Pages/Bookings/MyBookings.razor(.css)`, `Pl.Bookings`, list strings of `BookingsText` | UX-002 | M |
| UX-031 | Booking detail and its actions | `Pages/Bookings/BookingDetail.razor(.css)`, `ActionError.razor`, `CancelOutcome.cs`, detail strings of `BookingsText` | UX-002 | L |
| UX-032 | Reviews | `Pages/Reviews/**`, `ReviewsText` | UX-002 | M |
| UX-033 | Account page | `Pages/Account/Account.razor`, `DeleteAccountForm.cs`, `Pl.Account` | UX-002 | S |
| UX-040 | Practitioner dashboard and area tabs | `Pages/PractitionerArea/PractitionerHome.razor(.css)`, `PractitionerLayout.razor(.css)`, `NextSteps.cs`, `OnboardingChecklist.cs`, `PlPractitioner.Dashboard` | UX-002 | L |
| UX-041 | Availability editor | `Pages/PractitionerArea/Availability.razor(.css)`, `AvailabilityDraft.cs`, `PlPractitioner.Availability` | UX-002 | L |
| UX-042 | Offers | `Pages/PractitionerArea/Offers.razor(.css)`, `OfferForm.cs`, `PriceInput.cs`, `KindBadge.razor(.css)`, `PlPractitioner.Offers` | UX-002 | M |
| UX-043 | Profile editor and payments | `Pages/PractitionerArea/ProfileEdit.razor`, `Payments*.razor(.css)`, `PaymentsView.cs`, `FormField.razor(.css)`, `ErrorSummary.razor(.css)`, `PlPractitioner.Profile`, `PlPractitioner.Payments` | UX-002 | M |
| UX-050 | Administrator shell, verification, disputes | `Components/Layout/AdminLayout.razor(.css)`, `Components/Admin/**`, `Pages/Admin/Verification*.razor`, `Disputes.razor`, `DisputeCards.cs`, `DisputeResolveForm.cs`, matching parts of `PlAdmin` | UX-002 | L |
| UX-051 | Ledger, review moderation, suspicious pairs | `Pages/Admin/Ledger.razor`, `LedgerPeriod.cs`, `ReviewModeration.razor`, `SuspiciousPairs.razor`, `AdminFormat.cs`, matching parts of `PlAdmin` | UX-002; rebase on UX-050 for class names | M |

**UX-010** (S-5, S-6, finding 9). Primary navigation `Programiści`, `Jak to działa`, `Cennik`; `Zasady rankingu` moves to the footer (order pinned by the existing test; update it). Logo mark next to the name. Account trigger shows initials and the e-mail, the menu holds `Moje rezerwacje`, `Konto`, role links, `Wyloguj się`. `Panel programisty` only for practitioners, `Zostań programistą` otherwise (D-2). Phone menu becomes a full-width panel under the header with 44 px rows and closes on navigation. Footer in columns: product, for programmers, help, legal. Hooks: buttons `Zaloguj się`, `Załóż konto`; `data-testid="account-menu"`.

**UX-011** (V-1). Hero with the search and three example queries as links; category tiles with a distinct one-line description each and the practitioner count from the sitemap; a "featured" row of three `PractitionerCard`s from the list API (region fails soft); trust band with real numbers; "for programmers" call to action to `/rejestracja`; four questions and answers as `<details>`. The page stays cached and static.

**UX-012** (V-2, finding 4). Card: initials `Avatar` (deterministic colour from tokens), name, headline, rating as `★ 4,8 (25)` or the `Nowy` badge, one `Zweryfikowany` badge, up to three problem names from `ProblemSlugs`, price right-aligned in `--text-lg` as `od 120 zł`, durations as text; the whole card is the link (stretched link, accessible name stays the name). Result header `6 programistów` with correct Polish plural; empty state per contract 5.10 with the active filters as removable chips. Hooks: `Wyniki: <N>` is asserted by the journeys (`/^Wyniki: \d+$/`): keep it as the accessible text or change the page object in this PR; region `Lista programistów` with direct `li` children; card link named exactly by the display name; `Nowy` exact.

**UX-013** (V-3). Problem as a vertical list of links with counts; duration and price as true segmented rows that never wrap (equal columns); the panel is closed by default on the phone and always open from 52 rem, without script; a sticky `Pokaż wyniki (N)` on the phone; search field joins the panel head. Hooks: `navigation` named `Filtry`, category links with exact names.

**UX-014** (V-5, V-6). Two columns from 56 rem: content left, sticky booking panel right (offer, chosen time, price, primary button). Fix the dead end: `Zarezerwuj: <title>` leads to the offer's picker (`#oferta-<id>`) or straight to the first free slot, never to a booking URL without `start`. Header with avatar, one `Zweryfikowany` badge whose details (`E-mail`, `GitHub`, `NIP`) sit in a `<details>`, links as quiet text links, technologies of the offers as tags, review block with average and count above the list. Hooks: `h1` = display name, `h2` `O mnie` and `Oferta`, offer `h3` inside its `li`, link `Zarezerwuj: <offer title>`, region `Opinie`, one JSON-LD script, `Ocena: <n> z 5`.

**UX-015** (V-7, finding 5). Day strip as compact two-line chips (`pon.` / `30 lis`) in a week row with previous and next week links instead of a clipped scroller; selected day clearly filled; days without times shown disabled; a line `Najbliższy wolny termin: jutro, 09:30`; times grouped `Rano / Po południu / Wieczorem`; `Pokaż kolejne dni` as a quiet link. Optional island: `InteractiveServer` on the picker so a day change does not reload, with the present links as the prerendered fallback. Hooks: slot links named `HH:MM` inside the offer's `li`, the booking URL format, `Nie możemy teraz pokazać terminów`.

**UX-016** (V-8). Centred card at `--form-width`; when `redirect` points at `/rezerwacja/…` a context line "Zaloguj się, aby dokończyć rezerwację"; show-password control (enhance.js), password rules as a list that ticks while typing; GitHub button with its mark; consent row 44 px. "Forgot password" is UX-072 (needs the auth service). Hooks: labels `Adres e-mail`, `Hasło`, checkbox `/Akceptuję Regulamin/`, buttons `Zaloguj się`, `Załóż konto`; inputs typed before any script loads must not be lost.

**UX-017** (V-9 to V-12). `.prose` width and real list markers; `/jak-to-dziala` as numbered step cards per role with a closing call to action; `/cennik` with a second example and a link to the list; `/zasady-rankingu` tables readable at 390 px (stacked rows, no letter-per-line column) inside focusable `.table-wrap`; legal pages with a sticky contents list from 56 rem; 404 with the search form and three category links; error page with a retry link.

**UX-020** (C-1, finding 6). `BookingSteps` (`Termin`, `Opis`, `Płatność`, `Gotowe`) above the form; summary with a `Zmień termin` link back to the profile picker and a line `Bezpłatne anulowanie do <dzień, godzina>` computed from the start time and the full-refund window (`Gabinety.Billing.RefundPolicy.FullRefundWindow`, an owner's constant of CONTRACTS section 9; the web host does not reference `Gabinety.Billing` today, so take the deadline from the Booking API or ask before adding the reference: do not write 24 into the page); assistant result as a clear card (`data-testid="proposal"` kept) with the suggested category and the questions as a list; the pay button is the last element and never moves when the assistant answers. Hooks: `h1` `Rezerwacja konsultacji`, label `Opisz problem`, `Propozycja opisu`, buttons `Pomóż mi opisać problem`, `Dodaj propozycję do opisu`, `Zapłać`, texts `Sugerowana kategoria`, `Asystent jest chwilowo niedostępny…`.

**UX-021** (C-2, C-3, finding 8). Confirmation: `h1` `Rezerwacja potwierdzona`, the exact success text kept, a "Co dalej" list (e-mail sent, when the meeting link opens, how to cancel), primary `Zobacz rezerwację`, `Dodaj do kalendarza` (a `GET /moje/rezerwacje/<id>/kalendarz.ics` endpoint of the host, authorised like the detail page, plus a Google Calendar link). Checkout: summary and notice at one width. Hooks: `h1` `Płatność (tryb demonstracyjny)`, button `Symuluj udaną płatność`, text `Rezerwacja potwierdzona. Dziękujemy!`, `Odśwież stronę`, the URL shapes.

**UX-030** (C-4). Sections `Nadchodzące` and `Minione` (split by start time from `TimeProvider`); the next consultation as a lead card with relative time (`jutro, 09:30`) and the meeting link; whole card clickable with `Szczegóły` kept as the link name; status as filter chips (links) instead of select plus button; role tabs only for accounts with a practitioner profile; empty states with one action. Fixes the wrapped `Status` label.

**UX-031** (C-5). A header block per state: confirmed -> time, countdown wording, prominent `Dołącz do spotkania` (enabled text explains when it opens); completed -> `Wystaw opinię` first; cancelled -> refund line. Status history as a `.timeline`. Cancel and dispute panels open as native dialogs with enhance.js (inline panel stays the fallback), with one sentence of consequence, not the rule twice. `Zarezerwuj ponownie` to the practitioner's profile. Hooks: `alertdialog` named `Czy na pewno anulować rezerwację?`, buttons `Anuluj rezerwację`, `Tak, anuluj rezerwację`, `Zgłoś spór`, `Wyślij zgłoszenie`, label `Opis sporu`, real `dl`/`dd` for status and payment, `Zwrócono:`, flash texts.

**UX-032** (C-6). Rating as five star-shaped radio targets (names `1 z 5` … `5 z 5` kept) with the word for the value shown next to them; thank-you state with the heading `Dziękujemy za opinię` and a link back; practitioner's `Opinie o mnie` with the reply form in place.

**UX-033** (C-7). Sections as cards at `--form-width`; e-mail state as a badge; consents with their dates and a plain explanation when re-acceptance is needed; data export and account deletion in a "danger zone" where deletion is a collapsed `<details>`.

**UX-040** (P-1, P-2, P-6). Draft: one checklist with a progress bar and one link per row; `Wyślij do weryfikacji` disabled with the reason (hook texts `Gotowe`, `Szkic`, `Czeka na weryfikację` and the button name kept). Approved: upcoming consultations (first five from the bookings API, practitioner role), `Zobacz profil publiczny`, copy-link button, completed count and rating. Area tabs get a scroll fade so the cut tab is discoverable.

**UX-041** (P-3). One row per weekday with its intervals inline (`pon. 09:30–13:30, 15:00–18:30 [+]`), `Skopiuj na pozostałe dni robocze`, days off with the date shown in Polish next to the native field, a sticky save bar that states unsaved changes. Interactive island (`InteractiveServer`) so add, remove and copy do not reload; the form post stays the fallback. Hook: button `Zapisz`, flash `Zapisano.`.

**UX-042** (P-4). List first; the form opens from `Dodaj ofertę` (`?nowa=1`) or `Edytuj`; two columns from 48 rem; technology chips with a `2 z 4` count; a preview of the public offer card; fix the heading order. Hooks: labels `Nazwa oferty`, `Opis oferty`, `Cena (zł)`, `Rodzaj problemu` (option 0 `Wybierz…`), group `/Technologie/`, button `Zapisz`.

**UX-043** (P-5). Profile form with a preview of the public header beside it from 56 rem; `inputmode` and `maxlength` on NIP; payments page as a status card with the next step and what the client sees meanwhile. Hooks: labels `Nazwa wyświetlana`, `Nagłówek`, `Opis` (exactly one label containing "opis" on the page), `Link do portfolio`, `NIP`.

**UX-050** (A-1, A-3). Replace `admin-*` duplicates with the shared classes of UX-002; counts on the section tabs and status switches (`Spory (1)`); booking reference in a dispute links to a detail; verification list as `.table` that collapses to cards. Hooks: `h1` `Spory`, `Weryfikacja programistów`, dispute `li` containing the booking id, buttons `Zatwierdź`, `Rozstrzygnij`, `Przejdź do potwierdzenia`, `Potwierdź rozstrzygnięcie`, labels `Notatka`, `Kwota zwrotu w zł`, region `Historia weryfikacji`, `data-testid="forbidden"`.

**UX-051** (A-2). Ledger: a useful booking reference (offer title and the id's last characters, linked), period in Polish (`1–22 listopada 2026`), totals per kind, sticky table head. Same table pattern for moderation and suspicious pairs.

### Wave 2: cross-cutting (after wave 1)

Status: **UX-060 (partly) and UX-062 are done; UX-061 and UX-063 are deferred.** UX-060: the flash gets a dismiss control and a thin progress bar shows while a link or form is followed (`enhance.js`, `app.css`); a styled reconnect state was not needed (the only circuit is the checkout clock). UX-062: slot times keep the Warsaw `HH:MM`, a visitor in another zone gets a note with an example and each time's local equivalent in its title. UX-061 (theme switch) is deferred: the public pages are output-cached, so a server-rendered theme needs a cache variation on the cookie, and without it the theme flashes (a blocking head script would be a second script file, against D-1's one script). UX-063 (skeletons for interactive islands) does not apply: no island was built, UX-015 and UX-041 stayed static.

| ID | Task | Owns | Depends | Size |
|---|---|---|---|---|
| UX-060 | Feedback layer: flash as a dismissible toast region that also stays in the page, navigation progress bar for enhanced navigation, styled reconnect state | `Components/Layout/MainLayout.razor(.css)`, `FlashKey.cs`, reconnect markup in `App.razor` | UX-002, UX-005 | M |
| UX-061 | Theme switch (system, light, dark) remembered in a cookie so the server renders the right theme | header control (after UX-010), `tokens.css` `data-theme` block, `enhance.js` | D-4, UX-010 | S |
| UX-062 | Visitor's local time next to Warsaw time in the slot picker and booking views when the browser zone differs | `enhance.js`, a `data-utc` attribute contract in `PlFormat` users | UX-005, UX-015, UX-031 | S |
| UX-063 | Loading states: skeletons for the interactive islands and `aria-busy` regions, per contract 5.10 | the islands of UX-015 and UX-041 | UX-015, UX-041 | S |

### Wave 3: needs a contract or backend change first (start in `gabinety-tech-spec`)

| ID | Task | Needs |
|---|---|---|
| UX-070 | Practitioner photos (upload, moderation, `avatarUrl` on card and profile) | Directory contract and storage; replaces the initials of UX-012 |
| UX-071 | Sort (`trafność`, `cena`, `najbliższy termin`), next free time and technologies on the list card, price range per category | Directory read model and list query |
| UX-072 | Password reset and password change | the auth service's endpoints and e-mail template |
| UX-073 | Reschedule a booking instead of cancel-and-rebook | Booking state machine and refund rules |
| UX-074 | Reminder and notification preferences on `/konto`; in-app notification list | Booking notifications |
| UX-075 | Practitioner calendar view and earnings summary (paid out, pending) | Booking payout read endpoints |
| UX-076 | Help and contact page with a contact form | owner decision on the support channel |

## 7. Order and parallelism

```
D-1..D-4 (owner)                         UX-003, UX-004      (any time, parallel)
UX-001 -> UX-002 -> wave 1: 20 tasks in parallel, disjoint files
          UX-005 (after D-1) ------------^ optional for wave 1, required for wave 2
wave 1 merged -> UX-060..UX-063
wave 3: each starts with its contract change, independent of the rest
```

Suggested first batch if only a few sessions run at once (highest effect on conversion): UX-001, UX-002, then UX-014, UX-015, UX-012, UX-020, UX-010, UX-021.

Two couplings to watch: `Text/Pl.cs` and `Text/PlPages.cs` are touched by several wave 1 tasks (each inside its own nested class; rebase, do not reformat), and `App.razor` by UX-004, UX-005 and UX-060 (one line each).

## 8. What this plan does not claim

- "Best on the market" is a direction, not a measurement: there is no analytics in the product, so no finding above is backed by conversion data. After wave 1, measure the funnel (list -> profile -> slot -> form -> paid) before spending on wave 3.
- No test with real users was run. The findings are from the code, the captures and comparison with common booking products.
- Pages listed as "not captured" in section 1 were judged from code only.
