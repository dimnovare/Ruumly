"""Part 1 — health of existing directory rows.

Joins four signals per row and proposes ONE action for a human to approve:

  keep         nothing found that needs a change
  fix_contact  a better email / phone / website / registry code exists; the
               proposed value and the URL it was read from are in the row
  remove       strong evidence the business is gone (registry struck off /
               bankrupt / liquidating, or website AND mailbox both dead).
               Apply as a SOFT delete: DELETE /api/admin/suppliers/{id}
  call         conflicting or partial evidence — a phone call settles it
  skip_opt_out MarketingOptOutAt is set. Not probed, never proposed for change.
  skip_inactive already IsActive = false

Nothing is written anywhere except files under --out.
"""
from __future__ import annotations

import csv
import json
import math
import re
from concurrent.futures import ThreadPoolExecutor, as_completed

import registry
import web

FREEMAIL = {"gmail.com", "hot.ee", "mail.ee", "online.ee", "hotmail.com", "outlook.com", "yahoo.com",
            "inbox.lv", "inbox.lt", "mail.ru", "yandex.ru", "icloud.com", "zone.ee", "neti.ee", "email.ee",
            "gmail.lv", "one.lv", "takas.lt", "live.com", "me.com", "elkdata.ee", "hotmail.ee",
            "mail.com", "apollo.lv", "protonmail.com", "proton.me", "gmx.com", "gmx.net", "gmx.de",
            "inbox.eu", "yandex.com", "ya.ru", "list.ru", "bk.ru", "rambler.ru", "hotmail.co.uk",
            "outlook.lv", "outlook.lt", "yahoo.co.uk", "aol.com", "tvnet.lv", "delfi.lv", "post.ee",
            "hot.lt", "one.lt", "zebra.lt", "googlemail.com", "msn.com", "suhtlus.ee", "kodune.ee"}
# Hosts that are never a provider's own website.
NOT_OWN_SITE = {"facebook.com", "instagram.com", "google.com", "goo.gl", "linkedin.com", "youtube.com",
                "wixsite.com", "weebly.com", "blogspot.com", "tiktok.com", "x.com", "twitter.com",
                "1182.ee", "teatmik.ee", "inforegister.ee", "ariregister.rik.ee", "okidoki.ee", "osta.ee"}

# Priority zones. Harjumaa is an approximate county bbox; towns are radii.
ZONES = {
    "harjumaa": {"bbox": (59.10, 59.75, 23.40, 25.95),
                 "names": ("tallinn", "harju", "maardu", "keila", "saue", "saku", "viimsi", "rae", "jüri", "juri",
                           "kiili", "harku", "laagri", "peetri", "paldiski", "kose", "kuusalu", "loksa", "jõelähtme",
                           "joelahtme", "anija", "kehra", "raasiku", "saku", "tabasalu", "lagedi", "padise",
                           "lääne-harju", "kiisa", "saue", "loo")},
    "viljandi": {"center": (58.364, 25.590), "km": 25, "names": ("viljandi",)},
    "parnu": {"center": (58.385, 24.497), "km": 25, "names": ("pärnu", "parnu", "sindi", "paikuse", "audru")},
    "tartu": {"center": (58.378, 26.729), "km": 25, "names": ("tartu", "ülenurme", "ulenurme", "tõrvandi",
                                                              "torvandi", "kõrveküla", "korvekula", "elva")},
}


def _km(a, b):
    la1, lo1, la2, lo2 = map(math.radians, (*a, *b))
    h = math.sin((la2 - la1) / 2) ** 2 + math.cos(la1) * math.cos(la2) * math.sin((lo2 - lo1) / 2) ** 2
    return 6371 * 2 * math.asin(math.sqrt(h))


def zone_of(row) -> str:
    for loc in row["locations"]:
        if loc.get("synthetic"):
            continue
        lat, lng = loc.get("lat") or 0, loc.get("lng") or 0
        for z, spec in ZONES.items():
            if "bbox" in spec:
                a, b, c, d = spec["bbox"]
                if a <= lat <= b and c <= lng <= d:
                    return z
            elif lat and _km((lat, lng), spec["center"]) <= spec["km"]:
                return z
    text = " ".join([row.get("slug") or "", *[(l.get("city") or "") for l in row["locations"]]]).lower()
    for z, spec in ZONES.items():
        if any(n in text for n in spec["names"]):
            return z
    return ""


