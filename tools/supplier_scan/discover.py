"""Part 2 — providers missing from the directory.

Sources
  registry  (default, free) Estonian e-Business Register general data: every
            company's declared EMTAK activities, its REGISTERED e-mail /
            website / phone and its latest annual report. This is the data
            1182.ee, teatmik.ee and inforegister.ee republish — taken at the
            source, under the register's open-data licence, instead of scraping
            directories whose robots.txt / terms forbid harvesting.
  places    (PAID, opt-in) Google Places Text Search — see places.py.
  file      a hand-collected CSV with the review columns below.

Every candidate is deduped against ALL directory rows — including inactive
and opted-out ones, so an opt-out is never re-imported under a new slug — by
registry code, phone, e-mail / e-mail domain, website domain and name.

Output: discovery-review.csv. The founder fills the `approve` column (y) and
may edit any import column; `emit-import` turns approved rows into the JSON
array POST /api/admin/suppliers/bulk accepts (validated locally first).
"""
from __future__ import annotations

import csv
import json
import re
import unicodedata
from concurrent.futures import ThreadPoolExecutor
from datetime import date

import requests

import health
import registry
import web

CONSUMER_SLUGS = ["warehouse", "moving", "trailer", "cleaning", "vanrental"]
# Mirror of AdminDirectoryController.CountryBounds — keep in sync.
COUNTRY_BOUNDS = {"EE": (57.5, 59.7, 21.7, 28.2), "LV": (55.6, 58.1, 20.9, 28.3), "LT": (53.8, 56.5, 20.9, 26.9)}
RESERVED_SLUGS = {"dashboard", "onboarding", "new", "edit", "admin", "api"}

# ── how a register entry maps to a consumer service ─────────────────────────
KW = {
    "warehouse": re.compile(r"miniladu|minilaod|hoiuladu|hoiulao|hoiuruum|laoboks|self.?storage|homestorage|"
                            r"boxrent|konteinerladu|hoiuboks|ladu24|kapsel", re.I),
    # Anchored: "kolim" alone matched "birgitskolimowski", "moving" matched "Removing".
    "moving": re.compile(r"kolimi|kolija|(?<![a-z])mov(ing|ers?)(?![a-z])|pereezd|переезд", re.I),
    "trailer": re.compile(r"haagis|trailer|järelkäru|jarelkaru", re.I),
    "cleaning": re.compile(r"puhast|koristus|clean|majahoidja", re.I),
    "vanrental": re.compile(r"kaubik|furgoon|pakiauto|(?<![a-z])vans?(?![a-z])|minibuss|väikebuss|vaikebuss", re.I),
}
# A caravan / sauna trailer is not a household trailer hire.
NOT_TRAILER = re.compile(r"haagissuvila|suvila|sauna|camper|matkaauto|karavan", re.I)
# Chimney sweeps, pressure-washing of facades, industrial cleaning: not a home clean.
NOT_CLEANING = re.compile(r"korsten|korstna|tuleohutus|tööstus|industrial|tööriist|valla haldus|linna haldus|taara|jäätme", re.I)
CLEANING_MAIN = {"81211", "81291", "81231", "8121"}
CLEANING_CODES = CLEANING_MAIN | {"81221"}
WAREHOUSE_CODES = {"52101", "68201", "77399"}
# 77111 = cars and vans <= 3.5 t. 77121 = TRUCK rental (Scania, Wirtgen, GoBus) —
# only counts with an explicit van keyword in the name or website.
VAN_CODES = {"77111", "7711", "77121"}
TRAILER_CODES = {"77399", "77511", "77111", "77121", "7739"}

ZONE_COUNTIES = {"harjumaa": ("Harju maakond",), "parnu": ("Pärnu maakond",), "tartu": ("Tartu maakond",)}


