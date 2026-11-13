# Silent providers: re-engagement plan and drafts (2026-11-13)

**Status: drafts only. Nothing was sent.** Send via Resend from info@ruumly.eu (never from a personal
Gmail). One mail per provider, only to rows ticked `approve=y`.

## Who

`tools/supplier_scan/out/silent-providers.csv` (`scan.py silent`): providers that got at least one
customer request from Ruumly and never quoted or declined. Opt-outs, inactive rows and unusable
e-mails are already excluded.

| country | silent | answered at least once |
|---|---:|---:|
| EE | 96 | 8 |
| LV | 38 | 7 |
| LT | 1 | 0 |

How many requests the silent ones got: 1 request: 89 · 2: 29 · 3: 5 · 4: 6 · 5 or more: 6.

## Why they may be silent (what the data can and cannot tell)

- **We cannot tell "never saw it" from "ignored it".** `OpenedAt` is null on all 270 sends, including
  the 21 that quoted, so open tracking is off. Fix that first (Resend: enable open tracking on
  ruumly.eu, and subscribe the webhook to `email.opened`). Then the next round tells us which is which.
- 188 of 270 have a delivered receipt, so most mails did reach the server. Spam-foldering is possible
  but not measurable.
- The lead mail asks for a price on a specific job. That is a big ask from an unknown sender. The
  re-engagement mail below asks for something smaller: a one-word answer.

## The ask

One question, three possible one-word answers. Every answer is useful:

1. **JAH / YES**: keep sending requests. Optionally they can add a price range.
2. **ÄRIKLIENDID / BUSINESS ONLY**: set `ServesConsumers=false`, and stop sending household requests.
3. **EI / NO**: set `MarketingOptOutAt`, and never write again.

No answer within 14 days means the row is moved to the back of the fan-out order. It is not deleted:
it stays in the directory as a listing.

Product follow-up (not built): the fan-out could skip providers with 3 or more unanswered requests
and 0 replies by default. 17 of the 135 are in that group.

## Drafts

Placeholders: `{name}`, `{town}`, `{service}` (localized service name), `{n}` (number of requests sent).
Use the singular form when n = 1. Keep it plain text, with no tracking links except the existing
opt-out link.

### ET (96 rows)

**Teema:** {name}: kas võtate erakliente vastu?

> Tere!
>
> Ruumly on Eesti teenus, mis aitab kolijatel ja eraklientidel leida {service} teenust. Oleme Teile
> saatnud {n} kliendi päringut piirkonnast {town}, kuid vastust pole saanud. Võib-olla jäid kirjad
> rämpsposti või ei sobi need Teile.
>
> Palun vastake sellele kirjale ühe sõnaga:
>
> **JAH**: saatke edaspidi päringuid (soovi korral lisage hinnavahemik)
> **ÄRIKLIENDID**: teenindame ainult firmasid
> **EI**: ärge enam kirjutage
>
> Lugupidamisega
> Ruumly meeskond · info@ruumly.eu · ruumly.eu
> Diip Solutions OÜ

### RU (for EE providers whose site or name is Russian-language: pick by hand)

**Тема:** {name}: работаете ли вы с частными клиентами?

> Здравствуйте!
>
> Ruumly — эстонский сервис, который помогает людям при переезде найти {service}. Мы отправили вам
> {n} заявок клиентов из {town}, но ответа не получили. Возможно, письма попали в спам или заявки вам
> не подходят.
>
> Пожалуйста, ответьте одним словом:
>
> **ДА**: присылайте заявки и дальше (можно указать диапазон цен)
> **ТОЛЬКО ФИРМЫ**: работаем только с юрлицами
> **НЕТ**: больше не пишите
>
> С уважением,
> команда Ruumly · info@ruumly.eu

### LV (38 rows)

**Temats:** {name}: vai strādājat ar privātklientiem?

> Labdien!
>
> Ruumly ir Baltijas serviss, kas palīdz cilvēkiem, kuri pārceļas, atrast {service}. Esam Jums
> nosūtījuši {n} klientu pieprasījumus no {town}, bet atbildi neesam saņēmuši. Iespējams, vēstules
> nonāca mēstulēs vai pieprasījumi Jums neder.
>
> Lūdzu, atbildiet ar vienu vārdu:
>
> **JĀ**: sūtiet pieprasījumus arī turpmāk (varat norādīt cenu diapazonu)
> **TIKAI UZŅĒMUMI**: strādājam tikai ar uzņēmumiem
> **NĒ**: vairs nerakstiet
>
> Ar cieņu,
> Ruumly komanda · info@ruumly.eu

### LT (1 row)

**Tema:** {name}: ar dirbate su privačiais klientais?

> Laba diena!
>
> Ruumly yra Baltijos paslauga, padedanti žmonėms, kurie kraustosi, rasti {service}. Išsiuntėme Jums
> {n} klientų užklausas iš {town}, tačiau atsakymo negavome.
>
> Prašome atsakyti vienu žodžiu:
>
> **TAIP**: siųskite užklausas toliau
> **TIK ĮMONĖMS**: dirbame tik su įmonėmis
> **NE**: daugiau nerašykite
>
> Pagarbiai,
> Ruumly komanda · info@ruumly.eu

> Pricing terms (free / commission) are deliberately left out: add a line only once the founder confirms what providers pay.
> The LV and LT drafts are machine-written. Have a native speaker read them before sending.

## Before sending

- [ ] Open tracking enabled in Resend and the webhook subscribed to `email.opened`
- [ ] Check the Resend **daily** cap. On the free tier it is about 100 a day, so send in batches of
      at most 90. See the 2026-08-13 intro-campaign incident.
- [ ] Sample 10 rows in `silent-providers.csv` and confirm each e-mail is still current (the health
      scan's `fix_contact` rows must be applied first)
- [ ] Record each reply: JAH → note it; ÄRIKLIENDID → `ServesConsumers=false`; EI → opt-out
