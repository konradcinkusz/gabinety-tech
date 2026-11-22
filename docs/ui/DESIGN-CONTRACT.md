# Design contract for the Blazor port

Status: proposed, to be frozen by the owner when the port starts. Implements [`BLAZOR-WEB-APP.md`](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/guides/BLAZOR-WEB-APP.md) sections 9 and 10.
Evidence for every rule: [`UI-AUDIT.md`](UI-AUDIT.md) (finding ids G-x, P-x, F-x). Tokens: [`tokens.css`](tokens.css), the block to paste at the top of `wwwroot/app.css`.

Rules of the contract (from the guide, applied):

1. Plain CSS in two places: `wwwroot/app.css` (tokens, element styles, the shared classes of section 4) and one scoped `Foo.razor.css` per component. No CSS framework, no web font, no icon font, no JS.
   **Amended 2026-11-22 (owner decision D-1, UX-005):** one first-party script is allowed, `wwwroot/enhance.js` (same origin, no dependency, under 8 KB, covered by `script-src 'self'`). It is progressive enhancement only: every page works without it, and each behaviour (live counters, a pending submit button, a modal `<dialog>` for a state that also lives in the URL, show-password, copy link, auto-submit of a filter select) has a no-script fallback that is the page as served. Elements opt out with `data-enhance="off"`. No other script, no third-party script.
2. **Colour only through tokens.** No hex, `rgb()`, `hsl()` or named colour outside the two token blocks of `tokens.css`. A `StyleContractTests` class reads `app.css` from disk and fails on a stray colour, on a missing token, on a missing dark block, and on a missing shared class name of section 4.
3. Light is the default; dark is `@media (prefers-color-scheme: dark)` redefining colour tokens and **nothing else**. A user switch is out of scope; `data-theme="light"` on `<html>` is the one escape hatch the token block already honours.
4. A polish task uses the class names of section 4 or stops and asks. It does not invent one.
5. The test hooks of section 9 are part of the contract: a restyle that changes one changes the test in the same PR and says why.

## 1. Direction

A calm, high-trust marketplace for paid expert consultations: neutral surfaces, one strong blue accent, generous spacing, large touch targets, status colours used only for states. Content first: no hero imagery, no illustrations, no icon font (an inline SVG is allowed as a Razor component when a state needs a glyph; text always carries the meaning). Everything is readable at 390 px without horizontal scroll and at 1440 px without a column of text wider than 42 rem.

What the current app gets wrong and the contract changes (summary): session-blind header (G-1), flat hierarchy and unstructured spacing (G-5, G-6), glued tabs and badges (G-2), no dark theme (G-3), 20 px touch targets (G-4), one-error-at-a-time forms (G-9).

## 2. Files and ownership

| File | Holds |
|---|---|
| `wwwroot/app.css` | token blocks (copied unchanged from `docs/ui/tokens.css`), reset, element styles (`body`, `h1`..`h3`, `a`, `table`, form controls), the shared classes of section 4, the print rules |
| `Components/Layout/MainLayout.razor(.css)` | skip link, header, `main`, footer; the only place landmarks are declared |
| `Components/Shared/*.razor(.css)` | presentational components: `Badge`, `Callout`, `Field`, `EmptyState`, `Flash`, `PageHeader`, `Tabs`, `SlotPicker`, `OfferCard`, `PractitionerCard`, `BookingSummary`, `StatusBadge`, `Money`, `Stars` |
| `Components/Pages/<Area>/Foo.razor(.css)` | a page and the rules that are only its own (layout grid of the page); it never restyles a shared class |
| `Components/Pages/Shared/*Format.cs` | plain C# (guide section 10): `PlFormat` (dates, months in the genitive, money), `BookingStatusModel` (state -> badge kind, label, next actions), `VerificationModel`, `PaymentModel`, `DisputeModel` |

## 3. Tokens

The authoritative block is [`tokens.css`](tokens.css) (light default, dark redefinition, both complete). Summary:

**Names (UX-001).** The host kept the token names it was built with; the values are the contract's. Where this document says `--surface-page`, `--surface`, `--surface-sunken`, the files say `--color-bg`, `--color-surface-raised`, `--color-surface-sunken` (`--color-surface` is an older alias of the sunken surface that scoped sheets still use); every other colour token is the name below with the prefix `--color-` (`--text` is `--color-text`, `--accent-soft-text` is `--color-accent-soft-text`); `--font-body` is `--font-sans`, `--fs-*` is `--text-*`, `--lh-*` is `--leading-*`. The shipped file is `src/Gabinety.Web/wwwroot/tokens.css`; `docs/ui/tokens.css` is a byte-identical copy and a test compares them.

### 3.1 Colour tokens (redefined in dark)

`--surface-page`, `--surface`, `--surface-sunken`; `--text`, `--text-secondary`, `--text-muted`; `--border`, `--border-strong`; `--accent`, `--accent-hover`, `--accent-contrast`, `--accent-soft`, `--accent-soft-text`; `--success`, `--success-soft`, `--warning`, `--warning-soft`, `--danger`, `--danger-soft`, `--danger-contrast`; `--focus`; `--shadow-sm`, `--shadow-md`, `--scrim`.

