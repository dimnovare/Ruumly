# Partner confirmation: automatic "do you want our requests?" loop (2026-11-13)

**Status: built and tested on branch `claude/nice-golick-b75e7e`. Not deployed, nothing sent.**

## What it does

Providers whose services we could not confirm get one e-mail with three buttons:
- **Yes, send me requests**
- **We serve businesses only**
- **No, please stop writing**

| when | what happens |
|---|---|
| day 0 | letter (admin `POST /api/admin/suppliers/partner-confirm/campaign`, explicit ids, dry run by default) |
| any time | a click opens `api.ruumly.eu/api/partner-confirm/{token}` and **Confirm** records the answer; ops gets an e-mail |
| any time | the provider quotes on a customer request → counted as **yes** |
| day 14, no answer | one reminder ("we will remove you in 7 days"), max 80 per day |
| day 21, no answer | row deactivated (`PartnerConfirmExpiredAt`). **Not** an opt-out: a later **yes** restores it |

Each answer does the following:
- **yes** → `ServesConsumers = true`, and an expired row comes back.
- **business** → `ServesConsumers = false`, so the fan-out stops sending household requests.
- **no** → marketing opt-out and deactivated, exactly like a REMOVE reply.

Safety:
- Opening a button link records nothing. Mail scanners open every link, so the answer is recorded
  only by the POST behind the Confirm button.
- Opt-outs are never mailed or changed.
- One letter per inbox. Branches that share an address are all answered by one click.
- The daily job (07:00 UTC) does nothing until the PlatformSetting `partnerConfirmAutomation` is set
  to `on`. The per-run reminder cap is `partnerConfirmDailyCap` (default 80).
- E-mail replies ("JAH") are recorded by hand with
  `POST /api/admin/suppliers/partner-confirm/{id}/answer {"answer":"yes"}`.
- `GET /api/admin/suppliers/partner-confirm/status` shows each row's stage.

## Who gets it (`scan.py confirm-list`, from the re-scan)

| list | rows | file |
|---|---:|---|
| confirm letter (unsure or unverified, has a usable e-mail) | 309 rows / **300 inboxes** (LT 182, LV 80, EE 47) | `out/partner-confirm-list.csv/.json` |
| held: already mailed this week | 20 | `out/partner-confirm-held-recently-mailed.csv` |
| no e-mail at all: cannot be asked | 199 (LT 119, LV 77, EE 3) | `out/unreachable-no-email.csv` |
| remove: freight / 3PL, founder-approved | 38 | `out/remove-b2b.json` |

The re-scan retried unreachable sites over http/https and with and without `www.`. That recovered 31
sites, and confirmed partners rose from 544 to 574.

**Volume.** 300 letters do not fit the Resend free tier's 100 a day:
- With Resend Pro ($20/month, no daily cap): send all 300 in one paced run of about 75 minutes.
- On the free tier: send 3 batches of 90 to 100 on consecutive days.

## Deploy checklist

1. Merge, then let Railway deploy. Migration `AddPartnerConfirmation` adds 6 nullable columns and 1
   filtered unique index.
2. Run a dry run with `partner-confirm-list.json` and read the 3 samples.
3. Send a live batch.
4. Set `partnerConfirmAutomation = on`. Reminders begin 14 days later.

## Replies received on 2026-11-13

- **Maksten OÜ** (maksten.ou@gmail.com) answered **JAH**. Record it as yes.
- **Sipelgas Veod OÜ** (sipelgasveod@gmail.com) asked 6 questions; the draft answer is below. Their one
  request was the Jõhvi request of 24.08.2026. It went to info@sipelgasveod.ee and their mail server
  accepted it, but they reply from the Gmail address. Ask which address to use.

### Draft reply to Sipelgas Veod (ET)

Questions 2 and 3 need the founder's answer. Nothing has been sent.

> Tere, Aleksei!
>
> Aitäh kiire vastuse ja küsimuste eest.
>
> 1. Jah. Päringute saamine ja hinnapakkumiste saatmine on Teile tasuta.
> 2. **[FOUNDER: kas valitud pakkumiselt võetakse vahendustasu? Jah/ei, mitu %?]**
> 3. **[FOUNDER: kas klient maksab otse Teile või Ruumly kaudu?]**
> 4. Jah. Valite ise, millistele päringutele vastate. Kohustusi ei ole, ja päringust saab loobuda
>    ühe klikiga.
> 5. Jah. Päring läheb mitmele sobivale ettevõttele kliendi piirkonnas, ja klient valib pakkumiste
>    seast.
> 6. Kogu Eestis, kõige rohkem Tallinnas ja Harjumaal, Tartus, Pärnus ja Viljandis. Vahendame
>    laoruumi, kolimist, haagise- ja kaubikurenti ning koristust.
>
> Ainus päring, mille Teile seni saatsime, oli 25.08.2026 Jõhvist. See läks aadressile
> info@sipelgasveod.ee, ja Teie meiliserver kinnitas selle kättesaamise. See päring on praeguseks
> suletud. Kas saadame edaspidised päringud aadressile sipelgasveod@gmail.com?
>
> Lugupidamisega
> Ruumly meeskond · info@ruumly.eu
