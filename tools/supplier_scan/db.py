"""Read-only production dump of the supplier directory.

Runs inside `railway run --service Postgres -- python tools/supplier_scan/scan.py dump`,
which injects DATABASE_PUBLIC_URL. The session is forced READ ONLY at the
Postgres level (default_transaction_read_only), so a typo here cannot write.
"""
from __future__ import annotations

import json
import os
from pathlib import Path

SUPPLIERS_SQL = r'''
SELECT s."Id"::text                     AS id,
       s."Name"                         AS name,
       s."Slug"                         AS slug,
       s."RegistryCode"                 AS registry_code,
       s."ContactEmail"                 AS email,
       s."ContactPhone"                 AS phone,
       s."WebsiteUrl"                   AS website,
       s."Country"                      AS country,
       s."IsActive"                     AS is_active,
       s."IsDirectoryListing"           AS is_directory,
       s."IsPartnerPagePublished"       AS is_published,
       s."ServiceTypesJson"             AS service_types_json,
       s."GooglePlaceId"                AS google_place_id,
       s."MarketingOptOutAt"            AS opt_out_at,
       s."ContactEmailUnusable"         AS email_unusable,
       s."ContactEmailBouncedAt"        AS bounced_at,
       s."ContactEmailBounceType"       AS bounce_type,
       s."ContactEmailBounceReason"     AS bounce_reason,
       s."ClaimedAt"                    AS claimed_at,
       s."IntroEmailSentAt"             AS intro_sent_at,
       s."CreatedAt"                    AS created_at
FROM "Suppliers" s
ORDER BY s."Country", s."Name"
'''

# One row per supplier: its locations (city + coordinates), all of them, because
# a chain row can sit in several towns.
LOCATIONS_SQL = r'''
SELECT l."SupplierId"::text AS supplier_id, l."City" AS city, l."Address" AS address,
       l."Lat" AS lat, l."Lng" AS lng, l."IsActive" AS is_active, l."IsSynthetic" AS synthetic
FROM "SupplierLocations" l
'''

OUTREACH_SQL = r'''
SELECT o."SupplierId"::text                         AS supplier_id,
       COUNT(*)                                     AS sent,
       COUNT(o."DeliveredAt")                       AS delivered,
       COUNT(o."OpenedAt")                          AS opened,
       COUNT(o."QuotedAt")                          AS quoted,
       COUNT(o."DeclinedAt")                        AS declined,
       SUM(CASE WHEN o."Status" = 'Bounced'    THEN 1 ELSE 0 END) AS bounced,
       SUM(CASE WHEN o."Status" = 'Complained' THEN 1 ELSE 0 END) AS complained,
       SUM(CASE WHEN o."Status" = 'Replied'    THEN 1 ELSE 0 END) AS replied,
       MIN(o."SentAt")                              AS first_sent,
       MAX(o."SentAt")                              AS last_sent,
       MAX(GREATEST(o."QuotedAt", o."DeclinedAt"))  AS last_response,
       MAX(o."OpenedAt")                            AS last_opened,
       string_agg(DISTINCT o."SentTo", ' ')         AS sent_to
FROM "ProviderOutreaches" o
GROUP BY o."SupplierId"
'''


def _connect():
    import psycopg2  # local import: only the dump stage needs a driver

    url = os.environ.get("DATABASE_PUBLIC_URL") or os.environ.get("DATABASE_URL")
    if not url:
        raise SystemExit(
            "No DATABASE_PUBLIC_URL. Run via:\n"
            "  railway run --service Postgres -- python tools/supplier_scan/scan.py dump")
    conn = psycopg2.connect(url, options="-c default_transaction_read_only=on")
    conn.set_session(readonly=True, autocommit=False)
    return conn


def _rows(cur, sql):
    cur.execute(sql)
    cols = [c[0] for c in cur.description]
    return [dict(zip(cols, r)) for r in cur.fetchall()]


def _jsonable(v):
    if hasattr(v, "isoformat"):
        return v.isoformat()
    if isinstance(v, (int, float, str, bool)) or v is None:
        return v
    return str(v)


def dump(out: Path) -> Path:
    conn = _connect()
    try:
        with conn.cursor() as cur:
            suppliers = _rows(cur, SUPPLIERS_SQL)
            locations = _rows(cur, LOCATIONS_SQL)
            outreach = _rows(cur, OUTREACH_SQL)
    finally:
        conn.rollback()
        conn.close()

    locs: dict[str, list] = {}
    for l in locations:
        locs.setdefault(l.pop("supplier_id"), []).append({k: _jsonable(v) for k, v in l.items()})
    outs = {o.pop("supplier_id"): {k: _jsonable(v) for k, v in o.items()} for o in outreach}

    data = []
    for s in suppliers:
        row = {k: _jsonable(v) for k, v in s.items()}
        try:
            row["services"] = json.loads(row.pop("service_types_json") or "[]")
        except ValueError:
            row["services"] = []
        row["locations"] = locs.get(row["id"], [])
        row["outreach"] = outs.get(row["id"])
        data.append(row)

    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(data, ensure_ascii=False, indent=1), encoding="utf-8")
    return out