Use: page background `--surface-page`; cards, inputs, tables `--surface`; table heads, code, nested panels `--surface-sunken`. Body text `--text`; metadata and hints `--text-secondary` / `--text-muted` (never smaller than `--fs-sm`). Links and primary actions `--accent`. A status is always the strong colour on its soft fill (`--success` on `--success-soft`). Controls have a `--border-strong` outline (3:1), decoration uses `--border`.

### 3.2 Shape, space, width (identical in both themes)

| Token | Value | Use |
|---|---|---|
| `--radius-sm` / `--radius-md` / `--radius-pill` | 6 / 10 px / 999 px | inputs and badges / cards, buttons / chips |
| `--space-1`..`--space-8` | 4, 8, 12, 16, 24, 32, 48, 64 px | the only spacing values; `.stack` rhythm is `--space-4`, section gap `--space-6` |
| `--gutter` | 16 px | page side padding at every width |
| `--content-width` | 72 rem (1152 px) | page container |
| `--prose-width` | 42 rem | running text, legal pages |
| `--form-width` | 32 rem | single-column forms |
| `--tap` | 44 px | minimum height and width of every interactive element |

### 3.3 Contrast, computed

Computed by `python3 tests/e2e/tools/ui-audit/contrast.py` from the shipped `tokens.css` (WCAG 2.x relative luminance). Text pairs need 4.5, controls and the focus ring 3.0. All 28 pairs pass in both themes; a stylesheet test reruns the same arithmetic.

| Foreground on background | Use | Min | Light | Dark |
|---|---|---|---|---|
| `text` on `surface-page` | body text on page | 4.5 | 16.60 | 16.58 |
| `text` on `surface` | body text on card | 4.5 | 17.79 | 15.26 |
| `text` on `surface-sunken` | text on table head | 4.5 | 15.44 | 13.61 |
| `text-secondary` on `surface-page` | secondary text on page | 4.5 | 8.88 | 11.99 |
| `text-secondary` on `surface` | secondary text on card | 4.5 | 9.52 | 11.03 |
| `text-secondary` on `surface-sunken` | secondary on sunken | 4.5 | 8.26 | 9.84 |
| `text-muted` on `surface-page` | hints, captions on page | 4.5 | 5.92 | 7.56 |
| `text-muted` on `surface` | hints on card | 4.5 | 6.34 | 6.95 |
| `text-muted` on `surface-sunken` | muted on sunken | 4.5 | 5.50 | 6.20 |
| `accent` on `surface-page` | link on page | 4.5 | 6.25 | 7.62 |
| `accent` on `surface` | link on card | 4.5 | 6.70 | 7.02 |
| `accent-hover` on `surface` | link hover | 4.5 | 8.72 | 9.27 |
| `accent-contrast` on `accent` | primary button label | 4.5 | 6.70 | 7.69 |
| `accent-contrast` on `accent-hover` | primary button hover | 4.5 | 8.72 | 10.16 |
| `accent-soft-text` on `accent-soft` | selected chip, info callout | 4.5 | 8.83 | 9.58 |
| `accent` on `accent-soft` | link inside info callout | 4.5 | 5.71 | 5.99 |
| `success` on `success-soft` | success badge, callout | 4.5 | 5.79 | 7.74 |
| `success` on `surface` | success text on card | 4.5 | 6.61 | 9.35 |
| `warning` on `warning-soft` | warning badge, callout | 4.5 | 6.09 | 8.19 |
| `warning` on `surface` | warning text on card | 4.5 | 6.80 | 9.66 |
| `danger` on `danger-soft` | danger badge, callout | 4.5 | 5.67 | 7.37 |
| `danger` on `surface` | error text under a field | 4.5 | 6.57 | 7.95 |
| `danger-contrast` on `danger` | danger button label | 4.5 | 6.57 | 8.93 |
| `border-strong` on `surface` | control outline (UI component) | 3.0 | 3.77 | 3.99 |
| `border-strong` on `surface-page` | control outline on page | 3.0 | 3.52 | 4.33 |
| `focus` on `surface-page` | focus ring on page | 3.0 | 6.25 | 10.07 |
| `focus` on `surface` | focus ring on card | 3.0 | 6.70 | 9.27 |
| `focus` on `accent-soft` | focus ring on a selected chip | 3.0 | 5.71 | 7.91 |

### 3.4 Type

System stacks only (`--font-body`, `--font-mono`; no web fonts, so no layout shift from font loading).

| Role | Token | Size | Weight | Line height | Notes |
|---|---|---|---|---|---|
| Caption, badge | `--fs-xs` | 13 px | 500 | 1.2 | never for sentences |
| Hint, table cell, secondary line | `--fs-sm` | 14 px | 400 | 1.55 | the smallest size for reading text |
| Body, inputs, buttons | `--fs-md` | 16 px | 400 / 500 | 1.55 | inputs are never below 16 px (iOS zoom) |
| Lead, card title (`h3`) | `--fs-lg` | 18 px | 700 | 1.2 | |
| `h2` | `--fs-xl` | 22 px | 700 | 1.2 | |
| `h1` | `--fs-2xl` phone, `--fs-3xl` ≥ 40 rem | 28 / 36 px | 700 | 1.2 | exactly one `h1` per page |

Numbers in prices, tables and ids use `font-variant-numeric: tabular-nums` (class `.num`).

### 3.5 Layout

