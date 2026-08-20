# 2026-08-20 — Tukums → Riga: two quotes need a unit confirmed before the offer goes

Lead `de37e39d-c6ac-4e98-9b3a-334ecb428bb1`, draft offer
`06a4b9ee-f8d4-4c6c-bdad-f329fc7e21bf`. Customer `agensa@gmail.com` (lv), need
date **22 Aug**. Sofa and a table, ground floor at both ends, ~65 km.

**32 providers contacted, 4 quoted.** First genuinely competitive quote set the
concierge has produced — and it is in Latvia, which the roadmap treats as
directory-only.

## Why the offer was held

| Provider | Quoted | Likely real cost for this job (3–4 h) |
|---|---|---|
| Komanda24, SIA | 150 € one-time | 150 € |
| SIA JK Movers | 210 € one-time | 210 € |
| AA, SIA (CVN) | 60 €/hour | ~180–240 € |
| SIA RIX FREIGHT | 160 €/hour | **~480–640 €** |

Displayed as bare numbers the ranking reads 60 < 150 < 160 < 210. The real
ranking is roughly Komanda24 < AA ≈ JK ≪ RIX — close to reversed. The offer page
resolves and labels units honestly (`offerPricing.ts`), but a correct label does
not make an hourly rate comparable to a flat total for a customer.

**160 €/hour is almost certainly a unit mistake** — implausible for a mover, and
160 flat would sit exactly between the other two flat quotes.

Two replies fix it. Both are one line; the need date is the 22nd, so there is room.

---

## Email 1 — SIA RIX FREIGHT (`info@rixfreight-group.com`)

**Subject:** `Tukums → Rīga 22.08 — precizējums par cenu [DE37E39D]`

> Labdien!
>
> Paldies par piedāvājumu Tukums → Rīga pārvākšanās pieprasījumam (22.08).
>
> Lūdzu, precizējiet: **160 € ir par stundu vai par visu darbu?** Gribam
> pārliecināties, ka klientam rādām pareizo cenu.
>
> Ja tā ir stundas likme, pastāstiet, cik stundas paredzat šim darbam
> (dīvāns un galds, abās vietās pirmais stāvs, ~65 km).
>
> Ar cieņu,
> Ruumly
> info@ruumly.eu

## Email 2 — AA, SIA / CVN pārvākšanās serviss (`info@cvn.lv`)

**Subject:** `Tukums → Rīga 22.08 — cik stundas paredzat? [DE37E39D]`

> Labdien!
>
> Paldies par piedāvājumu (60 €/stundā) Tukums → Rīga pārvākšanās
> pieprasījumam 22.08.
>
> Lai klients varētu salīdzināt piedāvājumus godīgi, lūdzu, norādiet
> **aptuveno stundu skaitu vai kopējo summu** šim darbam: dīvāns un galds,
> abās vietās pirmais stāvs, ~65 km.
>
> Ar cieņu,
> Ruumly
> info@ruumly.eu

---

## Sent 2026-08-20 ~16:15 EEST

Both clarification emails were sent from info@ruumly.eu:

| To | Gmail id | Asking |
|---|---|---|
| `info@rixfreight-group.com` | `1a01f6d5e5726427` | is 160 € hourly or the whole job? |
| `info@cvn.lv` | `1a01f6d7ca3bd23e` | how many hours / total for this job? |

Both carry the `[DE37E39D]` lead reference in the subject, so replies thread with
the original outreach. **The offer stays in Draft until both answer.**

## 17:40 EEST — CVN replied, and it exposes a real intake gap

Artis Galdiņš (CVN) came back within the hour:

> *"Labvakar! Lai iedotu precīzu summu, vajadzētu zināt precīzas adreses."*

He is right, and he cannot be answered — **this lead carries no addresses at
all.** `FromAddress` and `ToAddress` are both empty, and there is no phone
either; the only channel to the customer is email.

That is not a one-off. The intake asks for a city (required) and street
addresses (optional), and this is the optional field costing a real quote on a
real job. It matches finding **C7** in `RUUMLY_FULL_AUDIT.md` — the from/to
address block is offered but never required, including for moving, where a
mover's price is a function of the distance and the parking at both ends.

**Recommendation for the funnel:** for `moving` (and `vanrental` with a driver),
either require both addresses or ask for them on the confirmation screen while
the customer is still engaged. Asking four hours later by email costs a day.

Actions taken:

| To | Gmail id | What |
|---|---|---|
| `agensa@gmail.com` (customer) | `1a01fa78ea36301f` | asked for the exact pickup and drop-off addresses, in Latvian; stated the address goes only to the provider they choose |
| `info@cvn.lv` (reply in thread) | `1a01fa7c8dc494da` | acknowledged, told them we are getting the addresses, nothing needed from them meanwhile |

RIX Freight has **not** replied yet — the 160 €/hour question is still open.

## After the replies

Update the two options in the admin offer editor, then send. Once the provider
outcome notifications ship, sending will also tell all four providers their price
reached the customer — and the winner/losers will be told automatically.

## Worth noting separately

The Latvian fan-out worked: **32 contacted, 4 real quotes (12.5%)** against
Viljandi's 0/18. The response-rate problem may be Estonian supply rather than the
outreach letter. Worth a look before drawing conclusions from the Viljandi run.
