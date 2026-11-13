"""EE / LV / LT company-register open data: download + lookup.

All three are free public bulk files, no key needed:

  EE  e-Business Register "lihtandmed" (simple data) zip, ';'-separated.
      Contains ONLY live entries: status R (registered), L (in liquidation),
      N (bankrupt). A code that is NOT in the file has been deleted (K) — or
      the code on our row is wrong. Either way it needs a human.
  LV  Uzņēmumu reģistrs register.csv on data.gov.lv, ';'-separated.
      `terminated` date + `closed` = L (liquidated / struck off) or R
      (reorganised away); blank = live.
  LT  Registrų centras JAR: JAR_IREGISTRUOTI (live, with legal status code)
      and JAR_ISREGISTRUOTI (struck off), '|'-separated.

The parsed index is small (only the fields we use) and cached as JSON next to
the raw files, so `health` does not re-parse 230 MB of CSV on every run.
"""
from __future__ import annotations

import csv
import io
import json
import re
import sys
import unicodedata
import urllib.request
import zipfile
from pathlib import Path

csv.field_size_limit(10_000_000)

SOURCES = {
    "EE": [("ee.zip", "https://avaandmed.ariregister.rik.ee/sites/default/files/avaandmed/"
                      "ettevotja_rekvisiidid__lihtandmed.csv.zip")],
    # General data: EMTAK activity codes, registered e-mail / website / phone and
    # the latest annual report (employees). ~230 MB zip, 4.6 GB JSON — streamed.
    "EE_GENERAL": [("ee_yld.json.zip", "https://avaandmed.ariregister.rik.ee/sites/default/files/avaandmed/"
                                       "ettevotja_rekvisiidid__yldandmed.json.zip")],
    "LV": [("lv.csv", "https://data.gov.lv/dati/dataset/4de9697f-850b-45ec-8bba-61fa09ce932f/"
                      "resource/25e80bf3-f107-4ab4-89ef-251b5b9374e9/download/register.csv")],
    "LT": [("lt_reg.csv", "https://www.registrucentras.lt/aduomenys/?byla=JAR_IREGISTRUOTI.csv"),
           ("lt_unreg.csv", "https://www.registrucentras.lt/aduomenys/?byla=JAR_ISREGISTRUOTI.csv")],
}

# Normalised verdicts every country maps onto.
ACTIVE, LIQUIDATION, BANKRUPT, DELETED, REORG, NOT_FOUND = (
    "active", "liquidation", "bankruptcy", "deleted", "reorganisation", "not_found")

LT_STATUS = {
    "0": ACTIVE,
    "7": LIQUIDATION, "9": LIQUIDATION,
    "26": BANKRUPT, "5": BANKRUPT, "6": BANKRUPT,
    "1": REORG, "2": REORG, "3": REORG, "4": REORG, "8": REORG,
}

UA = "RuumlyDirectoryScan/1.0 (+https://ruumly.eu)"


def fetch_all(dest: Path, countries):
    dest.mkdir(parents=True, exist_ok=True)
    for c in countries:
        for fname, url in SOURCES[c]:
            target = dest / fname
            print(f"[{c}] downloading {url}")
            req = urllib.request.Request(url, headers={"User-Agent": UA})
            with urllib.request.urlopen(req, timeout=600) as resp, open(target, "wb") as fh:
                while chunk := resp.read(1 << 20):
                    fh.write(chunk)
            print(f"    -> {target} ({target.stat().st_size // 1024} KiB)")
    build_index(dest)
    if (dest / "ee_yld.json.zip").exists():
        build_ee_general(dest)


# ── name normalisation (for rows that carry no registry code) ───────────────
LEGAL_FORMS = {
    "ou", "oü", "as", "mtu", "fie", "tu", "uü", "tü",
    "sia", "ik", "as", "ps", "ks",
    "uab", "ab", "mb", "ii", "iį", "vsi", "všį", "ub",
}


