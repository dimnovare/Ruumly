"""Enrich + validate the silent providers before writing to them again.

    python -X utf8 tools/supplier_scan/scan.py enrich

Input:  out/silent-providers.csv (scan.py silent), out/suppliers.json,
        out/registry/ee_general.jsonl (EE contact data from the register).
Output: out/silent-enriched.csv — one row per provider with what the web and
        the register say, a validation verdict, and the address to write to.

Sources: the provider's own website (home, contact and price pages, robots.txt
honoured, every hop SSRF-checked by web._guarded_request) and the EE register.
Facebook/Instagram pages are recorded when the site links them but NOT crawled:
they sit behind a login wall and their terms forbid scraping.
Nothing is written to the database and nobody is e-mailed.
"""
from __future__ import annotations

import csv
import json
import re
import urllib.parse
from concurrent.futures import ThreadPoolExecutor

import health
import registry
import web

PRICE_HINTS = ("hind", "hinnakiri", "price", "pricing", "tariif", "cen", "kain", "цен", "прайс", "тариф")
PRICE_RE = re.compile(r"(?:€\s?\d{1,4}(?:[.,]\d{1,2})?|\d{1,4}(?:[.,]\d{1,2})?\s?(?:€|eur\b|euro))", re.I)
SOCIAL_RE = re.compile(r'href="(https?://(?:www\.|m\.)?(?:facebook|instagram)\.com/[^"?#]+)', re.I)
DIAL = {"EE": "372", "LV": "371", "LT": "370"}
# National significant number lengths: EE 7-8, LV 8, LT 8.
NSN = {"EE": (7, 8), "LV": (8, 8), "LT": (8, 8)}


def norm_phone(p: str, country: str) -> str:
    """-> +CCC NSN, or '' when the number cannot be valid for the country."""
    d = web.digits(p)
    cc = DIAL.get(country, "")
    if d.startswith("00"):
        d = d[2:]
    if cc and d.startswith(cc):
        d = d[len(cc):]
    elif country == "LT" and d.startswith("8") and len(d) == 9:
        d = d[1:]  # LT trunk prefix 8
    lo, hi = NSN.get(country, (7, 9))
    if not (lo <= len(d) <= hi):
        return ""
    if country == "EE" and d[0] not in "345678":
        return ""
    if country == "LV" and d[0] not in "236":
        return ""
    if country == "LT" and d[0] not in "3456":
        return ""
    return f"+{cc} {d}"


def _price_links(base, text):
    out = []
    for href, label in re.findall(r'<a[^>]+href="([^"#]+)"[^>]*>(.*?)</a>', text, flags=re.I | re.S):
        h, lab = href.lower(), re.sub(r"<[^>]+>", "", label).lower()
        if h.startswith(("mailto:", "tel:", "javascript:")):
            continue
        if any(k in h or k in lab for k in PRICE_HINTS):
            u = urllib.parse.urljoin(base, href)
            if web.host_of(u) == web.host_of(base) and u not in out:
                out.append(u)
    return out[:2]


def _prices(text):
    plain = re.sub(r"\s+", " ", re.sub(r"<[^>]+>", " ", web.html.unescape(text)))
    snippets = []
    for m in PRICE_RE.finditer(plain):
        s = plain[max(0, m.start() - 45): m.end() + 15].strip()
        if s not in snippets:
            snippets.append(s)
        if len(snippets) >= 4:
            break
    return snippets


def deep(url):
    """web.probe + price pages + social links."""
    res = web.probe(url)
    res.update({"prices": [], "price_page": "", "social": []})
    if not url or res.get("site_status") not in ("200",):
        return res
    import requests
    s = requests.Session()
    s.headers["User-Agent"] = web.UA
    try:
        r, text = web._get(s, res.get("site_final_url") or url)
    except Exception:
        return res
    res["social"] = sorted({m.rstrip("/") for m in SOCIAL_RE.findall(text)
                            if not re.search(r"/(sharer|share|plugins|tr|dialog)", m)})[:3]
    res["prices"] = _prices(text)
    for link in _price_links(r.url, text):
        if not web._robots_ok(s, link):
            continue
        try:
            r2, t2 = web._get(s, link)
        except Exception:
            continue
        if r2.status_code < 400:
            p = _prices(t2)
            if p:
                res["prices"], res["price_page"] = p, r2.url
                break
    return res


def _ee_register(codes, names):
    out = {}
    for rec in registry.iter_ee_general(registry_dir()):
        if rec["code"] in codes or registry.norm_name(rec["name"]) in names:
            out[rec["code"]] = rec
            out[registry.norm_name(rec["name"])] = rec
    return out


def registry_dir():
    return OUT / "registry"


OUT = None


