# ADR-0011 (proposal): the payment provider is Stripe Connect, with separate charges and transfers

- Status: **proposed**, decided by the supervisor on the owner's instruction ("deliver the whole product autonomously"); revisable by the owner at any time. The spec repository
  (`gabinety-tech-spec`) is read-only for the implementing session, so this proposal lives here; DECISIONS D-26 (provider), D-23..D-25 (fee, refunds, payout rules) and ADR-0005
  need a line pointing at it when the supervisor accepts it.
- Date: 2026-11-22
- Task: T-09.05 (payment provider adapter), with the payout-rule decisions that touch T-04.07 and T-04.08.
- Supersedes nothing. Builds on ADR-0005 (marketplace-capable provider, platform never holds funds), D-23, D-24, D-25, CONTRACTS 4.1, 4.3, 8.1, 9.1, 9.2.

## Sources, and what is and is not verified

- **Verified:** request fields, response properties and event names are Stripe's official OpenAPI spec, `stripe/openapi` `latest/openapi.spec3.json`, cloned with `git clone --depth 1`
  on 2026-11-22, `info.version` **2026-11-18.endive**. `scripts/stripe-openapi-extract.py` turns the parts the adapter uses into
  `tests/Gabinety.Booking.Tests/Payments/Stripe/Fixtures/openapi-fields.json`, and a test fails when the adapter sends a field the spec does not know.
- **Not verified:** the Stripe documentation pages (`docs.stripe.com`, `stripe.com/docs`) could not be fetched: the session's egress proxy answers HTTP 403 for them. Every statement below
  about Stripe's *behaviour* (as opposed to its field names) is from the author's knowledge, marked **[unverified]**, and is part of what the owner's sandbox run must confirm
  (docs/operations/PAYMENTS-GO-LIVE.md).

## Context

The product sells a 30 or 60 minute consultation (D-24). The client pays when booking; the practitioner is paid after the consultation; the platform keeps 15 % of the first completed
consultation of a client-practitioner pair (D-23). Money may go back to the client until the booking is final (D-25): full refund on an early client cancel, on a practitioner cancel,
on a practitioner no-show and on an administrator's `refund_full`; a partial refund on `refund_partial`. The platform must not run its own pool of client money (ADR-0005).

Three facts of the existing code and spec decide the mechanism:

1. **The fee is known late.** `FeeHook` computes `fee_minor` when a booking *enters* a money-complete status, using the unique `PlatformFee` ledger row of the pair: two pending bookings of one pair
   both look like "first" until one completes. A late cancel, a no-show report and a `refund_partial` (fee on the kept part) also settle the fee after the charge.
2. **Money may not move before the booking is final.** CONTRACTS 8.1/9.2: payout after `end + 48 h`; a dispute freezes it; an administrator's resolution decides it.
3. **The charge is made when booking**, up to 60 days before the consultation.

## Decision

### 1. Provider and charge model

- **Stripe Connect**, **Express** connected accounts (`controller[stripe_dashboard][type]=express`, `fees.payer=application`, `losses.payments=application`, `requirement_collection=stripe`,
  `country=PL`, capability `transfers` requested). The practitioner onboards on Stripe's hosted flow (`POST /v1/account_links`, `type=account_onboarding`); Booking stores only the account id.