def _email_domain(e):
    return e.split("@", 1)[1].lower().strip() if e and "@" in e else ""


def _pick_email(site_emails, site_domains):
    """Prefer an address on the site's own domain(s), then info@/office@, then anything."""
    own = [e for e in site_emails if _email_domain(e) in site_domains
           or any(_email_domain(e).endswith("." + d) for d in site_domains)]
    pool = own or [e for e in site_emails if _email_domain(e) not in FREEMAIL] or site_emails
    pool = sorted(pool, key=lambda e: (0 if e.split("@")[0] in ("info", "office", "kontakt", "contact", "rent",
                                                                  "booking", "tellimus", "info.ee") else 1, e))
    return pool[0] if pool else ""


def classify(row, reg, site, mx, reg_index=None):
    reasons, prop = [], {"email": "", "phone": "", "website": "", "registry_code": ""}
    o = row.get("outreach") or {}
    sent, quoted, declined = o.get("sent", 0), o.get("quoted", 0), o.get("declined", 0)
    replied, delivered = o.get("replied", 0), o.get("delivered", 0)
    responded = quoted + declined + replied > 0

    # ── mailbox ─────────────────────────────────────────────────────────────
    email = (row.get("email") or "").strip().lower()
    edom = _email_domain(email)
    # A SOFT bounce (mailbox full, greylisting) is not a dead address; only a
    # hard bounce, the Unusable flag, or a domain that cannot take mail is.
    soft = (row.get("bounce_type") or "").lower() == "soft"
    # Outreach bounces only count against the CURRENT address: once the address
    # has been corrected, the old bounces are history, not evidence.
    sent_to = set((o.get("sent_to") or "").lower().split())
    outreach_bounced = o.get("bounced", 0) and email in sent_to
    bounced = bool(row.get("email_unusable") or (row.get("bounced_at") and not soft)
                   or (outreach_bounced and not soft))
    mail_dead = bool(bounced or (edom and edom not in FREEMAIL and mx in ("no", "nxdomain")))
    if bounced:
        reasons.append(f"email bounced ({row.get('bounce_type') or 'outreach status Bounced'})")
    elif soft:
        reasons.append("email soft-bounced once (not treated as dead)")
    if edom and mx in ("no", "nxdomain"):
        reasons.append(f"email domain {edom} cannot receive mail ({mx})")

    # ── website ─────────────────────────────────────────────────────────────
    # 401/403/429 = bot wall: says nothing about the business. 5xx (after one
    # retry) = degraded, weaker than gone.
    st = site.get("site_status", "")
    root_st = site.get("site_root_status") or ""
    deep_link_moved = st in ("404", "410") and root_st.isdigit() and int(root_st) < 400
    site_dead = (st in ("dns_fail", "conn_error", "404", "410") or bool(site.get("site_parked"))) \
        and not deep_link_moved
    if st in ("401", "403", "429"):
        reasons.append(f"website blocks automated checks ({st}) — not verified")
    elif st.isdigit() and int(st) >= 500:
        reasons.append(f"website error {st} (after retry)")
    elif site_dead:
        reasons.append("website dead: " + (f"parked ({site['site_parked']})" if site.get("site_parked")
                                           else f"{st} {site.get('site_error', '')}".strip()))
    if deep_link_moved:
        reasons.append(f"website page {st}; site root {site.get('site_root_url')} is live")
    if site.get("site_redirect_domain"):
        reasons.append(f"website redirects to {site['site_redirect_domain']}")

    site_emails = [e for e in (site.get("site_emails") or []) if e.split("@")[0] not in web.PLACEHOLDER_LOCALS]
    site_phones = site.get("site_phones") or []
    site_domains = {web.reg_domain(site.get("site_host", ""))}
    if site.get("site_final_url"):
        site_domains.add(web.reg_domain(web.host_of(site["site_final_url"])))
    site_domains.discard("")
    email_on_site = bool(email) and email in site_emails
    # Only propose an address whose domain can actually receive mail: catches
    # ROT13-obfuscated addresses ("…@nyybirefrnf.arg") and typos ("inbox.lv.lv").
    deliverable = [e for e in site_emails if e != email
                   and (_email_domain(e) in FREEMAIL or web.has_mx(_email_domain(e)) in ("yes", "a_only"))]
    alt_email = "" if email_on_site else _pick_email(deliverable, site_domains)
    alt_on_site_domain = bool(alt_email) and web.reg_domain(_email_domain(alt_email)) in site_domains
    our_phone = web.digits(row.get("phone"))
    phone_on_site = bool(our_phone) and any(web.digits(p) == our_phone for p in site_phones)
    alt_phone = "" if phone_on_site or not site_phones else sorted(site_phones)[0]

    # ── registry ────────────────────────────────────────────────────────────
    rstat = reg.get("status")
    reg_dead = reg.get("match") == "code" and rstat in (registry.DELETED, registry.BANKRUPT, registry.LIQUIDATION)
    name_clash = False
    if reg.get("match") == "code" and reg.get("reg_name"):
        a, b = set(registry.norm_name(row["name"]).split()), set(registry.norm_name(reg["reg_name"]).split())
        name_clash = bool(a) and bool(b) and not (a & b)
    if reg.get("match") == "code" and rstat and rstat != registry.ACTIVE:
        reasons.append(f"registry {row['registry_code']}: {rstat} ({reg.get('status_raw', '')})"
                       + (f", registered as '{reg.get('reg_name')}'" if name_clash else ""))
    elif name_clash:
        # Usually just brand vs legal name ("Kaubikud.ee" is run by DST Auto OÜ).
        reasons.append(f"operated by '{reg.get('reg_name')}' ({row['registry_code']})")
    if reg.get("match") == "name" and not row.get("registry_code"):
        prop["registry_code"] = reg["code"]
        reasons.append(f"registry code by unique name match: {reg['code']} ({reg.get('status')})")

    if sent:
        reasons.append(f"outreach sent={sent} delivered={delivered} quoted={quoted} replied={replied} "
                       f"declined={declined}" + (f" last_response={o['last_response'][:10]}"
                                                 if o.get("last_response") else ""))

    # ── decision ────────────────────────────────────────────────────────────
    if reg_dead:
        if responded:
            return "call", "medium", reasons + ["registry says gone but the provider has answered us"], prop
        ee_absent = row["country"] == "EE" and rstat == registry.DELETED
        if ee_absent:
            # EE open data omits deleted entries AND some branch types — absence alone is not proof.
            if site_dead or st == "no_website":
                return "remove", "medium", reasons + ["EE code absent from open data and no live website"], prop
            return "call", "medium", reasons + ["EE code absent from open data (struck off, a branch, or a typo)"], prop
        if name_clash:
            # The code belongs to a DIFFERENT company: our code is probably wrong,
            # which says nothing about the business on this row.
            byname = registry.lookup(reg_index, row["country"], None, row["name"]) if reg_index else {}
            if byname.get("match") == "name" and byname.get("status") == registry.ACTIVE:
                prop["registry_code"] = byname["code"]
                return "fix_contact", "medium", reasons + [
                    f"code on our row belongs to another company; '{byname.get('reg_name')}' "
                    f"is {byname['code']} (active)"], prop
            return "call", "medium", reasons + ["code on our row belongs to another company"], prop
        if site_dead or st == "no_website":
            return "remove", "high", reasons, prop
        return "call", "medium", reasons + ["registry entity gone but the website is live — the brand may "
                                            "now trade under another company"], prop

    if site_dead and mail_dead and not responded:
        return "remove", "medium", reasons, prop

    if mail_dead or (not email and alt_email):
        if alt_email:
            prop["email"] = alt_email
            if site.get("site_redirect_domain"):
                prop["website"] = site["site_final_url"]
            if alt_phone:
                prop["phone"] = alt_phone
            return ("fix_contact", "high" if alt_on_site_domain else "medium",
                    reasons + [f"{site.get('site_final_url')} prints {alt_email}"], prop)
        return "call", "medium", reasons + ["mailbox dead and no alternative address on the website"], prop

    if site_dead:
        prop["website"] = "(clear)"
        return "call", "low", reasons + ["website dead; mailbox not known-bad"], prop

    if deep_link_moved:
        prop["website"] = site.get("site_root_url", "")
        if alt_email and not email:
            prop["email"] = alt_email
        return "fix_contact", "medium", reasons, prop

    if site.get("site_redirect_domain") and alt_email and not email_on_site \
            and web.reg_domain(_email_domain(alt_email)) == site["site_redirect_domain"]:
        prop.update(email=alt_email, website=site["site_final_url"])
        if alt_phone:
            prop["phone"] = alt_phone
        return "fix_contact", "medium", reasons + [f"site moved; new domain prints {alt_email}"], prop

    if alt_email and email and not email_on_site:
        reasons.append(f"our email not on site; site prints {', '.join(site_emails[:4])}")
        if sent >= 2 and not responded:
            prop["email"] = alt_email
            return "fix_contact", "low", reasons + ["never answered at our address"], prop

    if site_phones and our_phone and not phone_on_site:
        reasons.append(f"our phone not on site; site prints {', '.join(site_phones[:3])}")
        if not email:
            prop["phone"] = alt_phone
            return "fix_contact", "medium", reasons, prop

    # The site moved to another domain and our (current) address is on it:
    # only the website URL is stale.
    if site.get("site_redirect_domain") and email and             web.reg_domain(edom) == site["site_redirect_domain"]:
        prop["website"] = site["site_final_url"]
        return "fix_contact", "high", reasons + [f"site moved to {site['site_final_url']}"], prop

    # Open tracking records nothing (0 opens on 270 sends, 21 of them quoted),
    # so silence is judged on answers only — and only for mail sent to the
    # CURRENT address (requests to a since-corrected address prove nothing).
    if sent >= 3 and not responded and email in sent_to:
        return "call", "low", reasons + [f"{sent} requests, never answered"], prop

    return "keep", "", reasons, prop


