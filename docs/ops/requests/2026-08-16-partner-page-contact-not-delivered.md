# 2026-08-16 / 2026-08-17 — partner-page messages, never delivered

Both messages ever sent through a partner-page contact dialog. Both failed the same
way. Neither partner was contacted.

## The two

**2026-08-16 — `ann` <anniviis@gmail.com>, et, Peetri Miniladu (`peetri-miniladu`)**
Message body: `test`.
Estonian visitor on a **self-storage** page in Peetri, Rae vald — inside the Tallinn
concierge catchment. Someone typing "test" into a contact form is normally checking
whether it works before writing a real message. Two readings, both open:
a customer probing the form, or someone at Peetri Miniladu checking their own page.
Distinguishable by comparing the address against the supplier row's `ContactEmail`;
needs an admin token.

**2026-08-17 — Asta Ivanauskienė <astulemazule@gmail.com>, lt, GREENAS, UAB (`greenas-klaipeda`)**
*"Sveiki gal reikalinga vadybininke, ar kokybės vadybininke?"* — "Hello, maybe you
need a manager, or a quality manager?" A speculative job application, almost certainly
addressed to GREENAS rather than to Ruumly.

## Where they were writing from

`https://ruumly.eu/{lang}/partner/{slug}` — route `partner/:slug`, `App.tsx:261`.
Only two components in the app call the contact form: `ContactPage` (honest — it is
Ruumly's own) and `PartnerPage` (this one).

Both partners are **imported directory rows**: `isDirectory: true`, `isVerified: false`,
`listingCount: 0`, never claimed. Ruumly published a profile page for a company that has
no relationship with Ruumly, and the only interactive element on it makes a promise on
that company's behalf.

## What we promised and did not do

`PartnerPage.tsx:91` posts to `POST /api/contact`, which emails `siteEmail` and stops.
No supplier email, no provider notification, no lead row. Meanwhile the dialog says, in
all five languages, that the partner will answer:

- `partner.contactIntro` — "Send {name} a message — **they'll reply by email**."
- `partner.contactToast` — "Your message was sent — **the partner will reply by email**."

The page does show the partner's `websiteUrl`, so a determined visitor has another
route. That is the only mitigation, and neither of these two used it.

## What would have prevented it

Delivering the message, or not promising delivery.

## Fix

A partner-page message is **demand**, not correspondence: ann was a storage enquiry in
Peetri. Make it a `DemandLead` with `Source = "partner-page"`, `SupplierId` set, and
category/city derived from the supplier — so it enters the ops queue and the metrics
instead of dying in a shared inbox. `POST /api/leads/quote` already does exactly this.

Then branch delivery on `isDirectory`, which the public DTO already exposes:

- **claimed partner** → deliver as `/leads/quote` does; the existing copy becomes true
- **directory row** → ops answers; copy changes to "Ruumly will get you an answer",
  making no promise on a stranger's behalf

Cold-forwarding arbitrary mail to 1,187 imported rows is not the fix.

## Correction

An earlier version of this entry called the 2026-08-16 message an internal test because
its body was the word "test". `anniviis@gmail.com` is not a Ruumly address. It was a real
person, and it was the *first* of the two, not a rehearsal for it. The same pass also
recorded "no website" for both partners, from querying DTO fields that do not exist and
reading the resulting nulls as fact. Both have a website.

## Backlog

Masterplan spec, cross-cutting finding #9, Phase 1.

**Open:** whether to reply to Asta, and whether to reply to ann. Both are outbound email
to members of the public — founder's call.
