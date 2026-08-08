# Contact repair — the 17 providers with no email

Research date: 2026-08-08. Data file: `contact-repair.json` (18 rows: the 17 no-email providers plus the
known-bad Miniladu24.eu Viljandi record).

## Headline

| Status | Count | Which |
|---|---|---|
| `found` (usable email) | **14 of 17** | Gjensidige, Alexela, Ants Viljandi, Bolt Drive, Box Storage, Hoog Mobility, Kaubikuterent.ee, Miil OÜ, Moving Expert, Puhastustäht, Salva, Tartu Kolimine, Transer, VANonsite |
| `no_email_found` | 1 | Ladu32 |
| `defunct` | 1 | NarvaSklad |
| `ambiguous` | 1 | KolimisPartner |

Eleven of the fourteen were verified on the company's own website; three (Gjensidige, Salva, Box Storage)
came only from the official business-registry contact record, because their own sites are JavaScript
apps or contact-form-only and publish no address at all. Those three are marked `medium` confidence in
the JSON so they can be treated differently if the first send bounces.

Nothing here was guessed. No `info@<domain>` was assumed. Two addresses (Transer, VANonsite) looked
missing on a plain scrape because the sites wrap their mailto links in Cloudflare email-protection —
those were decoded from the sites' own payloads, so they are still the sites' published addresses.

## The two single-provider cities

**Lagedi — fixed.** `Miil OÜ` (registry code 10435963) is registered and operating at **Valtsi tn 1,
Lagedi alevik, Rae vald**. Its contact page publishes `info@miil.ee` (+372 601 2957), corroborated by the
registry. Filling that one field makes Lagedi serviceable. Bonus: the same company runs the Smart
Warehouse container storage at Vaksali 36, Viljandi, so this record also covers part of Viljandi. If
`info@` is slow there are named sales people on the same page (Rait +372 505 9595, Heiki
heiki@miil.ee, Lauri +372 504 2025).

**Narva — not fixed, and it cannot be fixed by repairing this record.** See below.

## Should be removed from the directory, not repaired

**NarvaSklad — defunct, delete it.** Four independent pieces of evidence:

1. The operating company **Osaühing ESTIN Warehousing (10054540)** — which publishes the exact same
   phone as the site, +372 5363 3313 — was **deleted from the Estonian business register on
   04.08.2026**, four days before this research. Its VAT number expired the same day and its balance
   sheet total is **−€1,221,790**. This is a wind-up, not a rebrand.
2. The rental subdomain `arenda.narvasklad.ee` no longer resolves (NXDOMAIN).
3. What is left at `narvasklad.ee` is a single-page **asset sale**: an 11,361 m² Class A customs
   warehouse at Kadastiku tn 39b, Narva, offered at €5,540,000 + VAT, reachable only by phone/WhatsApp.
   No email is published anywhere on the site.
4. Even while trading it was a **bonded pallet-logistics warehouse** (21,500 pallet places, 7-tier
   racking, customs warehouse procedure) — never consumer self-storage. It could not have served an
   "I'm moving" request even when it was alive.

I searched in Estonian and in Russian for any consumer self-storage, storage-box or hoiuruum rental in
Narva / Ida-Virumaa and **found none**. Every real self-storage operator in the results (Box Storage,
BOXO, Puhver, Taskulaod, SpaceHub, Merekonteiner) is in Tallinn or Laagri. So Narva is not a broken
record — it is a genuine supply hole. The realistic way to open Narva is to recruit a **Narva
moving/transport company with spare warehouse space**, not to hunt for a self-storage brand that does
not exist there.

**KolimisPartner — probe once, then probably delete.** `info@kolimispartner.ee` is genuinely published
on their homepage, so it goes in the file. But the only phone on the site is `+372 567 890 121`, which is
**not a valid Estonian number** (Estonian mobiles are 7–8 digits; this is 9 and sequential — placeholder
text). There is **no company named Kolimispartner in the Estonian business register**, and the site names
no legal entity or registry code anywhere. The copy is generic SEO filler with an unverifiable "5.0
rating". This reads as a lead-generation landing page rather than a firm with trucks. Send one probe; if
the reply is not substantive, drop it.

**Ladu32 — keep, but stop expecting an email.** There is nothing to find: ladu32.ee (ET/EN), `/ladu32/`,
`/spaces/`, `/pricing/` and `/privacy-policy/` all publish only the phone **+372 502 4800** and a contact
person, Igo Sagri. The owning company **FI Arendused OÜ (12642258)** has no contact email in the registry
either. Separately, it is a poor fit: 16 commercial units of 93–320 m² at Kesk tee 32, Jüri, on six-month
minimum contracts from €5.6/m². That is commercial leasing, not consumer storage — worth deprioritising
in matching rather than chasing.

## Miniladu24.eu Viljandi — solved, and the phone is not wrong

**The number +372 5277638 is correct.** It is the officially registered phone of **RAL-EST OÜ
(reg 12940866, Iva tee 20, Viiratsi alevik, Viljandi vald)** — it appears both in the official Estonian
Business Register entry and on the company's own site, ralest.ee. So it is a live, accurate business
number from a primary source.

