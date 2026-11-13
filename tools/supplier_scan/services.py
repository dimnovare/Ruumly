"""Service verification: does each provider REALLY offer the services we list?

    python -X utf8 tools/supplier_scan/scan.py services [--only-active] [--source suppliers|discovery]

For every row with a website: fetch the homepage plus up to 5 same-host pages
whose link mentions a service, strip scripts/markup, and look for each claimed
service's own words (ET/RU/LV/LT/EN). A match is kept as an EXACT quote with
the page URL, so the founder can read the evidence instead of trusting a score.

Freight/logistics wording is counted separately. A row whose claimed services
never appear but freight words do is `freight_only`: a transport company, not a
target partner.

Facebook/Instagram are linked when the site links them, never crawled (login
wall, ToS). Rows with no website are `no_site` and need a manual check.
READ ONLY: nothing is written to the database and nobody is e-mailed.
Output: out/service-verification.csv (+ out/service-cache.json, re-used on re-runs).
"""
from __future__ import annotations

import csv
import json
import re
import urllib.parse
from collections import Counter
from concurrent.futures import ThreadPoolExecutor

import requests

import web

CONSUMER = ("warehouse", "moving", "trailer", "cleaning", "vanrental")

# Strong = the phrase means the household service itself. Each regex is
# matched case-insensitively against visible page text.
SERVICE_RE = {
    # Self-storage a household can rent. Commercial warehousing (3PL, "sandėlių
    # nuoma" in a logistics park) is NOT this: see COMMERCIAL_WH below.
    "warehouse": re.compile(
        r"self[\s-]?storage|mini[\s-]?(?:ladu|lao\w*|noliktav\w*|sandėl\w*)|hoiu(?:ruum|boks|koht|lad)\w*|"
        r"laoruum\w*|lao(?:boksi?)\w*\s*(?:rent|üür)|panipai\w*|"
        r"(?:individuāl\w*\s*)?glabātuv\w*|uzglabāšan\w*\s*(?:boks|telp)\w*|personīg\w*\s*mant\w*|"
        r"sandėliuk\w*|daiktų\s*saugoj\w*|saugykl\w*|asmeninių\s*daiktų|"
        r"склад\w*\s*(?:для\s*)?(?:хранени|вещ)\w*|хранени\w*\s*вещ\w*|бокс\w*\s*для\s*хранени\w*|"
        r"storage\s*(?:unit|room|box)s?|(?:rent|rental)\s*(?:a\s*)?storage\s*(?:unit|room|box)", re.I),
    "moving": re.compile(
        r"kolimine|kolimis(?:teenus|abi|firma|töö|e\s*teenus|ettevõt|vedu?)\w*|koliteenus\w*|kolijad\b|"
        r"(?:korteri|kodu|kontori|maja)kolimi\w*|mööbli\s*(?:vedu|transport)\w*|"
        r"переезд\w*|грузчик\w*|перевоз\w*\s*мебел\w*|"
        r"pārvākšan\w*|pārcelšan\w*|mēbeļu\s*pārvadā\w*|pārvāk\w*|"
        r"perkraust\w*|baldų\s*pervež\w*|"
        r"\bmoving\s*(?:service|company|help)?s?\b|removals?\b|relocation", re.I),
    "trailer": re.compile(
        r"haagiserent\w*|(?:haagis|järelkär)\w*\s*(?:rent|üür|laenutus)\w*|(?:rent|üür)\w*\s*(?:haagis|järelkär)\w*|"
        r"аренд\w*\s*прицеп\w*|прицеп\w*\s*(?:в\s*)?аренд\w*|прокат\w*\s*прицеп\w*|"
        r"piekab\w*\s*nom\w*|nom\w*\s*piekab\w*|priekab\w*\s*nuom\w*|nuom\w*\s*priekab\w*|"
        r"trailers?\s*(?:rental|hire)|rent\s*a\s*trailer", re.I),
    "vanrental": re.compile(
        r"(?:kaubik|pakiauto|väikebuss|furgoon)\w*\s*(?:rent|üür)\w*|(?:rent|üür)\w*\s*(?:kaubik|pakiauto|väikebuss)\w*|"
        r"kaubikurent\w*|kaubikute\s*rent\w*|"
        r"аренд\w*\s*(?:фургон|микроавтобус|грузов\w*\s*авто|бус)\w*|прокат\w*\s*(?:фургон|микроавтобус|бус)\w*|"
        r"(?:busiņ|mikroautobus|kravas\s*auto|furgon)\w*\s*nom\w*|nom\w*\s*(?:busiņ|mikroautobus|kravas)\w*|"
        r"(?:mikroautobus|krovinin\w*\s*automobil|furgon)\w*\s*nuom\w*|nuom\w*\s*(?:mikroautobus|krovinin|furgon)\w*|"
        r"\bvan\s*(?:rental|hire)|rent\s*a\s*van|(?:cargo|moving)\s*van\s*rent\w*", re.I),
    "cleaning": re.compile(
        r"koristus\w*|koristaja\w*|suurpuhastus\w*|puhastusteenus\w*|aknapesu\w*|kodupuhastus\w*|"
        r"уборк\w*|клининг\w*|"
        r"uzkopšan\w*|tīrīšan\w*|uzkop\w*|"
        r"valym\w*|valymo\s*paslaug\w*|švar\w*\s*paslaug\w*|"
        r"\bcleaning\b|cleaners?\b", re.I),
}

