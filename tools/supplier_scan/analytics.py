"""Read-only demand + service analytics, aggregated in SQL.

    railway run --service Postgres -- python -X utf8 tools/supplier_scan/scan.py analytics

Writes out/analytics.md (aggregates only: no customer e-mails, names or phones
leave the database). Test leads are those whose e-mail is on ruumly.eu, the
founder's own address, or contains "test"/"example"; they are counted once and
excluded from every other table.
"""
from __future__ import annotations

from datetime import datetime, timezone

from db import _connect, _rows

TEST = r'''(lower(d."Email") LIKE '%@ruumly.eu' OR lower(d."Email") LIKE '%test%'
            OR lower(d."Email") LIKE '%example.%' OR lower(d."Email") LIKE 'dim.novare%'
            OR lower(coalesce(d."Name",'')) LIKE '%test%')'''
REAL = f"NOT {TEST}"

Q = {
    "totals": f'''
SELECT COUNT(*) FILTER (WHERE {REAL}) AS real, COUNT(*) FILTER (WHERE {TEST}) AS test,
       MIN(d."CreatedAt") FILTER (WHERE {REAL}) AS first, MAX(d."CreatedAt") FILTER (WHERE {REAL}) AS last
FROM "DemandLeads" d''',
    "by_source": f'''
SELECT coalesce(d."Source",'(none)') AS source, COUNT(*) n FROM "DemandLeads" d WHERE {REAL}
GROUP BY 1 ORDER BY 2 DESC''',
    "by_category": f'''
SELECT d."Category" AS category, COUNT(*) n,
       COUNT(*) FILTER (WHERE d."ContactedAt" IS NOT NULL OR d."Status" <> 'New') AS handled,
       COUNT(*) FILTER (WHERE d."Status" IN ('Quoted','Converted')) AS quoted,
       COUNT(*) FILTER (WHERE d."Status" = 'Converted') AS booked,
       COUNT(*) FILTER (WHERE d."Status" = 'Unmatched') AS unmatched
FROM "DemandLeads" d WHERE {REAL} GROUP BY 1 ORDER BY 2 DESC''',
    "by_status": f'''
SELECT d."Status" AS status, COUNT(*) n FROM "DemandLeads" d WHERE {REAL} GROUP BY 1 ORDER BY 2 DESC''',
    "by_city": f'''
SELECT initcap(trim(coalesce(nullif(d."City",''),'(none)'))) AS city, COUNT(*) n
FROM "DemandLeads" d WHERE {REAL} GROUP BY 1 ORDER BY 2 DESC LIMIT 15''',
    "by_month": f'''
SELECT to_char(date_trunc('month', d."CreatedAt"),'YYYY-MM') AS month, COUNT(*) n,
       COUNT(*) FILTER (WHERE d."Source"='concierge') AS concierge,
       COUNT(*) FILTER (WHERE d."Status" IN ('Quoted','Converted')) AS quoted,
       COUNT(*) FILTER (WHERE d."Status"='Converted') AS booked
FROM "DemandLeads" d WHERE {REAL} GROUP BY 1 ORDER BY 1''',
    "by_week": f'''
SELECT to_char(date_trunc('week', d."CreatedAt"),'YYYY-MM-DD') AS week, COUNT(*) n
FROM "DemandLeads" d WHERE {REAL} AND d."CreatedAt" > now() - interval '12 weeks'
GROUP BY 1 ORDER BY 1''',
    "response": f'''
SELECT COUNT(*) FILTER (WHERE d."ContactedAt" IS NOT NULL) AS contacted,
       percentile_cont(0.5) WITHIN GROUP (ORDER BY extract(epoch FROM d."ContactedAt"-d."CreatedAt")/3600)
         FILTER (WHERE d."ContactedAt" >= d."CreatedAt") AS median_h,
       percentile_cont(0.9) WITHIN GROUP (ORDER BY extract(epoch FROM d."ContactedAt"-d."CreatedAt")/3600)
         FILTER (WHERE d."ContactedAt" >= d."CreatedAt") AS p90_h,
       COUNT(*) FILTER (WHERE d."ContactedAt" - d."CreatedAt" <= interval '24 hours') AS within_24h,
       COUNT(*) FILTER (WHERE d."Status"='New' AND d."CreatedAt" < now() - interval '2 days') AS stale_new
FROM "DemandLeads" d WHERE {REAL}''',
    "human": f'''
SELECT COUNT(*) FILTER (WHERE EXISTS(SELECT 1 FROM "ProviderOutreaches" o WHERE o."DemandLeadId"=d."Id"
                         AND o."SentAt" <= d."ContactedAt" + interval '1 minute')) AS contacted_by_fanout,
       COUNT(*) FILTER (WHERE EXISTS(SELECT 1 FROM "LeadMessages" m WHERE m."DemandLeadId"=d."Id")) AS with_operator_msg,
       percentile_cont(0.5) WITHIN GROUP (ORDER BY extract(epoch FROM
         (SELECT min(m."SentAt") FROM "LeadMessages" m WHERE m."DemandLeadId"=d."Id") - d."CreatedAt")/3600) AS median_msg_h,
       COUNT(*) FILTER (WHERE d."Status" IN ('Dismissed','Unmatched') AND coalesce(trim(d."AdminNotes"),'')='') AS closed_no_reason,
       COUNT(*) FILTER (WHERE d."Status" IN ('Dismissed','Unmatched')) AS closed
FROM "DemandLeads" d WHERE {REAL}''',
    "attribution": f'''
SELECT coalesce(nullif(substring(d."Attribution" from '"utm_source"\\s*:\\s*"([^"]+)"'),''),
                CASE WHEN d."Attribution" IS NULL THEN '(not captured)' ELSE '(direct/none)' END) AS utm_source,
       COUNT(*) n
FROM "DemandLeads" d WHERE {REAL} GROUP BY 1 ORDER BY 2 DESC LIMIT 10''',
    "language": f'''
SELECT coalesce(d."Language",'?') AS lang, COUNT(*) n FROM "DemandLeads" d WHERE {REAL} GROUP BY 1 ORDER BY 2 DESC''',
    "outreach": f'''
SELECT COUNT(*) sent, COUNT(DISTINCT o."DemandLeadId") leads, COUNT(DISTINCT o."SupplierId") suppliers,
       COUNT(o."DeliveredAt") delivered, COUNT(o."QuotedAt") quoted, COUNT(o."DeclinedAt") declined,
       SUM(CASE WHEN o."Status"='Bounced' THEN 1 ELSE 0 END) bounced,
       SUM(CASE WHEN o."Status"='Replied' THEN 1 ELSE 0 END) replied,
       percentile_cont(0.5) WITHIN GROUP (ORDER BY extract(epoch FROM o."QuotedAt"-o."SentAt")/3600)
         FILTER (WHERE o."QuotedAt" IS NOT NULL) AS median_quote_h
FROM "ProviderOutreaches" o JOIN "DemandLeads" d ON d."Id"=o."DemandLeadId" WHERE {REAL}''',
    "outreach_by_cat": f'''
SELECT d."Category" category, COUNT(*) sent, COUNT(DISTINCT o."DemandLeadId") leads,
       COUNT(o."QuotedAt") quoted, COUNT(o."DeclinedAt") declined,
       COUNT(DISTINCT o."DemandLeadId") FILTER (WHERE o."QuotedAt" IS NOT NULL) AS leads_with_quote
FROM "ProviderOutreaches" o JOIN "DemandLeads" d ON d."Id"=o."DemandLeadId" WHERE {REAL}
GROUP BY 1 ORDER BY 2 DESC''',
    "offers": f'''
SELECT f."Status" status, COUNT(*) n, COUNT(f."SentAt") sent, COUNT(f."ViewedAt") viewed, COUNT(f."ChosenAt") chosen
FROM "Offers" f JOIN "DemandLeads" d ON d."Id"=f."DemandLeadId" WHERE {REAL} GROUP BY 1 ORDER BY 2 DESC''',
    "offer_options": f'''
SELECT COUNT(*) options, COUNT(DISTINCT x."OfferId") offers,
       round(COUNT(*)::numeric / nullif(COUNT(DISTINCT x."OfferId"),0), 2) per_offer,
       COUNT(x."PriceAmount") priced
FROM "OfferOptions" x JOIN "Offers" f ON f."Id"=x."OfferId" JOIN "DemandLeads" d ON d."Id"=f."DemandLeadId"
WHERE {REAL}''',
    "bookings": '''
SELECT b."Status"::text status, COUNT(*) n, round(SUM(b."Total"),2) total
FROM "Bookings" b WHERE NOT b."IsDeleted" GROUP BY 1 ORDER BY 2 DESC''',
    "supply": '''
SELECT s."Country" country, COUNT(*) FILTER (WHERE s."IsActive") active,
       COUNT(*) FILTER (WHERE s."IsActive" AND coalesce(trim(s."ContactEmail"),'') <> '' AND NOT s."ContactEmailUnusable"
                        AND s."MarketingOptOutAt" IS NULL) contactable,
       COUNT(*) FILTER (WHERE s."ClaimedAt" IS NOT NULL) claimed,
       COUNT(*) FILTER (WHERE s."MarketingOptOutAt" IS NOT NULL) opted_out
FROM "Suppliers" s GROUP BY 1 ORDER BY 2 DESC''',
}


