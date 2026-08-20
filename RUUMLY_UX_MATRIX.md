# Ruumly UX / Friction Matrix

Companion to `RUUMLY_FULL_AUDIT.md`. Three separate products connected by one
workflow — customer, supplier, admin — each with its own friction table. Click
counts are the interaction cost measured against the live product (a field's
focus + typing counts as 2; a chip tap as 1). "After" reflects the recommended
change; ✅ marks what shipped this round.

The goal is not mathematically minimum clicks — it is **minimum unnecessary
interaction**. Confirmations on irreversible actions stay.

---

## CUSTOMER

| Flow / Page | Problem | Current friction | Recommended change | Clicks before → after | Impact | Priority |
|---|---|---|---|---|---|---|
| `/request` step 1 | A whole screen whose only action is one tap | +1 Next tap + a transition | Merge service-pick into step 2 as chips atop the details step | 16 → ~14 | Fewer drop-offs before the first real question | P1 |
| `/request` step 2 (moving) | Destination city not required, absent from the gate | A move filed with no "to" — unquotable | Require `toCity` when moving/vanrental-with-driver is selected | same clicks, 0 dead leads | Lead **quality**, not count | P1 |
| `/request` submit | Rate-limited in a bucket shared with the contact form | First-time customer 429'd behind office/CGNAT NAT | ✅ Own `lead-request` bucket (15/10min) | n/a | Removes a silent conversion cliff | **P0 ✅** |
| `/request` nav | Next/Submit below the fold on mobile | Scroll to find the way forward every step | Sticky bottom nav bar (pattern already exists) | −1 scroll/step | Mobile completion | P1 |
| `/request` step 3 | 5 inputs, 1 required (email); no read-back | Cognitive load at the finish line | Collapse address block; add a one-line service+city+date summary | −0 clicks, −load | Confidence to submit | P2 |
| Location detail | No path into `/request` at all | Dead end when nothing is bookable | Add a concierge CTA prefilled with the site's city + vertical | +1 recoverable path | Recovers a warm visitor | P1 |
| Storage scope | Never asks when storage starts | Operator can't confirm a unit | Add a `warehouseStart` chip group (optional to submit) | +1 tap, better lead | Lead quality | P2 |
| Copy | "2-minute request", "picked by hand" | Unenforced/inaccurate claims | Drop the timing number; "we send it to the providers who fit" | n/a | Trust; matches code | P2 |

**Homepage → submitted, single-service move (measured):**

| # | Screen | Action |
|---|--------|--------|
| 1 | Home | Hero CTA |
| 2 | Step 1 | Tap "Moving" |
| 3 | Step 1 | Next |
| 4–6 | Step 2 | City: focus + type + pick suggestion |
| 7–10 | Step 2 | 4 scope chips (size, floor-from, floor-to, heavy) |
| 11–12 | Step 2 | Date: open picker + pick |
| 13 | Step 2 | Next |
| 14–15 | Step 3 | Email: focus + type |
| 16 | Step 3 | Send |

**16 taps, 3 screens, 4 required facts.** Merging step 1 → ~14. The 7 optional
step-3 fields (name, phone, from/to address, note, photos) are correctly optional
and off the required path.

---

## SUPPLIER

