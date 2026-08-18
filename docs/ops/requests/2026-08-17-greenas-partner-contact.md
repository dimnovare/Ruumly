# 2026-08-17 — partner contact, GREENAS, UAB (LT)

**Not a demand lead.** First real message ever sent through a partner-page contact
dialog, and it failed.

- **From:** Asta Ivanauskienė <astulemazule@gmail.com>, lt
- **Partner:** GREENAS, UAB (`greenas-klaipeda`) — cleaning, Klaipėda, LT
- **Message:** *"Sveiki gal reikalinga vadybininke, ar kokybės vadybininke?"*
  — "Hello, maybe you need a manager, or a quality manager?"
- **Intent:** speculative job application, almost certainly addressed to GREENAS
  rather than to Ruumly. She was on GREENAS's page and the dialog is titled
  "Susisiekti su GREENAS, UAB".

## Where it stalled

It did not stall — it was never delivered. `PartnerPage.tsx:95` posts to
`POST /api/contact`, which emails `siteEmail` and nothing else. The partner is
never emailed, gets no notification, and no `DemandLead` row is created.

## What we promised that we did not do

The dialog she used says, in all five languages, that the **partner** will reply:

- `partner.contactIntro` — "Išsiųskite {name} žinutę — **jie atsakys el. paštu**."
- `partner.contactToast` — "Jūsų žinutė išsiųsta — **partneris atsakys el. paštu**."

Compounding it, `GET /api/suppliers/by-slug/greenas-klaipeda` returns
`contactEmail: null`, `phone: null`, `website: null`. The page deliberately
withholds the partner's details so the platform brokers the introduction, and the
brokering mechanism does not broker. There is no other route to GREENAS on the site.

## Blast radius

Two messages in the form's entire history: this one, and an internal `"test"` to
Peetri Miniladu on 2026-08-16. One real person affected. The bug is an honesty
failure rather than a volume problem — but it sits on the only interaction surface
the Latvian and Lithuanian directory has.

## What would have prevented it

Either delivering the message, or not promising delivery. The machinery already
exists one endpoint away: `POST /api/leads/quote` emails the supplier, creates a
provider notification and stores a routed `DemandLead`. The partner-page dialog
simply does not use it.

## Backlog item

Added to the masterplan spec as cross-cutting finding #9, Phase 1. Fix shape:
deliver directly for **claimed** partners; for unclaimed directory rows say plainly
that the Ruumly team relays the message, because cold-forwarding arbitrary mail to
1,187 rows that never signed up is its own problem.

**Open:** whether to reply to Asta telling her GREENAS never received it. Founder's
call — it is an outbound email to a member of the public.
