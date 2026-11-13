# Service verification: do listed providers really offer the service? (2026-11-13)

**Read only.** Nothing was changed in the database. Removals are proposals for the founder to approve,
applied by hand through the admin API.

## Method

`scan.py services --only-active` covers all 1,163 active suppliers. 753 of them have a website.
- For each one it reads the homepage plus up to 5 same-site pages whose link mentions a service, a
  price or the company. robots.txt is honoured.
- On those pages it looks for the service's own words in ET, RU, LV, LT and EN.
- Each match is kept as an **exact quote with its page URL**, in the `quote_<service>` and
  `url_<service>` columns of `out/service-verification.csv`.

Evidence has two tiers:
- **Strong**: the phrase names the household service itself, e.g. "laopinna rent", "kolimisteenus",
  "haagise rent", "perkraustymo paslaugos".
  - Storage words count only with a rental word nearby. Without that rule, a wardrobe shop's
    "hoiuruumi lahendus" would count as self-storage.
- **Weak**: the word also fits other contexts, e.g. "autorent", "sandėliavimas" (3PL warehousing),
  "kolimiseks" on a car-rental page. Weak evidence never counts as confirmed.

Freight and logistics wording is counted separately: kaubavedu, ekspedeerimine, krovinių pervežimas,
kravu pārvadājumi, FTL/LTL, customs brokerage and similar.

Facebook and Instagram pages are listed when a provider's site links them. They are not crawled
(login wall, and their terms forbid scraping).

## Result: `fit` column

| fit | rows | meaning | proposal |
|---|---:|---|---|
| target | 544 | at least one listed consumer service is confirmed with a strong quote, and the household signal outweighs freight | keep |
| review | 102 | only weak evidence, or freight-heavy with some household words | read the quotes, then decide |
| b2b_logistics | 38 | no consumer service confirmed, freight/3PL wording, no household words | remove from the directory, or hide |
| not_consumer | 23 | listed only as packing/insurance (packaging makers, insurers) | already never sold; leave as data, or remove |
| unverified | 456 | no website (410), site down or blocked (40), JS-only shell (6) | manual check or Places |

By country:

| | target | review | b2b_logistics | not_consumer | unverified |
|---|---:|---:|---:|---:|---:|
| EE | 150 | 32 | 1 | 21 | 39 |
| LV | 120 | 32 | 15 | 2 | 152 |
| LT | 274 | 38 | 22 | 0 | 265 |

**Main finding.** The transport and logistics companies are almost all in the LT and LV *warehouse*
imports. Examples:
- Transekspedicija, AD REM Transport, Rhenus Logistics, Lavisos terminals, Talga
- LATLOG, Presco Freight, REWICO, 3PL Services

They rent commercial warehouse space to shippers, not storage to households.

Estonia is clean apart from:
- Albion Motors: car dealer, freight-only wording.
- Home and Storage (homestorage.ee): sells wardrobe and shelving systems, not storage rental.
  It is not in the active list, but it was a discovery candidate.

`review` rows in Estonia are mostly real providers whose sites use words the strong patterns do not
cover: Tartu Minilaod, Boxibaas, Kookon, Bolt Drive, CityBee, Ramirent. Read the quotes before
removing any of them.

## The 80 re-engagement mails already sent (2026-11-13)

Checked against this scan after sending:
- 61 went to `target` providers.
- 7 went to `review` providers (real businesses, weak wording).
- 12 went to `unverified` providers (no website).
- **0 went to `b2b_logistics` providers.**

## The 45 mails still on hold

| fit | letters |
|---|---:|
| target | 22 |
| unverified | 12 |
| review | 5 |
| b2b_logistics | 6 |

Recommendation:
- Send only to `target` letters.
- Drop the 6 `b2b_logistics` letters: BALTROUTE, ENTREGA, EU Global Service, Kravas taksometrs,
  L S H Express, LATLOG.

## Re-run

```bash
python -X utf8 tools/supplier_scan/scan.py services --only-active          # crawls, then caches in out/service-cache.json
python -X utf8 tools/supplier_scan/scan.py services --source discovery     # the discovery candidates
```