FIELDS = ["action", "confidence", "country", "zone", "name", "slug", "id", "city", "services",
          "current_email", "proposed_email", "current_phone", "proposed_phone", "current_website",
          "proposed_website", "current_registry_code", "proposed_registry_code", "reasons",
          "registry_match", "registry_status", "registry_name", "registry_link",
          "site_status", "site_final_url", "site_emails", "site_phones", "email_mx",
          "outreach_sent", "outreach_delivered", "outreach_replied", "outreach_quoted", "outreach_declined",
          "outreach_last_sent", "outreach_last_response", "bounced_at", "opt_out_at", "approve"]


def run(a):
    rows = json.loads((a.out / "suppliers.json").read_text(encoding="utf-8"))
    countries = {c.strip().upper() for c in a.countries.split(",") if c.strip()}
    towns = [t.strip().lower() for t in a.towns.split(",") if t.strip()]
    rows = [r for r in rows if r["country"] in countries]
    for r in rows:
        r["_zone"] = zone_of(r)
    if towns:
        rows = [r for r in rows if r["_zone"] in towns]
    if a.limit:
        rows = rows[: a.limit]
    reg_index = registry.load_index(a.out / "registry")

    cache_path = a.out / "site-cache.json"
    cache = json.loads(cache_path.read_text(encoding="utf-8")) if cache_path.exists() else {}

    def norm_url(u):
        return (u or "").strip().rstrip("/").lower()

    todo = sorted({norm_url(r["website"]) for r in rows if r.get("website") and not r.get("opt_out_at")
                   and r.get("is_active") and norm_url(r["website"]) not in cache})
    if not a.no_web and todo:
        print(f"probing {len(todo)} websites ({a.workers} workers)…")
        with ThreadPoolExecutor(a.workers) as ex:
            futs = {ex.submit(web.probe, u): u for u in todo}
            for i, f in enumerate(as_completed(futs), 1):
                u = futs[f]
                try:
                    cache[u] = web.to_jsonable(f.result())
                except Exception as e:  # never let one site kill the run
                    cache[u] = {"site_status": "probe_error", "site_error": repr(e)[:200]}
                if i % 25 == 0:
                    print(f"  {i}/{len(todo)}")
                    cache_path.write_text(json.dumps(cache, ensure_ascii=False), encoding="utf-8")
        cache_path.write_text(json.dumps(cache, ensure_ascii=False), encoding="utf-8")

    out_rows = []
    for r in rows:
        base = {"country": r["country"], "zone": r["_zone"], "name": r["name"], "slug": r.get("slug") or "",
                "id": r["id"], "city": "; ".join(sorted({l.get("city") or "" for l in r["locations"]})),
                "services": "|".join(r.get("services") or []),
                "current_email": r.get("email") or "", "current_phone": r.get("phone") or "",
                "current_website": r.get("website") or "", "current_registry_code": r.get("registry_code") or "",
                "bounced_at": (r.get("bounced_at") or "")[:10], "opt_out_at": (r.get("opt_out_at") or "")[:10]}
        if r.get("opt_out_at"):
            out_rows.append({**base, "action": "skip_opt_out", "reasons": "MarketingOptOutAt set — never touch"})
            continue
        if not r.get("is_active"):
            out_rows.append({**base, "action": "skip_inactive", "reasons": "IsActive = false"})
            continue
        reg = registry.lookup(reg_index, r["country"], r.get("registry_code"), r["name"])
        site = cache.get(norm_url(r.get("website")), {}) if r.get("website") else {"site_status": "no_website"}
        if a.no_web and r.get("website"):
            site = cache.get(norm_url(r.get("website")), {"site_status": "not_probed"})
        edom = _email_domain(r.get("email") or "")
        mx = web.has_mx(edom) if edom and edom not in FREEMAIL else ""
        action, conf, reasons, prop = classify(r, reg, site, mx, reg_index)
        o = r.get("outreach") or {}
        out_rows.append({
            **base, "action": action, "confidence": conf,
            "proposed_email": prop["email"], "proposed_phone": prop["phone"],
            "proposed_website": prop["website"], "proposed_registry_code": prop["registry_code"],
            "reasons": " · ".join(reasons),
            "registry_match": reg.get("match", ""), "registry_status": reg.get("status", "") or "",
            "registry_name": reg.get("reg_name", "") or "", "registry_link": reg.get("link", "") or "",
            "site_status": site.get("site_status", ""), "site_final_url": site.get("site_final_url", ""),
            "site_emails": " ".join(site.get("site_emails") or []),
            "site_phones": " ".join(site.get("site_phones") or []), "email_mx": mx,
            "outreach_sent": o.get("sent", 0), "outreach_delivered": o.get("delivered", 0),
            "outreach_replied": o.get("replied", 0), "outreach_quoted": o.get("quoted", 0),
            "outreach_declined": o.get("declined", 0), "outreach_last_sent": (o.get("last_sent") or "")[:10],
            "outreach_last_response": (o.get("last_response") or "")[:10],
        })

    order = {"remove": 0, "fix_contact": 1, "call": 2, "keep": 3, "skip_opt_out": 4, "skip_inactive": 5}
    out_rows.sort(key=lambda x: (order.get(x["action"], 9), x["country"], x["zone"], x["name"]))
    tag = "-".join(sorted(countries)).lower() + (("-" + "-".join(towns)) if towns else "")
    path = a.out / f"health-{tag}.csv"
    with open(path, "w", encoding="utf-8-sig", newline="") as fh:
        w = csv.DictWriter(fh, fieldnames=FIELDS, extrasaction="ignore")
        w.writeheader()
        w.writerows(out_rows)
    from collections import Counter
    print(f"wrote {path}  ({len(out_rows)} rows)")
    for k, v in Counter((x["action"], x.get("confidence", "")) for x in out_rows).most_common():
        print(f"  {k[0]:<14}{k[1]:<8}{v}")
    return path