**What the founder actually reached.** RAL-EST's public identity today is **powder coating and chemical
paint stripping** ("Ral-Est — meie teame, kuidas värvida"). Their website markets nothing but coating
services. Anyone calling and asking about a mini-warehouse would reasonably be told they had the wrong
number — the number is right, the *label on our record* is wrong.

**Why they are nevertheless the right company.** Three links line up: RAL-EST's registered EMTAK
activities include **Real Estate Rental (68201)** alongside Coating of metals (25511); the company is in
**Viiratsi**, which matches the stored `viiratsiladu@gmail.com`; and the Facebook page for the brand is
titled "Miniladu24.eu | **Viiratski**". The storage operation was a sideline of the coating business.

**The storage brand itself is dead.**

- `miniladu24.eu` returns **NXDOMAIN** — the domain is gone.
- The soov.ee listing returns *"Kuulutust ei leitud"* (ad not found).
- Both city24.ee listings for the Koidu 13 Viljandi miniladu are marked **"Kuulutus ei ole aktiivne"**,
  last touched 11.07.2025 and 18.08.2025.

**Recommendation — do not null the phone.** Nulling would destroy verified-good data. Instead:

1. Relabel the record from "Miniladu24.eu Viljandi" to **RAL-EST OÜ**, keep +372 5277638, and set the
   email to **ralestou@gmail.com** (their registered *and* site-published address; contact person Olev
   Ustinov; company active, 6 employees, ~€426k forecast turnover).
2. Ask them once whether they still rent storage boxes in Viiratsi / at Koidu 13.
3. If the answer is no, delete the record — Viljandi still has **Blackline OÜ** (container storage,
   Vaksali 36, info@blackline.ee, +372 53 999 919), **Miil OÜ** (Smart Warehouse, Vaksali 36) and
   **Ants Viljandi** for moving.

**Do not trust the stored `viiratsiladu@gmail.com`.** It could not be corroborated anywhere and the brand
behind it no longer exists online. Replace it with ralestou@gmail.com or blank it.

One more trap worth recording: **Miniladu24 OÜ (reg 16960995) is a real, active company — but a
different one.** It is a newer business at Lepa tee 4, Loo, Harjumaa (miniladu24.ee), registered
08.04.2024, unrelated to Viljandi. Do not merge the two records because the names match.

## Stored names that point at a different business than what exists

| Stored as | Actually is | Consequence |
|---|---|---|
| `Miniladu24.eu Viljandi` | RAL-EST OÜ, a Viiratsi powder-coating firm with storage as a sideline; the miniladu brand is gone | Relabel or delete — see above |
| `NarvaSklad` | Osaühing ESTIN Warehousing, a bonded pallet warehouse now deleted from the register and being sold | Delete |
| `Kaubikuterent.ee` | A domain, not a company — operated by **OÜ Dragonet** (12208964), main brand dragonetrent.ee | Fine, but do not confuse with the separate `kaubikute-rent.ee` (Eesti Autorent OÜ, 12523134) |
| `Tartu Kolimine` | Trading name of **KUTO Tootmine OÜ** (11204875), Lohkva küla, Luunja vald | Invoices will arrive under a different name |
| `Transer` | Trading name of **Shipster OÜ** (14758686) | Same |
| `Moving Expert` | Trading name of **TJU NT OÜ** (14461489), which also runs expresskolimine.ee | Same team reachable via either brand |
| `Ladu32` | Project of **FI Arendused OÜ** (12642258) | Commercial leasing, not consumer storage |
| `VANonsite` | Brand of **1WAYEUROPE OÜ** (16572879) | **Not a van-rental company** — it is an international removals/relocation operator, and both published phones are Polish (+48). Wrong answer for a same-day Tallinn van |
| `Box Storage` | **BOX STORAGE PUNANE 46 OÜ** (16848152), Estonian arm of the Latvian Box Storage group | Only registered contact is a named person at the Latvian parent — verify an Estonian ops inbox at onboarding |
| `Hoog Mobility OÜ` | Correct name, but the business is mainly e-scooters and Hoog Delivery courier work | They do have a trailers line; confirm Harjumaa moving-day relevance before routing |
| `Puhastustäht OÜ` | Correct, but registered in **Pärnu county**, not Tallinn | Confirm service radius before routing Tallinn move-out cleaning |

## Practical notes for whoever sends the first emails

- **Alexela**: use `ariklient@alexela.ee` (business-client desk) rather than the general
  `alexela@alexela.ee` for a partnership pitch.
- **Bolt Drive**: `estonia-drive@bolt.eu` is a support queue, not a partnerships human; there is no phone.
- **Gjensidige / Salva**: both are insurers, not suppliers — low priority for lead routing, and both
  general inboxes came from the registry rather than their own (JS-only) sites.
- **Box Storage** and **Ladu32**: phone is the reliable channel; treat email as best-effort.