- **Separate charges and transfers**, not destination charges:
  - **Charge:** a hosted **Checkout Session** (`POST /v1/checkout/sessions`, `mode=payment`, PLN, `allowed_payment_method_types` = `card`, `blik`) on the platform account. This is the simplest hosted option:
    no front-end code, Stripe renders the BLIK and card forms, Polish locale (`locale=pl`). BLIK is a delayed-notification method **[unverified]**: the session can complete unpaid and
    pay later, so the adapter maps `checkout.session.completed` only when `payment_status` is `paid`, and `checkout.session.async_payment_succeeded` / `_failed` for the rest.
    The session expires after 35 minutes (Stripe's minimum is 30); Booking's hold is 10, and a payment that arrives after the hold is refunded in full by the existing handler.
  - **Payout:** after the due time, `POST /v1/transfers` for `price - fee` to the connected account (`destination`), tied to the charge with `source_transaction` and `transfer_group`.
    The fee is *what is not transferred*; there is no `application_fee_amount` and nothing to refund when the fee is waived or the booking is cancelled.
- **Why not destination charges with `application_fee_amount`** (the supervisor's first draft): the OpenAPI spec confirms the mechanism (`payment_intent_data.transfer_data`,
  `payment_intent_data.application_fee_amount`), but (a) the fee must be given at charge time, while D-23's pair rule is only decidable at completion (fact 1) — two simultaneous first
  bookings of a pair would both be charged the fee, or a refunded booking would have to give it back; (b) the transfer to the connected account happens at charge time, so the practitioner holds the money
  during the dispute window and a refund needs `reverse_transfer` (a transfer can be reversed only by the application that created the charge, spec text) and can fail if the practitioner has paid out;
  (c) D-25's "payout after `end + 48 h`" would need manual payout schedules on every connected account plus a platform-initiated payout per booking — more moving parts, same custody question.
- **Alternative kept in reserve: destination charge + manual payout schedule** on the connected account (`settings[payouts][schedule][interval]=manual`) + `POST /v1/payouts` with the `Stripe-Account` header at the due time, and the fee
  taken by a partial `POST /v1/transfers/{id}/reversals` at completion. It leaves the funds on the practitioner's Stripe balance instead of the platform's. It was not chosen because it is more calls, more failure modes
  and the reversal needs the balance to be still there; it is the fallback if the owner's lawyer (A-06) rejects the platform balance below.

### What "the platform never holds funds" means here (honest statement)

Between the charge and the transfer the money is on the **platform's Stripe balance**: at Stripe, a licensed payment institution, not in a platform bank account, and nothing in Booking pools or moves it.
For a booking made 60 days ahead this is up to about 63 days **[unverified: Stripe may pay the balance out to the platform's bank on its own schedule; the platform's payout schedule should be set to manual or a long
delay so that the balance is not swept to the bank before transfers are made]**. This is the standard Stripe pattern for marketplaces that pay out after delivery, but whether it keeps the platform outside
payment-service regulation in Poland is exactly assumption **A-06** and stays a question for a lawyer; this ADR does not answer it. Option B above is the fallback. Stripe's own processing fees on the charge are
absorbed by the platform (D-23) and are **not** returned by Stripe on a refund **[unverified]**, so every refund costs the platform the processing fee; the owner may want a number for that.

### 2. Refunds (D-25)

`RefundAsync` finds the Session's PaymentIntent (`GET /v1/checkout/sessions/{id}`) and posts `POST /v1/refunds` with `payment_intent`, `amount` and `metadata[provider_payment_id]` (so the refund webhook finds the payment again).
- Full refund = the whole charge, the fee portion included: because the fee never moved, "the fee is refunded too" is true by construction.
- `reverse_transfer` and `refund_application_fee` exist on `POST /v1/refunds` in the spec and are **not used**: no transfer exists when a refund is due. The windows guarantee it (section 3): a refund can only
  follow `Confirmed` (cancel, no-show claim, dispute) or `Disputed` (resolve), and the payout is made only from a money-complete status after its due time. A late cancel (< 24 h) is no refund and a payout at `start`.
- The ledger `Refund` row is written when Stripe confirms: `refund.created` or `refund.updated` with `status=succeeded` (the Mock confirms at once). Repeated events are no-ops (`RefundLedger`).

### 3. Payout timing

- `payout_due_at = max(end + 48 h, start + 72 h)` for `Completed` (set by the auto-complete job: `AutoCompleteJob.PayoutDueAt`), so the practitioner no-show claim window of CONTRACTS 4.2 (`start + 72 h`) has closed before money moves.
  For a 30 or 60 minute consultation the result is always `start + 72 h`.
- `Resolved`: `payout_due_at = resolved_at`, now stored at the resolution (before, the payout job fell back to `resolved_at`; the fallback stays for older rows).
- Unchanged: `LateCancelledByClient` = `start_utc`, `ClientNoShow` = the report time (neither has a dispute transition, so no window to wait for).
- **Decision (supervisor, on PR 127): the dispute right lasts to `start + 72 h`, as CONTRACTS 4.2 says.** Before, a dispute was only possible from `Confirmed`, and `Confirmed -> Completed` happens at `end + 48 h`, so the right
  ended at `start + 49 h` for a one-hour session. Now `Completed -> Disputed` (trigger `ClientDisputes`) is allowed while `now < start + 72 h`, any kind, client only (`DisputeFeature.InWindow`). No money has moved by then: the payout is due
  at `max(end + 48 h, start + 72 h)` and is never made for a `Disputed` booking. Completion had already set `fee_minor`, `payout_minor`, `payout_due_at` and claimed the pair's `PlatformFee` row; the dispute clears the three amounts and
  removes that unpaid fee row in the same transaction (the one deliberate delete of a ledger row; it frees the pair), and the resolution computes fee and payout again (`release`: fee and payout as for completion; `refund_full`: none;
  `refund_partial`: on the kept part). Tests: `DisputeFromCompletedTests` (start + 60 h succeeds, 71 h 59 min succeeds, 72 h and 73 h refused, payout held while disputed, release pays once, refund leaves no fee row).
  CONTRACTS 8.1 needs the row `Completed` + dispute (client, before `start + 72 h`) -> `Disputed`.
- **The payout job's retry bound is durable.** Migration `T-09.05_StripeConnect` adds `payouts.attempt_count` (int, default 0) and `payouts.next_attempt_at` (timestamptz null). A run takes a payout up under
  `SELECT ... FOR UPDATE SKIP LOCKED` on the booking row (a row held by another instance is skipped, not waited for), sets `Pending`, increments `attempt_count` and sets `next_attempt_at = now + lease`
  (`Jobs:Payout:StalePendingSeconds`, 120) — the lease keeps other instances off and a crashed owner's lease just runs out; a failure sets `Failed` and `next_attempt_at = now + backoff` (60 s, doubling);
  after `Jobs:Payout:MaxAttempts` (5) the job logs one error and no longer selects the row until an operator resets the counter (RUNBOOK 7.4). The in-memory counter is gone. The idempotency key stays
  `payout:<bookingId:N>`, so a retry, a crash recovery and two instances are one Stripe transfer.

### 4. The adapter

- `src/Gabinety.Booking/Payments/Stripe/**`, all types `internal`, no Stripe package: plain `HttpClient` and `System.Text.Json` (the official `Stripe.net` would add a large dependency tree and a vendor type surface for the six calls used;
  an architecture test enforces that nothing outside the namespace and `PaymentsFeature` names it and that no `Stripe*` package is referenced).
- Selected by `Payments:Provider=Stripe`; secrets `Payments__Stripe__SecretKey` (`sk_` or `rk_`) and `Payments__Stripe__WebhookSecret` (`whsec_`): the host refuses to start without them, in every profile. Mock stays fail-closed outside the fake profile (F-01).
- Every write sends `Idempotency-Key` = the caller's key (`checkout:<bookingId:N>`, `refund:<bookingId:N>:<reason>`, `late:<paymentId:N>`, `payout:<bookingId:N>`, `account:<practitionerId:N>`; an account link gets a fresh key: it is single use) and `Stripe-Version: 2026-11-18.endive`.
  **The webhook endpoint at Stripe must be created with the same API version**, or the event shapes may differ.
- Webhook: the documented `Stripe-Signature` scheme, `t=` and `v1=` (several `v1` while a secret is rolled), HMAC-SHA256 over `"{t}." + raw body`, key = the whole `whsec_...` string, 5 minute tolerance, constant-time comparison,
  verified over the raw bytes before any parsing. Event mapping: see `StripeEventMapper`; unknown events are `Ignored`; `amount_total` and `currency` are checked by the existing handler against the booking (mismatch = `Rejected` + alert log).
- Event delivery to configure at Stripe (the six used): `checkout.session.completed`, `checkout.session.async_payment_succeeded`, `checkout.session.async_payment_failed`, `checkout.session.expired`, `refund.created`, `refund.updated`.
  Events for sessions Booking does not know (another product in the same Stripe account) are answered `202` with `Retry-After`, per CONTRACTS 4.3: **use a Stripe account for this product only.**
- **Connected-account state is read, not pushed:** `GET /api/v1/me/payments/account` asks Stripe while the account is not ready (`details_submitted` and `payouts_enabled` and `capabilities.transfers=active`). `account.updated` is not consumed
  (it would need the internal vocabulary to grow); the practitioner's page calls the status endpoint when Stripe returns them to `return_url`.

### 5. Where the connected account lives

`practitioner_payment_accounts` in **bookingdb** (practitioner id, provider, provider account id, `ready`): Booking is the only service that pays, and Directory should not learn payment vocabulary. Booking creation refuses
an offering whose practitioner has no ready account of the active provider with `422 practitioner_payments_not_ready` (new code; CONTRACTS 10.1 needs it) — only when the provider requires accounts (Stripe), never for Mock.
Endpoints (practitioner only): `GET /api/v1/me/payments/account`, `POST /api/v1/me/payments/account/onboarding`, both in `docs/operations/ROUTE-AUTH.md`.

## Consequences

- Booking can be deployed once the owner sets the two secrets and the webhook endpoint exists (PAYMENTS-GO-LIVE.md); until then the deploy gate (`secrets.map` required rows) stops it, as intended.
- Funds sit on the platform's Stripe balance until the transfer; the A-06 question is sharper, not answered.
- A practitioner whose account stops being ready after a booking was charged blocks that payout (counted as failed attempts, then given up and logged): an operator action, RUNBOOK 7.4.
- Not built: Stripe-side disputes and chargebacks (`charge.dispute.*`), failed refunds (`refund.failed` is `Ignored`), account deauthorisation, payout reports, VAT invoices for the fee (A-07), multi-currency.

## Alternatives considered

- Destination charges with `application_fee_amount` (above): fee unknown at charge time; money at the practitioner during the dispute window.
- Destination charge + manual payouts + transfer reversal (above): fallback.
- Przelewy24 or Tpay: not evaluated here; D-26 listed them, the supervisor chose Stripe for its Connect product, its sandbox and `stripe-mock`.
- The `Stripe.net` package: more surface than six calls need; vendor types would reach the code. Can be adopted later behind the same adapter boundary.
- Embedding the card form (PaymentIntent + Elements): more front-end work and PCI scope; hosted Checkout documents BLIK and refunds with no code.
