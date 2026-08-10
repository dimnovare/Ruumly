# Provider email deliverability audit — 2026-08-09

Read-only DNS sweep of every email address stored on the live provider directory.
No supplier record was modified, no mail was sent, no setting was changed.

Machine-readable output: `email-mx-audit.json`

## Headline numbers

| Metric | Value |
|---|---|
| Providers in directory | 1,186 |
| Providers with a stored `contactEmail` | 801 (67.5%) |
| Distinct email domains | 482 |
| Domains with a working MX record (`ok`) | 477 (99.0%) |
| Domains that resolve but have no MX (`a_record_only`) | 3 |
| Domains that resolve with neither MX nor A (`no_mx`) | 0 |
| Domains that do not exist (`nxdomain`) | 2 |
| **Providers whose stored address is undeliverable** | **5 (0.6%)** |

The directory is far healthier than the three hand-caught cases suggested. 796 of 801
addresses sit on a domain that can accept mail.

## The important negative: no chain is broken

This was the main risk being tested — one dead domain silently taking out dozens of
branches at once. It did not happen. Every high-count domain is healthy:

| Domain | Providers | Status | Primary MX |
|---|---|---|---|
| gmail.com | 86 | ok | gmail-smtp-in.l.google.com |
| viada.lt | 35 | ok | viada-lt.mail.protection.outlook.com |
| viadabaltija.lv | 27 | ok | mail.viadabaltija.lv |
| balticpetroleum.lt | 25 | ok | mail.balticpetroleum.lt |
| kabi.lv | 15 | ok | aspmx.l.google.com |
| boxrent.lv | 13 | ok | mx1.hostinger.lv |
| noliktava1.lv | 13 | ok | mail.noliktava1.lv |
| inbox.lv | 10 | ok | mx1.inbox.lv |
| saurida.lt | 10 | ok | sekvoja.serveriai.lt |
| boxrent.lt | 9 | ok | mx1.hostinger.com |
| ramirent.ee | 8 | ok | ramirent-ee.mail.protection.outlook.com |
| virsi.lv | 7 | ok | virsi-lv.mail.protection.outlook.com |
| corpusa.lt | 6 | ok | corpusa-lt.mail.protection.outlook.com |
| boxstorage.lv | 5 | ok | mail.boxstorage.lv |
| safebox.lv | 5 | ok | mail.safebox.lv |

Every failure found is a single-provider domain. Blast radius is 1 in all five cases.

## Shared / free mailbox providers: all healthy

gmail.com (86), inbox.lv (10), mail.ee (4), miil.ee (4), hot.ee (3), inbox.lt (3),
latnet.lv (3), yahoo.com (2), apollo.lv (1), ava.ee (1), hotmail.com (1), is.ee (1),
one.lt (1) — all resolve with valid MX. 118 providers (14.7% of those with an address)
rely on a free mailbox; that is a business-continuity note, not a deliverability one.

## The 5 providers at risk

Ordered worst-first. All five are currently active and all five would silently swallow
a customer request today.

### 1. Kapsel Minilaod — `kapsel-minilaod` — Reola, EE — `info@kapsel24.ee` — nxdomain
**Highest priority.** Warehouse provider and the *only* provider in Reola — if a
request comes from there, no one else catches it. `kapsel24.ee` does not exist on any
resolver (no NS, no SOA, no A, no MX). The stored `websiteUrl` also points at
`https://kapsel24.ee`, so the company's whole web presence is gone — this looks like a
business that has closed or rebranded, not a typo. Phone on file: +372 5103812.

### 2. Esvo Transport OÜ — `esvo-transport-turi` — Türi, EE — `esvo@esvo.ee` — nxdomain
Moving provider, one of 5 in Türi. `esvo.ee` does not exist on any resolver. Registry
code 10169177 is on file, so the company is identifiable and may simply have dropped the
domain. Phone on file: +372 58046048.

