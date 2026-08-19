# Ruumly partner audit and expansion research

Research date: 2026-07-28  
Import and verification date: 2026-07-29  
Scope: Estonia; warehouse, moving, cleaning, trailer, van rental, packing, and insurance

## Outcome

The production directory contained exactly **163** suppliers before this work. Seven qualified providers were imported on 2026-07-29, bringing the verified production total to **170**.

The expansion scan produced **27 net-new leads** after deduplication:

- **10 outreach-now candidates** with active service evidence, public contact details, and an identifiable legal entity.
- **6 conditional candidates** that are contactable but need an identity, status, or product-fit check.
- **11 quarantined candidates** that should not be imported or automatically contacted until the stated issue is resolved.
- **7 candidates** passed the import gate and were added with public email, public phone, an official-site brand image, a usable address, and coordinates.

The production import created all seven rows with zero skips or errors. A fresh database export matched every payload field, all seven public profiles returned successfully, all six category-routing checks passed, and the production candidate finder returned every expected provider with matching contact information. Moving.ee, CoralClean, and Pansib Cleaner received lighter or more legible official logo variants after visual review. Moving/trailer feature toggles were not changed.

Files:

- [Full 27-lead research register](./partner-candidates.json)
- [Executed seven-row directory import payload](./directory-import-draft.json)
- [Production import verification](./import-verification-2026-07-29.json)

## Pre-import production audit

The authoritative baseline was production PostgreSQL `Suppliers` filtered by `IsDirectoryListing = true`, joined to `SupplierLocations`. A pre-import download of `https://api.ruumly.eu/api/locations` returned 163 rows, all directory rows with 163 distinct supplier IDs.

- 163 directory suppliers and 163 distinct supplier locations.
- All 163 are active, published, country `EE`, and have exactly one active non-synthetic location.
- Each existing supplier has exactly one service type, although the schema permits multiple.
- Import audit history totals `44 + 45 + 45 + 28 + 1 = 163`.
- No exact normalized duplicate group was found by name, domain, email, phone, registry code, or slug.

### Existing category mix

| Service | Current |
|---|---:|
| Warehouse | 40 |
| Moving | 37 |
| Cleaning | 25 |
| Trailer | 23 |
| Van rental | 18 |
| Packing | 11 |
| Insurance | 9 |
| **Total** | **163** |

### Existing data gaps

| Gap | Count | Operational impact |
|---|---:|---|
| Missing registry code | 104 | Weak legal-entity dedupe and status checks |
| Missing logo | 37 | Incomplete public partner pages |
| Missing email | 18 | Cannot send normal email outreach |
| Missing phone | 9 | Cannot fall back to phone outreach |
| Missing both email and phone | 3 | Alexela haagiserent, Bolt Drive, and Hoog Mobility OÜ cannot be contacted from the current directory record |
| Missing website | 1 | Miniladu24.eu Viljandi |

Freshness is the largest systemic risk: 159 of 163 supplier records have not been updated since the July 9 import. The other four were edited later that same day, so there has not yet been a systematic post-import refresh.

Fuzzy duplicate review found similar names but no proven duplicate records. The pairs that deserve a future manual ownership check are:

- Haagise Rent OÜ / Haagisrent / Haagiserent.com
- Kaubikurent Pärnus / Kaubiku Rent Tartus
- Kolimisabi OÜ / Kolimisabi Tallinnas
- Miniladu 24/7 / Miniladu24.eu Viljandi

## Best net-new candidates