def run(a):
    global OUT
    OUT = a.out
    rows = list(csv.DictReader(open(a.out / "silent-providers.csv", encoding="utf-8-sig")))
    sup = {s["id"]: s for s in json.loads((a.out / "suppliers.json").read_text(encoding="utf-8"))}
    ee = [sup[r["id"]] for r in rows if r["country"] == "EE"]
    codes = {re.sub(r"\D", "", s.get("registry_code") or "") for s in ee} - {""}
    reg = _ee_register(codes, {registry.norm_name(s["name"]) for s in ee})
    print(f"{len(rows)} providers; EE register hits {len({id(v) for v in reg.values()})}", flush=True)

    urls = [sup[r["id"]].get("website") for r in rows]
    with ThreadPoolExecutor(8) as ex:
        futs = [ex.submit(deep, u) for u in urls]
        sites = []
        for i, f in enumerate(futs, 1):
            try:
                sites.append(f.result(timeout=120))
            except Exception as e:
                sites.append({"site_status": "probe_error", "site_error": type(e).__name__})
            if i % 20 == 0:
                print(f"  {i}/{len(rows)}", flush=True)

    mx_cache = {}

    def mx(email):
        d = email.split("@")[-1].lower()
        if d not in mx_cache:
            mx_cache[d] = web.has_mx(d)
        return mx_cache[d]

    out = []
    for r, site in zip(rows, sites):
        s = sup[r["id"]]
        cc = s["country"]
        rec = reg.get(re.sub(r"\D", "", s.get("registry_code") or "")) or reg.get(registry.norm_name(s["name"])) \
            if cc == "EE" else None
        cur = (s.get("email") or "").strip().lower()
        site_emails = [e for e in site.get("site_emails") or [] if e]
        reg_emails = [e.lower() for e in (rec or {}).get("email", []) if not e.lower().endswith("@eesti.ee")]
        seen_on = [n for n, pool in (("site", site_emails), ("register", reg_emails)) if cur in pool]

        cur_mx = mx(cur) if cur else "no"
        own = {web.reg_domain(web.host_of(site.get("site_final_url") or s.get("website") or ""))} - {""}
        alt = health._pick_email([e for e in site_emails + reg_emails if e != cur], own) if (site_emails or reg_emails) else ""
        alt_mx = mx(alt) if alt else ""

        if cur and cur_mx == "yes" and seen_on:
            verdict, send_to = "verified", cur
        elif cur and cur_mx == "yes" and alt and alt_mx == "yes" and web.reg_domain(alt.split("@")[-1]) in own \
                and web.reg_domain(cur.split("@")[-1]) not in own:
            verdict, send_to = "replace_with_site_email", alt
        elif cur and cur_mx == "yes":
            verdict, send_to = "deliverable_unconfirmed", cur
        elif alt and alt_mx == "yes":
            verdict, send_to = "replace_current_dead", alt
        else:
            verdict, send_to = "no_valid_email", ""

        phones = []
        for p in [s.get("phone") or ""] + list(site.get("site_phones") or []) + list((rec or {}).get("phone", [])):
            n = norm_phone(p, cc)
            if n and n not in phones:
                phones.append(n)
        cur_phone = norm_phone(s.get("phone") or "", cc)

        out.append({
            "approve": "", "verdict": verdict, "send_to": send_to,
            "id": r["id"], "name": s["name"], "country": cc, "language": r["language"],
            "services": r["services"], "city": next((l.get("city") for l in s.get("locations") or [] if l.get("city")), ""),
            "lead_mails": r["lead_mails"], "last_sent": r["last_sent"],
            "current_email": cur, "current_email_mx": cur_mx, "current_email_seen_on": " ".join(seen_on),
            "site_emails": " ".join(site_emails[:4]), "register_emails": " ".join(reg_emails[:3]),
            "current_phone": s.get("phone") or "", "current_phone_valid": "yes" if cur_phone else "no",
            "phones_found": " ".join(phones[:4]),
            "website": s.get("website") or "", "site_status": site.get("site_status", ""),
            "site_brand": site.get("site_brand", ""), "household_hits": site.get("site_household_hits", ""),
            "b2b_hits": site.get("site_b2b_hits", ""),
            "prices": " || ".join(site.get("prices") or []), "price_page": site.get("price_page", ""),
            "social": " ".join(site.get("social") or []),
            "register_status": (rec or {}).get("status", ""), "register_www": " ".join((rec or {}).get("www", [])[:2]),
            "employees": (rec or {}).get("employees", ""),
        })

    path = a.out / "silent-enriched.csv"
    with open(path, "w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, fieldnames=list(out[0]))
        w.writeheader()
        w.writerows(out)
    from collections import Counter
    print(f"wrote {path}: {Counter(o['verdict'] for o in out)}")
    print(f"  with prices {sum(1 for o in out if o['prices'])}, social {sum(1 for o in out if o['social'])}, "
          f"valid phone {sum(1 for o in out if o['phones_found'])}")