| Flow / Page | Problem | Current friction | Recommended change | Clicks before → after | Impact | Priority |
|---|---|---|---|---|---|---|
| Outreach email → decline | No way to say "no" that gets recorded | Free-text reply into a shared inbox, parsed by nobody → counts as silence | ✅ `POST /decline` + reason chips + recorded state + ops alert + quote-page UI ×5 langs | ∞ → **2 taps** | Stops re-fanning-out to refusers; turns silence into signal | **P0 ✅** |
| Outreach email → price | (already good) | 3 taps, unit pre-set | Keep. Fix the `any`-lead `/month` default (S4) | 3 → 3 | — | P1 |
| Quote → outcome | Provider never told if they won | Re-opening shows winner and loser the same "wrapped up" line | On `Chosen`, email the chosen provider; GET carries `wonByYou` | n/a | Second-quote rate | P1 |
| Claim | 226 rows have no contact email → gate can never match | Provider waits for a mail that never comes | Show every claimant a "if nothing arrives in 15 min, write to us" line; backfill the 226 | n/a | Recovers 3 dead channels | P1 |
| Partner-page message | Notification hardcoded English | LV/LT partner's first customer message in the wrong language | Localise via `EmailTranslations` | n/a | Trust | P1 |
| Marketing page | Leads with badge + 28-item catalogue | The proof (a real enquiry) is nowhere | Replace the trust strip with one anonymised enquiry card | n/a | The cheapest credibility on the page | P1 |
| ET provider copy | `sina` on pages, `teie` in email | Register break reads as "not for me" | Convert `provPage/quote/claim` ET to `teie` (~30 strings) | n/a | B2B credibility | P1 (founder call) |

**Email open → price submitted: 3 taps. Email open → decline: 2 taps** (was
impossible — a free-text reply recorded nowhere).

---

## ADMIN

| Flow / Page | Problem | Current friction | Recommended change | Clicks before → after | Impact | Priority |
|---|---|---|---|---|---|---|
| Leads list on API failure | Renders the empty state | "No requests yet" while the API is down | ✅ `isError` → `SectionError` with retry (leads + suppliers) | n/a | The cockpit stops lying green | **P0 ✅** |
| Cockpit queues | Only `needsResponse` shown | `blocked`/`stalled` already in the payload, unread | Read `queueData.queues`; add two counters + deep links | +0 requests | Two queues surface for free | P1 |
| "Customer chose" | No in-app surface | Only an ops email announces a booking-ready offer | Add a `queue=chosen` predicate + cockpit counter | prevents lost conversions | Revenue | P0 |
| Waiting-on-customer | No status; sent offers fall out of every queue | A silent customer is invisible for a week | Add `AwaitingCustomer` (or extend the stalled predicate) | — | Pipeline visibility | P0 |
| Offer send | Unsaved price edits discarded silently | Retype price, forget Save → old price sent | Disable "Review & send" while the draft editor is dirty | −0, −1 mistake class | Correctness | P1 |
| Lead workspace | Nothing polls | A quote landing needs a browser reload to appear | `refetchInterval: 30s` while a lead is expanded | −1 reload | Responsiveness | P1 |
| Status transitions | `MoveTo` is not a state machine | 2 clicks un-book a Booked lead, no confirm | Transition table + 409 on illegal; disable chips on `Converted` | prevents data loss | Correctness | P1 |
| New lead → +1 outreach | 50-row unvirtualized candidate list to scroll | 4 clicks + a long scroll | Collapse candidates once outreach exists; badge already-contacted | 4 → 3 | Daily speed | P1 |
| Directory | No merge; deactivate has no confirm/toast | Silent action beside a hard-delete | Success toast + a duplicate-merge endpoint | — | Maintenance safety | P1 |

**Daily-loop click counts (measured in code):**

- New lead → one more provider contacted: **4 clicks** (5 with resend confirm).
- Provider quote arrives → offer sent to customer: **3 clicks** (4 if edited).
- Mark lead lost: **2 clicks**.

The quote → offer-option step is already **0 clicks** (auto-seeded server-side) —
the single best-designed part of the loop.

---

## Cross-role handoffs to watch

- Customer submits → auto fan-out → provider quotes → **quote auto-seeds the draft
  offer** (0 clicks) → admin sends → customer chooses → **only ops is emailed**
  (no in-app queue, no provider notification). The two weakest links are both at
  the *end* of the loop: the provider is never told the outcome (S3), and the
  admin has no in-app surface for "customer chose" (A2). Fixing those two closes
  the loop the rest of the system already runs well.
