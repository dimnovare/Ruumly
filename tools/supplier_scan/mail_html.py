"""Branded HTML version of the silent-provider letter (layout of
docs/ops/requests/2026-11-13-parnu-autorent-claim-letter.html).

    python -X utf8 tools/supplier_scan/scan.py mail-html [--samples]

Writes out/silent-mail-html/<n>.html per remaining inbox and, with --samples,
one finished sample per language to out/silent-mail-samples/. NOTHING IS SENT:
the founder approves one exact version first.
"""
from __future__ import annotations

import csv
import html
import re
from pathlib import Path

from mail import SVC, _brand

TEAL = "#00897B"

T = {
    "et": {
        "tag": "partneritele",
        "subject": "Tasuta kliendipäringud ettevõttele {brand}",
        "hello": "Tere!",
        "intro": "Kirjutame Ruumlyst seoses ettevõttega <b>{brand}</b>.",
        "what": "Ruumly (ruumly.eu) aitab inimestel, kes kolivad või vajavad lisaruumi, leida usaldusväärse "
                "teenusepakkuja: laoruumi, kolimisabi, haagise- ja kaubikurendi või koristuse. Klient kirjeldab oma "
                "vajaduse ja me edastame päringu otse sobivatele ettevõtetele tema piirkonnas.",
        "free": "<b>Päringud on Teile tasuta.</b> Te ei pea looma kontot ega sõlmima lepingut: vastate ainult "
                "päringutele, mis Teile sobivad.",
        "sent": "Oleme Teile juba edastanud {count} ({svc}), kuid vastust me ei saanud. Võimalik, et kirjad jäid "
                "rämpsposti kausta.",
        "how_h": "Kuidas see toimib:",
        "how": ["Saate e-kirja kliendi vajadusega: teenus, aeg ja asukoht.",
                "Lingi kaudu saadate oma hinna või loobute päringust.",
                "Kui klient valib Teie pakkumise, anname Teile kohe teada."],
        "ask": "Et saaksime Teile edaspidi saata ainult sobivaid päringuid, palun vastake sellele kirjale ühe sõnaga:",
        "answers": [("JAH", "soovin päringuid"), ("ÄRIKLIENDID", "teenindame ainult ettevõtteid"),
                    ("EI", "palun ärge rohkem kirjutage")],
        "button": "Loe lähemalt", "url": "https://ruumly.eu/et/provider",
        "bye": "Lugupidamisega", "team": "Ruumly meeskond",
        "line": "Hoiustamine, kolimine, koristus, haagise- ja kaubikurent",
        "footer": "Ruumly · Diip Solutions OÜ · registrikood 17527757 · Uus-Sadama tn 15-2, 10120 Tallinn<br>"
                  "Saite selle kirja, sest {brand} on Ruumly teenusepakkujate kataloogis. Kui Te ei soovi rohkem "
                  "kirju, vastake „EI\".",
    },
    "lv": {
        "tag": "partneriem",
        "subject": "Bezmaksas klientu pieprasījumi uzņēmumam {brand}",
        "hello": "Labdien!",
        "intro": "Rakstām no Ruumly saistībā ar uzņēmumu <b>{brand}</b>.",
        "what": "Ruumly (ruumly.eu) palīdz cilvēkiem, kuri pārceļas vai kuriem vajag papildu vietu, atrast uzticamu "
                "pakalpojumu sniedzēju: noliktavu, palīdzību pārvākšanās laikā, piekabju un busiņu nomu vai "
                "uzkopšanu. Klients apraksta savu vajadzību, un mēs pārsūtām pieprasījumu tieši piemērotiem "
                "uzņēmumiem viņa reģionā.",
        "free": "<b>Pieprasījumi Jums ir bez maksas.</b> Nav jāveido konts vai jāslēdz līgums: Jūs atbildat tikai uz "
                "tiem pieprasījumiem, kas Jums der.",
        "sent": "Esam Jums jau pārsūtījuši {count} ({svc}), taču atbildi nesaņēmām. Iespējams, vēstules nonāca "
                "mēstuļu mapē.",
        "how_h": "Kā tas notiek:",
        "how": ["Jūs saņemat e-pastu ar klienta vajadzību: pakalpojums, laiks un vieta.",
                "Caur saiti norādāt savu cenu vai atsakāties no pieprasījuma.",
                "Ja klients izvēlas Jūsu piedāvājumu, mēs Jums uzreiz paziņojam."],
        "ask": "Lai turpmāk sūtītu Jums tikai piemērotus pieprasījumus, lūdzu, atbildiet uz šo vēstuli ar vienu vārdu:",
        "answers": [("JĀ", "vēlos saņemt pieprasījumus"), ("TIKAI UZŅĒMUMI", "strādājam tikai ar uzņēmumiem"),
                    ("NĒ", "lūdzu, vairs nerakstiet")],
        "button": "Uzzināt vairāk", "url": "https://ruumly.eu/lv/provider",
        "bye": "Ar cieņu", "team": "Ruumly komanda",
        "line": "Noliktavas, pārvākšanās, uzkopšana, piekabju un busiņu noma",
        "footer": "Ruumly · Diip Solutions OÜ · reģistrācijas Nr. 17527757 · Uus-Sadama tn 15-2, 10120 Tallina, "
                  "Igaunija<br>Jūs saņēmāt šo vēstuli, jo {brand} ir iekļauts Ruumly pakalpojumu sniedzēju "
                  "katalogā. Ja nevēlaties saņemt vēstules, atbildiet „NĒ\".",
    },
    "lt": {
        "tag": "partneriams",
        "subject": "Nemokamos klientų užklausos įmonei {brand}",
        "hello": "Laba diena!",
        "intro": "Rašome iš Ruumly dėl įmonės <b>{brand}</b>.",
        "what": "Ruumly (ruumly.eu) padeda žmonėms, kurie kraustosi arba kuriems reikia papildomos vietos, rasti "
                "patikimą paslaugų teikėją: sandėliavimo, perkraustymo, priekabų ir mikroautobusų nuomos ar valymo "
                "paslaugas. Klientas aprašo savo poreikį, o mes užklausą perduodame tiesiogiai tinkamoms įmonėms "
                "jo regione.",
        "free": "<b>Užklausos Jums nemokamos.</b> Nereikia kurti paskyros ar sudaryti sutarties: atsakote tik į Jums "
                "tinkamas užklausas.",
        "sent": "Jau perdavėme Jums {count} ({svc}), tačiau atsakymo negavome. Galbūt laiškai pateko į šlamšto "
                "aplanką.",
        "how_h": "Kaip tai veikia:",
        "how": ["Gaunate el. laišką su kliento poreikiu: paslauga, laikas ir vieta.",
                "Per nuorodą nurodote savo kainą arba atsisakote užklausos.",
                "Jei klientas pasirenka Jūsų pasiūlymą, iškart Jums pranešame."],
        "ask": "Kad ateityje siųstume Jums tik tinkamas užklausas, prašome atsakyti į šį laišką vienu žodžiu:",
        "answers": [("TAIP", "noriu gauti užklausas"), ("TIK ĮMONĖMS", "dirbame tik su įmonėmis"),
                    ("NE", "prašome daugiau nerašyti")],
        "button": "Sužinoti daugiau", "url": "https://ruumly.eu/lt/provider",
        "bye": "Pagarbiai", "team": "Ruumly komanda",
        "line": "Sandėliavimas, perkraustymas, valymas, priekabų ir mikroautobusų nuoma",
        "footer": "Ruumly · Diip Solutions OÜ · įmonės kodas 17527757 · Uus-Sadama tn 15-2, 10120 Talinas, Estija<br>"
                  "Gavote šį laišką, nes {brand} yra įtraukta į Ruumly paslaugų teikėjų katalogą. Jei nenorite gauti "
                  "laiškų, atsakykite „NE\".",
    },
}


