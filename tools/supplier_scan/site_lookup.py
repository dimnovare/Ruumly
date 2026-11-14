"""Find a website for 'unverified' (no_site) suppliers via Google Places Text Search,
then crawl it and re-run the service evaluation. PAID: one Text Search (Enterprise
field websiteUri) per row. Read-only towards Ruumly: writes a CSV, changes nothing.

    python site_lookup.py --ids active.json --max-calls 300 --i-accept-cost

Output: out/places-lookup-<date>.csv
"""
from __future__ import annotations

import argparse
import csv
import datetime as dt
import json
import os
import re
import sys
import unicodedata
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

import requests

import services

OUT = Path(__file__).parent / "out"
FIELDS = "places.id,places.displayName,places.websiteUri,places.formattedAddress,places.businessStatus,places.nationalPhoneNumber"
REGION = {"EE": ("EE", "et"), "LV": ("LV", "lv"), "LT": ("LT", "lt")}
LEGAL = re.compile(r"\b(oü|ou|as|sia|uab|iį|ii|mb|ab|ltd|tü|fie|vši|ia)\b", re.I)
SOCIAL = re.compile(r"facebook\.com|instagram\.com|linkedin\.com|google\.com|goo\.gl", re.I)
PRICE_PER_1000_USD = 35.0   # Text Search Enterprise (websiteUri), before the monthly free tier


def norm(s: str) -> set[str]:
    s = unicodedata.normalize("NFKD", s or "").encode("ascii", "ignore").decode().lower()
    s = re.sub(r"\(.*?\)", " ", s)
    s = LEGAL.sub(" ", s)
    return {t for t in re.findall(r"[a-z0-9]{3,}", s)}


def name_match(ours: str, theirs: str) -> float:
    a, b = norm(ours), norm(theirs)
    return len(a & b) / len(a) if a else 0.0


def search(key: str, row: dict) -> dict:
    region, lang = REGION.get(row["country"], ("EE", "et"))
    q = f'{row["name"]} {row["city"]}'.strip()
    r = requests.post("https://places.googleapis.com/v1/places:searchText", timeout=30,
                      headers={"X-Goog-Api-Key": key, "X-Goog-FieldMask": FIELDS},
                      json={"textQuery": q, "regionCode": region, "languageCode": lang, "pageSize": 1})
    if r.status_code != 200:
        return {"error": f"{r.status_code} {r.text[:120]}"}
    places = r.json().get("places") or []
    return places[0] if places else {}


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--ids", required=True, help="JSON {id: website} of ACTIVE suppliers")
    ap.add_argument("--max-calls", type=int, default=300)
    ap.add_argument("--i-accept-cost", action="store_true")
    a = ap.parse_args()
    if not a.i_accept_cost:
        sys.exit("PAID API: pass --i-accept-cost")
    key = os.environ.get("GOOGLE_PLACES__API_KEY") or os.environ.get("GooglePlaces__ApiKey")
    if not key:
        sys.exit("no Places key in env")

    active = json.load(open(a.ids))
    rows = [r for r in csv.DictReader(open(OUT / "service-verification.csv", encoding="utf-8-sig"))
            if r["fit"] == "unverified" and r["verdict"] == "no_site" and r["id"] in active]
    if len(rows) > a.max_calls:
        sys.exit(f"{len(rows)} rows > --max-calls {a.max_calls}; projected ${len(rows) * PRICE_PER_1000_USD / 1000:.2f}")
    print(f"{len(rows)} rows; worst-case cost ${len(rows) * PRICE_PER_1000_USD / 1000:.2f}", flush=True)

    calls, found = 0, []
    for i, r in enumerate(rows, 1):
        p = search(key, r)
        calls += 1
        if "error" in p:
            print("  stop on API error:", p["error"], flush=True)
            r["_place"] = p
            break
        web = p.get("websiteUri") or ""
        score = name_match(r["name"], (p.get("displayName") or {}).get("text", ""))
        r["_place"], r["_score"] = p, score
        if web and score >= 0.5 and not SOCIAL.search(web):
            found.append(r)
        if i % 25 == 0:
            print(f"  {i}/{len(rows)} searched, {len(found)} sites", flush=True)

    with ThreadPoolExecutor(10) as ex:
        sites = dict(zip([id(r) for r in found], ex.map(lambda r: services.crawl(r["_place"]["websiteUri"]), found)))

    out = []
    for r in rows:
        p = r.get("_place") or {}
        site = sites.get(id(r))
        ev = services.evaluate(r["listed_services"].split(), site) if site else None
        row = {"id": r["id"], "name": r["name"], "country": r["country"], "city": r["city"],
               "listed_services": r["listed_services"], "email": r.get("email", ""),
               "place_name": (p.get("displayName") or {}).get("text", ""), "name_match": round(r.get("_score", 0), 2),
               "place_status": p.get("businessStatus", ""), "place_address": p.get("formattedAddress", ""),
               "place_phone": p.get("nationalPhoneNumber", ""), "place_website": p.get("websiteUri", ""),
               "used_website": r["_place"]["websiteUri"] if site else "",
               "new_fit": ev["fit"] if ev else "unverified", "new_verdict": ev["verdict"] if ev else
               ("closed" if p.get("businessStatus", "").startswith("CLOSED") else "no_site"),
               "confirmed": ev["confirmed"] if ev else "", "freight_hits": ev["freight_hits"] if ev else "",
               "household_hits": ev["household_hits"] if ev else ""}
        for svc in services.CONSUMER:
            row[f"quote_{svc}"] = (ev or {}).get("quotes", {}).get(svc, "")
        out.append(row)
    name = OUT / f"places-lookup-{dt.date.today():%Y-%m-%d}.csv"
    with open(name, "w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, fieldnames=list(out[0]))
        w.writeheader()
        w.writerows(out)
    from collections import Counter
    print(f"calls={calls}  worst-case ${calls * PRICE_PER_1000_USD / 1000:.2f}")
    print("new_fit:", Counter(o["new_fit"] for o in out))
    print("new_verdict:", Counter(o["new_verdict"] for o in out))
    print("wrote", name)


if __name__ == "__main__":
    main()
