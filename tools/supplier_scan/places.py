"""Google Places (New) — PAID, opt-in only.

Prices checked 2026-11-13 on developers.google.com/maps/billing-and-pricing/pricing
(USD, first tier, per 1,000 requests; free monthly cap per SKU in brackets):

  Place Details Pro         $17  (5,000 free)   <- businessStatus
  Text Search Pro           $32  (5,000 free)   <- displayName, formattedAddress, location, businessStatus
  Text Search Enterprise    $35  (1,000 free)   <- + websiteUri, nationalPhoneNumber

The free caps are per billing account per month and are SHARED with the
backend, which already calls Places for reviews (SuppliersController ->
placesService.GetReviewsAsync). Nobody can see that usage from here, so every
call is treated as billable: the tool prints its worst-case cost and refuses
to run without --i-accept-cost. Key: GOOGLE_PLACES_API_KEY (or the Railway
name GOOGLE_PLACES__API_KEY via `railway run --service Ruumly`).
"""
from __future__ import annotations

import csv
import json
import os
import sys
import time

import requests

PRICE = {"details_pro": 17.0, "text_pro": 32.0, "text_enterprise": 35.0}
TEXT_FIELDS = ",".join([
    "places.id", "places.displayName", "places.formattedAddress", "places.location", "places.businessStatus",
    "places.websiteUri", "places.nationalPhoneNumber", "places.internationalPhoneNumber", "places.types",
    "nextPageToken"])

# One Estonian query per service; Places matches et/en/ru listings with it.
QUERIES = {
    "warehouse": "hoiuruumi rent miniladu",
    "moving": "kolimisteenus",
    "trailer": "haagise rent",
    "cleaning": "koristusteenus",
    "vanrental": "kaubiku rent",
}
TOWN_CENTERS = {"harjumaa": (59.437, 24.745, 30000), "tallinn": (59.437, 24.745, 30000),
                "parnu": (58.385, 24.497, 20000), "tartu": (58.378, 26.729, 20000)}


def _key():
    k = os.environ.get("GOOGLE_PLACES_API_KEY") or os.environ.get("GOOGLE_PLACES__API_KEY")
    if not k:
        raise SystemExit("No GOOGLE_PLACES_API_KEY in the environment.")
    return k


def _gate(calls: int, sku: str, accepted: bool):
    cost = calls * PRICE[sku] / 1000
    print(f"worst case: {calls} x {sku} = ${cost:.2f} (before any free-tier allowance)")
    if not accepted:
        raise SystemExit("Refusing to call a paid API. Re-run with --i-accept-cost once approved.")


def run_status(a):
    rows = json.loads((a.out / "suppliers.json").read_text(encoding="utf-8"))
    todo = [r for r in rows if r.get("google_place_id") and not r.get("opt_out_at")]
    if a.max_calls:
        todo = todo[: a.max_calls]
    _gate(len(todo), "details_pro", a.i_accept_cost)
    key = _key()
    out = []
    for r in todo:
        resp = requests.get(f"https://places.googleapis.com/v1/places/{r['google_place_id']}",
                            headers={"X-Goog-Api-Key": key, "X-Goog-FieldMask": "id,displayName,businessStatus"},
                            timeout=20)
        j = resp.json() if resp.ok else {"error": resp.status_code}
        out.append({"id": r["id"], "name": r["name"], "place_id": r["google_place_id"],
                    "business_status": j.get("businessStatus", j.get("error", ""))})
        time.sleep(0.2)
    path = a.out / "places-status.csv"
    with open(path, "w", encoding="utf-8-sig", newline="") as fh:
        w = csv.DictWriter(fh, fieldnames=["id", "name", "place_id", "business_status"])
        w.writeheader()
        w.writerows(out)
    print(f"wrote {path}")


def text_search(towns, services, max_calls, accepted, pages=3):
    """-> list of raw candidate dicts (normalised to the discover.py candidate shape)."""
    planned = len(towns) * len(services) * pages
    if max_calls:
        planned = min(planned, max_calls)
    _gate(planned, "text_enterprise", accepted)
    key = _key()
    calls, found = 0, {}
    for town in towns:
        lat, lng, radius = TOWN_CENTERS[town]
        for svc in services:
            body = {"textQuery": QUERIES[svc], "languageCode": "et", "regionCode": "EE", "pageSize": 20,
                    "locationBias": {"circle": {"center": {"latitude": lat, "longitude": lng}, "radius": radius}}}
            for _ in range(pages):
                if max_calls and calls >= max_calls:
                    break
                resp = requests.post("https://places.googleapis.com/v1/places:searchText", json=body, timeout=30,
                                     headers={"X-Goog-Api-Key": key, "X-Goog-FieldMask": TEXT_FIELDS})
                calls += 1
                if not resp.ok:
                    print(f"  {town}/{svc}: HTTP {resp.status_code} {resp.text[:200]}", file=sys.stderr)
                    break
                j = resp.json()
                for p in j.get("places", []):
                    c = found.setdefault(p["id"], {
                        "source": "google_places", "source_ref": p["id"], "name": p["displayName"]["text"],
                        "address": p.get("formattedAddress", ""), "lat": p["location"]["latitude"],
                        "lng": p["location"]["longitude"], "website": p.get("websiteUri", ""),
                        "phones": [x for x in (p.get("internationalPhoneNumber"), p.get("nationalPhoneNumber")) if x][:1],
                        "emails": [], "registry_code": "", "services": set(), "zone_hint": town,
                        "evidence": f"Google Places {p.get('businessStatus', '')} types={','.join(p.get('types', [])[:4])}",
                        "business_status": p.get("businessStatus", "")})
                    c["services"].add(svc)
                tok = j.get("nextPageToken")
                if not tok:
                    break
                body = {**body, "pageToken": tok}
                time.sleep(1.5)
    print(f"places: {calls} calls, {len(found)} distinct places")
    return [c for c in found.values() if c["business_status"] in ("", "OPERATIONAL")]
