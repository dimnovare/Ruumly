"""silent-enriched.csv -> out/silent-mail.csv: one draft per inbox. NOTHING IS SENT.

    python -X utf8 tools/supplier_scan/scan.py mail

One row per distinct address (a chain's branches share one inbox and get one
mail). Rows approved for removal, B2B-only sites and rows without a valid
address are left out and listed. Sending is a separate, human step via
Resend from info@ruumly.eu, only for rows ticked approve=y.
"""
from __future__ import annotations

import csv
import re
from collections import Counter

# Manual findings from web search (2026-11-13), keyed by supplier-name prefix.
OVERRIDES = {
    "Aikerauto": {"send_to": "info@akrent.ee", "note": "same business as AK Rent (ssb.ee lists info@akrent.ee)"},
    "Hekkari Veod": {"send_to": "hekkariveod@gmail.com", "note": "address in the e-Business Register"},
    "Vervo": {"skip": "international freight forwarder, not household-facing"},
}

SVC = {
    "et": {"warehouse": "laoruum", "moving": "kolimine", "trailer": "haagiserent", "cleaning": "koristus",
           "vanrental": "kaubikurent", "packing": "kolimine"},
    "lv": {"warehouse": "noliktavas", "moving": "pārvākšanās", "trailer": "piekabju noma",
           "cleaning": "uzkopšana", "vanrental": "busiņu noma", "packing": "pārvākšanās"},
    "lt": {"warehouse": "sandėliai", "moving": "perkraustymas", "trailer": "priekabų nuoma", "cleaning": "valymas",
           "vanrental": "mikroautobusų nuoma", "packing": "perkraustymas"},
}

SUBJECT = {
    "et": "Tasuta kliendipäringud: {svc} ({city})",
    "lv": "Bezmaksas klientu pieprasījumi: {svc} ({city})",
    "lt": "Nemokamos klientų užklausos: {svc} ({city})",
}

BODY = {
    "et": """Tere{hello}!

Kirjutan Ruumlyst (ruumly.eu). Aitame inimestel, kes kolivad või vajavad lisaruumi, leida sobiva teenusepakkuja: laoruumi, kolimise, kaubiku- ja haagiserendi ning koristuse.

Klient kirjeldab meile oma vajaduse ning me saadame päringu otse sobivatele firmadele tema piirkonnas. Teie jaoks on päringud tasuta.

Oleme Teile juba saatnud {n} kliendipäringu{pl} ({svc}), kuid vastust me ei saanud. Võib-olla jäid kirjad rämpsposti.

Kuidas see toimib:
1. Saate e-kirja kliendi vajadusega: teenus, aeg ja asukoht.
2. Vastate lingi kaudu oma hinnaga või loobute. Kontot ega lepingut pole vaja.
3. Kui klient valib Teie pakkumise, anname Teile kohe teada.

Palun vastake sellele kirjale ühe sõnaga:
JAH: soovin päringuid
ÄRIKLIENDID: teenindame ainult firmasid
EI: ärge enam kirjutage

Lugupidamisega
Ruumly meeskond
info@ruumly.eu · ruumly.eu
Diip Solutions OÜ, Tallinn""",
    "lv": """Labdien!

Rakstu no Ruumly (ruumly.eu). Mēs palīdzam cilvēkiem, kuri pārceļas vai kuriem vajag papildu vietu, atrast piemērotu pakalpojumu sniedzēju: noliktavas, pārvākšanos, busiņu un piekabju nomu un uzkopšanu.

Klients mums apraksta savu vajadzību, un mēs nosūtām pieprasījumu tieši piemērotiem uzņēmumiem viņa reģionā. Jums pieprasījumi ir bez maksas.

Esam Jums jau nosūtījuši {n} klientu pieprasījumu{pl} ({svc}), bet atbildi nesaņēmām. Iespējams, vēstules nonāca mēstulēs.

Kā tas strādā:
1. Jūs saņemat e-pastu ar klienta vajadzību: pakalpojums, laiks un vieta.
2. Jūs atbildat caur saiti ar savu cenu vai atsakāties. Konts vai līgums nav vajadzīgs.
3. Ja klients izvēlas Jūsu piedāvājumu, mēs Jums uzreiz paziņojam.

Lūdzu, atbildiet uz šo vēstuli ar vienu vārdu:
JĀ: vēlos saņemt pieprasījumus
TIKAI UZŅĒMUMI: strādājam tikai ar uzņēmumiem
NĒ: vairs nerakstiet

Ar cieņu
Ruumly komanda
info@ruumly.eu · ruumly.eu""",
    "lt": """Laba diena!

Rašau iš Ruumly (ruumly.eu). Padedame žmonėms, kurie kraustosi arba kuriems reikia papildomos vietos, rasti tinkamą paslaugų teikėją: sandėlius, perkraustymą, mikroautobusų ir priekabų nuomą bei valymą.

Klientas mums aprašo savo poreikį, o mes užklausą siunčiame tiesiai tinkamoms įmonėms jo regione. Jums užklausos nemokamos.

Jau išsiuntėme Jums kliento užklausų: {n} ({svc}), tačiau atsakymo negavome. Galbūt laiškai pateko į šlamštą.

Kaip tai veikia:
1. Gaunate el. laišką su kliento poreikiu: paslauga, laikas ir vieta.
2. Atsakote per nuorodą su savo kaina arba atsisakote. Paskyros ar sutarties nereikia.
3. Jei klientas pasirenka Jūsų pasiūlymą, iš karto Jums pranešame.

Prašome atsakyti į šį laišką vienu žodžiu:
TAIP: noriu gauti užklausas
TIK ĮMONĖMS: dirbame tik su įmonėmis
NE: daugiau nerašykite

Pagarbiai
Ruumly komanda
info@ruumly.eu · ruumly.eu""",
}


