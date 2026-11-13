# Demand and service analytics (2026-11-13)

Source: production DB, READ ONLY session, aggregates only
(`railway run --service Postgres -- python -X utf8 tools/supplier_scan/scan.py analytics`).
Customer names, e-mails and phones never leave the database.

## What the numbers say

1. **40 real requests in 4 months, 0 bookings.** That is roughly 2–3 a week. 5 leads reached
   `Quoted`, 2 offers were chosen, and none was confirmed as booked. The marketplace side has
   **0 bookings ever**.
2. **Demand is not where ops is focused.** CLAUDE.md centres ops on Tallinn and Harjumaa, but
   requests came from Pärnu (11), Viljandi (8) and Tartu (5). Tallinn, Viimsi and Maardu together
   sent 3. Either point marketing at Tallinn, or move the supply work to Pärnu, Viljandi and Tartu.
   Discovery already covers Pärnu and Tartu. **Viljandi is not in any discovery zone yet** and has
   the silent-supplier cluster.
3. **Warehouse and van rental are 70% of demand** (14 + 14 of 40). Moving, trailer and cleaning
   have 4, 4 and 2. Warehouse leads mostly end `Unmatched` (4 of 14): that is a supply gap, not a
   demand gap.
4. **"Median first response 0 h" is the robot, not us.** 36 of 38 `ContactedAt` stamps are the
   automatic provider fan-out. Only **6 leads** have an operator message to the customer, median
   6 h after the request. The north-star "median first response" metric overstates speed until it
   is computed from the first human message.
5. **Providers answer 8% of lead mails** (21 quotes on 270 sends), with a median of 1.3 h when they
   do answer. Nobody has ever used the decline link (0). 135 providers who got a request never
   answered: see `silent-providers-reengagement-2026-11-13.md`.
6. **Why leads close is not recorded.** 19 of 26 Dismissed or Unmatched leads have no reason in
   AdminNotes. "Dismissed" is also used for lost leads, duplicates and spam. Without reasons, the
   loss analysis is guesswork.
7. **Acquisition is not attributed.** 0 of 40 leads carry a `utm_source`. 10 were captured
   before attribution existed, and 30 arrived direct or untagged. Tag every link you post or pay
   for, or this table stays empty.
8. **Supply contactability:** of 1,163 active rows, 226 have no e-mail at all (LT 129, LV 90,
   EE 7).

## Suggested next steps (none built)

- A `LostReason` pick-list on close (price, self-arranged, no supply, duplicate, spam, no answer).
- Compute "median first response" from the first `LeadMessages` row, not from `ContactedAt`.
- Add Viljandi (and Rakvere and Haapsalu, 2 leads each) as discovery zones.
- UTM-tag every outbound link.

---


## Headline funnel

| step | leads | of all |
|---|---:|---:|
| received | 40 | 100% |
| contacted by ops | 38 | 95% |
| quoted (Quoted+Converted) | 5 | 12% |
| booked (Converted) | 0 | 0% |
| unmatched | 9 | 22% |
| dismissed | 17 | 42% |

First response: median **0.0 h**, p90 5.5 h; 37 of 38 contacted within 24 h. Still `New` after 2+ days: **0**.

Caveat: 36 of those 38 `ContactedAt` stamps are the automatic provider fan-out, not a human. Leads with an operator message to the customer: **6**, median 6.0 h to the first one. Closed leads (Dismissed/Unmatched) with no reason in AdminNotes: **19 of 26**.

## Provider side

270 outreach mails for 36 leads to 152 suppliers: 21 quotes (8%), 0 declines, 5 bounces, delivered-receipts 188. Median time to quote 1.3 h.

| category | sent | leads | quoted | declined | leads_with_quote |
|---|---|---|---|---|---|
| VanRental | 82 | 13 | 6 | 0 | 6 |
| Moving | 69 | 4 | 7 | 0 | 2 |
| Warehouse | 49 | 11 | 4 | 0 | 4 |
| Cleaning | 36 | 2 | 2 | 0 | 1 |
| Trailer | 22 | 4 | 0 | 0 | 0 |
| Any | 12 | 2 | 2 | 0 | 1 |

## By service

| category | n | handled | quoted | booked | unmatched |
|---|---|---|---|---|---|
| Warehouse | 14 | 14 | 1 | 0 | 4 |
| VanRental | 14 | 14 | 3 | 0 | 1 |
| Moving | 4 | 4 | 0 | 0 | 1 |
| Trailer | 4 | 4 | 0 | 0 | 1 |
| Cleaning | 2 | 2 | 0 | 0 | 1 |
| Any | 2 | 2 | 1 | 0 | 1 |

## By source

| source | n |
|---|---|
| concierge | 39 |
| partner-page | 1 |

## By status

| status | n |
|---|---|
| Dismissed | 17 |
| Contacted | 9 |
| Unmatched | 9 |
| Quoted | 5 |

## By month

| month | n | concierge | quoted | booked |
|---|---|---|---|---|
| 2026-07 | 2 | 2 | 0 | 0 |
| 2026-08 | 16 | 16 | 0 | 0 |
| 2026-09 | 5 | 5 | 1 | 0 |
| 2026-10 | 12 | 12 | 2 | 0 |
| 2026-11 | 5 | 4 | 2 | 0 |

## Last 12 weeks

| week | n |
|---|---|
| 2026-08-24 | 3 |
| 2026-08-31 | 2 |
| 2026-09-21 | 1 |
| 2026-09-28 | 2 |
| 2026-10-05 | 2 |
| 2026-10-19 | 4 |
| 2026-10-26 | 6 |
| 2026-11-02 | 1 |
| 2026-11-09 | 4 |

## Top cities

| city | n |
|---|---|
| Pärnu | 11 |
| Viljandi | 8 |
| Tartu | 5 |
| Valga | 2 |
| Rakvere | 2 |
| Haapsalu | 2 |
| Rīga | 1 |
| Jūrmala | 1 |
| Daugavpils | 1 |
| Tallinn | 1 |
| Maardu | 1 |
| Adazi | 1 |
| Jõhvi | 1 |
| Viimsi | 1 |
| Võru | 1 |

## Acquisition (utm_source)

| utm_source | n |
|---|---|
| (direct/none) | 30 |
| (not captured) | 10 |

## Language

| lang | n |
|---|---|
| et | 26 |
| ru | 8 |
| en | 3 |
| lv | 3 |

## Offers

| status | n | sent | viewed | chosen |
|---|---|---|---|---|
| Sent | 8 | 8 | 0 | 0 |
| Viewed | 6 | 6 | 6 | 0 |
| Draft | 2 | 0 | 0 | 0 |
| Chosen | 2 | 2 | 2 | 2 |

| options | offers | per_offer | priced |
|---|---|---|---|
| 24 | 18 | 1.33 | 22 |

## Marketplace bookings (all, not only concierge)

_(none)_

## Supply

| country | active | contactable | claimed | opted_out |
|---|---|---|---|---|
| LT | 599 | 470 | 4 | 2 |
| LV | 321 | 231 | 3 | 15 |
| EE | 243 | 236 | 8 | 0 |