def services_for(rec) -> list[str]:
    codes = {c for c, _, _ in rec["emtak"]}
    if rec.get("report_emtak") and rec["report_emtak"][0]:
        codes.add(rec["report_emtak"][0])
    # Name + website only. E-mail local parts are not evidence: a mass mailbox
    # ("informationstorage@…") shared by shell companies matched "storage".
    text = " ".join([rec["name"] or "", *rec["www"]])
    out = []
    if "49421" in codes or KW["moving"].search(text):
        out.append("moving")
    if KW["warehouse"].search(text) and (codes & WAREHOUSE_CODES or "49421" in codes or not codes):
        out.append("warehouse")
    if KW["trailer"].search(text) and not NOT_TRAILER.search(text) and (codes & TRAILER_CODES or not codes):
        out.append("trailer")
    if codes & VAN_CODES and KW["vanrental"].search(text):
        out.append("vanrental")
    main = {c for c, _, m in rec["emtak"] if m} | ({rec["report_emtak"][0]} if rec.get("report_emtak") else set())
    if not NOT_CLEANING.search(text) and (main & CLEANING_MAIN or (codes & CLEANING_CODES
                                                                  and KW["cleaning"].search(text))):
        out.append("cleaning")
    return out


# ── dedupe against the directory ────────────────────────────────────────────
def _norm_url_domain(u):
    d = web.reg_domain(web.host_of(u)) if u else ""
    return "" if d in health.NOT_OWN_SITE else d


DIAL = {"EE": "372", "LV": "371", "LT": "370"}


def _intl(phone, country):
    """Country-qualified digits. National numbers alone collide across the
    Baltics (an Estonian mobile matched a Latvian row)."""
    raw = re.sub(r"\D", "", phone or "")
    if raw.startswith("00"):
        raw = raw[2:]
    if raw[:3] in DIAL.values() and len(raw) > 8:
        return raw
    return DIAL.get(country, "") + web.digits(phone)


def directory_keys(suppliers):
    keys = {"code": {}, "phone": {}, "email": {}, "edomain": {}, "wdomain": {}, "name": {}}
    for s in suppliers:
        ref = f"{s.get('slug') or s['id']}" + (" [opt-out]" if s.get("opt_out_at") else "") \
              + ("" if s.get("is_active") else " [inactive]")
        if s.get("registry_code"):
            keys["code"][re.sub(r"\D", "", s["registry_code"])] = ref
        d = _intl(s.get("phone"), s.get("country") or "EE")
        if len(d) >= 7:
            keys["phone"][d] = ref
        e = (s.get("email") or "").lower().strip()
        if e:
            keys["email"][e] = ref
            dom = e.split("@")[-1]
            if dom not in health.FREEMAIL:
                keys["edomain"][web.reg_domain(dom)] = ref
        wd = _norm_url_domain(s.get("website"))
        if wd:
            keys["wdomain"][wd] = ref
        n = registry.norm_name(s["name"])
        if n:
            keys["name"][(s.get("country") or "EE", n)] = ref  # same name in Vilnius is another firm
    return keys


def match_directory(c, keys):
    if c.get("registry_code") and c["registry_code"] in keys["code"]:
        return "registry_code", keys["code"][c["registry_code"]]
    for p in c["phones"]:
        if _intl(p, c.get("country", "EE")) in keys["phone"]:
            return "phone", keys["phone"][_intl(p, c.get("country", "EE"))]
    for e in c["emails"]:
        e = e.lower()
        if e in keys["email"]:
            return "email", keys["email"][e]
        dom = web.reg_domain(e.split("@")[-1])
        if dom in keys["edomain"]:
            return "email_domain", keys["edomain"][dom]
    wd = _norm_url_domain(c.get("website"))
    if wd and (wd in keys["wdomain"] or wd in keys["edomain"]):
        return "website_domain", keys["wdomain"].get(wd) or keys["edomain"][wd]
    n = (c.get("country", "EE"), registry.norm_name(c["name"]))
    if n[1] and n in keys["name"]:
        return "name", keys["name"][n]
    return "", ""