### 3. T49 Kolimisteenus — `t49` — Tallinn, EE — `info@t49.ee` — a_record_only
Moving provider. **No phone number on file** — with the email dead, this provider is
unreachable through any channel Ruumly holds. `t49.ee` has only Cloudflare proxy A
records (104.21.3.72 / 172.67.130.116) and no MX; TCP :25 refuses connections.

### 4. Noortegija — `noortegija` — Tallinn, EE — `info@noortegija.ee` — a_record_only
Moving provider. Same pattern: Cloudflare proxy A records (104.21.6.73 /
172.67.134.152), no MX, TCP :25 refused. Phone on file: +372 5689 51190.

### 5. Rekota, UAB — `rekota-siauliai` — Šiauliai, LT — `rekota@zebra.lt` — a_record_only
Cleaning provider (lower request volume than moving/warehouse). `zebra.lt` resolves to
185.11.24.15, no MX, TCP :25 refused. `zebra.lt` looks like a former Lithuanian ISP
mail domain that no longer runs mail. Phone on file: +370 61868716.

## Recommendation — what to do with each category

**`nxdomain` (2 providers) — clear the address.**
Proven dead on three independent resolvers. There is no configuration under which mail
reaches these. Null `contactEmail` so the concierge loop stops counting them as
"contacted, no reply" and routes them to the phone queue instead. Do not delete the
providers: both have a phone number, and Kapsel is the sole option in its city.

**`a_record_only` (3 providers) — treat as dead, clear the address.**
RFC 5321 implicit-MX means an A record *may* accept mail, so this class is normally
reported as "weaker, not proven broken". Here it is proven broken: all three refuse TCP
port 25, verified against a control (`gmail-smtp-in.l.google.com:25` connects fine from
the same host, so this is not an egress block on our side). Two of the three point at
Cloudflare proxy IPs, which never run SMTP. Same handling as `nxdomain`.
`t49` additionally needs a phone number sourced or the profile deactivated — it is
currently a provider Ruumly cannot reach at all.

**`ok` (477 domains / 796 providers) — cleared at the domain layer only.**
This sweep proves the domain can receive mail. It does **not** prove the mailbox exists
or that anyone reads it. `info@` addresses scraped from a website can still be
unmonitored, and a valid domain can reject an unknown local part. The remaining
uncertainty sits at the mailbox layer and cannot be resolved by DNS.

**Fix the class of bug, not just the 5 rows.**
1. Run this MX check at directory-import time and refuse to store an address whose
   domain has no MX. All five failures would have been caught at entry.
2. Capture hard bounces from the provider-notification send and write them back to the
   supplier record. Today an undeliverable address is indistinguishable from a provider
   who chose not to reply — that is the invisible failure this audit exists to remove,
   and DNS only catches the subset where the *domain* is broken.
3. Re-run this sweep periodically; domains lapse. `kapsel24.ee` was live enough to be
   scraped and is now fully gone.

## Method and caveats

- Source: `GET /api/admin/suppliers` (12 pages, 1,186 records) joined to
  `GET /api/locations` for city and service type. Production, read-only.
- Deduped 801 addresses to 482 distinct domains before resolving.
- Per domain: MX via `Resolve-DnsName -Server 1.1.1.1`, falling back to A/AAAA, then
  NS, then SOA to separate "exists but no mail" from NXDOMAIN. 60 ms between lookups.
- Every non-`ok` result was re-queried against 8.8.8.8, and the five survivors were
  re-checked a third time against 9.9.9.9. All five failed identically on all three.
- `a_record_only` domains were additionally probed on TCP :25 with a known-good control.
- Validation: the three domains from the manual spot-check reproduce exactly
  (`tautvista.lt` → nxdomain, `avus-kroviniai.lt` → nxdomain, `rmc-moving.lt` →
  a_record_only). None of the three is present in the directory today, so they were
  already removed or never stored.
- Not covered: mailbox existence, catch-all behaviour, spam placement, SPF/DKIM/DMARC
  alignment on Ruumly's sending side.