def count_phrase(lang, n):
    if lang == "et":
        return "ühe kliendipäringu" if n == 1 else f"{n} kliendipäringut"
    if lang == "lv":
        return "vienu klienta pieprasījumu" if n == 1 else (
            f"{n} klientu pieprasījumu" if n % 10 == 1 and n % 100 != 11 else f"{n} klientu pieprasījumus")
    if n == 1:
        return "vieną kliento užklausą"
    return f"{n} klientų užklausas" if 2 <= n % 10 <= 9 and not 11 <= n % 100 <= 19 else f"{n} klientų užklausų"


def render(lang, brand, n, svc):
    t = T[lang]
    e = html.escape
    b = e(brand)
    p = 'style="margin:0 0 14px;"'
    how = "".join(f'<li style="margin-bottom:6px;">{e(x)}</li>' for x in t["how"])
    answers = "".join(
        f'<tr><td style="padding:6px 14px 6px 0;font-weight:700;color:{TEAL};white-space:nowrap;">{e(k)}</td>'
        f'<td style="padding:6px 0;">{e(v)}</td></tr>' for k, v in t["answers"])
    body = f"""<table width="100%" cellpadding="0" cellspacing="0" style="max-width:600px;background:#ffffff;border:1px solid #ECEFF1;border-radius:8px;font-family:Arial,Helvetica,sans-serif;">
<tr><td style="background:{TEAL};padding:18px 28px;border-radius:8px 8px 0 0;color:#ffffff;font-size:22px;font-weight:700;letter-spacing:0.5px;">Ruumly<span style="font-size:13px;font-weight:400;opacity:0.9;">&nbsp;&nbsp;|&nbsp;&nbsp;{t["tag"]}</span></td></tr>
<tr><td style="padding:28px;color:#455A64;font-size:15px;line-height:1.6;">
<p {p}>{t["hello"]}</p>
<p {p}>{t["intro"].format(brand=b)}</p>
<p {p}>{e(t["what"])}</p>
<p {p}>{t["free"]}</p>
<p style="margin:0 0 16px;background:#F1F8F7;border-left:3px solid {TEAL};padding:10px 14px;">{e(t["sent"].format(count=count_phrase(lang, n), svc=svc))}</p>
<p style="margin:0 0 8px;"><b>{e(t["how_h"])}</b></p>
<ol style="margin:0 0 18px;padding-left:20px;">{how}</ol>
<p style="margin:0 0 8px;">{e(t["ask"])}</p>
<table cellpadding="0" cellspacing="0" style="margin:0 0 20px;">{answers}</table>
<table cellpadding="0" cellspacing="0" style="margin:0 0 22px;"><tr><td style="background:{TEAL};border-radius:6px;padding:12px 22px;"><a href="{t["url"]}" style="color:#ffffff;text-decoration:none;font-weight:700;font-size:15px;">{e(t["button"])}</a></td></tr></table>
<p style="margin:0 0 4px;">{e(t["bye"])}</p>
<table cellpadding="0" cellspacing="0" style="margin-top:8px;"><tr>
<td style="border-left:3px solid {TEAL};padding:2px 0 2px 12px;font-size:14px;line-height:1.5;color:#455A64;">
<span style="font-size:16px;font-weight:700;color:{TEAL};">{e(t["team"])}</span><br>
{e(t["line"])}<br>
<a href="mailto:info@ruumly.eu" style="color:{TEAL};text-decoration:none;">info@ruumly.eu</a> &nbsp;·&nbsp; <a href="https://ruumly.eu" style="color:{TEAL};text-decoration:none;">ruumly.eu</a>
</td></tr></table>
</td></tr>
<tr><td style="background:#f5f5f5;padding:14px 28px;border-radius:0 0 8px 8px;color:#78909C;font-size:12px;line-height:1.5;">{t["footer"].format(brand=b)}</td></tr>
</table>"""
    return t["subject"].format(brand=brand), body


