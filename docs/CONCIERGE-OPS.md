# Concierge ops playbook — the manual demand loop

> "Пока нет устойчивого потока заявок, никакая автоматизация не имеет значения." — S. Anikin
>
> The product is now a demand-first concierge. The software (admin match queue) supports
> a loop that is **run by a human**. This document is that loop.

## The promise
- **Customer:** tell us what you need (moving / storage / trailer) — we send you 2–3
  relevant local offers, usually within 24h. Free, no obligation.
- **Supplier:** we don't sell placement. We bring people who are looking *right now*.

## Daily cadence (~30 min, morning + evening check)
1. Open **Admin → Leads** (`/admin?tab=leads`). New requests arrive with status **New**
   (also emailed to info@ / admin@).
2. For each New lead (target: first touch **same day**):
   - Open the row → read the need (categories, city, route, date, details).
   - Click **Find partners** → suggested active suppliers (category + same-city first).
   - Call/email 2–3 suppliers. Script: *"Tere! Ruumlyst. Meil on klient, kes otsib
     [ladu/kolimist/haagist] [linnas] [kuupäeval]. Kas saate pakkumise teha? Saadan
     kontakti/detailid."*
   - Set status → **Contacted** (this stamps the response-time clock).
3. When a supplier quotes: forward the 2–3 options to the customer (email/phone),
   set status → **Quoted**, note the prices in the notes field.
4. Outcome: **Booked** (customer confirmed), **Lost** (chose elsewhere / went quiet after
   2 follow-ups), or **Unmatched** (no supplier available — this is *demand signal*:
   note what was missing).
5. Follow-ups: no customer reply in 48h → one nudge. No supplier reply in 24h → next
   supplier on the list.

## Weekly review (metrics row on the Leads tab)
| Metric | Meaning | Early target |
|---|---|---|
| Requests / week | Is the demand channel working? | grow week-over-week |
| Contact rate 30d | Are we touching every lead? | ≥ 95% |
| Quote → booking 30d | Is the matching any good? | ≥ 25% |
| Median first response | Speed = trust | < 4h (work hours) |

**Do NOT steer by:** partner signups, listing counts, feature count, raw traffic.

## Where demand comes from (the actual hard problem)
The funnel captures demand; it does not create it. Weekly demand work:
- SEO city/vertical pages (already live) + blog answers to "kolimine Tallinnas" queries.
- Small paid tests: Google Search ads on high-intent terms ("laopind Tallinn",
  "kolimisteenus Tallinn"), FB/IG partner + customer ads (see ruumly-ad/ assets).
- Local channels: Facebook groups (kolimine/kirbukas), housing communities, realtors.
- Every Unmatched lead = a supplier-recruitment call with proof in hand:
  *"Meil oli eile klient, keda me ei saanud teenindada — kas soovite selliseid?"*

## Supplier recruitment (only with demand as the argument)
Pitch order: (1) here is a real customer/lead volume, (2) enquiries are free right now,
(3) later: optional boosts/tools. Never lead with the platform.

## Scale-up triggers (don't automate before these)
- >10 requests/week sustained → templated supplier outreach (email templates in admin).
- >25 requests/week → automatic lead→supplier routing for repeat partners.
- Repeated adjacent asks in Details (packing, cleaning, boxes) → consider adding as
  request categories — the event ("I'm moving") defines the scope, not the taxonomy.

## Mechanics reference
- Public funnel: `/{lang}/request` → `POST /api/leads/request` (rate-limited 5/10min/IP).
- Statuses: New → Contacted → Quoted → Converted(=Booked) | Dismissed(=Lost) | Unmatched.
  First move off New stamps `ContactedAt` (drives the median-response metric).
- Hero flip: PlatformSettings `conciergeFirst` ("true"/"false") — admin → Settings.
  Old marketplace hero returns instantly when "false". `conciergeCities` = operating-area
  hint shown in the funnel.
- Metrics API: `GET /api/admin/leads/metrics`; matches: `GET /api/admin/leads/{id}/matches`.
