# 2026-08-18 — the Viljandi storage cluster: 18 contacts, 0 replies

First Phase 0 read of real data, and it re-ranks the masterplan.

## What the numbers say

`GET /api/admin/leads/metrics`, after deleting the 2026-08-17 canary:

| metric | value |
|---|---|
| requests30d | 11 |
| contactRate30d | 91% |
| quoteRate30d | 9% |
| matchRate30d | 2 / 11 |
| bookingRate30d | 0 |

## The cluster

Five of eleven requests are **Viljandi storage**, and three of those are the same
person (`aksemeria1995@gmail.com`, 7 Aug ×2, 11 Aug). A fourth arrived on 17 Aug
from `kadijaanson@gmail.com` and is still open.

Outreach on those five leads:

```
f0f1028b  3 contacted → 0 replied   (live, 2026-08-17)
06c6c92e  8 contacted → 0 replied
932beedb  6 contacted → 0 replied
1c022ff4  1 contacted → 0 replied
b082b460  0 contacted             ← never reached anyone
```

**18 provider contacts, every one still `sent`. No quote, no decline, no bounce.**

Supply is not the problem: Lahe miniladu is *in* Viljandi, so are Blackline
Konteinerladu and Miil Smart Warehouse. They were emailed. Nobody answered.

## Why this re-ranks the plan

The masterplan's organising test was *"can a provider price this from what we
sent?"* — and everything shipped on 2026-08-18 improves the page a provider
lands on **after clicking**: photos, per-service price units, the "I can't quote
this" path. All of it is correct and none of it helps if nobody clicks.

18 out of 18 silence points at a stage earlier than the quote page.

## The blind spot that makes this unanswerable

`ResendWebhookEvent` subscribes to `email.bounced` and `email.complained` only.
There is no `email.delivered` and no `email.opened`, and
`ProviderOutreachComposer` deliberately ships no tracking pixel.

So three completely different failures are currently indistinguishable:

1. the mail never reached an inbox (spam placement / domain reputation),
2. it reached them and was never opened (subject line, sender recognition),
3. it was read and ignored (the offer itself is not attractive to a provider).

Each needs a different fix, and we cannot tell which one we have. One opaque 9%
quote rate is not an actionable number.

## Recommended next step, ahead of the rest of Phase 1

Subscribe to `email.delivered` and `email.opened`, record them against
`ProviderOutreach`, and decompose the funnel into
sent → delivered → opened → quote page viewed → quoted. That converts a single
unexplained rate into the stage that is actually failing.

Cheap: two more event types in an existing webhook, one existing tracker, no new
subsystem. And it is a prerequisite for judging whether today's quote-page work
paid off at all — without it, a flat quote rate next month proves nothing either
way.

**This does not cancel the queued Phase 1 items.** ScopeJson, the per-service
intake gaps, the street address and the customer status page all still hold. It
puts one small measurement item in front of them, because it decides whether the
rest is aimed at the right stage.

## Also found

- `b082b460` reached **zero** providers and was dismissed with no note. Worth
  knowing why before assuming the fan-out was at fault.
- `06c6c92e` lists Lahe miniladu and Blackline **twice each** in eight rows.
  Either an admin resend, or the inbox dedupe missed sibling rows. Unconfirmed.
- Three requests from one person for the same thing, all dismissed, is a
  customer telling us plainly that Viljandi storage is real demand.
- `Peetri Miniladu` has an Active provider login but `ClaimedAt` is null — the
  two "claimed" facts have already drifted, as flagged when the delivery gate
  was built. Delivery keys off the login, which is the correct one.

---

## Correction, same day: it was not deliverability

The section above reasoned that the 18 silent contacts might never have been
delivered, and that the absence of bounces proved nothing. The first half of
that turned out to be wrong, and the second half was worse than stated.

**Worse first.** The Resend webhook had never been configured at all — there was
no endpoint in the Resend account, so no bounce, complaint or delivery event has
ever reached Ruumly since the feature shipped. `EmailDeliveryTracker` had never
executed against real traffic and `ContactEmailUnusable` was false on all 1,187
suppliers because nothing could set it. "No bounce recorded" was not weak
evidence; it was no evidence.

Fixed the same day: endpoint created and subscribed to `email.bounced`,
`email.complained`, `email.delivered` and `email.opened`; `Resend__WebhookSecret`
set in Railway. The endpoint now answers an unsigned caller with 401 rather than
admitting it has no secret with 503.

**Now the wrong half.** A full DNS pass over the directory — 1,187 rows, 957
syntactically valid addresses, 565 unique domains — found **562 domains with
valid MX**. Only three rows sit on a domain that cannot receive mail at all
(Merva OÜ, Rinoceras UAB, Valytė MB).

Every Viljandi provider resolves: Lahe miniladu, Blackline, Miil, Alevi Hoiuladu.
Their mail servers are live.

So the dead-address hypothesis is dead. The most likely remaining explanation is
the uncomfortable one — **the providers received the outreach and did not
answer** — with spam placement and unmonitored mailboxes still open as
alternatives. The webhook will separate those on the next send, which is exactly
what it was for.

That changes what to build. It is not a deliverability problem to engineer
around; it is the outreach email, or the proposition inside it, failing to earn
a reply from a business that got it.

## The finding nobody was looking for

**230 of 1,187 suppliers have no email address at all, and 226 of those are
active.** Nineteen percent of the directory is businesses no one can contact by
any means. They are returned by the candidate finder, they inflate every coverage
number the site publishes — the 124 trailer city hubs, the per-city counts — and
the fan-out skips them silently.

That is a supply number that is not what it appears to be, and it is worth
deciding what an uncontactable directory row is actually for.