# A storage word only counts when renting it is in the same breath: a wardrobe
# shop's "hoiuruumi lahendus" is not self-storage.
RENTAL_CTX = re.compile(r"rent|üür|broneeri|€|eur\b|kuus\b|kuu\b|nom[aā]|nuom|аренд|снять|rental|hire|for\s*rent|"
                        r"per\s*month|mēnes|mėn|месяц|m²|m2\b", re.I)

# Weak = the word fits the service but also other contexts ("for moving" on a
# car-rental page, a logistics warehouse). Shown as evidence, never `confirmed`.
WEAK_RE = {
    "warehouse": re.compile(r"laopin\w*|lao\w*\s*(?:rent|üür)\w*|noliktav\w*\s*(?:nom|īr)\w*|sandėl\w*\s*nuom\w*|"
                            r"аренд\w*\s*склад\w*|warehouse\s*(?:rent|space)|ladustamis\w*|\blao\w*|\bladu\b|noliktav\w*|sandėl\w*|склад\w*|warehous\w*|\bstorage\b", re.I),
    "moving": re.compile(r"koli\w*|перевоз\w*\s*вещ\w*|\bmoving\b|kraust\w*", re.I),
    "trailer": re.compile(r"haagis\w*|järelkär\w*|прицеп\w*|piekab\w*|priekab\w*|\btrailers?\b", re.I),
    "vanrental": re.compile(r"autorent\w*|kaubik\w*|pakiauto\w*|car\s*rental|rent\s*a\s*car|auto\s*nom\w*|"
                            r"automobilių\s*nuom\w*|аренд\w*\s*авто\w*|прокат\w*\s*авто\w*", re.I),
    "cleaning": re.compile(r"puhastus\w*|чистк\w*|tīr\w*|švar\w*|clean\w*", re.I),
}

FREIGHT_RE = re.compile(
    r"kaubavedu\w*|rahvusvaheli\w*\s*(?:vedu|transport)\w*|ekspedeeri\w*|täiskoorm\w*|osakoorm\w*|konteinervedu\w*|"
    r"külmvedu\w*|международн\w*\s*перевоз\w*|экспедир\w*|грузоперевоз\w*|"
    r"kravu\s*pārvadā\w*|starptautisk\w*\s*(?:pārvadā|kravu)\w*|ekspedīcij\w*|"
    r"krovinių\s*pervež\w*|tarptautin\w*\s*(?:pervež|krovin)\w*|ekspedijav\w*|"
    r"freight\s*forward\w*|\bFTL\b|\bLTL\b|haulage|full\s*truck\s*load|groupage|customs\s*clearance|"
    r"tolliformist\w*|muitas\s*noform\w*|muitinės\s*tarpinink\w*", re.I)

HOUSEHOLD_RE = re.compile(
    r"eraklient\w*|eraisik\w*|korteri\w*|kodu(?:kolim|ne|st)\w*|kliendile|"
    r"частн\w*\s*(?:лиц|клиент)\w*|квартир\w*|для\s*дома|"
    r"privātperson\w*|privātklient\w*|dzīvokļ\w*|mājokl\w*|"
    r"privat\w*\s*asmen\w*|privatiems|butų|buto\b|namų\s*perkraust\w*|gyventoj\w*|"
    r"household|private\s*(?:customer|client|person)s?|home\s*mov\w*|apartment\w*|personal\s*belongings", re.I)

