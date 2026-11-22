# Payments go-live: the owner's five steps (T-09.05)

What this is: the steps only the owner can do to turn the Stripe adapter from "built and tested against recorded shapes" into "taking real test payments". Choices and reasons: `docs/adr-proposals/0011-payment-provider-stripe-connect.md`.
Do it first in **test mode** (Stripe's sandbox), then repeat in live mode with the live keys.

**Not proven by the build or by CI** (say this to anyone who asks whether payments "work"):

- **Real traffic to Stripe.** Every test uses a recorded-shape double or `stripe-mock`; no request has reached Stripe. The API version pinned in the adapter (`2026-11-18.endive`, from the OpenAPI spec) has not been exercised.
- **Real webhook delivery.** The signature scheme is implemented from the documented form and tested with generated signatures; no event has been signed by Stripe. The header name, the secret's form and the event payload shapes are confirmed by step 4 only.
- **BLIK.** It is requested (`allowed_payment_method_types`) and its delayed outcome is mapped (`checkout.session.async_payment_*`), but BLIK availability on the account, the six-digit code flow and the delayed-notification behaviour were never seen.
- **Connect onboarding in Poland**, the `transfers` capability becoming active, a transfer to a real connected account, and a refund of a charge: not seen. `stripe-mock` (integration tier) shows the requests are well formed; it returns canned objects.
- **The legal structure** (platform balance until the transfer, A-06) and **Stripe's fees** on charges and refunds.

## The five steps

1. **Create the Stripe account** (stripe.com), country Poland, and stay in *test mode* (the dashboard toggle). Use this account for this product only: Booking answers `202 Retry-After` to events about sessions it does not know,
   so a second product on the same account would be retried by Stripe for days. In *Settings > Payments > Payment methods* enable **Cards** and **BLIK**. In *Settings > Balances* (Payouts) set the platform's own payout schedule to
   **manual** so the balance is not swept to your bank before Booking has made the transfers (ADR 0011, "what never holds funds means").
2. **Turn on Connect** (*Connect > Get started*) as a **platform that pays out sellers**; choose **Express** accounts. Complete the platform profile Stripe asks for. Note the Connect branding / onboarding settings (name, icon) the practitioners will see.
3. **Set the two secrets** in the GitHub environment `staging` (never in a chat or a file): `PAYMENTS_STRIPE_SECRET_KEY` = a secret key (`sk_test_...`) or, better, a restricted key (`rk_test_...`) with write access to Checkout Sessions, Refunds,
   Transfers, Accounts and Account Links and read access to PaymentIntents and Charges (*Developers > API keys*); `PAYMENTS_STRIPE_WEBHOOK_SECRET` is created in step 4. They map to `Payments__Stripe__SecretKey` and
   `Payments__Stripe__WebhookSecret` on Booking (`deploy/secrets.map`; both required). `deploy/fly/booking.fly.toml` already says `Payments__Provider = "Stripe"`. Set `Site__BaseUrl` (the web app's public address) in the same file:
   Checkout returns to `{Site__BaseUrl}/bookings/{id}` and Connect onboarding to `{Site__BaseUrl}/practitioner/payments` (that page is not built yet, see the PR).
4. **Register the webhook endpoint** (*Developers > Webhooks > Add endpoint*): URL `https://<booking-public-host>/webhooks/payments/stripe`; **API version `2026-11-18.endive`** (the one the adapter requests; pick it explicitly, do not leave the account default);
   events: `checkout.session.completed`, `checkout.session.async_payment_succeeded`, `checkout.session.async_payment_failed`, `checkout.session.expired`, `refund.created`, `refund.updated`. Copy the endpoint's **signing secret** (`whsec_...`)
   into `PAYMENTS_STRIPE_WEBHOOK_SECRET`, then run the Deploy workflow (a tag). With the secrets in place the Booking host starts; without them it refuses to, naming the missing setting. If the API version above is not offered
   in the dashboard, stop and tell the developer: the adapter's version has to be changed together with the extracted spec (`scripts/stripe-openapi-extract.py`).
5. **Run the booking journey** with two test users: (a) a practitioner calls `POST /api/v1/me/payments/account/onboarding` (the web page that does this is not built; use the API with the practitioner's token), opens the returned
   `onboardingUrl`, completes Stripe's test onboarding (test data from Stripe's Connect testing page), then `GET /api/v1/me/payments/account` answers `ready`; (b) a client books that practitioner and pays with the test card `4242 4242 4242 4242`
   (and, separately, BLIK in test mode with Stripe's test code); the booking becomes `Confirmed` and the webhook shows `Applied` in the dashboard (*Webhooks > the endpoint > events*: all `200`); (c) cancel an early booking: a refund appears in
   Stripe and the ledger `Refund` row follows; (d) move the clock (the fake-profile clock endpoint does not exist on staging, so use a booking whose start was set in the past by the owner's seeding, or wait) and check that after `start + 72 h` the
   payout job makes one **transfer** to the connected account (*Connect > Transfers*) and the `payouts` row is `Sent`. Record what you saw, including every difference from this document, in the PR or the decision log.

If a step fails, send the developer: the HTTP status and Stripe error `code` from the log line (`Payment provider refused an onboarding call: HTTP {Status} {Code}`, `Payout request failed ... {ExceptionType}`), and the event id from the dashboard. Never send a key, a signing secret or a customer's data.

## Rotating and switching

- Keys: RUNBOOK 5.4. Test to live: repeat steps 1 to 5 with the live-mode keys and a **new** webhook endpoint (live and test have different signing secrets); practitioners onboard again in live mode.
- To stop taking payments quickly: unset nothing (the host would refuse to start); disable the offering or the practitioner instead, or roll the Booking image back (DEPLOY section 3).