# ── geocoding (Maa-amet In-ADS, free, official EE address gazetteer) ────────
def geocode_ee(adr_id=None, address=None):
    params = {"results": 1}
    if adr_id:
        params["adrid"] = adr_id
    elif address:
        params["address"] = address
    else:
        return None
    try:
        r = requests.get("https://inaadress.maaamet.ee/inaadress/gazetteer", params=params, timeout=20,
                         headers={"User-Agent": web.UA})
        a = (r.json().get("addresses") or [None])[0]
        if not a:
            return None
        omav = a.get("omavalitsus") or ""
        asustus = re.sub(r"\s+(linnaosa|küla|alevik|alev|linn|asum)$", "", a.get("asustusyksus") or "")
        city = omav.replace(" linn", "") if not omav.endswith(" vald") else (asustus or omav)
        return {"lat": float(a["viitepunkt_b"]), "lng": float(a["viitepunkt_l"]), "city": city,
                "address": a.get("ipikkaadress") or a.get("pikkaadress")}
    except Exception:
        return None


# ── slugs ───────────────────────────────────────────────────────────────────
def slugify(name, city, taken):
    base = registry.norm_name(name) or "provider"
    s = unicodedata.normalize("NFKD", f"{base} {city}").encode("ascii", "ignore").decode().lower()
    s = re.sub(r"[^a-z0-9]+", "-", s).strip("-")[:70] or "provider"
    slug, i = s, 2
    while slug in taken or slug in RESERVED_SLUGS:
        slug, i = f"{s}-{i}", i + 1
    taken.add(slug)
    return slug


def _probe_all(urls, cache_path, deadline_s=90):
    """Probe each URL (shared cache with `health`), never waiting more than
    deadline_s on any one site — a host that stalls inside DNS or TLS would
    otherwise freeze the whole run."""
    cache = json.loads(cache_path.read_text(encoding="utf-8")) if cache_path.exists() else {}
    key = lambda u: (u or "").strip().rstrip("/").lower()
    todo = sorted({key(u) for u in urls if u and key(u) not in cache})
    print(f"probing {len(todo)} websites…", flush=True)
    ex = ThreadPoolExecutor(8)
    futs = {u: ex.submit(web.probe, u) for u in todo}
    for i, (u, f) in enumerate(futs.items(), 1):
        try:
            cache[u] = web.to_jsonable(f.result(timeout=deadline_s))
        except Exception as e:
            cache[u] = {"site_status": "probe_timeout" if "Timeout" in type(e).__name__ else "probe_error",
                        "site_error": type(e).__name__}
        if i % 20 == 0:
            print(f"  {i}/{len(todo)}", flush=True)
    ex.shutdown(wait=False, cancel_futures=True)
    cache_path.write_text(json.dumps(cache, ensure_ascii=False), encoding="utf-8")
    return [cache.get(key(u), {}) if u else {"site_status": "no_website"} for u in urls]


# ── sources ─────────────────────────────────────────────────────────────────
def from_registry(out_dir, zones, services, min_report_year):
    cands = []
    counties = {c: z for z in zones for c in ZONE_COUNTIES.get(z, ())}
    for rec in registry.iter_ee_general(out_dir / "registry"):
        if rec["status"] != "R" or not rec.get("ehak"):
            continue
        county = next((c for c in counties if c in rec["ehak"]), None)
        if not county:
            continue
        if (rec.get("last_report") or "0") < str(min_report_year):
            continue  # no annual report since min_report_year: dormant or brand new
        svcs = [s for s in services_for(rec) if s in services]
        if not svcs:
            continue
        # @eesti.ee is the state-issued personal address keyed on a person's ID
        # code: personal data, never a business contact.
        emails = [e.lower() for e in rec["email"] if not e.lower().endswith("@eesti.ee")]
        if not (emails or rec["www"]):
            continue  # no way to reach them by e-mail; phone-only rows are the 2026-08-19 audit's problem
        cands.append({
            "source": "ee_register", "source_ref": f"https://ariregister.rik.ee/est/company/{rec['code']}",
            "name": rec["name"], "registry_code": rec["code"], "address": rec.get("address") or "",
            "adr_id": rec.get("adr_id"), "lat": None, "lng": None, "zone_hint": counties[county],
            "website": (rec["www"] or [""])[0], "emails": emails,
            "phones": rec["phone"], "services": set(svcs),
            "employees": rec.get("employees") or "", "last_report": rec.get("last_report") or "",
            "evidence": "EMTAK " + ", ".join(f"{c} {t}" for c, t, _ in rec["emtak"][:3]),
        })
    return cands