LINK_HINT = re.compile(
    r"teenus|service|paslaug|pakalpoj|услуг|hind|price|kain|cen|ladu|lao|storage|noliktav|sandėl|склад|"
    r"koli|moving|pārvāk|perkraust|переезд|haagis|trailer|piekab|priekab|прицеп|rent|nom|nuom|аренд|"
    r"koristus|cleaning|uzkop|valym|уборк|about|meist|par-mums|apie", re.I)
SOCIAL_RE = re.compile(r'href="(https?://(?:www\.|m\.)?(?:facebook|instagram)\.com/[^"?#]+)', re.I)


def visible_text(html: str) -> str:
    html = re.sub(r"(?is)<(script|style|noscript|svg)[^>]*>.*?</\1>", " ", html)
    meta = " ".join(re.findall(r'<meta[^>]+(?:name|property)=["\'](?:description|og:description|og:title)["\'][^>]+content=["\']([^"\']+)', html, re.I))
    title = " ".join(re.findall(r"<title[^>]*>(.*?)</title>", html, re.I | re.S))
    text = re.sub(r"<[^>]+>", " ", html)
    return re.sub(r"\s+", " ", web.html.unescape(f"{title} . {meta} . {text}")).strip()


def _quote(text, m):
    """The sentence (or ~160 chars) around the match, verbatim."""
    s, e = m.start(), m.end()
    left = max(text.rfind(". ", 0, s), text.rfind("! ", 0, s), text.rfind("? ", 0, s), s - 110)
    right_c = [i for i in (text.find(". ", e), text.find("! ", e), text.find("? ", e)) if i != -1]
    right = min(right_c + [e + 90])
    return text[left + 1 if left >= 0 else 0: right + 1].strip(" .|·-")[:240]


def _links(base, html):
    out = []
    for href, label in re.findall(r'<a[^>]+href="([^"#]+)"[^>]*>(.*?)</a>', html, flags=re.I | re.S):
        if href.lower().startswith(("mailto:", "tel:", "javascript:")):
            continue
        lab = re.sub(r"<[^>]+>", "", label)
        if not (LINK_HINT.search(href) or LINK_HINT.search(lab)):
            continue
        u = urllib.parse.urljoin(base, href).split("#")[0]
        if web.host_of(u) == web.host_of(base) and u not in out and u.rstrip("/") != base.rstrip("/") \
                and not re.search(r"\.(pdf|jpe?g|png|gif|webp|zip|docx?)$", u, re.I):
            out.append(u)
    return out[:5]


def crawl(url: str) -> dict:
    res = {"status": "", "final_url": "", "pages": [], "social": [], "error": ""}
    if not url:
        res["status"] = "no_site"
        return res
    if not re.match(r"https?://", url):
        url = "https://" + url
    s = requests.Session()
    try:
        r, html = web._get(s, url)
    except Exception as e:
        res["status"], res["error"] = "unreachable", type(e).__name__
        return res
    res["status"], res["final_url"] = str(r.status_code), r.url
    if r.status_code >= 400:
        return res
    res["social"] = sorted({m.rstrip("/") for m in SOCIAL_RE.findall(html)
                            if not re.search(r"/(sharer|share|plugins|tr|dialog)", m)})[:3]
    res["pages"].append({"url": r.url, "text": visible_text(html)[:200_000]})
    for link in _links(r.url, html):
        if not web._robots_ok(s, link):
            continue
        try:
            r2, h2 = web._get(s, link)
        except Exception:
            continue
        if r2.status_code < 400:
            res["pages"].append({"url": r2.url, "text": visible_text(h2)[:200_000]})
    return res