def norm_name(name: str) -> str:
    s = unicodedata.normalize("NFKD", name or "").encode("ascii", "ignore").decode().lower()
    s = re.sub(r"[\"'«»“”„()]", " ", s)
    s = re.sub(r"\b(osauhing|aktsiaselts|sabiedriba ar ierobezotu atbildibu|uzdaroji akcine bendrove|"
               r"mazoji bendrija|individuali imone|akcine bendrove)\b", " ", s)
    toks = [t for t in re.split(r"[^a-z0-9]+", s) if t and t not in LEGAL_FORMS]
    return " ".join(toks)


def _put_name(idx, name, code):
    k = norm_name(name)
    if not k:
        return
    prev = idx.get(k)
    idx[k] = code if prev in (None, code) else "*ambiguous*"


def build_index(dest: Path):
    out = {}
    if (dest / "ee.zip").exists():
        codes, names = {}, {}
        z = zipfile.ZipFile(dest / "ee.zip")
        with io.TextIOWrapper(z.open(z.namelist()[0]), encoding="utf-8-sig") as fh:
            for d in csv.DictReader(fh, delimiter=";"):
                st = {"R": ACTIVE, "L": LIQUIDATION, "N": BANKRUPT}.get(d["ettevotja_staatus"], d["ettevotja_staatus"])
                codes[d["ariregistri_kood"]] = {
                    "name": d["nimi"], "status": st, "status_raw": d["ettevotja_staatus_tekstina"],
                    "address": d["ads_normaliseeritud_taisaadress"] or d["ettevotja_aadress"],
                    "link": d["teabesysteemi_link"]}
                _put_name(names, d["nimi"], d["ariregistri_kood"])
        out["EE"] = {"codes": codes, "names": names, "absent_means": DELETED}
    if (dest / "lv.csv").exists():
        codes, names = {}, {}
        with open(dest / "lv.csv", encoding="utf-8-sig", newline="") as fh:
            for d in csv.DictReader(fh, delimiter=";"):
                closed = (d.get("closed") or "").strip()
                st = ACTIVE if not closed else {"L": DELETED, "R": REORG}.get(closed, closed)
                codes[d["regcode"]] = {
                    "name": d["name"], "status": st,
                    "status_raw": f"closed={closed or '-'} terminated={d.get('terminated') or '-'}",
                    "address": d["address"], "link": f"https://info.ur.gov.lv/#/legal-entity/{d['regcode']}"}
                if not closed:
                    _put_name(names, d["name"], d["regcode"])
        out["LV"] = {"codes": codes, "names": names, "absent_means": NOT_FOUND}
    if (dest / "lt_reg.csv").exists():
        codes, names = {}, {}
        with open(dest / "lt_reg.csv", encoding="utf-8-sig", newline="") as fh:
            for d in csv.DictReader(fh, delimiter="|"):
                codes[d["ja_kodas"]] = {
                    "name": d["ja_pavadinimas"], "status": LT_STATUS.get(d["stat_kodas"], d["stat_kodas"]),
                    "status_raw": f"{d['stat_pavadinimas']} (since {d['stat_data_nuo']})",
                    "address": d["adresas"], "link": ""}
                _put_name(names, d["ja_pavadinimas"], d["ja_kodas"])
        if (dest / "lt_unreg.csv").exists():
            with open(dest / "lt_unreg.csv", encoding="utf-8-sig", newline="") as fh:
                for d in csv.DictReader(fh, delimiter="|"):
                    codes.setdefault(d["ja_kodas"], {
                        "name": d["ja_pavadinimas"], "status": DELETED,
                        "status_raw": f"išregistruotas {d['isreg_data']}", "address": d["adresas"], "link": ""})
        out["LT"] = {"codes": codes, "names": names, "absent_means": NOT_FOUND}
    (dest / "index.json").write_text(json.dumps(out, ensure_ascii=False), encoding="utf-8")
    print(f"index: " + ", ".join(f"{c}={len(v['codes'])}" for c, v in out.items()))
    return out


def _iter_pretty_json_array(fh):
    """Stream objects out of the register's pretty-printed top-level array
    (each element opens with '    {' and closes with '    }' at 4-space indent)
    without holding 4.6 GB in memory or needing ijson."""
    buf = None
    for line in fh:
        if line.startswith("    {"):
            buf = [line]
            continue
        if buf is not None:
            buf.append(line)
            if line.startswith("    }"):
                yield json.loads("".join(buf).rstrip().rstrip(","))
                buf = None


