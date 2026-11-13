"""Turn an approved health CSV into an explicit admin-API change plan.

Writes out/apply-plan.json: a list of {id, name, op, method, path, body,
before} entries. The plan is a FILE for a human to read before anything is
sent; applying it is a separate, deliberate step (apply.js, run in a
logged-in ruumly.eu admin tab), and every op is read back afterwards.

Which rows go in:
  --actions fix_contact,remove   rows with those actions
  --registry-codes               also the unique-name-match registry codes on
                                 keep rows (metadata only)
Opt-outs and inactive rows are never included, whatever the flags say.
"""
from __future__ import annotations

import csv
import json
import re
from collections import Counter


def run(a):
    rows = list(csv.DictReader(open(a.out / "health-ee-lt-lv.csv", encoding="utf-8-sig")))
    suppliers = {s["id"]: s for s in json.loads((a.out / "suppliers.json").read_text(encoding="utf-8"))}
    actions = {x.strip() for x in a.actions.split(",") if x.strip()}
    existing_codes = {re.sub(r"\D", "", s["registry_code"]) for s in suppliers.values() if s.get("registry_code")}

    # RegistryCode is unique in the DB: a code proposed for two rows (a chain's
    # branches) or already held by another row would fail — leave those out.
    proposed = Counter(r["proposed_registry_code"] for r in rows if r["proposed_registry_code"])
    plan, skipped = [], []
    for r in rows:
        s = suppliers.get(r["id"])
        if not s or s.get("opt_out_at") or not s.get("is_active"):
            continue
        before = {"contactEmail": s.get("email"), "contactPhone": s.get("phone"),
                  "websiteUrl": s.get("website"), "registryCode": s.get("registry_code")}
        if r["action"] == "remove" and "remove" in actions:
            plan.append({"id": r["id"], "name": r["name"], "op": "remove", "method": "DELETE",
                         "path": f"/admin/suppliers/{r['id']}", "body": None, "before": before,
                         "why": r["reasons"]})
            continue
        body = {}
        if r["action"] == "fix_contact" and "fix_contact" in actions:
            if r["proposed_email"]:
                body["contactEmail"] = r["proposed_email"]
            if r["proposed_phone"]:
                body["contactPhone"] = r["proposed_phone"]
            if r["proposed_website"] and r["proposed_website"] != "(clear)":
                body["websiteUrl"] = r["proposed_website"]
        code = r["proposed_registry_code"]
        if code and (r["action"] == "fix_contact" or a.registry_codes):
            if proposed[code] > 1 or code in existing_codes:
                skipped.append(f"{r['name']}: code {code} shared/already used")
            else:
                body["registryCode"] = code
        if body:
            plan.append({"id": r["id"], "name": r["name"],
                         "op": "fix_contact" if r["action"] == "fix_contact" else "registry_code",
                         "method": "PATCH", "path": f"/admin/suppliers/{r['id']}", "body": body,
                         "before": before, "why": r["reasons"]})

    path = a.out / "apply-plan.json"
    path.write_text(json.dumps(plan, ensure_ascii=False, indent=1), encoding="utf-8")
    print(f"wrote {path}: {Counter(p['op'] for p in plan)}")
    for x in skipped:
        print("  skipped", x)


def silent(a):
    """Providers that got lead mail and never answered -> out/silent-providers.csv.

    Input for the re-engagement mail in docs/ops (sent by a human via Resend,
    never by this tool). Opt-outs, inactive rows and unusable e-mails are left out.
    """
    suppliers = json.loads((a.out / "suppliers.json").read_text(encoding="utf-8"))
    lang = {"EE": "et", "LV": "lv", "LT": "lt"}
    rows = []
    for s in suppliers:
        o = s.get("outreach")
        if (not o or not s.get("is_active") or s.get("opt_out_at") or s.get("email_unusable")
                or not (s.get("email") or "").strip()):
            continue
        if (o.get("quoted") or 0) + (o.get("declined") or 0):
            continue
        rows.append({"id": s["id"], "name": s["name"], "country": s["country"],
                     "language": lang.get(s["country"], "en"), "email": s["email"],
                     "services": " ".join(s.get("services") or []),
                     "lead_mails": o["sent"], "last_sent": (o.get("last_sent") or "")[:10],
                     "approve": ""})
    rows.sort(key=lambda r: (r["country"], -r["lead_mails"], r["name"]))
    path = a.out / "silent-providers.csv"
    with open(path, "w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0]))
        w.writeheader()
        w.writerows(rows)
    print(f"wrote {path}: {len(rows)} rows {Counter(r['country'] for r in rows)}")