def evaluate(claimed, site):
    found, quotes = {}, {}
    for svc, rx in SERVICE_RE.items():
        for p in site["pages"]:
            m = next((m for m in rx.finditer(p["text"])
                      if svc != "warehouse" or RENTAL_CTX.search(p["text"][max(0, m.start() - 80): m.end() + 80])), None)
            if m:
                found[svc] = p["url"]
                quotes[svc] = _quote(p["text"], m)
                break
    weak = {}
    for svc, rx in WEAK_RE.items():
        if svc in found:
            continue
        for p in site["pages"]:
            m = rx.search(p["text"])
            if m:
                weak[svc] = p["url"]
                quotes[svc] = "[weak] " + _quote(p["text"], m)
                break
    freight = sum(len(FREIGHT_RE.findall(p["text"])) for p in site["pages"])
    freight_q = next((_quote(p["text"], m) for p in site["pages"] for m in [FREIGHT_RE.search(p["text"])] if m), "")
    household = sum(len(HOUSEHOLD_RE.findall(p["text"])) for p in site["pages"])
    claimed_c = [c for c in claimed if c in CONSUMER]
    conf = [c for c in claimed_c if c in found]
    missing = [c for c in claimed_c if c not in found and c not in weak]
    weak_only = [c for c in claimed_c if c not in found and c in weak]
    extra = [c for c in found if c not in claimed_c]
    status, words = site["status"], sum(len(p["text"]) for p in site["pages"])
    if status == "no_site":
        verdict = "no_site"
    elif status in ("unreachable",) or not status.startswith("2"):
        verdict = "site_down_or_blocked"
    elif words < 400:
        verdict = "site_empty_js"  # a JS-only shell: text not in the HTML, check by hand
    elif claimed_c and not missing and not weak_only:
        verdict = "confirmed"
    elif conf or weak_only:
        verdict = "partial"
    elif freight:
        verdict = "freight_only"
    elif found:
        verdict = "other_services_only"
    else:
        verdict = "not_confirmed"
    # Target fit: is this a business a household would hire?
    if verdict in ("no_site", "site_down_or_blocked", "site_empty_js"):
        fit = "unverified"
    elif not claimed_c:
        fit = "not_consumer"  # listed only as packing/insurance: not on sale
    elif conf and (household or any(c != "warehouse" for c in conf)) and not (freight >= 10 and freight > 5 * household):
        fit = "target"
    elif freight >= 3 and not household and all(c == "warehouse" for c in conf):
        fit = "b2b_logistics"
    else:
        fit = "review"
    return {"verdict": verdict, "fit": fit, "household_hits": household, "confirmed": " ".join(conf), "not_found": " ".join(missing), "weak_only": " ".join(weak_only),
            "also_offers": " ".join(extra), "freight_hits": freight, "freight_quote": freight_q,
            "quotes": quotes, "evidence_urls": {**weak, **found}}


def _rows(a):
    if getattr(a, "source", "suppliers") == "discovery":
        for path in sorted(a.out.glob("discovery-review-*.csv")):
            for r in csv.DictReader(open(path, encoding="utf-8-sig")):
                yield {"id": r.get("slug", ""), "name": r["name"], "country": r.get("country", ""),
                       "website": r.get("websiteUrl", ""), "services": r.get("serviceTypes", "").split(),
                       "city": r.get("city", ""), "email": r.get("contactEmail", ""), "phone": r.get("contactPhone", ""),
                       "source": path.name, "active": "", "published": ""}
        return
    for s in json.loads((a.out / "suppliers.json").read_text(encoding="utf-8")):
        if a.only_active and not s["is_active"]:
            continue
        yield {"id": s["id"], "name": s["name"], "country": s["country"], "website": s.get("website") or "",
               "services": s.get("services") or [], "email": s.get("email") or "", "phone": s.get("phone") or "",
               "city": next((l.get("city") for l in s.get("locations") or [] if l.get("city")), ""),
               "source": "suppliers", "active": s["is_active"], "published": s.get("is_published")}


