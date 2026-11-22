# ADR-0013 (proposal): contract availability and collaboration proposals

Status: **proposed, revisable.** The owner asked for a way for practitioners to advertise that they are open to contract work and for clients to start a negotiation
with them. The specification repository is read-only for the implementer; this file records the decisions taken with the owner and the CONTRACTS / SPEC text the
owner should change in `gabinety-tech-spec`. Task ids `T-13.01` and `T-13.02` are **working numbers**: no such task exists in the spec repository yet.

## 1. What is added

1. **Contract availability** on the practitioner profile (Directory): a status (`not available`, `available now`, `available from <date>`), weekly capacity in hours and
   an optional indicative hourly rate, shown on the public profile and on the search card, and filterable (`contract=1`).
2. **Collaboration proposals** (Booking): a signed-in client sends a structured proposal to a practitioner who accepts proposals; the two parties negotiate in
   rounds; on mutual acceptance each side sees the other's e-mail and they contract **outside the platform**.

## 2. Decisions taken with the owner

| # | Decision | Reason |
|---|---|---|
| 1 | **No fee at all** on proposals or on contracts agreed through them. `Gabinety.Billing`, `FeeHook` and the first-consultation fee are untouched. | Owner: "nothing is charged on the platform for now". |
| 2 | **Structured negotiation, not a chat.** A proposal carries kind (short or long term), hourly rate in grosz, hours per week or total hours, duration in months, start date and a short text. The party whose turn it is accepts, counters (new terms) or rejects; the client may withdraw. | Easier to filter for contact details, easier to show what was agreed, consistent with static server rendering (no live connections). |
| 3 | **Optional company name**, visible to the practitioner from the first round. Without it the practitioner sees "Klient D." (the existing client snapshot). | Owner choice. A company name is a route to off-platform contact before acceptance; this is accepted knowingly. |
| 4 | **Contact is revealed on mutual acceptance only.** The client sees the practitioner's contact address, the practitioner sees the client's e-mail snapshot. | Keeps the rule "contact details after commitment" that the booking flow already has. |
| 5 | **Visible client panel** (`/moje/propozycje`) and a "Propozycje" tab in the practitioner area. | The client must see status and answers. |
| 6 | **Lives in Booking**, not in a new service. | Booking already owns notifications, the client e-mail snapshot, idempotency, erasure and demo reset. A new service would add a large deploy ceremony for no gain. |
| 7 | **Naming: "propozycja" / Proposal, "współpraca".** "Oferta / Offering" stays the practitioner's consultation listing. | Avoids the clash with `Features/Offerings`. |

## 3. Behaviour

State machine (pure, no I/O):

| From | Action (who) | To |
|---|---|---|
| none | Propose (client) | AwaitingPractitioner |
| AwaitingPractitioner | Accept / Counter / Reject (practitioner) | Accepted / AwaitingClient / Rejected |
| AwaitingClient | Accept / Counter / Reject (client) | Accepted / AwaitingPractitioner / Rejected |
| either awaiting state | Withdraw (client) | Withdrawn |
| either awaiting state | Expire (system) | Expired |

- One Accept of the other side's latest terms is mutual acceptance: the author of those terms has already agreed to them.
- A stale round number, a wrong turn or a wrong state gives `409 invalid_transition`.
- At most one open proposal per client-practitioner pair (`409 proposal_exists`).
- Proposals expire after `Proposals:ExpiryDays` (default 14) without an answer; at most 6 counter-proposals per thread.
- Creating a proposal is rate limited per user (5 per hour, 20 per day) by a feature limiter, because the shared kernel is at its 400-line budget.

Visibility: terms and rounds are visible to both parties. A non-party gets 404. The practitioner sees the client as the "Klient X." snapshot plus the company name when
given. Contact addresses appear only in the `Accepted` state. After an erasure the counterpart reads "Usunięty użytkownik" and the text "[usunięto]". Admins have no access
in this version.

Free text (`Text`, `CompanyName`) is never logged, never put in an exception message, a metric, a notification or a problem `detail`. A per-service copy of the contact
detail rules refuses e-mails, handles, messenger links, long digit runs (ISO dates excepted) and foreign links. As with reviews, this is anti-leakage and not security.

## 4. Notifications

The existing `notifications` row is generalised: `booking_id` becomes nullable, `proposal_id` and `round` are added, and the one unique index becomes two filtered ones
(booking rows keep their guarantee; proposal rows are unique per proposal, kind, recipient and round so that each counter-proposal sends one e-mail). New kinds:
`ProposalReceived`, `ProposalCountered`, `ProposalAccepted`, `ProposalRejected`, `ProposalExpired`. Templates carry no user text, only a link to the thread.

## 5. Error codes added

`own_profile`, `practitioner_not_available`, `proposal_exists`. Reused: `validation_failed`, `contact_details_not_allowed`, `invalid_transition`, `rate_limited`,
`dependency_unavailable`, `not_found`, `idempotency_conflict`.

## 6. CONTRACTS / SPEC to change in `gabinety-tech-spec`

- 3.x: `PractitionerCard` and the profile get an optional `contract` object; add `GET/PUT /api/v1/me/practitioner/contract-availability` and the internal
  `GET /internal/v1/practitioners/{id}/contract`; add the `contract=1` search filter.
- 4.x: the proposal resources (`/api/v1/proposals`, `/api/v1/me/proposals`, accept, counter, reject, withdraw), their DTOs and visibility rules.
- 8: the new notification kinds. 9: the constants `Proposals:ExpiryDays` = 14, creation limits 5 per hour and 20 per day, counter-proposal cap 6, bounds of rate and
  hours. 10.1: the three new error codes. 10.2: the proposal creation limit.
- Task ids `T-13.01` and `T-13.02`, which the owner assigns.

## 7. Known weaknesses

- The company name and the free text are obfuscatable routes to off-platform contact (SPEC 6.1: anti-leakage, not security).
- Altering the unique index on `notifications` touches the table of the paid flow; the integration tier proves that the booking guarantee still holds.
- The practitioner's e-mail is resolved from Directory at read time; if Directory is down the accepted view says the contact is temporarily unavailable.
- Withdraw sends no e-mail; Reject carries no message. Both are cheap to add later.