def run(a):
    rows = list(csv.DictReader(open(a.out / "silent-mail.csv", encoding="utf-8-sig")))
    sent_p = a.out / "silent-mail-sent.csv"
    sent = {r["send_to"].lower() for r in csv.DictReader(open(sent_p, encoding="utf-8-sig"))} if sent_p.exists() else set()
    fit = {}
    sv = a.out / "service-verification.csv"
    if sv.exists():
        fit = {r["id"]: r["fit"] for r in csv.DictReader(open(sv, encoding="utf-8-sig"))}
    d = a.out / "silent-mail-html"
    d.mkdir(exist_ok=True)
    out, samples = [], {}
    for r in rows:
        if r["send_to"].lower() in sent:
            continue
        lang = r["language"]
        brand = _brand(r["names"].split(" | ")[0])
        # Directory names sometimes carry a branch address ("X degalinė, Sarakiškės, Vytauto g. 61").
        brand = re.sub(r",\s[^,]*\d.*$", "", brand)
        brand = re.sub(r",\s*\w+,?$", "", brand) if re.search(r"degalinė|DUS", brand) else brand
        brand = re.sub(r"\s+(degalinė|DUS)\b.*$", "", brand)
        svc = r["subject"].split(": ", 1)[-1].rsplit(" (", 1)[0]
        subject, body = render(lang, brand, int(r["lead_mails"] or 0), svc)
        fits = sorted({fit.get(i, "?") for i in r["supplier_ids"].split()})
        path = d / f"{len(out) + 1:03d}-{lang}.html"
        path.write_text(body, encoding="utf-8")
        out.append({"approve": "", "fit": " ".join(fits), "send_to": r["send_to"], "language": lang,
                    "subject": subject, "html_file": path.name, "names": r["names"], "lead_mails": r["lead_mails"]})
        if lang not in samples and "target" in fits:
            samples[lang] = (subject, body, r["send_to"])
    with open(a.out / "silent-mail-v2.csv", "w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, fieldnames=list(out[0]))
        w.writeheader()
        w.writerows(out)
    sd = a.out / "silent-mail-samples"
    sd.mkdir(exist_ok=True)
    for lang, (subject, body, to) in samples.items():
        page = (f'<!doctype html><meta charset="utf-8"><title>{html.escape(subject)}</title>'
                f'<body style="background:#ECEFF1;padding:24px;margin:0;">'
                f'<p style="font:13px Arial;color:#546E7A;max-width:600px;margin:0 auto 10px;">'
                f'<b>From:</b> Ruumly &lt;info@ruumly.eu&gt; · <b>To:</b> {html.escape(to)}<br>'
                f'<b>Subject:</b> {html.escape(subject)}</p><div style="max-width:600px;margin:0 auto;">{body}</div></body>')
        (sd / f"sample-{lang}.html").write_text(page, encoding="utf-8")
    from collections import Counter
    print(f"{len(out)} remaining letters; fit {Counter(o['fit'] for o in out)}; samples {sorted(samples)} -> {sd}")