def run(a):
    rows = list(_rows(a))
    cache_p = a.out / "service-cache.json"
    cache = json.loads(cache_p.read_text(encoding="utf-8")) if cache_p.exists() else {}
    urls = sorted({r["website"] for r in rows if r["website"] and r["website"] not in cache})
    print(f"{len(rows)} rows, {len(urls)} sites to crawl ({len(cache)} cached)", flush=True)
    with ThreadPoolExecutor(10) as ex:
        futs = {u: ex.submit(crawl, u) for u in urls}
        for i, (u, f) in enumerate(futs.items(), 1):
            try:
                cache[u] = f.result(timeout=180)
            except Exception as e:
                cache[u] = {"status": "unreachable", "error": type(e).__name__, "pages": [], "social": [], "final_url": ""}
            if i % 50 == 0:
                print(f"  {i}/{len(urls)}", flush=True)
                cache_p.write_text(json.dumps(cache, ensure_ascii=False), encoding="utf-8")
    cache_p.write_text(json.dumps(cache, ensure_ascii=False), encoding="utf-8")

    out = []
    for r in rows:
        site = cache.get(r["website"]) if r["website"] else {"status": "no_site", "pages": [], "social": [], "final_url": ""}
        ev = evaluate(r["services"], site)
        row = {"approve": "", "fit": ev["fit"], "verdict": ev["verdict"], "household_hits": ev["household_hits"], "id": r["id"], "name": r["name"], "country": r["country"],
               "city": r["city"], "listed_services": " ".join(r["services"]), "confirmed": ev["confirmed"],
               "not_found": ev["not_found"], "weak_only": ev["weak_only"], "also_offers": ev["also_offers"], "freight_hits": ev["freight_hits"],
               "website": r["website"], "site_status": site.get("status", ""), "pages_read": len(site.get("pages", [])),
               "social": " ".join(site.get("social", []))}
        for svc in CONSUMER:
            row[f"quote_{svc}"] = ev["quotes"].get(svc, "")
            row[f"url_{svc}"] = ev["evidence_urls"].get(svc, "")
        row["freight_quote"] = ev["freight_quote"]
        row.update({"email": r["email"], "phone": r["phone"], "source": r["source"], "is_active": r["active"]})
        out.append(row)
    order = ["freight_only", "not_confirmed", "other_services_only", "partial", "site_empty_js",
             "site_down_or_blocked", "no_site", "confirmed"]
    fits = ["b2b_logistics", "review", "not_consumer", "unverified", "target"]
    out.sort(key=lambda o: (fits.index(o["fit"]), order.index(o["verdict"]), o["country"], o["name"]))
    name = "service-verification" + ("-discovery" if getattr(a, "source", "") == "discovery" else "") + ".csv"
    with open(a.out / name, "w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, fieldnames=list(out[0]))
        w.writeheader()
        w.writerows(out)
    print(f"wrote {name}: {Counter(o['verdict'] for o in out)}")
    print("by country:", Counter((o["country"], o["verdict"]) for o in out))


def confirm_list(a):
    """service-verification.csv -> who gets the partner-confirm letter, who is removed.

    out/partner-confirm-list.csv + .json (supplierIds for POST /api/admin/suppliers/partner-confirm/campaign):
      fit review/unverified, active, has a usable e-mail, not opted out, inbox not mailed in the last 7 days.
    out/remove-b2b.json: fit b2b_logistics (founder-approved removal, soft delete).
    out/unreachable-no-email.csv: unsure rows with no e-mail at all (Places or manual).
    """
    rows = list(csv.DictReader(open(a.out / "service-verification.csv", encoding="utf-8-sig")))
    sup = {s["id"]: s for s in json.loads((a.out / "suppliers.json").read_text(encoding="utf-8"))}
    recent = set()
    log = a.out / "silent-mail-sent.csv"
    if log.exists():
        recent = {r["send_to"].lower() for r in csv.DictReader(open(log, encoding="utf-8-sig"))}
    mail, nomail, held = [], [], []
    for r in rows:
        if r["fit"] not in ("review", "unverified"):
            continue
        s = sup[r["id"]]
        if s.get("opt_out_at") or not s["is_active"]:
            continue
        e = (s.get("email") or "").strip().lower()
        row = {"id": r["id"], "name": r["name"], "country": r["country"], "city": r["city"], "email": e,
               "phone": s.get("phone") or "", "fit": r["fit"], "verdict": r["verdict"],
               "listed_services": r["listed_services"], "website": r["website"]}
        if not e or s.get("email_unusable"):
            nomail.append(row)
        elif e in recent:
            held.append(row)
        else:
            mail.append(row)
    for name, data in (("partner-confirm-list.csv", mail), ("unreachable-no-email.csv", nomail),
                       ("partner-confirm-held-recently-mailed.csv", held)):
        with open(a.out / name, "w", newline="", encoding="utf-8-sig") as f:
            w = csv.DictWriter(f, fieldnames=list((data or [{"id": ""}])[0]))
            w.writeheader()
            w.writerows(data)
    (a.out / "partner-confirm-list.json").write_text(json.dumps({"supplierIds": [m["id"] for m in mail]}), encoding="utf-8")
    b2b = [{"id": r["id"], "name": r["name"], "country": r["country"]} for r in rows if r["fit"] == "b2b_logistics"]
    (a.out / "remove-b2b.json").write_text(json.dumps(b2b, ensure_ascii=False, indent=1), encoding="utf-8")
    print(f"confirm letters: {len(mail)} rows ({len({m['email'] for m in mail})} inboxes) "
          f"{Counter(m['country'] for m in mail)}; no e-mail: {len(nomail)} {Counter(m['country'] for m in nomail)}; "
          f"held (mailed this week): {len(held)}; remove b2b: {len(b2b)}")