def _brand(name):
    n = re.split(r"\s+[—–-]\s+", name)[0]
    n = re.sub(r"\s*\(.*?\)", "", n)
    n = re.sub(r"^(OÜ|AS|SIA|UAB|IĮ|MB)\s+|,?\s+(OÜ|AS|SIA|UAB|IĮ|MB)$", "", n.strip(), flags=re.I)
    return n.strip(' "')


def run(a):
    rows = list(csv.DictReader(open(a.out / "silent-enriched.csv", encoding="utf-8-sig")))
    health = {r["id"]: r for r in csv.DictReader(open(a.out / "health-ee-lt-lv.csv", encoding="utf-8-sig"))}
    groups, skipped = {}, []
    for r in rows:
        h = health.get(r["id"], {})
        ov = next((v for k, v in OVERRIDES.items() if r["name"].startswith(k)), {})
        if ov.get("skip"):
            skipped.append((r["name"], ov["skip"]))
            continue
        if h.get("action") == "remove":
            skipped.append((r["name"], "approved for removal"))
            continue
        if r["b2b_hits"] not in ("", "0"):
            skipped.append((r["name"], "site mentions business-only: check by hand"))
            continue
        # The founder approved the health fixes, so a proposed e-mail wins.
        to = ov.get("send_to") or (h.get("proposed_email") if h.get("action") == "fix_contact" else "") \
            or r["send_to"]
        if not to:
            skipped.append((r["name"], "no valid e-mail"))
            continue
        g = groups.setdefault(to.lower(), {"rows": [], "notes": []})
        g["rows"].append(r)
        if ov.get("note"):
            g["notes"].append(ov["note"])

    out = []
    for to, g in groups.items():
        rs = g["rows"]
        lang = rs[0]["language"] if rs[0]["language"] in BODY else "et"
        svcs = []
        for r in rs:
            for s in r["services"].split():
                t = SVC[lang].get(s)
                if t and t not in svcs:
                    svcs.append(t)
        svc = ", ".join(svcs[:3]) or SVC[lang]["warehouse"]
        city = next((r["city"] for r in rs if r["city"]), "")
        n = sum(int(r["lead_mails"] or 0) for r in rs)
        pl = {"et": "" if n == 1 else "t", "lv": "" if n == 1 else "s", "lt": ""}[lang]
        out.append({
            "approve": "", "send_to": to, "language": lang,
            "verdict": " ".join(sorted({r["verdict"] for r in rs})),
            "subject": SUBJECT[lang].format(svc=svc, city=city or {"et": "Eesti", "lv": "Latvija", "lt": "Lietuva"}[lang]),
            "body": BODY[lang].format(hello="", n=n, pl=pl, svc=svc),
            "supplier_ids": " ".join(r["id"] for r in rs), "names": " | ".join(r["name"] for r in rs),
            "lead_mails": n, "prices_seen": rs[0]["prices"][:300], "phones": rs[0]["phones_found"],
            "social": rs[0]["social"], "notes": "; ".join(g["notes"]),
        })
    out.sort(key=lambda o: (o["language"], o["verdict"] != "verified", -o["lead_mails"]))
    path = a.out / "silent-mail.csv"
    with open(path, "w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, fieldnames=list(out[0]))
        w.writeheader()
        w.writerows(out)
    print(f"wrote {path}: {len(out)} mails {Counter(o['language'] for o in out)}; "
          f"verdicts {Counter(o['verdict'] for o in out)}; skipped {len(skipped)}")
    for name, why in skipped:
        print(f"  skip {name}: {why}")