def from_file(path):
    rows = list(csv.DictReader(open(path, encoding="utf-8-sig")))
    out = []
    for r in rows:
        out.append({"source": r.get("source") or "manual", "source_ref": r.get("source_ref", ""),
                    "name": r["name"], "registry_code": re.sub(r"\D", "", r.get("registryCode", "")),
                    "address": r.get("address", ""), "lat": float(r["lat"]) if r.get("lat") else None,
                    "lng": float(r["lng"]) if r.get("lng") else None, "zone_hint": "",
                    "website": r.get("websiteUrl", ""), "emails": [r["contactEmail"]] if r.get("contactEmail") else [],
                    "phones": [r["contactPhone"]] if r.get("contactPhone") else [],
                    "services": set((r.get("serviceTypes") or "").split("|")) - {""},
                    "evidence": r.get("evidence", "manual")})
    return out


REVIEW_FIELDS = [
    "approve", "zone", "rank", "name", "serviceTypes", "contactEmail", "contactPhone", "websiteUrl",
    "registryCode", "city", "address", "lat", "lng", "country", "slug", "tagline",
    "site_status", "site_emails", "employees", "last_report", "evidence", "source", "source_ref", "coords_note"]


def run(a):
    suppliers = json.loads((a.out / "suppliers.json").read_text(encoding="utf-8"))
    zones = [z.strip().lower().replace("tallinn", "harjumaa") for z in a.towns.split(",") if z.strip()]
    services = [s.strip() for s in a.services.split(",") if s.strip() in CONSUMER_SLUGS]

    if a.source == "registry":
        cands = from_registry(a.out, zones, services, a.min_report_year)
    elif a.source == "places":
        import places
        cands = places.text_search(zones, services, a.max_calls, a.i_accept_cost)
    else:
        cands = from_file(a.input)
    print(f"{len(cands)} raw candidates")
    # A mailbox registered on 3+ companies is a registered-agent / shell-company
    # address (one protonmail inbox sat on ten "warehouse" OÜs), not a provider.
    from collections import Counter
    shared = Counter(e for c in cands for e in set(c["emails"]))
    mass = {e for e, n in shared.items() if n >= 3}
    if mass:
        before = len(cands)
        cands = [c for c in cands if not (set(c["emails"]) & mass)]
        print(f"dropped {before - len(cands)} candidates on {len(mass)} mass mailboxes")

    keys = directory_keys(suppliers)
    fresh, dupes = [], []
    seen = set()
    for c in cands:
        how, ref = match_directory(c, keys)
        k = c.get("registry_code") or registry.norm_name(c["name"])
        if k in seen:
            continue
        seen.add(k)
        if how:
            dupes.append({**c, "dup_by": how, "dup_of": ref})
        else:
            fresh.append(c)
    print(f"{len(dupes)} already in the directory, {len(fresh)} new")

    # Rank inside each zone x service: employees, then having a website + e-mail.
    def score(c):
        try:
            emp = int(c.get("employees") or 0)
        except ValueError:
            emp = 0
        own = any(web.reg_domain(e.split("@")[-1]) == _norm_url_domain(c.get("website")) for e in c["emails"])
        # Headcount alone ranked 1,200-person facility-management firms first;
        # the concierge sells to households. 1-60 staff is the sweet spot.
        size = 2 if 1 <= emp <= 60 else (1 if emp > 60 else 0)
        return (bool(c["website"]) + own, size, min(emp, 60))

    picked, seen_mail = [], set()
    for z in zones:
        for s in services:
            pool = sorted([c for c in fresh if c.get("zone_hint") == z and s in c["services"]], key=score, reverse=True)
            i = 0
            for c in pool:
                if i >= a.per_cell:
                    break
                if c not in picked and set(c["emails"]) & seen_mail:
                    continue  # same inbox as a company already shortlisted
                i += 1
                c.setdefault("rank", f"{s}#{i}")
                if c not in picked:
                    picked.append(c)
                    seen_mail |= set(c["emails"])
    print(f"{len(picked)} shortlisted (top {a.per_cell} per zone x service)")

    # Verify the website is alive and pick up an e-mail when the register has none.
    sites = _probe_all([c.get("website") for c in picked], a.out / "site-cache.json")
    print("geocoding…", flush=True)
    with ThreadPoolExecutor(4) as ex:
        geos = list(ex.map(lambda c: None if c.get("lat") else geocode_ee(c.get("adr_id"), c.get("address")),
                           picked))

    taken = {s.get("slug") for s in suppliers if s.get("slug")}
    rows = []
    for c, site, geo in zip(picked, sites, geos):
        site_emails = site.get("site_emails") or []
        email = (c["emails"] or [""])[0]
        if not email and site_emails:
            email = health._pick_email(site_emails, {web.reg_domain(site.get("site_host", ""))})
        lat, lng = c.get("lat"), c.get("lng")
        city, address = "", c.get("address", "")
        note = ""
        if geo:
            lat, lng, city = geo["lat"], geo["lng"], geo["city"]
            address = geo["address"] or address
            note = "registered office (In-ADS) — may not be the operating site"
        st = site.get("site_status", "")
        if st in ("dns_fail", "conn_error", "404", "410") or site.get("site_parked"):
            continue  # registered website is dead: not a good first contact
        name = re.sub(r"\s+(OÜ|AS|MTÜ|FIE|TÜ|UÜ)$", "", c["name"]).strip()
        rows.append({
            "approve": "", "zone": c.get("zone_hint", ""), "rank": c.get("rank", ""), "name": name,
            "serviceTypes": "|".join(sorted(c["services"])), "contactEmail": email,
            "contactPhone": (c["phones"] or [""])[0], "websiteUrl": site.get("site_final_url") or c.get("website", ""),
            "registryCode": c.get("registry_code", ""), "city": city, "address": address,
            "lat": f"{lat:.6f}" if lat else "", "lng": f"{lng:.6f}" if lng else "", "country": "EE",
            "slug": slugify(name, city, taken), "tagline": "",
            "site_status": st, "site_emails": " ".join(site_emails[:5]),
            "employees": c.get("employees", ""), "last_report": c.get("last_report", ""),
            "evidence": c.get("evidence", ""), "source": c["source"], "source_ref": c.get("source_ref", ""),
            "coords_note": note,
        })

    stamp = date.today().isoformat()
    path = a.out / f"discovery-review-{a.source}-{stamp}.csv"
    with open(path, "w", encoding="utf-8-sig", newline="") as fh:
        w = csv.DictWriter(fh, fieldnames=REVIEW_FIELDS)
        w.writeheader()
        w.writerows(rows)
    dpath = a.out / f"discovery-already-listed-{a.source}-{stamp}.csv"
    with open(dpath, "w", encoding="utf-8-sig", newline="") as fh:
        w = csv.DictWriter(fh, fieldnames=["dup_by", "dup_of", "name", "registry_code", "services", "website",
                                           "emails", "phones", "source_ref"])
        w.writeheader()
        for d in dupes:
            w.writerow({"dup_by": d["dup_by"], "dup_of": d["dup_of"], "name": d["name"],
                        "registry_code": d.get("registry_code", ""), "services": "|".join(sorted(d["services"])),
                        "website": d.get("website", ""), "emails": " ".join(d["emails"]),
                        "phones": " ".join(d["phones"]), "source_ref": d.get("source_ref", "")})
    print(f"wrote {path} ({len(rows)} rows)\nwrote {dpath} ({len(dupes)} rows)")