def _current(items):
    return [i for i in (items or []) if not i.get("lopp_kpv")]


def build_ee_general(dest: Path) -> Path:
    """-> ee_general.jsonl: one compact line per company (all statuses)."""
    src = dest / "ee_yld.json.zip"
    out = dest / "ee_general.jsonl"
    z = zipfile.ZipFile(src)
    n = 0
    with io.TextIOWrapper(z.open(z.namelist()[0]), encoding="utf-8") as fh, \
            open(out, "w", encoding="utf-8") as w:
        for o in _iter_pretty_json_array(fh):
            y = o.get("yldandmed") or {}
            addr = (_current(y.get("aadressid")) or [{}])[0]
            contacts = _current(y.get("sidevahendid"))
            reports = sorted(y.get("info_majandusaasta_aruannetest") or [],
                             key=lambda r: r.get("majandusaasta_perioodi_lopp_kpv", "")[-4:], reverse=True)
            last = reports[0] if reports else {}
            rec = {
                "code": str(o["ariregistri_kood"]), "name": o.get("nimi"),
                "status": y.get("staatus"), "deleted": y.get("kustutamise_kpv"),
                "address": addr.get("aadress_ads__ads_normaliseeritud_taisaadress") or addr.get("tanav_maja_korter"),
                "ehak": addr.get("ehak_nimetus"),
                "adr_id": addr.get("aadress_ads__adr_id"),
                "emtak": [[t.get("emtak_kood"), t.get("emtak_tekstina"), bool(t.get("on_pohitegevusala"))]
                          for t in _current(y.get("teatatud_tegevusalad"))],
                "report_emtak": [last.get("tegevusala_emtak_kood"), last.get("tegevusala_emtak_tekstina")] if last else None,
                "last_report": (last.get("majandusaasta_perioodi_lopp_kpv") or "")[-4:],
                "employees": last.get("tootajate_arv"),
                "email": [c["sisu"] for c in contacts if c.get("liik") == "EMAIL"],
                "www": [c["sisu"] for c in contacts if c.get("liik") == "WWW"],
                "phone": [c["sisu"] for c in contacts if c.get("liik") in ("MOB", "TEL")],
            }
            w.write(json.dumps(rec, ensure_ascii=False) + "\n")
            n += 1
    print(f"ee_general: {n} companies -> {out}")
    return out


def iter_ee_general(dest: Path):
    p = dest / "ee_general.jsonl"
    if not p.exists():
        build_ee_general(dest)
    with open(p, encoding="utf-8") as fh:
        for line in fh:
            yield json.loads(line)


def load_index(dest: Path):
    p = dest / "index.json"
    if not p.exists():
        if any(dest.glob("*.csv")) or any(dest.glob("*.zip")):
            return build_index(dest)
        print("no registry data — run `scan.py registry` first; registry checks skipped", file=sys.stderr)
        return {}
    return json.loads(p.read_text(encoding="utf-8"))


def lookup(index, country: str, code: str | None, name: str):
    """-> dict(match=code|name|none, code, status, status_raw, reg_name, address, link)."""
    reg = index.get(country)
    if not reg:
        return {"match": "no_data"}
    code = re.sub(r"\D", "", code or "")
    if code:
        hit = reg["codes"].get(code)
        if hit:
            return {"match": "code", "code": code, **_flat(hit)}
        return {"match": "code", "code": code, "status": reg["absent_means"],
                "status_raw": "code not present in the open-data file"}
    k = norm_name(name)
    c = reg["names"].get(k) if k else None
    if c and c != "*ambiguous*":
        return {"match": "name", "code": c, **_flat(reg["codes"][c])}
    return {"match": "ambiguous" if c else "none"}


def _flat(hit):
    return {"status": hit["status"], "status_raw": hit["status_raw"], "reg_name": hit["name"],
            "address": hit["address"], "link": hit.get("link", "")}
