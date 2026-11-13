"""Ruumly supplier directory scan — read-only, report-first.

Nothing in this tool writes to the database, calls the admin API, or emails
anyone. It produces files a human reviews row by row; approved changes are then
applied by hand through the admin API (see the report in docs/ops/).

Stages (each reads the previous stage's output from --out, default
tools/supplier_scan/out/, which is gitignored):

  dump       production supplier rows + locations + outreach aggregates
             (needs `railway run --service Postgres -- ...`; READ ONLY session)
  registry   download the EE/LV/LT open-data company registers (free, public)
  health     website + registry + outreach checks -> health.csv with a proposed
             action per row (keep | fix_contact | remove | call)
  places     OPTIONAL, PAID: Google Places business_status for rows with a
             GooglePlaceId. Refuses to run without --i-accept-cost.
  discover   candidate providers missing from the directory -> discovery-review.csv
  report     markdown approval tables (for docs/ops/) from the latest CSVs
  emit-import  approved rows of discovery-review.csv -> JSON array for
             POST /api/admin/suppliers/bulk (validated locally first)
"""
from __future__ import annotations

import argparse
import os
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

DEFAULT_OUT = HERE / "out"


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--out", type=Path, default=DEFAULT_OUT)
    sub = p.add_subparsers(dest="cmd", required=True)

    sub.add_parser("dump")

    r = sub.add_parser("registry")
    r.add_argument("--countries", default="EE,LV,LT")

    h = sub.add_parser("health")
    h.add_argument("--countries", default="EE")
    h.add_argument("--towns", default="",
                   help="comma list of priority towns/counties, e.g. 'harjumaa,parnu,tartu'; empty = all rows")
    h.add_argument("--no-web", action="store_true", help="skip website fetches")
    h.add_argument("--workers", type=int, default=12)
    h.add_argument("--limit", type=int, default=0)

    pl = sub.add_parser("places")
    pl.add_argument("--i-accept-cost", action="store_true")
    pl.add_argument("--max-calls", type=int, default=0)

    d = sub.add_parser("discover")
    d.add_argument("--source", choices=["registry", "places", "file"], default="registry",
                   help="registry = EE e-Business Register open data (free, default); "
                        "places = Google Places Text Search (PAID, needs --i-accept-cost); "
                        "file = a hand-collected candidates CSV (--input)")
    d.add_argument("--input", type=Path)
    d.add_argument("--towns", default="harjumaa,parnu,tartu")
    d.add_argument("--services", default="warehouse,moving,trailer,cleaning,vanrental")
    d.add_argument("--per-cell", type=int, default=12, help="shortlist size per zone x service")
    d.add_argument("--min-report-year", type=int, default=2024,
                   help="registry source: drop companies with no annual report since this year")
    d.add_argument("--i-accept-cost", action="store_true")
    d.add_argument("--max-calls", type=int, default=0)

    sub.add_parser("report", help="markdown approval tables from the latest CSVs")

    pl2 = sub.add_parser("plan", help="approved health rows -> out/apply-plan.json (no API calls)")
    pl2.add_argument("--actions", default="fix_contact,remove")
    pl2.add_argument("--registry-codes", action="store_true")

    sub.add_parser("silent", help="providers that never answered lead mail -> out/silent-providers.csv")
    sub.add_parser("analytics", help="read-only demand/service aggregates -> out/analytics.md")
    e = sub.add_parser("emit-import")
    e.add_argument("--review", type=Path)

    a = p.parse_args(argv)
    a.out.mkdir(parents=True, exist_ok=True)

    if a.cmd == "dump":
        import db
        path = db.dump(a.out / "suppliers.json")
        print(f"wrote {path}")
    elif a.cmd == "registry":
        import registry
        registry.fetch_all(a.out / "registry", [c.strip().upper() for c in a.countries.split(",")] + ["EE_GENERAL"] * ("EE" in a.countries.upper()))
    elif a.cmd == "health":
        import health
        health.run(a)
    elif a.cmd == "places":
        import places
        places.run_status(a)
    elif a.cmd == "discover":
        import discover
        discover.run(a)
        # A probe thread stuck in a stalled TLS handshake would block interpreter
        # exit; all output is already flushed to disk at this point.
        sys.stdout.flush()
        os._exit(0)
    elif a.cmd == "plan":
        import plan
        plan.run(a)
    elif a.cmd == "silent":
        import plan
        plan.silent(a)
    elif a.cmd == "analytics":
        import analytics
        analytics.run(a)
    elif a.cmd == "report":
        import report
        report.run(a)
    elif a.cmd == "emit-import":
        import discover
        discover.emit_import(a)


if __name__ == "__main__":
    main()