# ── approved rows -> admin bulk-import JSON ─────────────────────────────────
def _row_error(r):
    if not r.get("name"):
        return "name is required"
    slug = (r.get("slug") or "").strip().lower()
    if not re.fullmatch(r"[a-z0-9-]{2,80}", slug) or slug in RESERVED_SLUGS:
        return "bad slug"
    if not r.get("city"):
        return "city is required"
    country = (r.get("country") or "EE").upper()
    if country not in COUNTRY_BOUNDS:
        return f"unknown country {country}"
    try:
        lat, lng = float(r["lat"]), float(r["lng"])
    except (KeyError, ValueError):
        return "lat/lng required"
    a, b, c, d = COUNTRY_BOUNDS[country]
    if not (a <= lat <= b and c <= lng <= d):
        return f"coordinates outside {country} bounds"
    for f in ("name", "city", "address", "tagline"):
        if any(ch in (r.get(f) or "") for ch in "<>"):
            return f"{f} contains < or >"
    if r.get("websiteUrl") and not r["websiteUrl"].lower().startswith(("http://", "https://")):
        return "websiteUrl must be http(s)"
    if r.get("contactEmail") and not re.fullmatch(r"[^@\s]+@[^@\s]+\.[^@\s]+", r["contactEmail"]):
        return "bad contactEmail"
    svcs = [s for s in (r.get("serviceTypes") or "").split("|") if s]
    if not svcs or any(s not in CONSUMER_SLUGS for s in svcs):
        return f"serviceTypes must be from {'|'.join(CONSUMER_SLUGS)}"
    return None