| Priority | Provider | Services | Coverage | Public contact | Logo/social evidence | Qualification note |
|---:|---|---|---|---|---|---|
| 1 | [Box Moving](https://www.boxmoving.ee/) | Warehouse, moving, packing | Tallinn/Harjumaa, nationwide | info@boxmoving.ee · +372 512 3178 | [Logo](https://www.boxmoving.ee/wp-content/uploads/elementor/thumbs/logo-new-222-q90w9fivqyq4e0jxlbmj3xtw3erna0zbcucu17us5g.png) · [Facebook](https://www.facebook.com/profile.php?id=100095018473558) · [LinkedIn](https://www.linkedin.com/company/box-moving/) | Best combined fit, but conditional until the current registry deletion-notice banner is cleared. |
| 2 | [Lubja Laod](https://viimsiarimaja.eu/lubjalaod/) | Warehouse, trailer | Viimsi/northern Harjumaa | info@viimsiarimaja.eu · +372 523 7563 | [Logo](https://viimsiarimaja.eu/wp-content/uploads/2021/04/Lubja-laod.png) | Excellent consumer self-storage fit: 72 heated, ventilated mini-storages and 24/7 access. |
| 3 | [Movers24](https://www.movers24.ee/) | Warehouse, moving, packing | Estonia and Europe | info@movers24.ee · +372 5800 2827 | [Brand image](https://www.movers24.ee/assets/og-card.jpg) | Strong 2026 activity and valid 2024/2025 reports, but conditional until the deletion-notice banner is cleared. |
| 4 | [Moving.ee](https://moving.ee/) | Warehouse, moving, packing | Estonia and international | info@moving.ee · +372 5800 0626 | [Logo](https://moving.ee/wp-content/uploads/2025/04/cropped-favI.png) | Active site and quote path; privacy policy updated April 2026. |
| 5 | [Rapla Hoonegrupp](https://hoonegrupp.ee/) | Cleaning | Rapla/Kohila | info@hoonegrupp.ee · +372 527 6527 | Official-site brand source | Good local coverage; direct standalone logo asset still needs extraction. |
| 6 | [Lux Puhastus](https://www.luxpuhastus.ee/) | Cleaning | Tallinn | info@luxpuhastus.ee · +372 5836 0116 | [Logo](https://www.luxpuhastus.ee/wp-content/uploads/2021/08/LOGO-LUX-Koristusteenused-Hoolduskoristus-Koristusfirma-koristaja-koristus-Koristusteenused-Tallinnas.svg) · [Facebook](https://www.facebook.com/luxpuhastus) | Established provider; strongest fit for commercial or large-property cleaning. |
| 7 | [CoralClean](https://coralclean.ee/et) | Cleaning | Tallinn/Harjumaa | info@coralclean.ee · +372 5843 2023 | [Logo](https://coralclean.ee/img/coralclean/logo1.png) | Strong move-in, move-out, deep, and post-renovation fit; young company. |
| 8 | [Pansib Cleaner](https://www.pansib.ee/et/) | Cleaning | Tallinn/Harjumaa | cleaner@pansib.ee · +372 5517 195 | [Brand icon](https://www.pansib.ee/apple-touch-icon.png) | Explicit move-in/move-out service and live July/August 2026 promotion; likely smaller capacity. |
| 9 | [Balcia](https://www.balcia.ee/et/kodukindlustus) | Insurance | Estonia-wide | info@balcia.ee · +372 5777 9090 | [Logo](https://www.balcia.ee/build/assets/images/logo_balcia.webp) · [LinkedIn](https://www.linkedin.com/company/balcia-insurance-se/) | High-confidence insurer; general support may need to route partnership enquiries. |
| 10 | [BTA](https://www.bta.ee/era/kodukindlustus) | Insurance | Estonia-wide | bta@bta.ee · +372 5686 8668 | [Brand image](https://www.bta.ee/images/one/social/bta-open-graph-logo.jpeg) · [LinkedIn](https://www.linkedin.com/company/btakindlustus/) | High-confidence insurer; general support may need to route partnership enquiries. |
| 11 | [Kolmestar](https://www.kolmestar.ee/) | Warehouse | Türi/Järva | kolmestar@kolmestar.ee · +372 501 5328 | Official-site logo source | Fills a geography gap, but spaces start around 130 m² and are mainly B2B. |
| 12 | [4U Logistics](https://4ul.ee/) | Warehouse | Loo and Rakvere | info@4ul.ee · +372 636 3095 | [Logo](https://framerusercontent.com/modules/XdkID0xG6ofY1u699Ut5/AwO3puX3JBmgEpX40SSm/assets/qScCNajw5MhMkx7T4EeDkJC3hs.png) · [LinkedIn](https://www.linkedin.com/company/4u-logistics-o%C3%BC) | Active customs/B2B storage, not consumer self-storage. |

The six conditional leads are Box Moving, Movers24, ABEMI Laoteenused, Tellikolimine, Kaubikute-Rent.ee, and Vikatimees. Their exact contacts, assets, evidence, and risks are in the full research register.

## What the seven-row import added

The payload is shaped for `POST /api/admin/suppliers/bulk` and contains valid Estonia coordinates, allowed service slugs, public contact details, official-site brand images/icons, and registry codes. The exact import path was exercised first inside a rolled-back production transaction, then committed only after all seven rows passed.

| Category | Additional matching suppliers |
|---|---:|
| Warehouse | 2 |
| Moving | 1 |
| Packing | 1 |
| Cleaning | 3 |
| Insurance | 2 |
| Trailer | 1 |

Because providers can carry multiple service types, those category additions total more than seven supplier rows. Importing all seven raised the directory from 163 to 170.

The payload deliberately excludes Box Moving and Movers24 pending further compliance review. It also excludes Rapla Hoonegrupp and the other specialized/conditional leads because a direct brand asset or another qualification item remains unresolved, as well as every quarantined company.

### Post-import verification

- Production database: 170 active directory suppliers; all seven new records have email, phone, website, logo, registry code, one active non-synthetic location, and published partner pages.
- Public API: 170 locations; the seven new slugs are visible as directory profiles.
- Public profiles: seven of seven `/api/suppliers/by-slug/{slug}` responses succeeded with the expected service types, website, logo, and location.
- Request routing: all expected matches passed for warehouse, moving, trailer, packing, cleaning, and insurance.
- Candidate finder: all seven were returned for their intended categories with exact stored email/phone and a location.
- Logo delivery: seven of seven final assets returned HTTP 200 with an image content type.

## Quarantine summary

Do not import these until the cited issue is cleared:

- KLC Minilaod — excellent storage fit, but a registry deletion notice is displayed.
- KolimisTiim — deletion notice, missing reports, and zero reported 2024 revenue.
- Haapsalu Miniladu — probable operator is inferred; Miil says the facility is partner-operated.
- Kobrit — deletion notice and incomplete legal disclosure on the service site.
- MLM Trans — deletion notice and very low reported 2025 turnover.
- Töömesilased24 — useful Hiiumaa coverage, but a deletion notice is displayed.
- Puhastus Pro — active service site does not disclose the controlling legal entity.
- Haagiserent123 — official site indicates temporary closure or unavailable stock.
- Kaubiku rent Tallinnas — activity/report mismatch plus deletion notice.
- Clean Partner — tax and registry/report warnings need a fresh check.
- Narva Logistics — website names a company deleted in 2019; the likely successor must confirm control.

## Dedupe catches

Several attractive search results were not new partners:

- Kolimisabi Tartus is Fastway Move OÜ (`12953515`), already in production as **Kolimisprofid**.
- Kolimisteenus24 is Transit Expert OÜ (`14007922`), already in production as **Uksest Ukseni**.
- Konteinerladu.ee is connected to **Blackline**, while **Konteinerladu OÜ** is also already present.
- Haagisemaailm.ee is part of the existing **Haagise Rent OÜ** operator/brand family.
- Plasticbox.ee belongs to the same operator/contact family as existing **Packpros**.
- Miil, Blackline, ESPAK, KLIN, and Rokk regional pages are location-expansion opportunities, not net-new suppliers.
- Compensa’s Estonian non-life business is represented by the existing **Seesam** brand.

## Recommended next actions

1. Contact Lubja Laod and Moving.ee first because storage remains the launch focus.
2. Contact the three cleaning candidates for bundled move-in/move-out requests.
3. Route Balcia and BTA to a partnership/business-development contact rather than treating the public support desk as the final owner.
4. Monitor Poolsaare Ärimaja OÜ's overdue 2025 annual report; the company is entered in the register, but the report was due 2026-06-30.
5. Keep Box Moving, Movers24, and the other conditional/quarantined candidates out until their stated qualification issue is cleared.
6. Separately backfill the remaining 37 older records without logos, 18 without emails, and 9 without phones; prioritize the three records with neither email nor phone.

## Method and evidence policy

Research used:

- A read-only production database export and the public Ruumly locations/sitemap surfaces.
- Official provider websites and their public contact/legal pages.
- Official e-Äriregister pages for legal identity and report/status checks.
- Public Facebook, Instagram, and LinkedIn pages only when an official attribution could be verified.
- OpenStreetMap/Nominatim building coordinates for the import draft.

No private profiles, gated contact databases, personal-data enrichment, or automated outreach were used. Production writes were limited to the seven validated directory inserts and three logo-URL refinements described above.