def _fmt(v):
    if v is None:
        return "–"
    if isinstance(v, float):
        return f"{v:.1f}"
    if hasattr(v, "isoformat"):
        return v.strftime("%Y-%m-%d")
    return str(v)


def _table(rows):
    if not rows:
        return "_(none)_\n"
    cols = list(rows[0])
    out = ["| " + " | ".join(cols) + " |", "|" + "---|" * len(cols)]
    out += ["| " + " | ".join(_fmt(r[c]) for c in cols) + " |" for r in rows]
    return "\n".join(out) + "\n"


def _pct(a, b):
    return f"{100 * a / b:.0f}%" if b else "–"


def run(a):
    conn = _connect()
    res = {}
    try:
        with conn.cursor() as cur:
            for k, sql in Q.items():
                try:
                    res[k] = _rows(cur, sql)
                except Exception as e:  # one bad table must not hide the rest
                    conn.rollback()
                    res[k] = [{"error": str(e).splitlines()[0]}]
    finally:
        conn.rollback()
        conn.close()

    t = res["totals"][0]
    r = res["response"][0]
    o = res["outreach"][0]
    h = res["human"][0]
    st = {x["status"]: x["n"] for x in res["by_status"]}
    real = t["real"] or 0
    quoted = st.get("Quoted", 0) + st.get("Converted", 0)
    md = [f"# Demand and service analytics ({datetime.now(timezone.utc):%Y-%m-%d})\n",
          f"Real leads: **{real}** ({_fmt(t['first'])} → {_fmt(t['last'])}); "
          f"test leads excluded: {t['test']}.\n",
          "## Headline funnel\n",
          "| step | leads | of all |", "|---|---:|---:|",
          f"| received | {real} | 100% |",
          f"| contacted by ops | {r['contacted']} | {_pct(r['contacted'], real)} |",
          f"| quoted (Quoted+Converted) | {quoted} | {_pct(quoted, real)} |",
          f"| booked (Converted) | {st.get('Converted', 0)} | {_pct(st.get('Converted', 0), real)} |",
          f"| unmatched | {st.get('Unmatched', 0)} | {_pct(st.get('Unmatched', 0), real)} |",
          f"| dismissed | {st.get('Dismissed', 0)} | {_pct(st.get('Dismissed', 0), real)} |",
          "",
          f"First response: median **{_fmt(r['median_h'])} h**, p90 {_fmt(r['p90_h'])} h; "
          f"{r['within_24h']} of {r['contacted']} contacted within 24 h. "
          f"Still `New` after 2+ days: **{r['stale_new']}**.\n",
          f"Caveat: {h['contacted_by_fanout']} of those {r['contacted']} `ContactedAt` stamps are the "
          f"automatic provider fan-out, not a human. Leads with an operator message to the customer: "
          f"**{h['with_operator_msg']}**, median {_fmt(h['median_msg_h'])} h to the first one. "
          f"Closed leads (Dismissed/Unmatched) with no reason in AdminNotes: "
          f"**{h['closed_no_reason']} of {h['closed']}**.\n",
          "## Provider side\n",
          f"{o['sent']} outreach mails for {o['leads']} leads to {o['suppliers']} suppliers: "
          f"{o['quoted']} quotes ({_pct(o['quoted'], o['sent'])}), {o['declined']} declines, "
          f"{o['bounced']} bounces, delivered-receipts {o['delivered']}. "
          f"Median time to quote {_fmt(o['median_quote_h'])} h.\n",
          _table(res["outreach_by_cat"]),
          "## By service\n", _table(res["by_category"]),
          "## By source\n", _table(res["by_source"]),
          "## By status\n", _table(res["by_status"]),
          "## By month\n", _table(res["by_month"]),
          "## Last 12 weeks\n", _table(res["by_week"]),
          "## Top cities\n", _table(res["by_city"]),
          "## Acquisition (utm_source)\n", _table(res["attribution"]),
          "## Language\n", _table(res["language"]),
          "## Offers\n", _table(res["offers"]), _table(res["offer_options"]),
          "## Marketplace bookings (all, not only concierge)\n", _table(res["bookings"]),
          "## Supply\n", _table(res["supply"])]
    path = a.out / "analytics.md"
    path.write_text("\n".join(md), encoding="utf-8")
    print(f"wrote {path}")