def emit_import(a):
    review = a.review or max(a.out.glob("discovery-review-*.csv"), key=lambda p: p.stat().st_mtime)
    suppliers = json.loads((a.out / "suppliers.json").read_text(encoding="utf-8"))
    existing = {s.get("slug") for s in suppliers}
    rows = list(csv.DictReader(open(review, encoding="utf-8-sig")))
    approved = [r for r in rows if (r.get("approve") or "").strip().lower() in ("y", "yes", "x", "1", "true", "jah")]
    payload, errors = [], []
    for r in approved:
        err = _row_error(r)
        if not err and r["slug"].strip().lower() in existing:
            err = "slug already exists in the directory"
        if err:
            errors.append(f"{r.get('name')}: {err}")
            continue
        # Exactly the DirectorySupplierRow members — the endpoint 400s on any unknown field.
        row = {"name": r["name"].strip(), "slug": r["slug"].strip().lower(), "city": r["city"].strip(),
               "lat": float(r["lat"]), "lng": float(r["lng"]),
               "serviceTypes": [s for s in r["serviceTypes"].split("|") if s],
               "address": r.get("address") or None, "country": (r.get("country") or "EE").upper(),
               "websiteUrl": r.get("websiteUrl") or None, "contactEmail": r.get("contactEmail") or None,
               "contactPhone": r.get("contactPhone") or None, "tagline": r.get("tagline") or None,
               "registryCode": r.get("registryCode") or None}
        payload.append(row)
    # scripts/import-directory.ps1 globs "import-{COUNTRY}*.json" in -DataDir.
    imp_dir = a.out / "import"
    imp_dir.mkdir(exist_ok=True)
    print(f"{len(approved)} approved, {len(payload)} valid")
    for cc in sorted({p["country"] for p in payload}):
        out = imp_dir / f"import-{cc}-discovery-{date.today().isoformat()}.json"
        out.write_text(json.dumps([p for p in payload if p["country"] == cc], ensure_ascii=False, indent=1),
                       encoding="utf-8")
        print(f"  -> {out}")
    for e in errors:
        print("  REJECTED", e)
    print(f"Import (admin token required, a human runs this):\n"
          f"  ./scripts/import-directory.ps1 -Token (Get-Clipboard) -DataDir '{imp_dir}' -WhatIf")