Page container: `max-width: var(--content-width); padding-inline: var(--gutter); margin-inline: auto`. Forms and single-purpose pages (login, register, review, booking form column) are `--form-width`; running text is `--prose-width`. Two-column pages (profile, booking, practitioner list) use CSS grid with `minmax(0, 1fr)` and a 22 rem side column from 56 rem up; below that, one column in DOM order (main content first, summary after it, except the booking summary which stays above the submit button).

## 4. Shared class vocabulary (frozen)

All classes below exist in `app.css`; `StyleContractTests` asserts each name. State modifiers use BEM style `block--modifier`.

### 4.1 Layout primitives

| Class | Purpose |
|---|---|
| `.container` | the page container of 3.5 (`.container--form`, `.container--prose` narrow it) |
| `.stack` | vertical flow with `gap: var(--space-4)`; `.stack--tight` (`--space-2`), `.stack--loose` (`--space-6`). The only way to space siblings vertically |
| `.cluster` | wrapping horizontal row with `gap: var(--space-3)`, aligned to the baseline; for action rows, badge rows, meta lines |
| `.cluster--between` | cluster with the first and last child pushed to the edges (card header: title left, badge right) |
| `.grid-2` | two columns from 56 rem (`minmax(0,1fr) 22rem`), one column below; `.grid-2--even` for equal columns |
| `.divider` | a labelled rule (`lub` between two sign-in methods) |
| `.sr-only`, `.skip-link` | visually hidden text; the skip link (visible on focus, top-left pill) |

### 4.2 Surfaces and content

| Class | Purpose |
|---|---|
| `.card` | `--surface`, `1px --border`, `--radius-md`, `--shadow-sm`, padding `--space-5` (`--space-4` on the phone). `.card--link` makes the whole card a link target (stretched link pattern, accessible name stays the title) |
| `.card__header`, `.card__body`, `.card__footer` | optional slots; the footer holds actions in a `.cluster` |
| `.callout` | inline message block with a left border; modifiers `--info`, `--success`, `--warning`, `--danger`; `--danger` and `--warning` are `role="alert"`/`role="status"` as set by the component |
| `.badge` | small pill for a state; modifiers `--neutral`, `--info`, `--success`, `--warning`, `--danger` (mapping in 5.7) |
| `.chip` | 44 px selectable pill (filters, slot times, categories); `aria-current="true"` or `.chip--selected` = `--accent-soft` fill |
| `.dl` | definition list as label/value rows: label column `max-content` (min 8 rem), value column `1fr`, row gap `--space-2`; used for summaries and details |
| `.prose` | running text at `--prose-width`, paragraph and list rhythm |
| `.empty-state` | centred block for "nothing here": heading, one sentence, one action (5.10) |
| `.skeleton` | placeholder block for a loading state (no animation under `prefers-reduced-motion`) |
| `.num` | tabular numerals, right-aligned in tables |
| `.muted` | `--text-muted` secondary text (at least `--fs-sm`) |

### 4.3 Actions

