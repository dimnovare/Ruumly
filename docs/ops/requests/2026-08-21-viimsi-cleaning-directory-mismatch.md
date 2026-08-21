# 2026-08-21 — Viimsi cleaning: four replies, four refusals, one pattern

Lead `b8384bc5-7b31-4088-948e-1b265437de58`. Customer Vladimir, Viimsi,
`ru`. Terraced house, 4 rooms, **107 m²**, **regular weekly** cleaning, oven and
fridge as extras, 3-year-old child, no pets.

17 cleaning providers were asked. Four have answered. **All four said no, and
three of them said no to the same thing.**

| Provider | What they said | What it means |
|---|---|---|
| Terve Puhastus | *"ei paku kodukoristust, vaid teostame ainult kodu suurpuhastust"* | deep cleans only, not domestic upkeep |
| Kendra OÜ | *"regulaarset hoolduskoristust eraklientidele ei paku… ainult ühekordseid eritöid"* | one-off specialist work only |
| Lux Puhastus | *"Pakume teenust vaid äriklientidele"* | **B2B only** — not a domestic cleaner at all |
| IM Puhastus | *"Ei ole võimalik."* | no reason given |

## The finding

This is not a supply shortage. It is a **classification problem in the
directory**: `cleaning` is one service slug covering at least three businesses
that do not substitute for each other —

1. **domestic recurring** — weekly/fortnightly upkeep of someone's home;
2. **domestic one-off specialist** — move-out cleans, `suurpuhastus`, windows,
   floor oiling;
3. **commercial / B2B** — offices, industrial floors.

Vladimir wants (1). At least three of the four repliers sell (2) or (3). Every
one of those emails was a real business reading a request they were never going
to take, and each refusal costs a little goodwill we will want later.

`Supplier.ServiceTypesJson` has no way to express this. There is one `cleaning`
value and nothing beneath it, so `ProviderCandidateFinder` cannot tell the three
apart and fans out to all of them.

## What was done

- All four recorded as `Declined` with their reason in the note — **including
  both outreach rows per provider**, since the 21.08 resend minted a second row
  each and marking only one would leave the other reading as silence.
- All four answered by email, in Estonian, from `info@ruumly.eu`.
- Lux Puhastus asked whether Ruumly passes on **business** enquiries. Answered
  honestly: essentially no — the front door is built around a private person's
  life event — and their interest was noted rather than promised anything.

## What is NOT done, and needs a decision

**Nothing in the product prevents this happening again on the next Viimsi
cleaning request.** The notes are prose an operator has to read; the candidate
finder cannot act on them.

The cheapest real fix is a sub-type on the supplier row — e.g. `cleaning`
splitting into `cleaning:domestic` / `cleaning:commercial`, or a boolean pair —
set from these refusals as they arrive, and honoured by
`ProviderCandidateFinder`. That is a schema and matching change, which is
squarely a founder decision: it touches the matching rules, which this project
does not retune unilaterally.

Until then the honest position is that Estonian cleaning fan-out will keep
including providers who cannot take domestic work, and the response rate for
`cleaning` should be read with that in mind.

## Still open on this lead

- 13 providers have not replied.
- Vladimir has not been told his answers were received and passed on.
