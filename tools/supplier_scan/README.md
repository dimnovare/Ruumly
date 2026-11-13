# Supplier directory scan

Read-only, report-first. **Nothing here writes to the database, calls the admin
API or emails anyone.** It produces CSVs that the founder approves row by row;
approved changes are applied by hand through the admin API.

Findings and the approval tables live in `docs/ops/supplier-directory-scan-<date>.md`.
Working files (prod data, registers, site cache) go to `tools/supplier_scan/out/`,
which is gitignored.

Python 3.11+ with `requests` and `psycopg2` (dump stage only). No other deps.

## Stages

Run from the repo root.

```bash
# 1. Production snapshot (READ ONLY Postgres session; needs the Railway CLI)
railway run --service Postgres -- python tools/supplier_scan/scan.py dump

# 2. Company registers, all free open data (~500 MB download, ~5 min to index)
python tools/supplier_scan/scan.py registry --countries EE,LV,LT

# 3. Health of existing rows -> out/health-<countries>[-<zones>].csv
python -X utf8 tools/supplier_scan/scan.py health --countries EE --towns harjumaa,parnu,tartu
python -X utf8 tools/supplier_scan/scan.py health --countries EE,LV,LT

# 4. Missing providers (EE register, free) -> out/discovery-review-registry-<date>.csv
python -X utf8 tools/supplier_scan/scan.py discover

# 5. After the founder marks approve=y (and edits any column):
python -X utf8 tools/supplier_scan/scan.py emit-import
#    -> out/import/import-EE-discovery-<date>.json, then a human runs
#       ./scripts/import-directory.ps1 -Token (Get-Clipboard) -DataDir tools/supplier_scan/out/import -WhatIf
```

Paid, opt-in only (prints worst-case cost, refuses without `--i-accept-cost`):

```bash
railway run --service Ruumly -- python tools/supplier_scan/scan.py places --i-accept-cost
railway run --service Ruumly -- python tools/supplier_scan/scan.py discover --source places --i-accept-cost --max-calls 45
```

## Health actions

| action | meaning | how to apply once approved |
|---|---|---|
| `keep` | nothing to change | — |
| `fix_contact` | proposed email / phone / website / registry code + evidence URL | `PATCH /api/admin/suppliers/{id}` (a new `contactEmail` also clears the bounce flags) |
| `remove` | registry struck off/bankrupt/liquidating, or site AND mailbox both dead | soft delete: `DELETE /api/admin/suppliers/{id}` |
| `call` | conflicting or partial evidence | phone the provider |
| `skip_opt_out` | `MarketingOptOutAt` set — not probed, never proposed for change | — |
| `skip_inactive` | already `IsActive = false` | — |

## Signals and their limits

- **Registers.** EE "lihtandmed" lists only live entries (R/L/N): a code that is
  absent is struck off *or* a branch type the file omits *or* a typo, so absence
  alone never yields `remove` while the website is live. LV `closed=L` =
  liquidated. LT JAR live file carries the legal status; the struck-off file is
  separate. Rows without a code get one proposed only on a **unique** normalised
  name match against live entries.
- **Brand ≠ legal name** is normal (Kaubikud.ee is DST Auto OÜ) and is reported,
  not acted on — unless the company behind the code is dead, which means our code
  is probably wrong.
- **Website.** 401/403/429 = bot wall, not evidence. 5xx retried once. A 404 on a
  deep link with a live root proposes the root URL. Hosts resolving to private IPs
  are never fetched (stand-in for the backend's OutboundEndpointValidator).
  robots.txt is honoured for the extra contact pages. Redirects are followed by hand and every hop is re-checked (http(s) only, public IPs only); DNS rebinding between check and connect is an accepted residual risk for a local report script.
- **Email proposals** must have a deliverable domain (MX or A via DNS-over-HTTPS)
  and are not form placeholders — this drops ROT13-obfuscated and typo'd addresses.
- **Outreach.** `OpenedAt` is never set in production (0 opens on 270 sends, 21 of
  which quoted), so silence is judged on quotes/replies/declines only.
- **Discovery coordinates** are the *registered office* geocoded by Maa-amet
  In-ADS — often an accountant's address. Check before approving.