| Class | Purpose |
|---|---|
| `.btn` | base button/link-as-button: `min-height: var(--tap)`, padding `--space-2 --space-4`, `--radius-md`, weight 500 |
| `.btn--primary` | the one main action of a view (`--accent` / `--accent-contrast`); at most one per card, one per page area |
| `.btn--secondary` | `--surface` with `--accent` outline; alternative actions |
| `.btn--ghost` | text button for low emphasis (cancel, "Wyczyść filtry", demo "nieudana płatność") |
| `.btn--danger` | solid `--danger`; **only inside a confirmation** (the dialog's confirm button) |
| `.btn--ghost-danger` | text button in `--danger` for the trigger of a destructive action ("Wyłącz", "Anuluj rezerwację") |
| `.btn--block` | full width (phone forms, checkout) |
| `.btn[aria-busy="true"]` | pending state: label stays, spinner glyph, `pointer-events: none` |
| `.btn:disabled`, `.btn[aria-disabled="true"]` | disabled: `--surface-sunken` fill and `--text-muted` label (still 4.5:1), never an opacity trick; the reason is stated in text next to the button |
| `.link` | inline link style (underlined, `--accent`); used in running text only |
| `.tabs`, `.tabs__link` | the single section/role navigation pattern (5.9); `aria-current="page"` styles the active tab |
| `.segmented` | 2-5 mutually exclusive options as one joined control (status switch, range switch); radio inputs inside |

### 4.4 Forms

| Class | Purpose |
|---|---|
| `.form` | a `.stack` of fields at `--form-width` |
| `.field` | label + hint + control + counter + error as one unit, `gap: var(--space-1)` |
| `.field__label` | the label; required fields are the default, optional ones carry `.field__optional` ("opcjonalnie"); no "(wymagane)" |
| `.field__hint` | helper text under the label, `--fs-sm`, `--text-muted`, referenced by `aria-describedby` |
| `.field__control` | the `input`/`select`/`textarea` (`min-height: var(--tap)`, `--border-strong`, `--radius-sm`, 16 px) |
| `.field__counter` | `12 / 80`, right-aligned **inside** the field directly under the control |
| `.field__error` | message under the control, `--danger`, begins with the word "Błąd:" visually hidden for screen readers |
| `.field--invalid` | red 2 px control border + `aria-invalid="true"` set by the component |
| `.option` | one checkbox/radio row or card: whole row is the label, 44 px high; `.option-group` is the `fieldset` |
| `.form-summary` | the `role="alert"` callout that lists every error with links to the fields |
| `.form-actions` | the action `.cluster` at the end of a form; primary first |

### 4.5 Tables and data

| Class | Purpose |
|---|---|
| `.table-wrap` | horizontal scroller around a wide table: `overflow-x: auto`, `tabindex="0"`, `role="region"`, `aria-label`, edge shadow; the page itself never scrolls sideways |
| `.table` | the table: `--surface`, sticky `thead` in `--surface-sunken`, `--fs-sm`, row hover `--accent-soft`; `th[scope]` always set |
| `.table--cards` | under 40 rem every row becomes a `.card`-like block with `data-label` pseudo-labels (admin lists) |
| `.row-actions` | the actions cell: a `.cluster` with 8 px gap, right-aligned; destructive action last |

### 4.6 Navigation, feedback, overlays

| Class | Purpose |
|---|---|
| `.site-header`, `.site-nav`, `.site-menu` | header, primary navigation, the `<details>` menu under 40 rem |
| `.breadcrumb` | `nav aria-label="Okruszki"` with an ordered list |
| `.pagination` | previous/next/numbers as `.btn--secondary` links; the current page `aria-current="page"` |
| `.flash`, `.flash-region` | transient confirmation ("Zapisano.") shown in the persistent `role="status"` region (5.12) |
| `.dialog` | styling of the native `<dialog>` modal (scrim `--scrim`, `.card` look, focus trap by the platform, Esc closes) |
| `.spinner` | 20 px CSS spinner inside `.btn[aria-busy]` and `.loading` blocks |
| `.loading` | wrapper for `aria-busy="true"` regions with `.skeleton` children |

### 4.7 Domain components (built from the above, each its own Razor component)

`.practitioner-card`, `.offer-card`, `.slot-picker`, `.slot-day`, `.slot` (a `.chip` that is a link), `.booking-summary`, `.rating` (`★ 4,8 (25)` or the `Nowy` badge), `.checklist` (onboarding), `.timeline` (verification and dispute history), `.status-badge` (the mapping of 5.7). They are scoped CSS of their component and compose the shared classes; they are listed here because the journeys address them by role and name (section 9).

## 5. Page patterns

Every pattern lists its structure, then the rules that the audit found missing.

### 5.1 Shell and header (G-1, G-2, G-4)

```
<a class="skip-link" href="#main">Przejdź do treści</a>
<header class="site-header"> brand | <nav class="site-nav" aria-label="Menu główne"> | account area </header>
<main id="main" tabindex="-1"> … </main>
<footer> © year · Regulamin · Polityka prywatności </footer>
```

- Order of the primary nav is pinned by a test: **Programiści** first, then Jak to działa, Zasady rankingu, Cennik.
- Account area by session (read once in the layout from the authentication state, never from `HttpContext` in components, guide section 7): visitor `Zaloguj się` (`.btn--ghost`) + `Załóż konto` (`.btn--primary`); client `Moje rezerwacje` + account menu (`Konto`, `Wyloguj`); practitioner adds `Panel programisty`; administrator adds `Administracja`; a client sees `Zostań programistą` in the menu (open question 1 of the audit).
- The current section carries `aria-current="page"`.
- Under 40 rem the nav becomes one `Menu` button: a native `<details class="site-menu">` (works without JS, 44 px target, closes on navigation).
- The header is `position: sticky; top: 0` only on desktop; on the phone it scrolls away (slot pickers and forms need the room).
- Role areas (practitioner, administrator) add a `.tabs` strip under the header for their sections.

### 5.2 Home

Hero (`h1`, one sentence, search field and button in one row), category tiles (8 `.card--link` with a one-line description), three-step band (`Znajdź`, `Zarezerwuj`, `Porozmawiaj`; full-width `.card`s, the "Szczegóły" link inside the band), a trust row (number of verified practitioners, price range, "Płatność z góry, programista dostaje pieniądze po konsultacji").

### 5.3 Marketplace list with filters (P-LIST-1..3)

```
h1 Programiści            breadcrumb on category pages
.grid-2:  [ filters ]  [ results ]
filters: problem (radio list), duration (.segmented), price (.segmented), search input; as links, no JS
results: header line "6 programistów" + sort select, then .practitioner-card list, then .pagination
```

- Phone: the filters collapse into `<details class="filters">` titled `Filtry (2)`; results first.
- `.practitioner-card` (a `.card--link`): name (the link, `h2`), headline, `.rating`, one `.badge--success` `Zweryfikowany` (the full list of checks lives on the profile), `od 110 zł` (right, `--fs-lg`, `.num`), durations as text. Hooks in section 9.
- The count equals the number of results; zero results renders `.empty-state` ("Nie ma programistów dla tych filtrów", the active filters as removable chips, `Wyczyść filtry`).

### 5.4 Practitioner profile, offer card, slot picker (P-PROF-1..3)

```
.grid-2:  main                              side (sticky from 56 rem)
          h1 name, headline, .rating,        .booking-summary of the selected slot:
          badges, facts (consultations,        offer title, chosen time, price, [Zarezerwuj]
          member since), links
          h2 O mnie  (.prose)
          h2 Oferta  .offer-card ×N
          h2 Opinie  list or one empty sentence
```

- `.offer-card` (an `li` with an `h3`): title, description, `60 min · 180 zł`, then the `.slot-picker`; the primary button sits in the summary and is disabled with the text "Wybierz termin" until a time is chosen.
- `.slot-picker`: a date strip of 7 days (`.chip`s inside a `.table-wrap`-style scroller, `aria-label="Wybierz dzień"`), then the times of the selected day (`.slot` links, 4 per row on the phone, 6+ on desktop), `Pokaż kolejne dni` as `.btn--ghost`, the zone note "Godziny w czasie polskim (Europe/Warsaw)". Each time is a link (hook: name `^\d{2}:\d{2}$`), keeps the URL `/rezerwacja/<offer>?start=…&gabinet=<slug>`.
- Loading: `aria-busy` with three `.skeleton` rows; failure: `.callout--warning` "Nie możemy teraz pokazać terminów" + `Spróbuj ponownie`; no times in range: "Brak wolnych terminów w najbliższych 14 dniach".
- Dates: weekday and month lowercase (`wtorek, 24 listopada`), genitive month, 24 h time (`PlFormat`).
- A new practitioner shows the `Nowy` badge (`getByText("Nowy", exact)`) instead of a rating.

### 5.5 Form pages with inline validation (G-9, P-REG-1, P-BOOK-2, P-PRO-1)

- Structure: `.form` > `.field`s > `.form-actions`. A form never exceeds `--form-width`; fields that are long by nature (description) stay at that width too.
- On submit with errors: (1) a `.form-summary` (`role="alert"`, heading "Popraw N pól") lists every error as a link to its field; (2) every invalid field gets `.field--invalid`, `aria-invalid="true"`, `aria-describedby` pointing at hint + error, and an inline `.field__error`; (3) focus moves to the summary (or the first control when there is one error). After the first submit, fields revalidate on blur.
- Errors say what to do, in Polish, never a code: "Hasło musi mieć co najmniej 8 znaków", not "Invalid".
- Counters (`0 / 80`) are inside the field. Required is the default; optional fields say "opcjonalnie".
- Pending: the submit button goes `aria-busy`, the form stays editable, a double submit is impossible. Success: navigate with a `.flash` (5.12) or, for in-page saves, the flash region says "Zapisano.".
- Checkbox consent: its own `.field` with the whole sentence as the label and its own error; the submit button is a separate row.

### 5.6 Booking summary and checkout (P-BOOK-1, P-CHK-1, P-CONF-1)

`.booking-summary` is one component used on the booking form, checkout, confirmation, booking detail and the profile side panel: a `.dl` with `Konsultacja`, `Programista` (name), `Termin` (date, time range, zone note), `Czas trwania`, `Do zapłaty` (`.num`, `--fs-lg`, weight 700). It always shows the practitioner's name (F-03).

- Booking form: two columns from 56 rem (form | sticky summary); the hold notice ("Termin jest wstrzymany na 10 minut od kliknięcia „Zapłać”") as `.callout--info` under the total; the button is `Zapłać 180 zł` (accessible name still matches `Zapłać`).
- Mock checkout: summary + `.callout--warning role="timer"` with the remaining time; `Symuluj udaną płatność` primary, `Symuluj nieudaną płatność` `.btn--ghost`; an unmistakable "tryb demonstracyjny" `.callout--info`.
- Confirmation: `.callout--success` "Rezerwacja potwierdzona. Dziękujemy!" (exact text, hook), the summary, `Zobacz rezerwację` primary, `Wróć do wyszukiwarki` secondary.

### 5.7 Status badges (G-8)

One component, `StatusBadge(kind, value)`, maps a state to a badge kind and Polish label through the C# models; colour never carries the meaning alone (the label is always there). The labels are the strings the journeys read.

| Family | Value → label → kind |
|---|---|
| Booking | PendingPayment `Oczekuje na płatność` warning · Confirmed `Potwierdzona` info · Completed `Zakończona` success · CancelledByClient `Anulowana przez klienta` neutral · CancelledByClientNoRefund `Anulowana przez klienta (bez zwrotu)` warning · CancelledByPractitioner `Anulowana przez programistę` danger · Expired `Wygasła` neutral · NoShow `Klient nie stawił się` danger · Disputed `Spór w toku` warning · DisputeResolved `Spór rozstrzygnięty` neutral |
| Payment | `Opłacona` success · `Zwrócona w całości` info · `Zwrócona częściowo` info · `Oczekuje` warning · `Nieudana` danger. The line `Zwrócono: 180,00 zł` stays text next to the badge (hook `/Zwrócono:/`) |
| Verification | `Szkic` neutral · `Czeka na weryfikację` warning · `Zatwierdzony` success · `Zawieszony` danger · `Odrzucony` danger |
| Practitioner checks | `E-mail zweryfikowany`, `GitHub połączony`, `NIP zweryfikowany`, `Zatwierdzony` appear **only on the profile**, as one `.cluster` of `.badge--success`; the list card shows the single `Zweryfikowany` |
| Dispute | `Otwarty` warning · `Rozstrzygnięty` neutral |
| Review | `Opublikowana` success · `Do moderacji` warning |

### 5.8 Admin tables with actions (P-ADM-1..5)

- Section navigation: `.tabs` under a strip `Administracja`; status switch inside a page: `.segmented`.
- List = `.table-wrap` > `.table` with `th scope="col"`, name first, status as `StatusBadge`, dates `24 lis 2026, 13:00`, money `.num` right-aligned, id columns hidden behind `title` (never raw GUIDs as the primary text), last column `.row-actions` (`Zatwierdź` `.btn--primary` small, `Odrzuć` `.btn--ghost-danger`). Under 40 rem the table becomes `.table--cards`.
- Lists that the journeys treat as `listitem`s (disputes: `item(bookingId)`, `Rozstrzygnij`) keep an `ul > li` structure: on the phone and on desktop each dispute is an `li.card` with the booking id as text (the J6 hook `hasText: bookingId`), so the disputes page is a **card list**, not a table; verification and ledger are tables.
- Destructive or irreversible actions (approve, reject, resolve) go through a two-step flow already present in the product (`Przejdź do potwierdzenia` → `Potwierdź rozstrzygnięcie`); the second step shows the consequence ("Klient dostanie 90,00 zł, programista 90,00 zł").
- A dispute shows offer, names, price, payment state and the maximum refund; the amount field appears only for "Zwróć klientowi część".

### 5.9 Tabs and segmented controls (G-2)

`.tabs` is `nav aria-label="…"` > `ul` > `li` > `a.tabs__link[aria-current="page"]`, 44 px high, 2 px bottom border in `--accent` for the active one, scrolls horizontally inside itself on the phone. Used for: practitioner area (Start, Profil, Oferty, Dostępność), admin sections, `Klient | Programista` in bookings. `.segmented` is for choices inside a page (status, date range) and is a radio group, not navigation.

### 5.10 Empty, error and loading states

| State | Rule | Component |
|---|---|---|
| Empty | A heading (what is empty), one sentence (why / what to do), at most one action. Never an empty `ul`/`tbody`. | `.empty-state` |
| Error loading | A `.callout--danger role="alert"` at the place of the content, in Polish, with `Spróbuj ponownie` (a link to the same URL or a button); the rest of the page still renders (guide section 5). Hook `data-testid="unavailable"` where it exists today. | `.callout--danger` |
| Forbidden | `data-testid="forbidden"` callout "Brak dostępu" + link home; the HTTP status is 403, not a silent redirect (F-10). | `.callout--danger` |
| Loading | `aria-busy="true"` on the region with 3 `.skeleton` rows; buttons use `.btn[aria-busy]`; nothing jumps (reserve the height). | `.loading` |
| 404 | Specific title ("Nie znaleziono programisty"), search box, three category links. | `.empty-state` |

### 5.11 Dialogs (P-BKS-2)

Native `<dialog>` with `.dialog`: `role="alertdialog"` when destructive, `aria-labelledby` the title, initial focus on the title (or the safe button), Esc and the safe button close it, confirm is `.btn--danger` and names the consequence. The cancel dialog computes the refund from the start time ("Do konsultacji zostało 20 godz.: nie otrzymasz zwrotu" / "otrzymasz zwrot 180,00 zł").

### 5.12 Flash and toast

A single persistent `.flash-region` (`role="status"`, `aria-live="polite"`) exists in the layout from first render so that announcements are read. A save writes "Zapisano." into it for 6 s (also visible text, never only a toast that disappears); the text stays in the DOM for the page's life when it confirms a transaction ("Rezerwacja została anulowana.", "Spór został zgłoszony.", "Profil wysłany. Administrator sprawdzi go i da znać."). Errors are not flashes: they are `role="alert"` callouts at the place of the action.

## 6. Responsive rules

| Rule | Value |
|---|---|
| Phone width | 390 px has **no horizontal page scroll**; the audit (`audit.mjs`) fails on `documentElement.scrollWidth > clientWidth` |
| Gutter | `--gutter` 16 px at every width |
| Breakpoints | 40 rem (header menu, tables → cards), 56 rem (two-column pages), 72 rem (container cap). Mobile first: base CSS is the phone |
| Tables | scroll inside `.table-wrap` (focusable, labelled) or collapse with `.table--cards`; never widen the page |
| Touch | every interactive element ≥ 44 x 44 px incl. inline link rows (list links get `padding-block`), checkbox/radio rows, chips, tabs. The audit counts violations at 390 px |
| Images | none in the contract; any future image has `max-width: 100%`, `height: auto`, `alt` |
| Text | body 16 px; no `text-size-adjust` tricks; wrap long words (`overflow-wrap: anywhere` on user text: briefs, headlines) |
| Sticky | only the desktop header and the booking side panel; nothing sticky on the phone except the filters' `Pokaż wyniki` bar |
| Zoom | works at 200 % and 320 CSS px without loss (WCAG 1.4.10): the 390 px rules apply |

## 7. Accessibility rules

1. **Contrast**: text 4.5:1, UI components and focus ring 3:1 in both themes (table in 3.3). A new colour needs a new token and a new row in `contrast.py`.
2. **Focus**: `:focus-visible { outline: 3px solid var(--focus); outline-offset: 2px }` on every interactive element; never `outline: none` without a replacement; focus is not clipped by `overflow` (scrollers add padding).
3. **Reduced motion**: `@media (prefers-reduced-motion: reduce)` turns off all transitions, animations (spinner becomes a static glyph) and smooth scrolling.
4. **Skip link** first in the DOM, visible on focus; `main` has `id="main"` and `tabindex="-1"`.
5. **Landmarks**: one `header`, one `nav` per navigation (each with a distinct `aria-label`: `Menu główne`, `Filtry`, `Okruszki`, section tabs), one `main`, one `footer`; regions that tests address are named `region` sections (`Lista programistów`, `Opinie`, `Historia weryfikacji`).
6. **Headings**: one `h1` per page, no skipped level; `/konto` and error pages included.
7. **Forms**: every control has a visible `<label>`; hints and errors are linked with `aria-describedby`; errors have text and are not colour-only; required is the default and optional is stated; `autocomplete` on e-mail/password; `inputmode` on numeric fields.
8. **State is never only colour**: every badge has a label; links in running text are underlined; selected chips also get `aria-current`/`aria-pressed`.
9. **Live regions**: `.flash-region` (`status`) and `.form-summary`/error callouts (`alert`) only; the checkout timer announces at 5 min and 1 min.
10. **Dialogs**: native `<dialog>`, focus trap by the platform, focus returns to the trigger on close.
11. **Language**: `<html lang="pl">`; foreign terms (`Europe/Warsaw`) in `lang="en"` spans only where read aloud matters.
12. **Gate**: the audit script runs axe on every route in both themes; `serious` and `critical` fail the journey job (existing `expectNoSeriousA11yViolations`).

## 8. Copy rules (Polish)

- Imperative verbs on actions with the object ("Zapłać 180 zł", "Wyślij opinię"), sentence case, no full stops on buttons.
- Months and weekdays lowercase and in the genitive: `wtorek, 24 listopada`; "od listopada 2026"; money `180,00 zł` in lists and totals, `180 zł` in compact card prices; durations `60 min`.
- One word per concept: **konsultacja** (the service), **rezerwacja** (the booking record), **termin** (the time), **programista** (practitioner; the product name "gabinet" stays in the URL and title), **opinia** (review), **spór** (dispute).
- Never print internal versions, ids or codes (`wersja dev-1`, GUIDs) as primary text.
- Empty and error texts say what happened and what to do next in one or two sentences.
- All strings stay in one resource per area (the current `strings.ts` become `*.resx` or constant classes); the hook texts of section 9 are copied verbatim.

## 9. Hooks the Playwright journeys rely on (the rewrite MUST preserve)

Source: `tests/e2e/journeys/*` (J1 browse, J2 onboard+approve, J3 book+pay+review, J4 assistant down, J5 cancel+refund, J6 dispute, J7 foreign origin) and `tests/e2e/harness/pages*.ts`, `tests/e2e/journeys/support/*`. Locators are role/label/text first; `data-testid` only where no accessible name exists. Everything in quotes is an exact accessible name or visible text; changing one changes the page object in the same PR.

### 9.1 URLs (routes and query parameters)

`/`, `/programisci`, `/programisci?q=<text>`, `/programisci/<problem>[/<tech>]`, `/gabinet/<slug>`, `/rezerwacja/<offeringId>?start=<utc>&gabinet=<slug>`, `/checkout/mock/<bookingId>`, `/rezerwacja/potwierdzenie/<bookingId>`, `/moje/rezerwacje`, `/moje/rezerwacje?role=practitioner`, `/moje/rezerwacje/<id>?role=client`, `/moje/opinie/<bookingId>`, `/logowanie?redirect=<path>`, `/rejestracja`, `/gabinet-programisty`, `/gabinet-programisty/{profil,oferty,dostepnosc}`, `/admin/{weryfikacja,spory,ksiega}` (`/admin/weryfikacja?status=Submitted|Approved`). Sign-in redirects back to `redirect`; a visitor on a protected route is redirected to `/logowanie?redirect=…`. `POST /api/auth/login`, `/api/auth/logout` stay available (the harness signs in through them; in Blazor: keep the same two endpoints on the host, J-harness `signInViaBff`).

### 9.2 Landmarks, headings, regions

| Hook | Where |
|---|---|
| `h1` | every page has exactly one; named: practitioner display name on `/gabinet/<slug>`; `Panel programisty`; `Rezerwacja konsultacji`; `Płatność (tryb demonstracyjny)`; `Spory`; `Weryfikacja programistów` |
| `h2` | `O mnie`, `Oferta` (profile); first `h2` of the booking detail (the harness waits for it) |
| `h3` | offer title inside the offer's list item (`getByRole("heading", {level: 3, name: <title>})`) |
| heading | `Dziękujemy za opinię` (after a review is sent) |
| `navigation` named `Filtry` | the filter links of the list; category links inside it, `exact` name = category name |
| `region` named `Lista programistów` | holds the result `ul`; each direct `li` is one card (the card's own lists must not be the first `ul` inside the region) |
| `region` named `Opinie` | reviews on the profile |
| `region` named `Historia weryfikacji` | admin verification detail |
| `listitem` | offer card (contains the offer `h3`); checklist item (`hasText` = checklist name); dispute card (`hasText` = booking id); practitioner results |
| `definition` (`dd`) | booking detail values: status badge text, payment line (`Opłacona`, `/Zwrócono:/`); keep a real `dl` |
| `alert` | error callouts and banners (the harness reads `getByRole("alert")` texts) |
| `alertdialog` named `Czy na pewno anulować rezerwację?` | cancel confirmation |
| `group` named `/Technologie/` | the technology checkboxes of the offer form |
| `script[type="application/ld+json"]` | exactly one on the public profile, containing the display name (J-test of SEO) |
| `data-testid` | `forbidden` (admin pages for a non-admin), `unavailable` (service unavailable callouts), `proposal` (the assistant's proposal block) |

### 9.3 Links

`Zarezerwuj: <offer title>` (profile, one per offer), the slot links named by their time `HH:MM` (regex `^\d{2}:\d{2}$`, inside the offer's list item), practitioner card link named exactly by the display name, `Szczegóły` (booking list rows), filter and category links.

### 9.4 Buttons (accessible names)

`Zaloguj się`, `Załóż konto`, `Załóż gabinet`, `Zapisz`, `Pokaż kod`, `Sprawdź kod`, `Wyślij do weryfikacji`, `Pomóż mi opisać problem`, `Dodaj propozycję do opisu`, `Zapłać`, `Symuluj udaną płatność`, `Anuluj rezerwację`, `Tak, anuluj rezerwację`, `Zgłoś spór`, `Wyślij zgłoszenie`, `Wyślij opinię`, `Zatwierdź`, `Rozstrzygnij`, `Przejdź do potwierdzenia`, `Potwierdź rozstrzygnięcie`. Playwright matches `name` as a case-insensitive **substring** unless the page object says `exact`, so `Zapłać 180 zł` still satisfies `getByRole("button", {name: "Zapłać"})`; keep the leading word unchanged and make sure no second button on the same page contains it.

### 9.5 Form labels (`getByLabel`)

`Adres e-mail`, `Hasło`, `Nazwa wyświetlana`, `Nagłówek`, `Opis` (profile bio; `getByLabel` is a substring match, so on `/gabinet-programisty/profil` exactly one control may contain "opis" in its label: do not add `Opis publiczny` next to it, and renaming it to `O mnie` needs the page object changed in the same PR), `Link do portfolio`, `NIP`, `Nazwa oferty`, `Opis oferty`, `Cena (zł)`, `Rodzaj problemu` (a `select` whose option 0 is `Wybierz…`), `Login GitHub`, `Opisz problem`, `Propozycja opisu`, `Treść opinii`, `Opis sporu`, `Notatka`, `Kwota zwrotu w zł`. Checkbox `/Akceptuję Regulamin/` (role checkbox). Radios: `1 z 5` … `5 z 5` (role radio, review rating), `Zwróć klientowi część` (and its siblings `Wypłać programiście`, `Zwróć klientowi całość`). Label matching is by substring, case-insensitive: a label may gain words, but must not become ambiguous with another label on the same page (`Opis` vs `Opis oferty` are on different pages for this reason).

### 9.6 Texts the journeys assert

`Wyniki: <N>` (`/^Wyniki: \d+$/`, list count), `Nowy` (exact, new practitioner badge), `Zatwierdzony`, `Szkic`, `Czeka na weryfikację`, `Gotowe` (checklist), `Opłacona`, `Zwrócono: <kwota>`, `Rezerwacja potwierdzona. Dziękujemy!`, `Rezerwacja została anulowana.`, `Spór został zgłoszony.`, `Zapisano.`, `Profil wysłany. Administrator sprawdzi go i da znać.`, `Połączono konto GitHub: <login>.`, `Opinia została opublikowana na profilu programisty.`, `Ocena: <n> z 5` (review in the profile list), `Sugerowana kategoria`, `Asystent jest chwilowo niedostępny. Opisz problem samodzielnie i zarezerwuj termin.`, `Nie możemy teraz pokazać terminów`. Texts rendered through the badge components must remain real text nodes (not CSS `content`).

### 9.7 Behaviour the harness waits on

- Pages are HTML first; for Blazor interactive server the journeys await the circuit (guide section 13, `CircuitWatch`) before clicking; inputs before the circuit is connected must not be silently lost on the pages a journey drives first (login, register).
- The profile renders its h1 without JavaScript; the slots load afterwards (hook: first slot link appears; harness reloads until it does).
- Checkout, confirmation and booking detail read the booking from the backend and may show "Odśwież stronę" on a 429/503; keep that message and its recovery (reload).
- After `Zapłać` the browser lands on `/checkout/mock/<id>`; after `Symuluj udaną płatność` on `/rezerwacja/potwierdzenie/<id>`; the booking id in the URL is read by the harness (`bookingIdFromUrl`).
- Cross-origin: a foreign `Origin` on a state-changing request answers `403 cross_site_request` (J7): the origin guard of guide section 8 stays in front of the host.

## 10. How the contract is enforced

1. `StyleContractTests` (xUnit, reads `wwwroot/app.css`): all tokens of 3.1/3.2 exist in both blocks where required; the dark block redefines only colour tokens; no hex/`rgb(`/`hsl(`/named colour outside the two token blocks; every class name of section 4 exists; `:focus-visible` and `prefers-reduced-motion` rules exist.
2. `ContrastTests`: parse the token block and assert the 28 pairs of 3.3 (same arithmetic as `contrast.py`).
3. `HooksTests` (bUnit): the components render the names of section 9 (one test per component).
4. `ui-audit` (Playwright, `tests/e2e/tools/ui-audit`): run in the journeys CI job on a seeded stack; zero horizontal overflow, zero axe serious/critical, zero tap targets < 44 px at 390 px on every route in both themes become assertions once the port is in (today they are findings).
5. Screenshots of the audit are the visual reference; they are regenerated, not hand-edited.

## 11. Not in this contract

Brand identity (logo, name styling), illustration, charts (the product has none; if added they follow guide section 9 with `--chart-*` tokens and a text alternative), animations beyond 150 ms opacity/transform transitions, a user-controlled theme switch, web fonts, an icon font, any JS dependency.
