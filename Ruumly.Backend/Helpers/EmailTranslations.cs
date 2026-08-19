using Ruumly.Backend.Constants;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Helpers;

public static class EmailTranslations
{
    public record EmailStrings(
        string PasswordResetSubject,
        string PasswordResetGreeting,
        string PasswordResetBody1,
        string PasswordResetBody2,
        string PasswordResetExpiry,
        string PasswordResetButton,
        string PasswordResetCopyLabel,
        string PasswordResetSecurityTitle,
        string PasswordResetSecurityBody,
        string PasswordResetContactUs,
        string PasswordResetFooter,
        string BookingConfirmSubject,
        string BookingConfirmGreeting,
        string BookingConfirmBody,
        string BookingConfirmService,
        string BookingConfirmStartDate,
        string BookingConfirmPeriod,
        string BookingConfirmTotal,
        string BookingConfirmVat,
        string BookingConfirmNext,
        string BookingConfirmViewButton,
        string BookingConfirmFooter,
        // Email verification
        string EmailVerifySubject,
        string EmailVerifyGreeting,
        string EmailVerifyBody,
        string EmailVerifyButton,
        string EmailVerifyExpiry,
        string EmailVerifyFooter,
        // Booking status update emails
        string BookingStatusConfirmedSubject,
        string BookingStatusConfirmedBody,
        string BookingStatusRejectedSubject,
        string BookingStatusRejectedBody,
        string BookingStatusCompletedSubject,
        string BookingStatusCompletedBody,
        string BookingStatusCancelledSubject,
        string BookingStatusCancelledBody,
        string BookingStatusViewLink,
        // Timeline events
        string TimelineBookingCreated,
        string TimelineBookingCancelled,
        string TimelineOrderApproved,
        string TimelinePartnerConfirmed,
        string TimelineOrderRejected,
        string TimelineServiceActive,
        string TimelineServiceCompleted,
        string TimelineStatusChanged,
        // Notification titles
        string NotifBookingConfirmed,
        string NotifBookingRejected,
        string NotifBookingCancelled,
        string NotifNewMessage,
        // Notification bodies
        string NotifBookingConfirmedBody,
        string NotifBookingRejectedBody,
        string NotifServiceActiveBody,
        string NotifServiceCompletedBody,
        string NotifBookingCancelledBody,
        // Routing timeline
        string TimelineAwaitingApproval,
        string TimelineManualApprovalNeeded,
        // Supplier email body
        string EmailGreeting,
        string EmailNewOrder,
        string EmailOrderDetails,
        string EmailOrderNumber,
        string EmailService,
        string EmailType,
        string EmailClient,
        string EmailName,
        string EmailPhone,
        string EmailDetails,
        string EmailStartDate,
        string EmailEndDate,
        string EmailPeriod,
        string EmailExtras,
        string EmailPrice,
        string EmailPartnerPrice,
        string EmailTotalPartner,
        string EmailNotes,
        string EmailConfirmRequest,
        string EmailConfirmInstructions,
        string EmailRegards,
        string EmailTypeWarehouse,
        string EmailTypeMoving,
        string EmailTypeTrailer,
        // Abandoned booking reminder
        string AbandonedSubject,
        string AbandonedGreeting,
        string AbandonedBody,
        string AbandonedService,
        string AbandonedTotal,
        string AbandonedCta,
        // Reservation expired
        string ReservationExpiredSubject,
        string ReservationExpiredGreeting,
        string ReservationExpiredBody,
        string ReservationExpiredCta,
        // Refund initiated notification
        string RefundInitiatedTitle,
        string RefundInitiatedDesc,
        // Supplier approval welcome
        string SupplierWelcomeSubject,
        string SupplierWelcomeBodyTpl,
        // Quote-request reply (partner → customer, one-time moving/trailer price)
        string QuoteReplySubject,
        string QuoteReplyBodyTpl,
        // Concierge offer → customer (offer_to_customer)
        string OfferSubject,
        string OfferGreeting,
        string OfferIntro,
        string OfferNoteLabel,
        string OfferCta,
        string OfferQuestions,
        string OfferSignature,
        // Provider availability request (outreach_to_provider) — never contains
        // customer name/email/phone; the admin brokers the introduction
        //
        // "{city}: {category} — customer request". PLACE FIRST: a phone lock
        // screen shows about thirty-five characters, and the two facts that
        // decide whether a cold recipient opens this at all are "is this my
        // area" and "is this what I do". The brand used to occupy the front of
        // the line and the city the very end, where it was always truncated; the
        // brand is the From display name anyway.
        string OutreachSubjectTpl,
        // Fallback greeting for a recipient whose company name we do not hold,
        // or whose stored name is too long to paste into a sentence.
        string OutreachGreeting,
        // "Hello, {company}!" — the recipient's own company name. The single
        // cheapest signal that a letter is not a mailshot, and we are already
        // holding the name: outreach is composed from a Supplier row.
        string OutreachGreetingTpl,
        // Who we are and WHY THIS COMPANY. A cold recipient's first two
        // questions about unsolicited business mail are "what is this" and "why
        // me / where did you get my address", and both have honest answers: they
        // offer this service in this area and they are in the Ruumly directory.
        string OutreachIntro,
        // "A customer submitted this request on our website on {date}. Answering
        // is free and non-binding, and you do not need an account to send a
        // price."
        //
        // This REPLACES an assertion with evidence. The old intro said "this is
        // a real customer request" — the one sentence a lead-generation bot
        // would also write, and one the recipient cannot check. A submission
        // date is specific, sits beside the need date where it can be judged,
        // and — because outreach is re-composed on every fan-out — quietly stops
        // a three-week-old request from arriving dressed as news.
        string OutreachProvenanceTpl,
        // The ask, and what the smallest useful answer looks like: whether the
        // date works and roughly what it would cost. "Roughly" is deliberate —
        // an approximate number is a real answer and demanding an exact one is
        // how you get none. Closes by saying what happens to the price, because
        // a provider handing a number to a broker is entitled to know.
        string OutreachAsk,
        // The second answer: "I can take this, but I cannot price it from what
        // you sent me — here is what is missing."
        //
        // Not new functionality. The need-info action has been on the quote page
        // since 2026-08-18 (POST /api/quote/{token}/need-info); the email simply
        // never said so, which left a provider who was blocked with silence as
        // their only available move.
        string OutreachCannotPrice,
        // Discrete field labels — the request facts are rendered as a table in
        // HTML and as "Label: value" lines in the text fallback.
        string OutreachLabelService,
        string OutreachLabelLocation,
        string OutreachLabelDate,
        string OutreachLabelDetails,
        string OutreachLabelPhotos,
        // Shown INSTEAD of a bare "—" when the customer gave no date / no
        // details: a provider must never be asked to price a dash.
        string OutreachDateAsap,
        // The visitor explicitly said any day suits them — a fact a provider can
        // quote against, unlike the silence OutreachDateAsap covers.
        string OutreachDateFlexible,
        // Photos exist for this request. The provider never receives them as
        // attachments — they are pictures of somebody's home, served only behind
        // the per-recipient quote token. This line is what makes the CTA worth
        // clicking: the thing they need in order to price the job is on the
        // other side of it.
        string OutreachPhotosTpl,
        string OutreachDetailsMissing,
        // Packing add-on. A "packing" request is routed to a Moving lead, so the
        // lead's Category cannot carry the ask; ProviderOutreachComposer recovers
        // it from the Query marker (ServiceCategories.HasPackingAddOn) and renders
        // THIS line. It exists so the intent never has to travel as English prose
        // inside Details, which is printed verbatim into a cold email written in
        // the provider's own language.
        string OutreachPackingAddOn,
        // Next-day demand is the dominant pattern — a need date within
        // ProviderOutreachComposer.UrgentWithinDays is flagged in the subject
        // line and at the top of the body.
        string OutreachUrgentBadge,
        string OutreachUrgentTpl,
        // Primary CTA of the provider outreach email — points at the tokenized
        // quote page ("Submit your price"), replacing "reply to this email" as
        // the action. The URL is appended by ProviderOutreachComposer.
        string OutreachQuoteCta,
        // Explicit low-friction alternative to the link: Reply-To is the ops
        // inbox, so a plain reply with a price works today.
        string OutreachReplyAlternative,
        // Carries the public site URL as well as the reply address: a recipient
        // deciding whether to trust an unfamiliar sender should not have to
        // guess at, or search for, where it can be checked.
        string OutreachSignature,
        // "Questions? Reply to this email, or write to us through our contact
        // page: {url}". The only two channels a provider is offered — the
        // support phone was retired in 2026-08 (the founder does not take
        // provider calls), so no number is printed anywhere in outbound mail.
        string OutreachQuestionsTpl,
        // ── Customer request acknowledgement (concierge_ack) ──────────────────
        // Sent the instant a request is submitted. Before this existed the
        // customer heard nothing until an offer arrived days later, from an
        // address they had never corresponded with.
        //
        // Says NOTHING about timing. Some requests reach no provider
        // automatically — a multi-service ask is routed by hand — and repeating
        // the "24 hours" from the success screen in the one message that proves
        // we received the request would turn an honest wait into a broken
        // promise.
        string AckSubject,
        string AckGreetingTpl,
        string AckGreetingNoName,
        string AckReceived,
        string AckSummaryHeading,
        string AckLabelService,
        string AckLabelCity,
        string AckLabelDate,
        string AckLabelDetails,
        string AckDateAsap,
        string AckWhatNext,
        // ── The customer's own status page ───────────────────────────────────
        // /{lang}/request-status/{token} shipped with nothing linking to it, so
        // the page built to end the silence was itself unreachable. This is the
        // durable half of the fix: the success screen is seen once, this mail
        // is kept.
        //
        // Same discipline as the rest of this block — it says where to LOOK,
        // never when something will happen. The page reports the stage the
        // request actually reached; a sentence here promising a stage would be
        // the deadline claim this mail exists without.
        //
        // Two strings, not one, because the two bodies need different shapes:
        // the text body prints "{cta} → {url}" (the house pattern from
        // OutreachQuoteCta) and the HTML puts the CTA on a button, where a raw
        // URL would be noise.
        string AckStatusLine,
        string AckStatusCta,
        // The point of the whole mail: a thread they can answer. This address is
        // the only channel back from a customer and it was never exercised.
        string AckReply,
        string AckContactTpl,
        string AckSignature,
        // ── One-off supplier INTRODUCTION campaign (supplier_intro) ───────────
        // Sent ONCE to every directory provider we added from public research,
        // BEFORE an auto-fanout request ever lands in their inbox: a cold
        // availability request from a name they have never seen reads as spam.
        //
        // Structure is the founder's own 2026-08 draft: three answered questions
        // (what we want from you / why answering matters / what your profile is)
        // instead of a feature list. Keep it under ~450 words — a small operator
        // reads this on a phone.
        string IntroSubjectTpl,
        string IntroGreeting,
        // Why this email exists, in one line, addressed to them and not to us:
        // "we are writing because your company offers something our customers
        // are looking for."
        string IntroOpening,
        // What Ruumly is, described by what the customer does, not by what we
        // are. Lists the service categories a provider might recognise.
        string IntroWhoWeAre,
        // "If a request matches your service and your area, we forward it."
        string IntroForwarding,
        // The sentence that defuses the spam reaction: these are not test
        // requests and not a marketing list — a real person, needing it now.
        string IntroNotTestRequests,
        // ── Section 1: what we want from you ─────────────────────────────────
        string IntroExpectHeading,
        string IntroExpectIntro,
        // The three things a useful reply contains. Kept as separate fields so
        // the HTML can render a real <ul> and the text body a "- " list.
        string IntroExpectBullet1,
        string IntroExpectBullet2,
        string IntroExpectBullet3,
        // "No account, no joining fee, no separate system to learn."
        string IntroNoAccount,
        // Lowers the cost of the ask to almost nothing: "not possible" is a
        // perfectly good answer. A provider who believes silence is the only
        // alternative to a full quote will choose silence.
        //
        // ALSO USED BY ProviderOutreachComposer, verbatim. The sentence was
        // written for the introduction campaign, but it is the request email
        // where the choice is actually made — and only about a quarter of the
        // directory ever received the introduction (the 2026-08-13 send hit
        // Resend's daily cap). Two copies would be two chances to soften one of
        // them; editing this string edits both letters, on purpose.
        string IntroIfNotSuitable,
        // ── Section 2: why answering matters ─────────────────────────────────
        // THE behavioural target of the whole campaign. Providers who run their
        // own booking system reply to a request with a link to their website,
        // and the customer — who came to Ruumly precisely to avoid visiting ten
        // websites — drops out. Says plainly that a bare link cannot be put in
        // front of the customer alongside the other options.
        string IntroWhyHeading,
        string IntroWhyBody,
        // "Our goal is simple: bring you a suitable customer, and make reaching
        // you easy for them."
        string IntroGoal,
        // Honest sizing, kept in EVERY language and placed before the profile
        // ask. We promise no request count and no monthly flow — but every
        // request that does go out is tied to a real customer, place, time and
        // need. An inflated promise here is discovered the first month.
        string IntroVolume,
        // ── Section 3: your profile ──────────────────────────────────────────
        string IntroProfileHeading,
        // "{company}: we built a first profile from public information."
        string IntroProfileListedTpl,
        // Invitation to send a price list / standard rates / rules of thumb, so
        // we can route better-matched requests. NOT a claim-form field: the
        // claim endpoint cannot store prices (see IntroClaimIntro).
        string IntroPriceList,
        // The only place paid promotion is mentioned, and only as something that
        // opens up LATER, once the profile is correct. Ruumly earns by serving
        // the customer across the whole move, so a provider looking good is in
        // our own interest.
        //
        // Only three visibility products have enforcing code (ListingService):
        // featured_search EUR29, service_area_boost EUR29, pickup_location_boost EUR24
        // — hence "24-29 a month". city_pages is seeded at EUR39 and does NOTHING, so it
        // must never appear here. Mechanics stated once and honestly: requested from
        // us, switched on by us, bank transfer, never an automatic charge, listing free.
        string IntroVisibilityLater,
        // The one thing to remember if they remember nothing else: answer the
        // requests that fit. Rendered as a highlighted block in the HTML.
        string IntroFinalAsk,
        // "Just reply, or write to us through our contact page: {url}."
        // Reply-To is the ops inbox, so a plain reply already reaches a human.
        // Same two-channel rule as OutreachQuestionsTpl: no phone number.
        string IntroQuestionsTpl,
        // Claim path. The real flow is the public partner page
        // (/{lang}/partner/{slug}), which carries the "claim this profile"
        // CTA — SupplierIntroComposer links straight to it.
        string IntroClaimIntro,
        string IntroClaimCta,
        // Fallback for a supplier with no published partner page: no link
        // exists, so claiming is a plain email to {email}.
        string IntroClaimByEmailTpl,
        // Opt-out. Legally required for B2B marketing mail in the EU
        // (ePrivacy) and simply decent: one reply with {keyword}, or one click
        // on the mailto link in the HTML body. Never a form.
        //
        // ALSO USED BY ProviderOutreachComposer. The request email is the one
        // that arrives repeatedly and unbidden — a single Viljandi provider was
        // a candidate for four storage requests inside ten days — and it shipped
        // with no way to stop it at all. A recipient who cannot find an
        // unsubscribe uses the one their client gives them, and a spam complaint
        // both retires the address (EmailDeliveryTracker) and costs sending
        // reputation for everyone else. The keyword is the same token in every
        // language so one inbox filter catches every opt-out from either letter.
        string IntroOptOutTpl,
        string IntroOptOutLinkLabel,
        string IntroSignature,
        // ── "Claim your profile" magic link (supplier_claim) ──────────────────
        // Sent ONLY to the ContactEmail already stored on the supplier row, and
        // only when the visitor typed that same address. It carries the single
        // credential for the claim session, so the copy has to make three things
        // unmissable: what was asked for, that the link dies after one use, and
        // what to do if the recipient did NOT ask.
        string ClaimSubject,
        string ClaimGreeting,
        // "Someone asked to take over the Ruumly profile for {company}."
        string ClaimBodyTpl,
        string ClaimCta,
        // "The link works once and expires in {hours} hours."
        string ClaimExpiryTpl,
        // "Didn't ask for this? Ignore this email — nothing changes. Concerned:
        // write to {email}." A cold recipient must never be left wondering.
        string ClaimIgnoreTpl,
        // ── "You already have an account" (apply_sign_in) ─────────────────────
        // Sent when the PUBLIC partner-application form is submitted with an
        // address that already has a Ruumly user. The anonymous caller proved
        // nothing, so no supplier is created and no account is touched; this mail
        // is the only thing that happens, and it goes to the account holder.
        //
        // Carries NO data from the submission — not the company name, not the
        // registry code, nothing. Whoever filled the form is unauthenticated and
        // may not be the recipient, so printing their text here would turn this
        // into a delivery channel for a stranger's message. The application's
        // details go to the ops inbox instead, where a human reads them.
        string ApplySignInSubject,
        string ApplySignInGreeting,
        string ApplySignInBody,
        string ApplySignInCta,
        // "Wasn't you? Nothing was created and nothing changed." Same duty as
        // ClaimIgnoreTpl: a recipient who did not act must never be left guessing.
        string ApplySignInIgnoreTpl,
        // Service-category labels missing from the legacy EmailType* trio.
        // CategoryPacking / CategoryInsurance are STILL REFERENCED and must not be
        // deleted: we stopped selling those two as consumer services in 2026-08, but
        // DemandLead rows created before that are still worked in the admin CRM and
        // still generate provider outreach, which renders CategoryLabel(lead.Category).
        string CategoryCleaning,
        string CategoryPacking,
        string CategoryVanRental,
        string CategoryInsurance,
        string CategoryAny,
        // ── Intake scoping answers (see Constants/ScopeQuestions) ─────────────
        // The chips the customer tapped — home size, floor and lift, how long,
        // what is being hauled — rendered as fact lines in the RECIPIENT's
        // language. The whole point of storing the answer as a position rather
        // than as a sentence: the same lead has to read Estonian to an Estonian
        // mover and Latvian to a Latvian one, whatever language the customer
        // filled the form in.
        //
        // ONE FLAT DICTIONARY rather than ~66 more positional parameters, keyed
        // exactly like the frontend's own translation keys ("movingSize.label",
        // "movingSize.opt3") so the two catalogues can be diffed by eye. A
        // question with no entry here renders NO line at all — never a raw id
        // and never a bare number, which is worse than silence in a cold email.
        IReadOnlyDictionary<string, string> ScopeText
    )
    {
        public string SupplierWelcomeBody(string name) =>
            SupplierWelcomeBodyTpl.Replace("{name}", name);

        public string QuoteReplyBody(string name, string partner, string listing, string price) =>
            QuoteReplyBodyTpl
                .Replace("{name}", name)
                .Replace("{partner}", partner)
                .Replace("{listing}", listing)
                .Replace("{price}", price);

        /// <summary>
        /// Localized label for any DemandLeadCategory (Any → generic "service").
        /// Packing/Insurance stay mapped on purpose — new leads are never created in
        /// those categories any more, but historical ones must still render.
        /// </summary>
        public string CategoryLabel(DemandLeadCategory category) => category switch
        {
            DemandLeadCategory.Warehouse => EmailTypeWarehouse,
            DemandLeadCategory.Moving    => EmailTypeMoving,
            DemandLeadCategory.Trailer   => EmailTypeTrailer,
            DemandLeadCategory.Cleaning  => CategoryCleaning,
            DemandLeadCategory.Packing   => CategoryPacking,
            DemandLeadCategory.VanRental => CategoryVanRental,
            DemandLeadCategory.Insurance => CategoryInsurance,
            _                            => CategoryAny,
        };

        /// <summary>
        /// Fact-table label for a scoping question ("Home size"), or null when
        /// this language has no wording for it. A NOUN PHRASE, not the question
        /// the customer was asked: the customer saw "What size is the home?",
        /// the provider reads a row in a table of job facts.
        /// </summary>
        public string? ScopeLabel(string questionId) =>
            ScopeText.GetValueOrDefault($"{questionId}.label");

        /// <summary>
        /// The wording of one chip ("2nd–3rd floor, no lift"), or null when this
        /// language has no wording for it. Null is a normal answer, not an error:
        /// a stored row can name a question — or an option — that this build no
        /// longer has copy for, and the caller drops the line.
        /// </summary>
        public string? ScopeOption(string questionId, int option) =>
            ScopeText.GetValueOrDefault($"{questionId}.opt{option}");

        /// <summary>"The customer attached N photos — view them on the quote page."</summary>
        public string OutreachPhotos(int count) =>
            OutreachPhotosTpl.Replace("{count}", count.ToString());

        public string OutreachSubject(string category, string city) =>
            OutreachSubjectTpl
                .Replace("{category}", category)
                .Replace("{city}", city);

        /// <summary>"Hello, {company}!" — the recipient's own company name.</summary>
        public string OutreachGreetingTo(string company) =>
            OutreachGreetingTpl.Replace("{company}", company);

        /// <summary>"A customer submitted this request on our website on
        /// {date}." Pass the lead's own CreatedAt, formatted yyyy-MM-dd: the
        /// need date in the fact table below uses the same format, and two date
        /// formats in one short email is the kind of seam that makes a letter
        /// look assembled rather than written.</summary>
        public string OutreachProvenance(string date) =>
            OutreachProvenanceTpl.Replace("{date}", date);

        /// <summary>"URGENT: the customer needs this by {date}" — only rendered
        /// when the lead actually carries a near-term need date.</summary>
        public string OutreachUrgent(string date) =>
            OutreachUrgentTpl.Replace("{date}", date);

        /// <summary>"Questions? Reply to this email, or write to us through our
        /// contact page: {url}" — build the URL with
        /// <see cref="FrontendUrl.Contact"/> so it is never relative.</summary>
        public string OutreachQuestions(string contactUrl) =>
            OutreachQuestionsTpl.Replace("{url}", contactUrl);

        // ── Supplier introduction campaign ────────────────────────────────────

        public string AckGreeting(string name) =>
            AckGreetingTpl.Replace("{name}", name);

        public string AckContact(string url) =>
            AckContactTpl.Replace("{url}", url);

        /// <summary>Subject line carrying the recipient's own company name — the
        /// single strongest signal that this is not a bulk blast.</summary>
        public string IntroSubject(string company) =>
            IntroSubjectTpl.Replace("{company}", company);

        public string IntroProfileListed(string company) =>
            IntroProfileListedTpl.Replace("{company}", company);

        public string IntroQuestions(string contactUrl) =>
            IntroQuestionsTpl.Replace("{url}", contactUrl);

        public string IntroClaimByEmail(string email) =>
            IntroClaimByEmailTpl.Replace("{email}", email);

        /// <summary>"Reply with REMOVE and we'll take you off." The keyword is
        /// deliberately the same token in every language so one inbox filter
        /// catches every opt-out.</summary>
        public string IntroOptOut(string keyword) =>
            IntroOptOutTpl.Replace("{keyword}", keyword);

        // ── Claim-your-profile magic link ─────────────────────────────────────

        public string ClaimBody(string company) =>
            ClaimBodyTpl.Replace("{company}", company);

        public string ClaimExpiry(int hours) =>
            ClaimExpiryTpl.Replace("{hours}", hours.ToString());

        public string ClaimIgnore(string email) =>
            ClaimIgnoreTpl.Replace("{email}", email);

        // ── "You already have an account" ─────────────────────────────────────

        public string ApplySignInIgnore(string email) =>
            ApplySignInIgnoreTpl.Replace("{email}", email);
    }

    // ─── Intake scoping answers, one dictionary per language ──────────────────
    //
    // Declared BEFORE the EmailStrings instances below: static field
    // initializers run in textual order, so a dictionary defined after Et would
    // still be null when Et is constructed.
    //
    // Keys mirror the frontend's own translation keys exactly
    // ("movingSize.label", "movingSize.opt3" → request.scope.movingSize.opt3),
    // so the customer-facing catalogue and this provider-facing one can be
    // diffed by eye. The WORDING deliberately differs in two places:
    //
    //   • Labels are noun phrases, not questions. The customer was asked "What
    //     size is the home?"; the provider reads a row in a table of job facts.
    //   • "Not sure" says WHOSE uncertainty it is. "Home size: Not sure" in a
    //     cold email reads like a broken template; "the customer is not sure" is
    //     a fact a provider can price a range against — the same reason
    //     OutreachDateAsap exists instead of a dash.
    //
    // Every option is the SAME answer the customer tapped, in the recipient's
    // language. Nothing here is ever rendered to the customer.

    /// <summary>
    /// Fills in the chips of <c>movingAccessFrom</c> and <c>movingAccessTo</c>
    /// by copying <c>movingAccess</c>'s, and returns the same dictionary.
    ///
    /// The two ends ask the identical question about a different address, so
    /// their five answers must READ identically — a provider comparing
    /// "Access at pickup" with "Access at destination" is comparing two floors,
    /// and any wording difference between the rows would look like a difference
    /// in what was asked. Typing them out three times per language is 75 string
    /// literals that a later fix ("4. korrus" → "4. korrus või kõrgem") would
    /// only ever be applied to one or two of. Copying at construction makes the
    /// drift impossible instead of merely discouraged.
    ///
    /// Only the OPTIONS are shared. The three labels are genuinely different
    /// wording and stay written out per language.
    /// </summary>
    private static Dictionary<string, string> WithSharedAccessChips(Dictionary<string, string> scope)
    {
        for (var option = 1; option <= 5; option++)
        {
            var chip = scope[$"{ScopeQuestions.MovingAccess}.opt{option}"];
            scope[$"{ScopeQuestions.MovingAccessFrom}.opt{option}"] = chip;
            scope[$"{ScopeQuestions.MovingAccessTo}.opt{option}"]   = chip;
        }
        return scope;
    }

    private static readonly IReadOnlyDictionary<string, string> ScopeEt =
        WithSharedAccessChips(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["warehouseSize.label"]      = "Vajalik pind",
            ["warehouseSize.opt1"]       = "Paar kasti (~1–2 m²)",
            ["warehouseSize.opt2"]       = "Väike tuba (~3–5 m²)",
            ["warehouseSize.opt3"]       = "Tuba (~5–10 m²)",
            ["warehouseSize.opt4"]       = "2-toaline korter (~10–15 m²)",
            ["warehouseSize.opt5"]       = "Maja või rohkem (15+ m²)",
            ["warehouseSize.opt6"]       = "Klient ei ole kindel",
            ["warehouseDuration.label"]  = "Hoiuperiood",
            ["warehouseDuration.opt1"]   = "Alla kuu",
            ["warehouseDuration.opt2"]   = "1–3 kuud",
            ["warehouseDuration.opt3"]   = "3–12 kuud",
            ["warehouseDuration.opt4"]   = "Üle aasta",
            ["warehouseDuration.opt5"]   = "Klient ei ole kindel",
            ["warehouseGoods.label"]     = "Hoiustatav vara",
            ["warehouseGoods.opt1"]      = "Kodune vara ja mööbel",
            ["warehouseGoods.opt2"]      = "Kastid, dokumendid või ärikaup",
            ["warehouseGoods.opt3"]      = "Auto, mootorratas või paat",
            ["warehouseGoods.opt4"]      = "Tööriistad, tehnika või ehitusmaterjal",
            ["warehouseGoods.opt5"]      = "Kliimatundlik kaup (vein, elektroonika, pillid)",
            ["warehouseGoods.opt6"]      = "Klient ei ole kindel",
            ["movingSize.label"]         = "Kodu suurus",
            ["movingSize.opt1"]          = "Stuudio või 1-toaline",
            ["movingSize.opt2"]          = "2-toaline korter",
            ["movingSize.opt3"]          = "3-toaline korter",
            ["movingSize.opt4"]          = "4-toaline või suurem",
            ["movingSize.opt5"]          = "Kontor või äripind",
            ["movingSize.opt6"]          = "Klient ei ole kindel",
            ["movingAccess.label"]       = "Korrus ja lift",
            ["movingAccess.opt1"]        = "Maja või esimene korrus",
            ["movingAccess.opt2"]        = "Korrus liftiga",
            ["movingAccess.opt3"]        = "2.–3. korrus, liftita",
            ["movingAccess.opt4"]        = "4. korrus või kõrgem, liftita",
            ["movingAccess.opt5"]        = "Klient ei ole kindel",
            // Chips for the two ends are copied from movingAccess above by
            // WithSharedAccessChips — only the labels differ.
            ["movingAccessFrom.label"]   = "Juurdepääs lähtekohas",
            ["movingAccessTo.label"]     = "Juurdepääs sihtkohas",
            ["movingHeavyItems.label"]   = "Rasked või keerukad esemed",
            ["movingHeavyItems.opt1"]    = "Midagi erilist ei ole",
            ["movingHeavyItems.opt2"]    = "Klaver",
            ["movingHeavyItems.opt3"]    = "Seif, jõusaali seadmed või masin",
            ["movingHeavyItems.opt4"]    = "Akvaarium, kunstiteos või muu õrn ese",
            ["movingHeavyItems.opt5"]    = "Mitu neist",
            ["movingHeavyItems.opt6"]    = "Klient ei ole kindel",
            ["packingHelp.label"]        = "Pakkimisabi",
            ["packingHelp.opt1"]         = "Jah — pakkige kõik ära",
            ["packingHelp.opt2"]         = "Ainult õrnad ja suured esemed",
            ["packingHelp.opt3"]         = "Ainult kastid ja pakkematerjal",
            ["packingHelp.opt4"]         = "Ei — klient pakib ise",
            ["trailerDuration.label"]    = "Haagise rendiperiood",
            ["trailerDuration.opt1"]     = "Paar tundi",
            ["trailerDuration.opt2"]     = "Üks päev",
            ["trailerDuration.opt3"]     = "2–3 päeva",
            ["trailerDuration.opt4"]     = "Nädal või rohkem",
            ["trailerDuration.opt5"]     = "Klient ei ole kindel",
            ["trailerType.label"]        = "Veos",
            ["trailerType.opt1"]         = "Mööbel või kolimiskraam",
            ["trailerType.opt2"]         = "Ehitusmaterjal või aiajäätmed",
            ["trailerType.opt3"]         = "Tehnika või ATV",
            ["trailerType.opt4"]         = "Paat või jett",
            ["trailerType.opt5"]         = "Klient ei ole kindel",
            ["trailerTow.label"]         = "Haakeseade ja juhiluba",
            ["trailerTow.opt1"]          = "Auto haakeseadmega, B-kategooria (kuni 750 kg)",
            ["trailerTow.opt2"]          = "Auto haakeseadmega, BE-kategooria (üle 750 kg)",
            ["trailerTow.opt3"]          = "Haakeseade on olemas, kategooria pole teada",
            ["trailerTow.opt4"]          = "Haakeseadet ei ole — vajab ka autot",
            ["trailerTow.opt5"]          = "Klient ei ole kindel",
            ["vanrentalDriver.label"]    = "Juht",
            ["vanrentalDriver.opt1"]     = "Ilma juhita — klient sõidab ise",
            ["vanrentalDriver.opt2"]     = "Koos juhiga",
            ["vanrentalDriver.opt3"]     = "Koos juhi ja laadijatega",
            ["vanrentalDriver.opt4"]     = "Ükskõik kumb — kumb on odavam",
            ["vanrentalDriver.opt5"]     = "Klient ei ole kindel",
            ["vanrentalDuration.label"]  = "Kaubiku rendiperiood",
            ["vanrentalDuration.opt1"]   = "Paar tundi",
            ["vanrentalDuration.opt2"]   = "Üks päev",
            ["vanrentalDuration.opt3"]   = "2–3 päeva",
            ["vanrentalDuration.opt4"]   = "Nädal või rohkem",
            ["vanrentalDuration.opt5"]   = "Klient ei ole kindel",
            ["vanrentalSize.label"]      = "Kaubiku suurus",
            ["vanrentalSize.opt1"]       = "Väike (kuni ~6 m³)",
            ["vanrentalSize.opt2"]       = "Keskmine (~8–12 m³)",
            ["vanrentalSize.opt3"]       = "Suur (~15 m³ või rohkem)",
            ["vanrentalSize.opt4"]       = "Klient ei ole kindel",
            ["cleaningType.label"]       = "Koristuse liik",
            ["cleaningType.opt1"]        = "Koristus vanas kodus",
            ["cleaningType.opt2"]        = "Koristus uues kodus",
            ["cleaningType.opt3"]        = "Remondijärgne koristus",
            ["cleaningType.opt4"]        = "Regulaarne koristus",
            ["cleaningType.opt5"]        = "Klient ei ole kindel",
            ["cleaningSize.label"]       = "Pinna suurus",
            ["cleaningSize.opt1"]        = "Kuni 40 m² (1 tuba)",
            ["cleaningSize.opt2"]        = "40–70 m² (2 tuba)",
            ["cleaningSize.opt3"]        = "70–110 m² (3–4 tuba)",
            ["cleaningSize.opt4"]        = "Üle 110 m² või maja",
            ["cleaningSize.opt5"]        = "Klient ei ole kindel",
            ["cleaningExtras.label"]     = "Lisatööd",
            ["cleaningExtras.opt1"]      = "Ainult tavakoristus",
            ["cleaningExtras.opt2"]      = "Aknad",
            ["cleaningExtras.opt3"]      = "Ahi",
            ["cleaningExtras.opt4"]      = "Külmik",
            ["cleaningExtras.opt5"]      = "Aknad, ahi ja külmik",
            ["cleaningExtras.opt6"]      = "Klient ei ole kindel",
        });

    private static readonly IReadOnlyDictionary<string, string> ScopeEn =
        WithSharedAccessChips(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["warehouseSize.label"]      = "Space needed",
            ["warehouseSize.opt1"]       = "A few boxes (~1–2 m²)",
            ["warehouseSize.opt2"]       = "Small room (~3–5 m²)",
            ["warehouseSize.opt3"]       = "Room (~5–10 m²)",
            ["warehouseSize.opt4"]       = "1-bedroom flat (~10–15 m²)",
            ["warehouseSize.opt5"]       = "House or more (15+ m²)",
            ["warehouseSize.opt6"]       = "The customer is not sure",
            ["warehouseDuration.label"]  = "Storage period",
            ["warehouseDuration.opt1"]   = "Under a month",
            ["warehouseDuration.opt2"]   = "1–3 months",
            ["warehouseDuration.opt3"]   = "3–12 months",
            ["warehouseDuration.opt4"]   = "Over a year",
            ["warehouseDuration.opt5"]   = "The customer is not sure",
            ["warehouseGoods.label"]     = "What is being stored",
            ["warehouseGoods.opt1"]      = "Household items and furniture",
            ["warehouseGoods.opt2"]      = "Boxes, documents or business stock",
            ["warehouseGoods.opt3"]      = "A car, motorcycle or boat",
            ["warehouseGoods.opt4"]      = "Tools, machinery or building materials",
            ["warehouseGoods.opt5"]      = "Climate-sensitive goods (wine, electronics, instruments)",
            ["warehouseGoods.opt6"]      = "The customer is not sure",
            ["movingSize.label"]         = "Home size",
            ["movingSize.opt1"]          = "Studio",
            ["movingSize.opt2"]          = "1-bedroom",
            ["movingSize.opt3"]          = "2-bedroom",
            ["movingSize.opt4"]          = "3-bedroom or larger",
            ["movingSize.opt5"]          = "Office or business",
            ["movingSize.opt6"]          = "The customer is not sure",
            ["movingAccess.label"]       = "Floor and lift",
            ["movingAccess.opt1"]        = "House or ground floor",
            ["movingAccess.opt2"]        = "Upper floor with a lift",
            ["movingAccess.opt3"]        = "2nd–3rd floor, no lift",
            ["movingAccess.opt4"]        = "4th floor or higher, no lift",
            ["movingAccess.opt5"]        = "The customer is not sure",
            ["movingAccessFrom.label"]   = "Access at pickup",
            ["movingAccessTo.label"]     = "Access at destination",
            ["movingHeavyItems.label"]   = "Heavy or awkward items",
            ["movingHeavyItems.opt1"]    = "Nothing unusual",
            ["movingHeavyItems.opt2"]    = "A piano",
            ["movingHeavyItems.opt3"]    = "A safe, gym equipment or a machine",
            ["movingHeavyItems.opt4"]    = "An aquarium, artwork or something fragile",
            ["movingHeavyItems.opt5"]    = "Several of these",
            ["movingHeavyItems.opt6"]    = "The customer is not sure",
            ["packingHelp.label"]        = "Packing help",
            ["packingHelp.opt1"]         = "Yes — pack everything",
            ["packingHelp.opt2"]         = "Only fragile and bulky items",
            ["packingHelp.opt3"]         = "Just boxes and packing materials",
            ["packingHelp.opt4"]         = "No — the customer packs themselves",
            ["trailerDuration.label"]    = "Trailer rental period",
            ["trailerDuration.opt1"]     = "A few hours",
            ["trailerDuration.opt2"]     = "One day",
            ["trailerDuration.opt3"]     = "2–3 days",
            ["trailerDuration.opt4"]     = "A week or more",
            ["trailerDuration.opt5"]     = "The customer is not sure",
            ["trailerType.label"]        = "Cargo",
            ["trailerType.opt1"]         = "Furniture or moving boxes",
            ["trailerType.opt2"]         = "Building materials or garden waste",
            ["trailerType.opt3"]         = "Machinery or an ATV",
            ["trailerType.opt4"]         = "A boat or jet ski",
            ["trailerType.opt5"]         = "The customer is not sure",
            ["trailerTow.label"]         = "Towing capability",
            ["trailerTow.opt1"]          = "Car with tow bar, licence B (up to 750 kg)",
            ["trailerTow.opt2"]          = "Car with tow bar, licence BE (over 750 kg)",
            ["trailerTow.opt3"]          = "Has a tow bar, unsure about licence class",
            ["trailerTow.opt4"]          = "No tow bar — needs a vehicle too",
            ["trailerTow.opt5"]          = "The customer is not sure",
            ["vanrentalDriver.label"]    = "Driver",
            ["vanrentalDriver.opt1"]     = "Without a driver — the customer drives",
            ["vanrentalDriver.opt2"]     = "With a driver",
            ["vanrentalDriver.opt3"]     = "With a driver and loaders",
            ["vanrentalDriver.opt4"]     = "Either — whichever is cheaper",
            ["vanrentalDriver.opt5"]     = "The customer is not sure",
            ["vanrentalDuration.label"]  = "Van rental period",
            ["vanrentalDuration.opt1"]   = "A few hours",
            ["vanrentalDuration.opt2"]   = "One day",
            ["vanrentalDuration.opt3"]   = "2–3 days",
            ["vanrentalDuration.opt4"]   = "A week or more",
            ["vanrentalDuration.opt5"]   = "The customer is not sure",
            ["vanrentalSize.label"]      = "Van size",
            ["vanrentalSize.opt1"]       = "Small (up to ~6 m³)",
            ["vanrentalSize.opt2"]       = "Medium (~8–12 m³)",
            ["vanrentalSize.opt3"]       = "Large (~15 m³ or more)",
            ["vanrentalSize.opt4"]       = "The customer is not sure",
            ["cleaningType.label"]       = "Cleaning type",
            ["cleaningType.opt1"]        = "Move-out cleaning",
            ["cleaningType.opt2"]        = "Move-in cleaning",
            ["cleaningType.opt3"]        = "After renovation",
            ["cleaningType.opt4"]        = "Regular cleaning",
            ["cleaningType.opt5"]        = "The customer is not sure",
            ["cleaningSize.label"]       = "Property size",
            ["cleaningSize.opt1"]        = "Up to 40 m² (1 room)",
            ["cleaningSize.opt2"]        = "40–70 m² (2 rooms)",
            ["cleaningSize.opt3"]        = "70–110 m² (3–4 rooms)",
            ["cleaningSize.opt4"]        = "Over 110 m² or a house",
            ["cleaningSize.opt5"]        = "The customer is not sure",
            ["cleaningExtras.label"]     = "Extras",
            ["cleaningExtras.opt1"]      = "Standard clean only",
            ["cleaningExtras.opt2"]      = "Windows",
            ["cleaningExtras.opt3"]      = "Oven",
            ["cleaningExtras.opt4"]      = "Fridge",
            ["cleaningExtras.opt5"]      = "Windows, oven and fridge",
            ["cleaningExtras.opt6"]      = "The customer is not sure",
        });

    private static readonly IReadOnlyDictionary<string, string> ScopeRu =
        WithSharedAccessChips(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["warehouseSize.label"]      = "Требуемая площадь",
            ["warehouseSize.opt1"]       = "Пара коробок (~1–2 м²)",
            ["warehouseSize.opt2"]       = "Небольшая комната (~3–5 м²)",
            ["warehouseSize.opt3"]       = "Комната (~5–10 м²)",
            ["warehouseSize.opt4"]       = "2-комнатная (~10–15 м²)",
            ["warehouseSize.opt5"]       = "Дом или больше (15+ м²)",
            ["warehouseSize.opt6"]       = "Клиент пока не знает",
            ["warehouseDuration.label"]  = "Срок хранения",
            ["warehouseDuration.opt1"]   = "Меньше месяца",
            ["warehouseDuration.opt2"]   = "1–3 месяца",
            ["warehouseDuration.opt3"]   = "3–12 месяцев",
            ["warehouseDuration.opt4"]   = "Больше года",
            ["warehouseDuration.opt5"]   = "Клиент пока не знает",
            ["warehouseGoods.label"]     = "Предмет хранения",
            ["warehouseGoods.opt1"]      = "Домашние вещи и мебель",
            ["warehouseGoods.opt2"]      = "Коробки, документы или товар компании",
            ["warehouseGoods.opt3"]      = "Автомобиль, мотоцикл или лодка",
            ["warehouseGoods.opt4"]      = "Инструменты, техника или стройматериалы",
            ["warehouseGoods.opt5"]      = "Требует климат-контроля (вино, электроника, музыкальные инструменты)",
            ["warehouseGoods.opt6"]      = "Клиент пока не знает",
            ["movingSize.label"]         = "Размер жилья",
            ["movingSize.opt1"]          = "Студия или 1-комнатная",
            ["movingSize.opt2"]          = "2-комнатная квартира",
            ["movingSize.opt3"]          = "3-комнатная квартира",
            ["movingSize.opt4"]          = "4-комнатная и больше",
            ["movingSize.opt5"]          = "Офис или бизнес",
            ["movingSize.opt6"]          = "Клиент пока не знает",
            ["movingAccess.label"]       = "Этаж и лифт",
            ["movingAccess.opt1"]        = "Дом или первый этаж",
            ["movingAccess.opt2"]        = "Этаж с лифтом",
            ["movingAccess.opt3"]        = "2–3 этаж, без лифта",
            ["movingAccess.opt4"]        = "4 этаж и выше, без лифта",
            ["movingAccess.opt5"]        = "Клиент пока не знает",
            ["movingAccessFrom.label"]   = "Доступ по адресу отправления",
            ["movingAccessTo.label"]     = "Доступ по адресу назначения",
            ["movingHeavyItems.label"]   = "Тяжёлые или негабаритные предметы",
            ["movingHeavyItems.opt1"]    = "Ничего необычного",
            ["movingHeavyItems.opt2"]    = "Пианино",
            ["movingHeavyItems.opt3"]    = "Сейф, тренажёры или станок",
            ["movingHeavyItems.opt4"]    = "Аквариум, картина или что-то хрупкое",
            ["movingHeavyItems.opt5"]    = "Несколько из перечисленного",
            ["movingHeavyItems.opt6"]    = "Клиент пока не знает",
            ["packingHelp.label"]        = "Помощь с упаковкой",
            ["packingHelp.opt1"]         = "Да — упаковать всё",
            ["packingHelp.opt2"]         = "Только хрупкое и крупногабаритное",
            ["packingHelp.opt3"]         = "Только коробки и упаковочные материалы",
            ["packingHelp.opt4"]         = "Нет — клиент упакует сам",
            ["trailerDuration.label"]    = "Срок аренды прицепа",
            ["trailerDuration.opt1"]     = "Пара часов",
            ["trailerDuration.opt2"]     = "Один день",
            ["trailerDuration.opt3"]     = "2–3 дня",
            ["trailerDuration.opt4"]     = "Неделя или больше",
            ["trailerDuration.opt5"]     = "Клиент пока не знает",
            ["trailerType.label"]        = "Груз",
            ["trailerType.opt1"]         = "Мебель или вещи при переезде",
            ["trailerType.opt2"]         = "Стройматериалы или садовый мусор",
            ["trailerType.opt3"]         = "Техника или квадроцикл",
            ["trailerType.opt4"]         = "Лодка или гидроцикл",
            ["trailerType.opt5"]         = "Клиент пока не знает",
            ["trailerTow.label"]         = "Фаркоп и категория прав",
            ["trailerTow.opt1"]          = "Автомобиль с фаркопом, категория B (до 750 кг)",
            ["trailerTow.opt2"]          = "Автомобиль с фаркопом, категория BE (свыше 750 кг)",
            ["trailerTow.opt3"]          = "Фаркоп есть, категория прав неизвестна",
            ["trailerTow.opt4"]          = "Фаркопа нет — нужен и автомобиль",
            ["trailerTow.opt5"]          = "Клиент пока не знает",
            ["vanrentalDriver.label"]    = "Водитель",
            ["vanrentalDriver.opt1"]     = "Без водителя — клиент едет сам",
            ["vanrentalDriver.opt2"]     = "С водителем",
            ["vanrentalDriver.opt3"]     = "С водителем и грузчиками",
            ["vanrentalDriver.opt4"]     = "Любой вариант — что дешевле",
            ["vanrentalDriver.opt5"]     = "Клиент пока не знает",
            ["vanrentalDuration.label"]  = "Срок аренды фургона",
            ["vanrentalDuration.opt1"]   = "Пара часов",
            ["vanrentalDuration.opt2"]   = "Один день",
            ["vanrentalDuration.opt3"]   = "2–3 дня",
            ["vanrentalDuration.opt4"]   = "Неделя или больше",
            ["vanrentalDuration.opt5"]   = "Клиент пока не знает",
            ["vanrentalSize.label"]      = "Размер фургона",
            ["vanrentalSize.opt1"]       = "Небольшой (до ~6 м³)",
            ["vanrentalSize.opt2"]       = "Средний (~8–12 м³)",
            ["vanrentalSize.opt3"]       = "Большой (~15 м³ и больше)",
            ["vanrentalSize.opt4"]       = "Клиент пока не знает",
            ["cleaningType.label"]       = "Тип уборки",
            ["cleaningType.opt1"]        = "Уборка при выезде",
            ["cleaningType.opt2"]        = "Уборка при заезде",
            ["cleaningType.opt3"]        = "После ремонта",
            ["cleaningType.opt4"]        = "Регулярная уборка",
            ["cleaningType.opt5"]        = "Клиент пока не знает",
            ["cleaningSize.label"]       = "Площадь",
            ["cleaningSize.opt1"]        = "До 40 м² (1 комната)",
            ["cleaningSize.opt2"]        = "40–70 м² (2 комнаты)",
            ["cleaningSize.opt3"]        = "70–110 м² (3–4 комнаты)",
            ["cleaningSize.opt4"]        = "Более 110 м² или дом",
            ["cleaningSize.opt5"]        = "Клиент пока не знает",
            ["cleaningExtras.label"]     = "Дополнительно",
            ["cleaningExtras.opt1"]      = "Только стандартная уборка",
            ["cleaningExtras.opt2"]      = "Окна",
            ["cleaningExtras.opt3"]      = "Духовка",
            ["cleaningExtras.opt4"]      = "Холодильник",
            ["cleaningExtras.opt5"]      = "Окна, духовка и холодильник",
            ["cleaningExtras.opt6"]      = "Клиент пока не знает",
        });

    private static readonly IReadOnlyDictionary<string, string> ScopeLv =
        WithSharedAccessChips(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["warehouseSize.label"]      = "Nepieciešamā platība",
            ["warehouseSize.opt1"]       = "Dažas kastes (~1–2 m²)",
            ["warehouseSize.opt2"]       = "Maza telpa (~3–5 m²)",
            ["warehouseSize.opt3"]       = "Istaba (~5–10 m²)",
            ["warehouseSize.opt4"]       = "2-ist. dzīvoklis (~10–15 m²)",
            ["warehouseSize.opt5"]       = "Māja vai vairāk (15+ m²)",
            ["warehouseSize.opt6"]       = "Klients vēl nezina",
            ["warehouseDuration.label"]  = "Glabāšanas periods",
            ["warehouseDuration.opt1"]   = "Mazāk par mēnesi",
            ["warehouseDuration.opt2"]   = "1–3 mēneši",
            ["warehouseDuration.opt3"]   = "3–12 mēneši",
            ["warehouseDuration.opt4"]   = "Vairāk par gadu",
            ["warehouseDuration.opt5"]   = "Klients vēl nezina",
            ["warehouseGoods.label"]     = "Uzglabājamā manta",
            ["warehouseGoods.opt1"]      = "Mājsaimniecības mantas un mēbeles",
            ["warehouseGoods.opt2"]      = "Kastes, dokumenti vai uzņēmuma prece",
            ["warehouseGoods.opt3"]      = "Automašīna, motocikls vai laiva",
            ["warehouseGoods.opt4"]      = "Instrumenti, tehnika vai būvmateriāli",
            ["warehouseGoods.opt5"]      = "Klimatam jutīgas preces (vīns, elektronika, mūzikas instrumenti)",
            ["warehouseGoods.opt6"]      = "Klients vēl nezina",
            ["movingSize.label"]         = "Mājokļa lielums",
            ["movingSize.opt1"]          = "Studija vai 1 istaba",
            ["movingSize.opt2"]          = "2 istabu dzīvoklis",
            ["movingSize.opt3"]          = "3 istabu dzīvoklis",
            ["movingSize.opt4"]          = "4 istabas vai vairāk",
            ["movingSize.opt5"]          = "Birojs vai uzņēmums",
            ["movingSize.opt6"]          = "Klients vēl nezina",
            ["movingAccess.label"]       = "Stāvs un lifts",
            ["movingAccess.opt1"]        = "Māja vai pirmais stāvs",
            ["movingAccess.opt2"]        = "Stāvs ar liftu",
            ["movingAccess.opt3"]        = "2.–3. stāvs, bez lifta",
            ["movingAccess.opt4"]        = "4. stāvs vai augstāk, bez lifta",
            ["movingAccess.opt5"]        = "Klients vēl nezina",
            ["movingAccessFrom.label"]   = "Piekļuve sākuma adresē",
            ["movingAccessTo.label"]     = "Piekļuve galamērķa adresē",
            ["movingHeavyItems.label"]   = "Smagi vai neērti priekšmeti",
            ["movingHeavyItems.opt1"]    = "Nekas neparasts",
            ["movingHeavyItems.opt2"]    = "Klavieres",
            ["movingHeavyItems.opt3"]    = "Seifs, trenažieri vai iekārta",
            ["movingHeavyItems.opt4"]    = "Akvārijs, mākslas darbs vai kas trausls",
            ["movingHeavyItems.opt5"]    = "Vairāki no minētajiem",
            ["movingHeavyItems.opt6"]    = "Klients vēl nezina",
            ["packingHelp.label"]        = "Palīdzība ar pakošanu",
            ["packingHelp.opt1"]         = "Jā — sapakot visu",
            ["packingHelp.opt2"]         = "Tikai trauslās un lielgabarīta lietas",
            ["packingHelp.opt3"]         = "Tikai kastes un iepakojuma materiāli",
            ["packingHelp.opt4"]         = "Nē — klients pakos pats",
            ["trailerDuration.label"]    = "Piekabes nomas periods",
            ["trailerDuration.opt1"]     = "Dažas stundas",
            ["trailerDuration.opt2"]     = "Viena diena",
            ["trailerDuration.opt3"]     = "2–3 dienas",
            ["trailerDuration.opt4"]     = "Nedēļa vai ilgāk",
            ["trailerDuration.opt5"]     = "Klients vēl nezina",
            ["trailerType.label"]        = "Krava",
            ["trailerType.opt1"]         = "Mēbeles vai pārvākšanās mantas",
            ["trailerType.opt2"]         = "Būvmateriāli vai dārza atkritumi",
            ["trailerType.opt3"]         = "Tehnika vai kvadricikls",
            ["trailerType.opt4"]         = "Laiva vai ūdens motocikls",
            ["trailerType.opt5"]         = "Klients vēl nezina",
            ["trailerTow.label"]         = "Sakabes āķis un kategorija",
            ["trailerTow.opt1"]          = "Auto ar sakabes āķi, B kategorija (līdz 750 kg)",
            ["trailerTow.opt2"]          = "Auto ar sakabes āķi, BE kategorija (virs 750 kg)",
            ["trailerTow.opt3"]          = "Sakabes āķis ir, kategorija nav zināma",
            ["trailerTow.opt4"]          = "Sakabes āķa nav — vajadzīgs arī auto",
            ["trailerTow.opt5"]          = "Klients vēl nezina",
            ["vanrentalDriver.label"]    = "Vadītājs",
            ["vanrentalDriver.opt1"]     = "Bez vadītāja — klients brauc pats",
            ["vanrentalDriver.opt2"]     = "Ar vadītāju",
            ["vanrentalDriver.opt3"]     = "Ar vadītāju un krāvējiem",
            ["vanrentalDriver.opt4"]     = "Jebkurš variants — kas lētāk",
            ["vanrentalDriver.opt5"]     = "Klients vēl nezina",
            ["vanrentalDuration.label"]  = "Furgona nomas periods",
            ["vanrentalDuration.opt1"]   = "Dažas stundas",
            ["vanrentalDuration.opt2"]   = "Viena diena",
            ["vanrentalDuration.opt3"]   = "2–3 dienas",
            ["vanrentalDuration.opt4"]   = "Nedēļa vai ilgāk",
            ["vanrentalDuration.opt5"]   = "Klients vēl nezina",
            ["vanrentalSize.label"]      = "Furgona izmērs",
            ["vanrentalSize.opt1"]       = "Mazs (līdz ~6 m³)",
            ["vanrentalSize.opt2"]       = "Vidējs (~8–12 m³)",
            ["vanrentalSize.opt3"]       = "Liels (~15 m³ vai vairāk)",
            ["vanrentalSize.opt4"]       = "Klients vēl nezina",
            ["cleaningType.label"]       = "Uzkopšanas veids",
            ["cleaningType.opt1"]        = "Uzkopšana pēc izvākšanās",
            ["cleaningType.opt2"]        = "Uzkopšana pirms ievākšanās",
            ["cleaningType.opt3"]        = "Pēc remonta",
            ["cleaningType.opt4"]        = "Regulāra uzkopšana",
            ["cleaningType.opt5"]        = "Klients vēl nezina",
            ["cleaningSize.label"]       = "Platība",
            ["cleaningSize.opt1"]        = "Līdz 40 m² (1 istaba)",
            ["cleaningSize.opt2"]        = "40–70 m² (2 istabas)",
            ["cleaningSize.opt3"]        = "70–110 m² (3–4 istabas)",
            ["cleaningSize.opt4"]        = "Vairāk nekā 110 m² vai māja",
            ["cleaningSize.opt5"]        = "Klients vēl nezina",
            ["cleaningExtras.label"]     = "Papildu darbi",
            ["cleaningExtras.opt1"]      = "Tikai standarta uzkopšana",
            ["cleaningExtras.opt2"]      = "Logi",
            ["cleaningExtras.opt3"]      = "Cepeškrāsns",
            ["cleaningExtras.opt4"]      = "Ledusskapis",
            ["cleaningExtras.opt5"]      = "Logi, cepeškrāsns un ledusskapis",
            ["cleaningExtras.opt6"]      = "Klients vēl nezina",
        });

    private static readonly IReadOnlyDictionary<string, string> ScopeLt =
        WithSharedAccessChips(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["warehouseSize.label"]      = "Reikalingas plotas",
            ["warehouseSize.opt1"]       = "Kelios dėžės (~1–2 m²)",
            ["warehouseSize.opt2"]       = "Maža patalpa (~3–5 m²)",
            ["warehouseSize.opt3"]       = "Kambarys (~5–10 m²)",
            ["warehouseSize.opt4"]       = "2 kambarių butas (~10–15 m²)",
            ["warehouseSize.opt5"]       = "Namas ar daugiau (15+ m²)",
            ["warehouseSize.opt6"]       = "Klientas dar nežino",
            ["warehouseDuration.label"]  = "Saugojimo laikotarpis",
            ["warehouseDuration.opt1"]   = "Trumpiau nei mėnuo",
            ["warehouseDuration.opt2"]   = "1–3 mėnesiai",
            ["warehouseDuration.opt3"]   = "3–12 mėnesių",
            ["warehouseDuration.opt4"]   = "Ilgiau nei metai",
            ["warehouseDuration.opt5"]   = "Klientas dar nežino",
            ["warehouseGoods.label"]     = "Saugomas turtas",
            ["warehouseGoods.opt1"]      = "Namų apyvokos daiktai ir baldai",
            ["warehouseGoods.opt2"]      = "Dėžės, dokumentai ar įmonės prekės",
            ["warehouseGoods.opt3"]      = "Automobilis, motociklas ar valtis",
            ["warehouseGoods.opt4"]      = "Įrankiai, technika ar statybinės medžiagos",
            ["warehouseGoods.opt5"]      = "Klimatui jautrios prekės (vynas, elektronika, muzikos instrumentai)",
            ["warehouseGoods.opt6"]      = "Klientas dar nežino",
            ["movingSize.label"]         = "Būsto dydis",
            ["movingSize.opt1"]          = "Studija ar 1 kambarys",
            ["movingSize.opt2"]          = "2 kambarių butas",
            ["movingSize.opt3"]          = "3 kambarių butas",
            ["movingSize.opt4"]          = "4 kambariai ar daugiau",
            ["movingSize.opt5"]          = "Biuras ar verslo patalpos",
            ["movingSize.opt6"]          = "Klientas dar nežino",
            ["movingAccess.label"]       = "Aukštas ir liftas",
            ["movingAccess.opt1"]        = "Namas arba pirmas aukštas",
            ["movingAccess.opt2"]        = "Aukštas su liftu",
            ["movingAccess.opt3"]        = "2–3 aukštas, be lifto",
            ["movingAccess.opt4"]        = "4 aukštas ar aukščiau, be lifto",
            ["movingAccess.opt5"]        = "Klientas dar nežino",
            ["movingAccessFrom.label"]   = "Patekimas pakrovimo vietoje",
            ["movingAccessTo.label"]     = "Patekimas paskirties vietoje",
            ["movingHeavyItems.label"]   = "Sunkūs ar nepatogūs daiktai",
            ["movingHeavyItems.opt1"]    = "Nieko neįprasto",
            ["movingHeavyItems.opt2"]    = "Pianinas",
            ["movingHeavyItems.opt3"]    = "Seifas, treniruokliai ar staklės",
            ["movingHeavyItems.opt4"]    = "Akvariumas, meno kūrinys ar kas nors trapaus",
            ["movingHeavyItems.opt5"]    = "Keli iš išvardytų",
            ["movingHeavyItems.opt6"]    = "Klientas dar nežino",
            ["packingHelp.label"]        = "Pakavimo pagalba",
            ["packingHelp.opt1"]         = "Taip — supakuoti viską",
            ["packingHelp.opt2"]         = "Tik trapūs ir dideli daiktai",
            ["packingHelp.opt3"]         = "Tik dėžės ir pakavimo medžiagos",
            ["packingHelp.opt4"]         = "Ne — klientas supakuos pats",
            ["trailerDuration.label"]    = "Priekabos nuomos laikotarpis",
            ["trailerDuration.opt1"]     = "Kelios valandos",
            ["trailerDuration.opt2"]     = "Viena diena",
            ["trailerDuration.opt3"]     = "2–3 dienos",
            ["trailerDuration.opt4"]     = "Savaitė ar ilgiau",
            ["trailerDuration.opt5"]     = "Klientas dar nežino",
            ["trailerType.label"]        = "Krovinys",
            ["trailerType.opt1"]         = "Baldai ar kraustymosi daiktai",
            ["trailerType.opt2"]         = "Statybinės medžiagos ar sodo atliekos",
            ["trailerType.opt3"]         = "Technika ar keturratis",
            ["trailerType.opt4"]         = "Valtis ar vandens motociklas",
            ["trailerType.opt5"]         = "Klientas dar nežino",
            ["trailerTow.label"]         = "Vilkimo kablys ir kategorija",
            ["trailerTow.opt1"]          = "Automobilis su vilkimo kabliu, B kategorija (iki 750 kg)",
            ["trailerTow.opt2"]          = "Automobilis su vilkimo kabliu, BE kategorija (virš 750 kg)",
            ["trailerTow.opt3"]          = "Vilkimo kablys yra, kategorija nežinoma",
            ["trailerTow.opt4"]          = "Vilkimo kablio nėra — reikia ir automobilio",
            ["trailerTow.opt5"]          = "Klientas dar nežino",
            ["vanrentalDriver.label"]    = "Vairuotojas",
            ["vanrentalDriver.opt1"]     = "Be vairuotojo — klientas vairuoja pats",
            ["vanrentalDriver.opt2"]     = "Su vairuotoju",
            ["vanrentalDriver.opt3"]     = "Su vairuotoju ir krovėjais",
            ["vanrentalDriver.opt4"]     = "Bet kuris variantas — kas pigiau",
            ["vanrentalDriver.opt5"]     = "Klientas dar nežino",
            ["vanrentalDuration.label"]  = "Furgono nuomos laikotarpis",
            ["vanrentalDuration.opt1"]   = "Kelios valandos",
            ["vanrentalDuration.opt2"]   = "Viena diena",
            ["vanrentalDuration.opt3"]   = "2–3 dienos",
            ["vanrentalDuration.opt4"]   = "Savaitė ar ilgiau",
            ["vanrentalDuration.opt5"]   = "Klientas dar nežino",
            ["vanrentalSize.label"]      = "Furgono dydis",
            ["vanrentalSize.opt1"]       = "Mažas (iki ~6 m³)",
            ["vanrentalSize.opt2"]       = "Vidutinis (~8–12 m³)",
            ["vanrentalSize.opt3"]       = "Didelis (~15 m³ ar daugiau)",
            ["vanrentalSize.opt4"]       = "Klientas dar nežino",
            ["cleaningType.label"]       = "Valymo tipas",
            ["cleaningType.opt1"]        = "Valymas išsikraustant",
            ["cleaningType.opt2"]        = "Valymas prieš įsikraustant",
            ["cleaningType.opt3"]        = "Po remonto",
            ["cleaningType.opt4"]        = "Reguliarus valymas",
            ["cleaningType.opt5"]        = "Klientas dar nežino",
            ["cleaningSize.label"]       = "Patalpų plotas",
            ["cleaningSize.opt1"]        = "Iki 40 m² (1 kambarys)",
            ["cleaningSize.opt2"]        = "40–70 m² (2 kambariai)",
            ["cleaningSize.opt3"]        = "70–110 m² (3–4 kambariai)",
            ["cleaningSize.opt4"]        = "Daugiau nei 110 m² arba namas",
            ["cleaningSize.opt5"]        = "Klientas dar nežino",
            ["cleaningExtras.label"]     = "Papildomi darbai",
            ["cleaningExtras.opt1"]      = "Tik standartinis valymas",
            ["cleaningExtras.opt2"]      = "Langai",
            ["cleaningExtras.opt3"]      = "Orkaitė",
            ["cleaningExtras.opt4"]      = "Šaldytuvas",
            ["cleaningExtras.opt5"]      = "Langai, orkaitė ir šaldytuvas",
            ["cleaningExtras.opt6"]      = "Klientas dar nežino",
        });

    private static readonly EmailStrings Et = new(
        PasswordResetSubject:       "Ruumly — parooli taastamine",
        PasswordResetGreeting:      "Tere,",
        PasswordResetBody1:         "Saime parooli taastamise taotluse teie Ruumly kontole",
        PasswordResetBody2:         "Klikkige alloleval nupul parooli vahetamiseks.",
        PasswordResetExpiry:        "Link kehtib <strong>2 tundi</strong>.",
        PasswordResetButton:        "Vaheta parool",
        PasswordResetCopyLabel:     "Või kopeerige see link oma brauserisse:",
        PasswordResetSecurityTitle: "⚠ Kui te seda taotlust ei teinud",
        PasswordResetSecurityBody:
            "Ignoreerige seda e-kirja — teie parool jääb muutmata ja keegi teine ei pääse " +
            "teie kontole ligi. Kui kahtlustate, et keegi üritab teie kontot kasutada, " +
            "võtke meiega ühendust:",
        PasswordResetContactUs:     "info@ruumly.eu",
        PasswordResetFooter:        "See on automaatne e-kiri. Palun ärge vastake sellele.",
        EmailVerifySubject:         "Ruumly — kinnitage oma e-posti aadress",
        EmailVerifyGreeting:        "Tere,",
        EmailVerifyBody:            "Täname registreerumise eest! Konto aktiveerimiseks kinnitage oma e-posti aadress.",
        EmailVerifyButton:          "Kinnita e-post",
        EmailVerifyExpiry:          "Link kehtib 24 tundi.",
        EmailVerifyFooter:          "See on automaatne e-kiri. Palun ärge vastake sellele.",
        BookingConfirmSubject:      "Ruumly — broneeringu kinnitus",
        BookingConfirmGreeting:     "Tere",
        BookingConfirmBody:         "Teie broneeringu taotlus on vastu võetud.",
        BookingConfirmService:      "Teenus",
        BookingConfirmStartDate:    "Alguskuupäev",
        BookingConfirmPeriod:       "Periood",
        BookingConfirmTotal:        "Kokku",
        BookingConfirmVat:          "sisaldab KM",
        BookingConfirmNext:
            "Partner võtab teiega ühendust kinnitamisel. Broneeringu staatust " +
            "saate jälgida oma kontol.",
        BookingConfirmViewButton:   "Vaata broneeringut",
        BookingConfirmFooter:       "See on automaatne e-kiri. Palun ärge vastake sellele.",
        BookingStatusConfirmedSubject: "Ruumly — broneering kinnitatud",
        BookingStatusConfirmedBody:    "Teie broneering #{id} on kinnitatud!",
        BookingStatusRejectedSubject:  "Ruumly — broneering tagasi lükatud",
        BookingStatusRejectedBody:     "Teie broneering #{id} on kahjuks tagasi lükatud",
        BookingStatusCompletedSubject: "Ruumly — broneering lõpetatud",
        BookingStatusCompletedBody:    "Teie broneering #{id} on lõpetatud",
        BookingStatusCancelledSubject: "Ruumly — broneering tühistatud",
        BookingStatusCancelledBody:    "Teie broneering #{id} on tühistatud",
        BookingStatusViewLink:         "Vaata broneeringut",
        TimelineBookingCreated:        "Broneering loodud",
        TimelineBookingCancelled:      "Broneering tühistatud",
        TimelineOrderApproved:         "Tellimus kinnitatud",
        TimelinePartnerConfirmed:      "Partner kinnitas",
        TimelineOrderRejected:         "Tellimus tagasi lükatud",
        TimelineServiceActive:         "Teenus on aktiivne",
        TimelineServiceCompleted:      "Teenus lõpetatud",
        TimelineStatusChanged:         "Staatus muudetud",
        NotifBookingConfirmed:         "Broneering kinnitatud",
        NotifBookingRejected:          "Broneering tagasi lükatud",
        NotifBookingCancelled:         "Broneering tühistatud",
        NotifNewMessage:               "Uus sõnum broneeringus",
        NotifBookingConfirmedBody:     "on kinnitatud",
        NotifBookingRejectedBody:      "Teie broneering lükati tagasi",
        NotifServiceActiveBody:        "teenus on aktiivne",
        NotifServiceCompletedBody:     "on lõpetatud",
        NotifBookingCancelledBody:     "on tühistatud",
        TimelineAwaitingApproval:      "Ootame kinnitust",
        TimelineManualApprovalNeeded:  "Tellimus vajab käsitsi kinnitust enne saatmist",
        EmailGreeting:                 "Tere",
        EmailNewOrder:                 "Ruumly platvormilt on saabunud uus tellimus.",
        EmailOrderDetails:             "TELLIMUSE ANDMED",
        EmailOrderNumber:              "Tellimuse nr",
        EmailService:                  "Teenus",
        EmailType:                     "Tüüp",
        EmailClient:                   "KLIENT",
        EmailName:                     "Nimi",
        EmailPhone:                    "Telefon",
        EmailDetails:                  "DETAILID",
        EmailStartDate:                "Alguskuupäev",
        EmailEndDate:                  "Lõppkuupäev",
        EmailPeriod:                   "Periood",
        EmailExtras:                   "Lisateenused",
        EmailPrice:                    "HIND",
        EmailPartnerPrice:             "Partneri hind",
        EmailTotalPartner:             "Kokku partnerile",
        EmailNotes:                    "MÄRKUSED",
        EmailConfirmRequest:           "Palun kinnitage tellimus 2 tunni jooksul.",
        EmailConfirmInstructions:      "Kinnitamiseks vastake sellele e-kirjale märksõnaga KINNITAN\nvõi logige sisse Ruumly partneripaneeli.",
        EmailRegards:                  "Lugupidamisega,",
        EmailTypeWarehouse:            "Laopind",
        EmailTypeMoving:               "Kolimine",
        EmailTypeTrailer:              "Haagise rent",
        AbandonedSubject:              "Teie broneering ootab kinnitust",
        AbandonedGreeting:             "Tere",
        AbandonedBody:                 "Märkasime, et alustasite broneeringut, kuid makse on veel tegemata.",
        AbandonedService:              "Teenus",
        AbandonedTotal:                "Summa",
        AbandonedCta:                  "Lõpetage broneering",
        ReservationExpiredSubject:     "Broneering aegus",
        ReservationExpiredGreeting:    "Tere {name},",
        ReservationExpiredBody:        "Teie broneering teenusele \"{listing}\" aegus, kuna makset ei laekunud 24 tunni jooksul.",
        ReservationExpiredCta:         "Broneerige uuesti",
        RefundInitiatedTitle:          "Tagasimakse algatatud",
        RefundInitiatedDesc:           "Tagasimakse broneeringu #{bookingRef} jaoks on algatatud. Summa kantakse teie kontole 3–5 tööpäeva jooksul.",
        SupplierWelcomeSubject:        "Tere tulemast Ruumlysse!",
        SupplierWelcomeBodyTpl:        "Tere {name}!\n\nTeie taotlus on heaks kiidetud. Saate nüüd sisse logida ja oma kuulutusi hallata.\n\nTeretulemast!\n\nRuumly meeskond",
        QuoteReplySubject:             "Ruumly — teie hinnapakkumine",
        QuoteReplyBodyTpl:             "Tere {name}!\n\nPartner {partner} saatis hinnapakkumise teenusele \"{listing}\": {price}.\n\nVõtke partneriga otse ühendust või vastake sellele e-kirjale kokkuleppe sõlmimiseks.\n\nRuumly meeskond",
        OfferSubject:                  "Ruumly — teie pakkumised on valmis",
        OfferGreeting:                 "Tere!",
        OfferIntro:                    "Vaatasime teie päringu üle ja kogusime teile sobivad pakkumised. Siin on teie valikud:",
        OfferNoteLabel:                "Meie märkus:",
        OfferCta:                      "Vaadake pakkumisi ja valige sobiv:",
        OfferQuestions:                "Kui teil on küsimusi, vastake lihtsalt sellele e-kirjale.",
        OfferSignature:                "Ruumly meeskond\ninfo@ruumly.eu",
        OutreachSubjectTpl:            "{city}: {category} — kliendipäring",
        OutreachGreeting:              "Tere!",
        OutreachGreetingTpl:           "Tere, {company}!",
        OutreachIntro:                 "Ruumly aitab inimestel leida kohalikke teenusepakkujaid. Kirjutame teile, sest teie ettevõte pakub seda teenust selles piirkonnas ja on Ruumly kataloogis kirjas.",
        OutreachProvenanceTpl:         "Klient esitas selle päringu meie veebilehel {date}. Vastamine on tasuta ja mittesiduv ning hinna esitamiseks ei ole kontot vaja.",
        OutreachAsk:                   "Kas saate selle töö vastu võtta? Piisab lühikesest vastusest: kas aeg sobib ja milline oleks ligikaudne hind. Kui saadate hinna, saame selle kliendile edasi anda; kui klient teie pakkumise valib, viime teid omavahel kokku.",
        OutreachCannotPrice:           "Kui te ei saa selle info põhjal hinda anda, märkige samal lehel, mida on juurde vaja — küsime kliendilt üle.",
        OutreachLabelService:          "Teenus",
        OutreachLabelLocation:         "Asukoht",
        OutreachLabelDate:             "Soovitud aeg",
        OutreachLabelDetails:          "Lisainfo",
        OutreachLabelPhotos:           "Pildid",
        OutreachDateAsap:              "esimesel võimalusel — klient kuupäeva ei märkinud, täpsustame selle üle",
        OutreachDateFlexible:          "klient on kuupäevaga paindlik — pakkuge teile sobiv aeg",
        OutreachPhotosTpl:             "Klient lisas {count} pilti — need on nähtavad hinna esitamise lehel.",
        OutreachDetailsMissing:        "klient ei täpsustanud — küsime tema käest üle",
        OutreachPackingAddOn:          "Klient soovib lisaks pakkimisabi — palun arvestage see oma hinna sisse.",
        OutreachUrgentBadge:           "KIIRE",
        OutreachUrgentTpl:             "KIIRE: klient vajab teenust {date}",
        OutreachQuoteCta:              "Esitage oma hind",
        OutreachReplyAlternative:      "Või vastake lihtsalt sellele e-kirjale koos hinnaga — kiri jõuab otse meie meeskonnani.",
        OutreachSignature:             "Ruumly meeskond\ninfo@ruumly.eu\nhttps://ruumly.eu",
        OutreachQuestionsTpl:          "Küsimused? Vastake sellele kirjale või kirjutage meile kontaktivormi kaudu: {url}",
        AckSubject:                    "Sinu päring on meil käes — Ruumly",
        AckGreetingTpl:                "Tere, {name}!",
        AckGreetingNoName:             "Tere!",
        AckReceived:                   "Sinu päring jõudis meile kohale. Vaatame selle üle ja küsime teenusepakkujatelt hinnad.",
        AckSummaryHeading:             "Mida sa küsisid:",
        AckLabelService:               "Teenus",
        AckLabelCity:                  "Asukoht",
        AckLabelDate:                  "Soovitud aeg",
        AckLabelDetails:               "Täpsustused",
        AckDateAsap:                   "esimesel võimalusel",
        AckWhatNext:                   "Mis edasi saab: võtame ühendust piirkonna pakkujatega ja saadame sulle koondatud pakkumised. Kui midagi jääb segaseks, küsime sinult üle.",
        // Formal (teie): "näete", "hoidke", "Vaadake" — the register the rest of
        // the Estonian mail corpus uses. The pronoun itself is left out on
        // purpose: the four Ack strings around this one are still informal
        // ("Sinu päring", "Mida sa küsisid"), and a bare "Teie" three lines
        // under "sinult" is the seam a reader notices first. Dropping it keeps
        // the formal verbs without putting the two pronouns side by side. The
        // real fix is to make the whole Ack block formal like et's every other
        // customer mail — that is founder copy, not a refactor.
        AckStatusLine:                 "Päringul on oma leht, kust näete, mis seisus see parasjagu on. Link on isiklik — palun hoidke see endale.",
        AckStatusCta:                  "Vaadake oma päringut",
        AckReply:                      "Kui midagi muutub — kuupäev, kogus, aadress — vasta lihtsalt sellele kirjale. Nii jõuab info otse meieni.",
        AckContactTpl:                 "Võid kirjutada ka siit: {url}",
        AckSignature:                  "Parimate soovidega\nRuumly meeskond\ninfo@ruumly.eu",
        IntroSubjectTpl:               "Kliendipäringud Ruumlyst — {company}",
        IntroGreeting:                 "Tere!",
        IntroOpening:                  "Kirjutame Ruumlyst, sest teie ettevõte pakub teenust, mida meie kliendid otsivad.",
        IntroWhoWeAre:                 "Ruumly aitab inimestel leida sobivaid kohalikke teenusepakkujaid üle Eesti. Meieni jõuavad inimesed, kellel on konkreetne vajadus – näiteks ladu, kolimine, kaubiku või haagise rent, koristus või pakkimine.",
        IntroForwarding:               "Kui päring sobib teie teenuse ja piirkonnaga, saadame selle teile edasi.",
        IntroNotTestRequests:          "Need ei ole testpäringud ega turundusnimekirjad. Iga Ruumly päring tuleb päris inimeselt, kes otsib teenust päriselt praegu.",
        IntroExpectHeading:            "Mida me teilt ootame?",
        IntroExpectIntro:              "Kui saate klienti aidata, vastake meie päringukirjale võimalikult lihtsalt:",
        IntroExpectBullet1:            "kas teil on soovitud ajal võimalus;",
        IntroExpectBullet2:            "mis oleks ligikaudne või lõplik hind;",
        IntroExpectBullet3:            "vajadusel mõni oluline tingimus või täpsustav küsimus.",
        IntroNoAccount:                "Kontot ei ole vaja luua, liitumistasu ei ole ja eraldi süsteemi kasutama ei pea.",
        IntroIfNotSuitable:            "Kui päring teile ei sobi, piisab ka lühikesest vastusest „ei ole võimalik“.",
        IntroWhyHeading:               "Miks on oluline pakkumisele vastata?",
        IntroWhyBody:                  "Klient tuli Ruumlysse selleks, et ta ei peaks ise kümneid ettevõtteid läbi helistama ja veebilehti võrdlema. Kui saame teilt hinna ja saadavuse, saame teie pakkumise kliendile konkreetselt edasi anda. Kui vastuseks tuleb ainult link kodulehele või me vastust ei saa, ei ole meil kahjuks võimalik teie pakkumist teiste võimaluste kõrval kliendile esitada.",
        IntroGoal:                     "Meie eesmärk on lihtne: tuua teile sobiv klient ja teha kliendil teieni jõudmine võimalikult lihtsaks.",
        IntroVolume:                   "Me ei luba kindlat päringute arvu ega igakuist tellimuste voogu. Ruumly on alles kasvamas. Kuid iga päring, mille saadame, on seotud konkreetse kliendi, asukoha, aja ja vajadusega.",
        IntroProfileHeading:           "Teie ettevõtte profiil Ruumlys",
        IntroProfileListedTpl:         "Oleme loonud paljudele Eesti teenusepakkujatele esmase profiili avalikult kättesaadava info põhjal. Ka {company} on meil juba kirjas.",
        IntroPriceList:                "Kui teil on olemas hinnakiri, tüüphinnad või lihtsad reeglid, mille järgi pakkumisi teete, võite need meile samuti saata. See aitab meil tulevikus teile ainult sobivamaid päringuid edastada.",
        IntroVisibilityLater:          "Kui profiil on korras, saab hiljem soovi korral oma kirjet ka tasu eest esile tõsta – eelisasetus otsingus ja teie piirkonna linnalehtedel, 24–29 € kuus. See on täiesti vabatahtlik: küsite meilt, lülitame sisse, maksate ülekandega. Automaatselt ei võeta kunagi midagi ja nimekirjas olemine on ja jääb tasuta.",
        IntroFinalAsk:                 "Kõige olulisem palve on aga lihtne: kui saate Ruumlylt päringu, mis teie teenusega sobib, palun vastake sellele. Selle kirja taga on päris klient, kes ootab lahendust.",
        IntroQuestionsTpl:             "Kui teil tekib küsimusi Ruumly, koostöö või päringute kohta, vastake lihtsalt sellele kirjale või kirjutage siit: {url}",
        IntroClaimIntro:               "Soovi korral saate oma profiili üle võtta ning kontaktid, teenused ja kirjelduse ise üle vaadata. See ei ole vajalik selleks, et meie kliendipäringutele vastata.",
        IntroClaimCta:                 "Võtke oma profiil üle",
        IntroClaimByEmailTpl:          "Kui soovite oma profiili andmeid parandada, kirjutage aadressile {email} ja me uuendame need.",
        IntroOptOutTpl:                "Kui te ei soovi Ruumlylt päringuid ega kirju saada, vastake sõnaga {keyword} ja eemaldame teie ettevõtte nimekirjast.",
        IntroOptOutLinkLabel:          "Eemaldage minu ettevõte",
        IntroSignature:                "Parimate soovidega\nRuumly meeskond\ninfo@ruumly.eu\nhttps://ruumly.eu",
        ClaimSubject:                  "Ruumly — kinnitage oma profiili ülevõtmine",
        ClaimGreeting:                 "Tere,",
        ClaimBodyTpl:                  "Keegi soovis üle võtta ettevõtte {company} Ruumly profiili. Kui see olite teie, kinnitage see allolevast nupust — seejärel saate oma andmeid ise parandada.",
        ClaimCta:                      "Kinnitage ja muutke profiili",
        ClaimExpiryTpl:                "Link töötab ühe korra ja aegub {hours} tunni pärast.",
        ClaimIgnoreTpl:                "Kui teie seda ei küsinud, jätke see kiri tähelepanuta — midagi ei muutu. Küsimuste korral kirjutage aadressile {email}.",
        ApplySignInSubject:            "Ruumly — teie e-posti aadressiga esitati partneriavaldus",
        ApplySignInGreeting:           "Tere,",
        ApplySignInBody:
            "Ruumlys esitati partneriavaldus selle e-posti aadressiga. Sellel aadressil on juba " +
            "Ruumly konto, seega me ei loonud ega muutnud midagi — avaldus peab tulema konto alt. " +
            "Kui see olite teie, logige sisse ja esitage avaldus oma kontolt: nii on ettevõte kohe " +
            "teie enda konto all ja saate seda ise hallata.",
        ApplySignInCta:                "Logi sisse",
        ApplySignInIgnoreTpl:
            "Kui see polnud teie, jätke see kiri tähelepanuta — midagi ei loodud ja teie kontol ei " +
            "muudetud midagi. Küsimuste korral kirjutage aadressile {email}.",
        CategoryCleaning:              "Koristus",
        CategoryPacking:               "Pakkimine",
        CategoryVanRental:             "Kaubiku rent",
        CategoryInsurance:             "Kindlustus",
        CategoryAny:                   "Teenus",
        ScopeText:                     ScopeEt
    );

    private static readonly EmailStrings En = new(
        PasswordResetSubject:       "Ruumly — password reset",
        PasswordResetGreeting:      "Hello,",
        PasswordResetBody1:         "We received a password reset request for your Ruumly account",
        PasswordResetBody2:         "Click the button below to reset your password.",
        PasswordResetExpiry:        "The link is valid for <strong>2 hours</strong>.",
        PasswordResetButton:        "Reset password",
        PasswordResetCopyLabel:     "Or copy this link into your browser:",
        PasswordResetSecurityTitle: "⚠ If you didn't request this",
        PasswordResetSecurityBody:
            "Ignore this email — your password will remain unchanged and no one " +
            "can access your account without it. If you suspect someone is trying " +
            "to access your account, contact us:",
        PasswordResetContactUs:     "info@ruumly.eu",
        PasswordResetFooter:        "This is an automated email. Please do not reply.",
        EmailVerifySubject:         "Ruumly — verify your email address",
        EmailVerifyGreeting:        "Hello,",
        EmailVerifyBody:            "Thanks for signing up! Please verify your email address to activate your account.",
        EmailVerifyButton:          "Verify email",
        EmailVerifyExpiry:          "This link is valid for 24 hours.",
        EmailVerifyFooter:          "This is an automated email. Please do not reply.",
        BookingConfirmSubject:      "Ruumly — booking confirmation",
        BookingConfirmGreeting:     "Hello",
        BookingConfirmBody:         "Your booking request has been received.",
        BookingConfirmService:      "Service",
        BookingConfirmStartDate:    "Start date",
        BookingConfirmPeriod:       "Period",
        BookingConfirmTotal:        "Total",
        BookingConfirmVat:          "incl. VAT",
        BookingConfirmNext:
            "The partner will contact you upon confirmation. You can track your " +
            "booking status in your account.",
        BookingConfirmViewButton:   "View booking",
        BookingConfirmFooter:       "This is an automated email. Please do not reply.",
        BookingStatusConfirmedSubject: "Ruumly — booking confirmed",
        BookingStatusConfirmedBody:    "Your booking #{id} has been confirmed!",
        BookingStatusRejectedSubject:  "Ruumly — booking rejected",
        BookingStatusRejectedBody:     "Unfortunately, your booking #{id} has been rejected",
        BookingStatusCompletedSubject: "Ruumly — booking completed",
        BookingStatusCompletedBody:    "Your booking #{id} has been completed",
        BookingStatusCancelledSubject: "Ruumly — booking cancelled",
        BookingStatusCancelledBody:    "Your booking #{id} has been cancelled",
        BookingStatusViewLink:         "View booking",
        TimelineBookingCreated:        "Booking created",
        TimelineBookingCancelled:      "Booking cancelled",
        TimelineOrderApproved:         "Order approved",
        TimelinePartnerConfirmed:      "Partner confirmed",
        TimelineOrderRejected:         "Order rejected",
        TimelineServiceActive:         "Service active",
        TimelineServiceCompleted:      "Service completed",
        TimelineStatusChanged:         "Status changed",
        NotifBookingConfirmed:         "Booking confirmed",
        NotifBookingRejected:          "Booking rejected",
        NotifBookingCancelled:         "Booking cancelled",
        NotifNewMessage:               "New message in booking",
        NotifBookingConfirmedBody:     "has been confirmed",
        NotifBookingRejectedBody:      "Your booking was rejected",
        NotifServiceActiveBody:        "service is now active",
        NotifServiceCompletedBody:     "has been completed",
        NotifBookingCancelledBody:     "has been cancelled",
        TimelineAwaitingApproval:      "Awaiting approval",
        TimelineManualApprovalNeeded:  "Order requires manual approval before dispatch",
        EmailGreeting:                 "Hello",
        EmailNewOrder:                 "A new order has arrived from Ruumly.",
        EmailOrderDetails:             "ORDER DETAILS",
        EmailOrderNumber:              "Order number",
        EmailService:                  "Service",
        EmailType:                     "Type",
        EmailClient:                   "CLIENT",
        EmailName:                     "Name",
        EmailPhone:                    "Phone",
        EmailDetails:                  "DETAILS",
        EmailStartDate:                "Start date",
        EmailEndDate:                  "End date",
        EmailPeriod:                   "Period",
        EmailExtras:                   "Extras",
        EmailPrice:                    "PRICE",
        EmailPartnerPrice:             "Partner price",
        EmailTotalPartner:             "Total for partner",
        EmailNotes:                    "NOTES",
        EmailConfirmRequest:           "Please confirm the order within 2 hours.",
        EmailConfirmInstructions:      "To confirm, reply to this email with the keyword CONFIRM\nor log into the Ruumly partner panel.",
        EmailRegards:                  "Best regards,",
        EmailTypeWarehouse:            "Storage",
        EmailTypeMoving:               "Moving",
        EmailTypeTrailer:              "Trailer rental",
        AbandonedSubject:              "Your booking is waiting",
        AbandonedGreeting:             "Hi",
        AbandonedBody:                 "We noticed you started a booking but haven't completed payment yet.",
        AbandonedService:              "Service",
        AbandonedTotal:                "Total",
        AbandonedCta:                  "Complete your booking",
        ReservationExpiredSubject:     "Reservation expired",
        ReservationExpiredGreeting:    "Hi {name},",
        ReservationExpiredBody:        "Your reservation for \"{listing}\" has expired because payment was not received within 24 hours.",
        ReservationExpiredCta:         "Book again",
        RefundInitiatedTitle:          "Refund initiated",
        RefundInitiatedDesc:           "A refund for booking #{bookingRef} has been initiated. The amount will be transferred to your account within 3–5 business days.",
        SupplierWelcomeSubject:        "Welcome to Ruumly!",
        SupplierWelcomeBodyTpl:        "Hi {name},\n\nYour application has been approved. You can now log in and start managing your listings.\n\nWelcome aboard!\n\nThe Ruumly team",
        QuoteReplySubject:             "Ruumly — your quote",
        QuoteReplyBodyTpl:             "Hi {name},\n\nPartner {partner} has sent a quote for \"{listing}\": {price}.\n\nContact the partner directly or reply to this email to arrange the service.\n\nThe Ruumly team",
        OfferSubject:                  "Ruumly — your offers are ready",
        OfferGreeting:                 "Hello!",
        OfferIntro:                    "We've reviewed your request and collected offers for you. Here are your options:",
        OfferNoteLabel:                "Our note:",
        OfferCta:                      "View the offers and pick the one that suits you:",
        OfferQuestions:                "Questions? Just reply to this email.",
        OfferSignature:                "The Ruumly team\ninfo@ruumly.eu",
        OutreachSubjectTpl:            "{city}: {category} — customer request",
        OutreachGreeting:              "Hello!",
        OutreachGreetingTpl:           "Hello, {company}!",
        OutreachIntro:                 "Ruumly helps people find local service providers. We are writing to you because your company offers this service in this area and is listed in the Ruumly directory.",
        OutreachProvenanceTpl:         "A customer submitted this request on our website on {date}. Answering is free and non-binding, and you don't need an account to send a price.",
        OutreachAsk:                   "Can you take this job? A short answer is enough: whether the date works for you and roughly what it would cost. If you send us a price, we can pass it to the customer; if the customer picks your offer, we put the two of you in touch.",
        OutreachCannotPrice:           "If you can't price this from what we've sent, say on the same page what is missing — we'll ask the customer.",
        OutreachLabelService:          "Service",
        OutreachLabelLocation:         "Location",
        OutreachLabelDate:             "Preferred date",
        OutreachLabelDetails:          "Details",
        OutreachLabelPhotos:           "Photos",
        OutreachDateAsap:              "as soon as possible — the customer gave no date, we'll confirm it",
        OutreachDateFlexible:          "the customer is flexible on the date — propose a day that suits you",
        OutreachPhotosTpl:             "The customer attached {count} photo(s) — you can view them on the quote page.",
        OutreachDetailsMissing:        "not specified — we'll check with the customer",
        OutreachPackingAddOn:          "The customer also wants packing help — please include it in your price.",
        OutreachUrgentBadge:           "URGENT",
        OutreachUrgentTpl:             "URGENT: the customer needs this by {date}",
        OutreachQuoteCta:              "Submit your price",
        OutreachReplyAlternative:      "Or simply reply to this email with your price — it reaches our team directly.",
        OutreachSignature:             "The Ruumly team\ninfo@ruumly.eu\nhttps://ruumly.eu",
        OutreachQuestionsTpl:          "Questions? Reply to this email, or write to us through our contact page: {url}",
        AckSubject:                    "We have your request — Ruumly",
        AckGreetingTpl:                "Hi {name},",
        AckGreetingNoName:             "Hello,",
        AckReceived:                   "Your request has reached us. We will go through it and ask providers for prices.",
        AckSummaryHeading:             "What you asked for:",
        AckLabelService:               "Service",
        AckLabelCity:                  "Location",
        AckLabelDate:                  "When",
        AckLabelDetails:               "Details",
        AckDateAsap:                   "as soon as possible",
        AckWhatNext:                   "What happens next: we contact providers in your area and send you their offers together. If anything is unclear, we will ask you first.",
        AckStatusLine:                 "Your request has its own page — it shows what stage it has reached. The link is private, so please keep it to yourself.",
        AckStatusCta:                  "See your request",
        AckReply:                      "If anything changes — the date, the amount, the address — just reply to this email. It comes straight to us.",
        AckContactTpl:                 "You can also write to us here: {url}",
        AckSignature:                  "Best regards\nThe Ruumly team\ninfo@ruumly.eu",
        IntroSubjectTpl:               "Customer requests from Ruumly — {company}",
        IntroGreeting:                 "Hello,",
        IntroOpening:                  "We are writing from Ruumly because your company offers a service our customers are looking for.",
        IntroWhoWeAre:                 "Ruumly helps people find the right local providers. The people who reach us have a specific need — storage, moving, van or trailer hire, cleaning or packing.",
        IntroForwarding:               "When a request matches your service and your area, we forward it to you.",
        IntroNotTestRequests:          "These are not test requests and this is not a marketing list. Every Ruumly request comes from a real person looking for the service right now.",
        IntroExpectHeading:            "What we ask of you",
        IntroExpectIntro:              "If you can help the customer, reply to our request email as simply as you like:",
        IntroExpectBullet1:            "whether you have availability at the time they want;",
        IntroExpectBullet2:            "what the approximate or final price would be;",
        IntroExpectBullet3:            "any important condition or question, if you have one.",
        IntroNoAccount:                "There is no account to create, no joining fee, and no separate system to use.",
        IntroIfNotSuitable:            "If a request does not suit you, a short \"not possible\" is a perfectly good answer.",
        IntroWhyHeading:               "Why answering matters",
        IntroWhyBody:                  "The customer came to Ruumly so they would not have to ring dozens of companies themselves. If we have your price and availability, we can put your offer in front of them. If the reply is only a link to your website, or none comes, we cannot show your offer alongside the others.",
        IntroGoal:                     "Our goal is simple: bring you a suitable customer, and make reaching you easy.",
        IntroVolume:                   "We do not promise a fixed number of requests or a monthly flow of orders. Ruumly is still growing. But every request we send is tied to a specific customer, place, time and need.",
        IntroProfileHeading:           "Your company profile on Ruumly",
        IntroProfileListedTpl:         "We have created first profiles for many providers from publicly available information. {company} is already listed too.",
        IntroPriceList:                "If you have a price list, standard rates or simple rules you quote by, send those too. It helps us forward only the requests that actually fit.",
        IntroVisibilityLater:          "Once the profile is correct, you can later promote your listing if you want — featured placement in search and on the city pages for your area, €24–29 a month. Entirely optional: you ask, we switch it on, you pay by bank transfer. Nothing is ever charged automatically, and being listed stays free.",
        IntroFinalAsk:                 "The most important ask is simple: if you get a Ruumly request that fits your service, please answer it. There is a real customer behind that email, waiting.",
        IntroQuestionsTpl:             "Questions about Ruumly, working together or the requests? Reply to this email, or write to us here: {url}",
        IntroClaimIntro:               "If you wish, you can claim your profile and review the contact details, services and description yourself. This is not required to answer our requests.",
        IntroClaimCta:                 "Claim your profile",
        IntroClaimByEmailTpl:          "If you would like to correct your profile details, write to {email} and we will update them.",
        IntroOptOutTpl:                "If you do not wish to receive requests or emails from Ruumly, reply with {keyword} and we will remove your company from the list.",
        IntroOptOutLinkLabel:          "Remove my company",
        IntroSignature:                "Best regards\nThe Ruumly team\ninfo@ruumly.eu\nhttps://ruumly.eu",
        ClaimSubject:                  "Ruumly — confirm you're claiming your profile",
        ClaimGreeting:                 "Hello,",
        ClaimBodyTpl:                  "Someone asked to claim the Ruumly profile for {company}. If that was you, confirm with the button below — you can then correct your own details.",
        ClaimCta:                      "Confirm and edit my profile",
        ClaimExpiryTpl:                "The link works once and expires in {hours} hours.",
        ClaimIgnoreTpl:                "If you didn't ask for this, ignore this email — nothing changes. Any concerns, write to {email}.",
        ApplySignInSubject:            "Ruumly — a partner application used your email address",
        ApplySignInGreeting:           "Hello,",
        ApplySignInBody:
            "A partner application was submitted on Ruumly with this email address. This address " +
            "already has a Ruumly account, so nothing was created and nothing was changed — an " +
            "application has to come from the account itself. If it was you, sign in and submit it " +
            "from your account: your business is then linked to it straight away and you can manage " +
            "it yourself.",
        ApplySignInCta:                "Sign in",
        ApplySignInIgnoreTpl:
            "If this wasn't you, ignore this email — nothing was created and nothing about your " +
            "account was changed. Any concerns, write to {email}.",
        CategoryCleaning:              "Cleaning",
        CategoryPacking:               "Packing",
        CategoryVanRental:             "Van rental",
        CategoryInsurance:             "Insurance",
        CategoryAny:                   "Service",
        ScopeText:                     ScopeEn
    );

    private static readonly EmailStrings Ru = new(
        PasswordResetSubject:       "Ruumly — восстановление пароля",
        PasswordResetGreeting:      "Здравствуйте,",
        PasswordResetBody1:         "Мы получили запрос на восстановление пароля для вашего аккаунта Ruumly",
        PasswordResetBody2:         "Нажмите кнопку ниже, чтобы сменить пароль.",
        PasswordResetExpiry:        "Ссылка действительна <strong>2 часа</strong>.",
        PasswordResetButton:        "Сменить пароль",
        PasswordResetCopyLabel:     "Или скопируйте эту ссылку в браузер:",
        PasswordResetSecurityTitle: "⚠ Если вы не делали этот запрос",
        PasswordResetSecurityBody:
            "Проигнорируйте это письмо — ваш пароль останется прежним. Если вы подозреваете, " +
            "что кто-то пытается получить доступ к вашему аккаунту, свяжитесь с нами:",
        PasswordResetContactUs:     "info@ruumly.eu",
        PasswordResetFooter:        "Это автоматическое письмо. Пожалуйста, не отвечайте на него.",
        EmailVerifySubject:         "Ruumly — подтвердите адрес электронной почты",
        EmailVerifyGreeting:        "Здравствуйте,",
        EmailVerifyBody:            "Спасибо за регистрацию! Пожалуйста, подтвердите адрес электронной почты для активации аккаунта.",
        EmailVerifyButton:          "Подтвердить email",
        EmailVerifyExpiry:          "Ссылка действительна 24 часа.",
        EmailVerifyFooter:          "Это автоматическое письмо. Пожалуйста, не отвечайте на него.",
        BookingConfirmSubject:      "Ruumly — подтверждение бронирования",
        BookingConfirmGreeting:     "Здравствуйте",
        BookingConfirmBody:         "Ваш запрос на бронирование получен.",
        BookingConfirmService:      "Услуга",
        BookingConfirmStartDate:    "Дата начала",
        BookingConfirmPeriod:       "Период",
        BookingConfirmTotal:        "Итого",
        BookingConfirmVat:          "включая НДС",
        BookingConfirmNext:
            "Партнёр свяжется с вами при подтверждении. Статус бронирования " +
            "можно отслеживать в личном кабинете.",
        BookingConfirmViewButton:   "Посмотреть бронирование",
        BookingConfirmFooter:       "Это автоматическое письмо. Пожалуйста, не отвечайте на него.",
        BookingStatusConfirmedSubject: "Ruumly — бронирование подтверждено",
        BookingStatusConfirmedBody:    "Ваше бронирование #{id} подтверждено!",
        BookingStatusRejectedSubject:  "Ruumly — бронирование отклонено",
        BookingStatusRejectedBody:     "К сожалению, ваше бронирование #{id} было отклонено",
        BookingStatusCompletedSubject: "Ruumly — бронирование завершено",
        BookingStatusCompletedBody:    "Ваше бронирование #{id} завершено",
        BookingStatusCancelledSubject: "Ruumly — бронирование отменено",
        BookingStatusCancelledBody:    "Ваше бронирование #{id} отменено",
        BookingStatusViewLink:         "Посмотреть бронирование",
        TimelineBookingCreated:        "Бронирование создано",
        TimelineBookingCancelled:      "Бронирование отменено",
        TimelineOrderApproved:         "Заказ подтверждён",
        TimelinePartnerConfirmed:      "Партнёр подтвердил",
        TimelineOrderRejected:         "Заказ отклонён",
        TimelineServiceActive:         "Услуга активна",
        TimelineServiceCompleted:      "Услуга завершена",
        TimelineStatusChanged:         "Статус изменён",
        NotifBookingConfirmed:         "Бронирование подтверждено",
        NotifBookingRejected:          "Бронирование отклонено",
        NotifBookingCancelled:         "Бронирование отменено",
        NotifNewMessage:               "Новое сообщение в бронировании",
        NotifBookingConfirmedBody:     "подтверждено",
        NotifBookingRejectedBody:      "Ваше бронирование отклонено",
        NotifServiceActiveBody:        "услуга активна",
        NotifServiceCompletedBody:     "завершено",
        NotifBookingCancelledBody:     "отменено",
        TimelineAwaitingApproval:      "Ожидание подтверждения",
        TimelineManualApprovalNeeded:  "Заказ требует ручного подтверждения перед отправкой",
        EmailGreeting:                 "Здравствуйте",
        EmailNewOrder:                 "С платформы Ruumly поступил новый заказ.",
        EmailOrderDetails:             "ДАННЫЕ ЗАКАЗА",
        EmailOrderNumber:              "Номер заказа",
        EmailService:                  "Услуга",
        EmailType:                     "Тип",
        EmailClient:                   "КЛИЕНТ",
        EmailName:                     "Имя",
        EmailPhone:                    "Телефон",
        EmailDetails:                  "ДЕТАЛИ",
        EmailStartDate:                "Дата начала",
        EmailEndDate:                  "Дата окончания",
        EmailPeriod:                   "Период",
        EmailExtras:                   "Доп. услуги",
        EmailPrice:                    "ЦЕНА",
        EmailPartnerPrice:             "Цена для партнёра",
        EmailTotalPartner:             "Итого для партнёра",
        EmailNotes:                    "ПРИМЕЧАНИЯ",
        EmailConfirmRequest:           "Пожалуйста, подтвердите заказ в течение 2 часов.",
        EmailConfirmInstructions:      "Для подтверждения ответьте на это письмо ключевым словом ПОДТВЕРЖДАЮ\nили войдите в панель партнёра Ruumly.",
        EmailRegards:                  "С уважением,",
        EmailTypeWarehouse:            "Складское помещение",
        EmailTypeMoving:               "Переезд",
        EmailTypeTrailer:              "Аренда прицепа",
        AbandonedSubject:              "Ваше бронирование ожидает",
        AbandonedGreeting:             "Здравствуйте",
        AbandonedBody:                 "Мы заметили, что вы начали бронирование, но ещё не оплатили.",
        AbandonedService:              "Услуга",
        AbandonedTotal:                "Сумма",
        AbandonedCta:                  "Завершить бронирование",
        ReservationExpiredSubject:     "Бронирование истекло",
        ReservationExpiredGreeting:    "Здравствуйте, {name},",
        ReservationExpiredBody:        "Срок вашего бронирования \"{listing}\" истёк, так как оплата не поступила в течение 24 часов.",
        ReservationExpiredCta:         "Забронировать снова",
        RefundInitiatedTitle:          "Возврат инициирован",
        RefundInitiatedDesc:           "Возврат средств для бронирования #{bookingRef} инициирован. Сумма будет переведена на ваш счёт в течение 3–5 рабочих дней.",
        SupplierWelcomeSubject:        "Добро пожаловать в Ruumly!",
        SupplierWelcomeBodyTpl:        "Здравствуйте, {name}!\n\nВаша заявка одобрена. Теперь вы можете войти и управлять своими объявлениями.\n\nДобро пожаловать!\n\nКоманда Ruumly",
        QuoteReplySubject:             "Ruumly — ваше ценовое предложение",
        QuoteReplyBodyTpl:             "Здравствуйте, {name}!\n\nПартнёр {partner} отправил предложение по услуге «{listing}»: {price}.\n\nСвяжитесь с партнёром напрямую или ответьте на это письмо, чтобы договориться.\n\nКоманда Ruumly",
        OfferSubject:                  "Ruumly — ваши предложения готовы",
        OfferGreeting:                 "Здравствуйте!",
        OfferIntro:                    "Мы рассмотрели ваш запрос и собрали для вас подходящие предложения. Вот ваши варианты:",
        OfferNoteLabel:                "Наш комментарий:",
        OfferCta:                      "Посмотрите предложения и выберите подходящее:",
        OfferQuestions:                "Если у вас есть вопросы, просто ответьте на это письмо.",
        OfferSignature:                "Команда Ruumly\ninfo@ruumly.eu",
        OutreachSubjectTpl:            "{city}: {category} — запрос клиента",
        OutreachGreeting:              "Здравствуйте!",
        OutreachGreetingTpl:           "Здравствуйте, {company}!",
        OutreachIntro:                 "Ruumly помогает людям находить местных исполнителей. Мы пишем вам, потому что ваша компания оказывает эту услугу в этом районе и есть в каталоге Ruumly.",
        OutreachProvenanceTpl:         "Клиент отправил этот запрос на нашем сайте {date}. Ответ бесплатный и ни к чему не обязывает, а для отправки цены не нужен аккаунт.",
        OutreachAsk:                   "Можете взять этот заказ? Достаточно короткого ответа: подходит ли дата и какой была бы примерная цена. Если вы пришлёте цену, мы сможем передать её клиенту; если клиент выберет ваше предложение, мы вас сведём.",
        OutreachCannotPrice:           "Если по этим данным цену назвать нельзя, укажите на той же странице, чего не хватает, — мы уточним у клиента.",
        OutreachLabelService:          "Услуга",
        OutreachLabelLocation:         "Местоположение",
        OutreachLabelDate:             "Желаемая дата",
        OutreachLabelDetails:          "Детали",
        OutreachLabelPhotos:           "Фото",
        OutreachDateAsap:              "как можно скорее — клиент не указал дату, мы её уточним",
        OutreachDateFlexible:          "клиент гибок по дате — предложите удобный вам день",
        OutreachPhotosTpl:             "Клиент приложил {count} фото — их можно посмотреть на странице подачи цены.",
        OutreachDetailsMissing:        "клиент не указал — мы уточним у него",
        OutreachPackingAddOn:          "Клиент также хочет помощь с упаковкой — пожалуйста, включите её в свою цену.",
        OutreachUrgentBadge:           "СРОЧНО",
        OutreachUrgentTpl:             "СРОЧНО: услуга нужна клиенту к {date}",
        OutreachQuoteCta:              "Отправьте вашу цену",
        OutreachReplyAlternative:      "Или просто ответьте на это письмо, указав свою цену — оно придёт напрямую нашей команде.",
        OutreachSignature:             "Команда Ruumly\ninfo@ruumly.eu\nhttps://ruumly.eu",
        OutreachQuestionsTpl:          "Вопросы? Ответьте на это письмо или напишите нам через форму на странице контактов: {url}",
        AckSubject:                    "Ваш запрос у нас — Ruumly",
        AckGreetingTpl:                "Здравствуйте, {name}!",
        AckGreetingNoName:             "Здравствуйте!",
        AckReceived:                   "Ваш запрос дошёл до нас. Мы его рассмотрим и запросим цены у исполнителей.",
        AckSummaryHeading:             "Что вы запросили:",
        AckLabelService:               "Услуга",
        AckLabelCity:                  "Место",
        AckLabelDate:                  "Когда",
        AckLabelDetails:               "Уточнения",
        AckDateAsap:                   "как можно скорее",
        AckWhatNext:                   "Что дальше: мы свяжемся с исполнителями в вашем районе и пришлём вам их предложения вместе. Если что-то будет неясно, мы спросим у вас.",
        AckStatusLine:                 "У вашего запроса есть своя страница — на ней видно, на каком он этапе. Ссылка личная, пожалуйста, не пересылайте её.",
        AckStatusCta:                  "Открыть страницу запроса",
        AckReply:                      "Если что-то изменится — дата, объём, адрес — просто ответьте на это письмо. Оно придёт прямо к нам.",
        AckContactTpl:                 "Также можно написать нам здесь: {url}",
        AckSignature:                  "С наилучшими пожеланиями\nКоманда Ruumly\ninfo@ruumly.eu",
        IntroSubjectTpl:               "Заявки клиентов от Ruumly — {company}",
        IntroGreeting:                 "Здравствуйте!",
        IntroOpening:                  "Пишем вам из Ruumly, потому что ваша компания оказывает услугу, которую ищут наши клиенты.",
        IntroWhoWeAre:                 "Ruumly помогает людям находить подходящих местных исполнителей. К нам приходят люди с конкретной задачей — склад, переезд, аренда микроавтобуса или прицепа, уборка или упаковка.",
        IntroForwarding:               "Если заявка подходит вашей услуге и вашему региону, мы передаём её вам.",
        IntroNotTestRequests:          "Это не тестовые заявки и не маркетинговая рассылка. Каждая заявка Ruumly приходит от реального человека, которому услуга нужна прямо сейчас.",
        IntroExpectHeading:            "Что мы ждём от вас?",
        IntroExpectIntro:              "Если вы можете помочь клиенту, ответьте на наше письмо с заявкой как можно проще:",
        IntroExpectBullet1:            "есть ли у вас возможность в нужное время;",
        IntroExpectBullet2:            "какой будет ориентировочная или окончательная цена;",
        IntroExpectBullet3:            "при необходимости — важное условие или уточняющий вопрос.",
        IntroNoAccount:                "Создавать аккаунт не нужно, платы за участие нет, отдельной системой пользоваться не придётся.",
        IntroIfNotSuitable:            "Если заявка вам не подходит, достаточно короткого ответа «не получится».",
        IntroWhyHeading:               "Почему важно ответить на заявку?",
        IntroWhyBody:                  "Клиент пришёл в Ruumly именно затем, чтобы самому не обзванивать десятки компаний и не сравнивать сайты. Если мы получаем от вас цену и наличие, мы можем представить клиенту ваше предложение конкретно. Если в ответ приходит только ссылка на сайт или ответа нет вовсе, мы, к сожалению, не можем показать ваше предложение наряду с остальными вариантами.",
        IntroGoal:                     "Наша цель проста: привести вам подходящего клиента и сделать так, чтобы ему было максимально легко до вас дойти.",
        IntroVolume:                   "Мы не обещаем определённого количества заявок или ежемесячного потока заказов. Ruumly ещё растёт. Но каждая заявка, которую мы отправляем, связана с конкретным клиентом, местом, временем и потребностью.",
        IntroProfileHeading:           "Профиль вашей компании в Ruumly",
        IntroProfileListedTpl:         "Для многих исполнителей мы создали первичный профиль на основе общедоступной информации. {company} у нас тоже уже есть.",
        IntroPriceList:                "Если у вас есть прайс-лист, типовые цены или простые правила, по которым вы считаете предложения, их тоже можно прислать нам. Это поможет в дальнейшем передавать вам только более подходящие заявки.",
        IntroVisibilityLater:          "Когда профиль в порядке, при желании можно позже выделить свою карточку платно — приоритетное место в поиске и на страницах городов вашего региона, 24–29 € в месяц. Это полностью добровольно: вы просите, мы подключаем, оплата банковским переводом. Автоматически никогда ничего не списывается, а само размещение было и остаётся бесплатным.",
        IntroFinalAsk:                 "Но самая важная просьба простая: если вы получили от Ruumly заявку, которая подходит вашей услуге, пожалуйста, ответьте на неё. За этим письмом стоит реальный клиент, который ждёт решения.",
        IntroQuestionsTpl:             "Если возникнут вопросы о Ruumly, сотрудничестве или заявках, просто ответьте на это письмо или напишите нам здесь: {url}",
        IntroClaimIntro:               "При желании вы можете забрать свой профиль и сами проверить контакты, услуги и описание. Для ответа на наши заявки это не требуется.",
        IntroClaimCta:                 "Забрать свой профиль",
        IntroClaimByEmailTpl:          "Если хотите исправить данные профиля, напишите на {email}, и мы их обновим.",
        IntroOptOutTpl:                "Если вы не хотите получать от Ruumly заявки и письма, ответьте словом {keyword}, и мы уберём вашу компанию из списка.",
        IntroOptOutLinkLabel:          "Удалить мою компанию",
        IntroSignature:                "С наилучшими пожеланиями\nКоманда Ruumly\ninfo@ruumly.eu\nhttps://ruumly.eu",
        ClaimSubject:                  "Ruumly — подтвердите, что забираете свой профиль",
        ClaimGreeting:                 "Здравствуйте,",
        ClaimBodyTpl:                  "Кто-то запросил передачу профиля компании {company} в Ruumly. Если это были вы, подтвердите кнопкой ниже — после этого вы сможете сами исправить свои данные.",
        ClaimCta:                      "Подтвердить и изменить профиль",
        ClaimExpiryTpl:                "Ссылка одноразовая и действительна {hours} ч.",
        ClaimIgnoreTpl:                "Если вы этого не запрашивали, просто проигнорируйте письмо — ничего не изменится. Если есть вопросы, напишите на {email}.",
        ApplySignInSubject:            "Ruumly — с вашим адресом подана заявка партнёра",
        ApplySignInGreeting:           "Здравствуйте,",
        ApplySignInBody:
            "На Ruumly подана заявка партнёра с этим адресом электронной почты. На этом адресе уже " +
            "есть аккаунт Ruumly, поэтому мы ничего не создали и ничего не изменили — заявка должна " +
            "подаваться из самого аккаунта. Если это были вы, войдите и отправьте заявку из своего " +
            "аккаунта: тогда компания сразу будет привязана к нему, и вы сможете управлять ею сами.",
        ApplySignInCta:                "Войти",
        ApplySignInIgnoreTpl:
            "Если это были не вы, просто проигнорируйте это письмо — ничего не создано и в вашем " +
            "аккаунте ничего не изменилось. Если есть вопросы, напишите на {email}.",
        CategoryCleaning:              "Уборка",
        CategoryPacking:               "Упаковка",
        CategoryVanRental:             "Аренда фургона",
        CategoryInsurance:             "Страхование",
        CategoryAny:                   "Услуга",
        ScopeText:                     ScopeRu
    );

    private static readonly EmailStrings Lv = new(
        PasswordResetSubject:       "Ruumly — paroles atjaunošana",
        PasswordResetGreeting:      "Sveiki,",
        PasswordResetBody1:         "Mēs saņēmām paroles atjaunošanas pieprasījumu jūsu Ruumly kontam",
        PasswordResetBody2:         "Noklikšķiniet uz pogas zemāk, lai nomainītu paroli.",
        PasswordResetExpiry:        "Saite ir derīga <strong>2 stundas</strong>.",
        PasswordResetButton:        "Nomainīt paroli",
        PasswordResetCopyLabel:     "Vai kopējiet šo saiti pārlūkprogrammā:",
        PasswordResetSecurityTitle: "⚠ Ja jūs to neprasījāt",
        PasswordResetSecurityBody:
            "Ignorējiet šo e-pastu — jūsu parole paliks nemainīta un neviens nevarēs " +
            "piekļūt jūsu kontam. Ja aizdomājaties, ka kāds mēģina piekļūt jūsu kontam, " +
            "sazinieties ar mums:",
        PasswordResetContactUs:     "info@ruumly.eu",
        PasswordResetFooter:        "Šis ir automātisks e-pasts. Lūdzu, neatbildiet uz to.",
        EmailVerifySubject:         "Ruumly — apstipriniet savu e-pasta adresi",
        EmailVerifyGreeting:        "Sveiki,",
        EmailVerifyBody:            "Paldies par reģistrāciju! Lūdzu, apstipriniet savu e-pasta adresi, lai aktivizētu kontu.",
        EmailVerifyButton:          "Apstiprināt e-pastu",
        EmailVerifyExpiry:          "Saite ir derīga 24 stundas.",
        EmailVerifyFooter:          "Šis ir automātisks e-pasts. Lūdzu, neatbildiet uz to.",
        BookingConfirmSubject:      "Ruumly — rezervācijas apstiprinājums",
        BookingConfirmGreeting:     "Sveiki",
        BookingConfirmBody:         "Jūsu rezervācijas pieprasījums ir saņemts.",
        BookingConfirmService:      "Pakalpojums",
        BookingConfirmStartDate:    "Sākuma datums",
        BookingConfirmPeriod:       "Periods",
        BookingConfirmTotal:        "Kopā",
        BookingConfirmVat:          "ieskaitot PVN",
        BookingConfirmNext:
            "Partneris sazināsies ar jums pēc apstiprināšanas. Rezervācijas statusu " +
            "varat izsekot savā kontā.",
        BookingConfirmViewButton:   "Skatīt rezervāciju",
        BookingConfirmFooter:       "Šis ir automātisks e-pasts. Lūdzu, neatbildiet uz to.",
        BookingStatusConfirmedSubject: "Ruumly — rezervācija apstiprināta",
        BookingStatusConfirmedBody:    "Jūsu rezervācija #{id} ir apstiprināta!",
        BookingStatusRejectedSubject:  "Ruumly — rezervācija noraidīta",
        BookingStatusRejectedBody:     "Diemžēl jūsu rezervācija #{id} tika noraidīta",
        BookingStatusCompletedSubject: "Ruumly — rezervācija pabeigta",
        BookingStatusCompletedBody:    "Jūsu rezervācija #{id} ir pabeigta",
        BookingStatusCancelledSubject: "Ruumly — rezervācija atcelta",
        BookingStatusCancelledBody:    "Jūsu rezervācija #{id} ir atcelta",
        BookingStatusViewLink:         "Skatīt rezervāciju",
        TimelineBookingCreated:        "Rezervācija izveidota",
        TimelineBookingCancelled:      "Rezervācija atcelta",
        TimelineOrderApproved:         "Pasūtījums apstiprināts",
        TimelinePartnerConfirmed:      "Partners apstiprināja",
        TimelineOrderRejected:         "Pasūtījums noraidīts",
        TimelineServiceActive:         "Pakalpojums ir aktīvs",
        TimelineServiceCompleted:      "Pakalpojums pabeigts",
        TimelineStatusChanged:         "Statuss mainīts",
        NotifBookingConfirmed:         "Rezervācija apstiprināta",
        NotifBookingRejected:          "Rezervācija noraidīta",
        NotifBookingCancelled:         "Rezervācija atcelta",
        NotifNewMessage:               "Jauns ziņojums rezervācijā",
        NotifBookingConfirmedBody:     "ir apstiprināta",
        NotifBookingRejectedBody:      "Jūsu rezervācija tika noraidīta",
        NotifServiceActiveBody:        "pakalpojums ir aktīvs",
        NotifServiceCompletedBody:     "ir pabeigta",
        NotifBookingCancelledBody:     "ir atcelta",
        TimelineAwaitingApproval:      "Gaida apstiprinājumu",
        TimelineManualApprovalNeeded:  "Pasūtījumam nepieciešams manuāls apstiprinājums pirms nosūtīšanas",
        EmailGreeting:                 "Sveiki",
        EmailNewOrder:                 "No Ruumly platformas ir saņemts jauns pasūtījums.",
        EmailOrderDetails:             "PASŪTĪJUMA DATI",
        EmailOrderNumber:              "Pasūtījuma nr.",
        EmailService:                  "Pakalpojums",
        EmailType:                     "Veids",
        EmailClient:                   "KLIENTS",
        EmailName:                     "Vārds",
        EmailPhone:                    "Tālrunis",
        EmailDetails:                  "DETAĻAS",
        EmailStartDate:                "Sākuma datums",
        EmailEndDate:                  "Beigu datums",
        EmailPeriod:                   "Periods",
        EmailExtras:                   "Papildpakalpojumi",
        EmailPrice:                    "CENA",
        EmailPartnerPrice:             "Partnera cena",
        EmailTotalPartner:             "Kopā partnerim",
        EmailNotes:                    "PIEZĪMES",
        EmailConfirmRequest:           "Lūdzu, apstipriniet pasūtījumu 2 stundu laikā.",
        EmailConfirmInstructions:      "Lai apstiprinātu, atbildiet uz šo e-pastu ar atslēgvārdu APSTIPRINU\nvai piesakieties Ruumly partneru panelī.",
        EmailRegards:                  "Ar cieņu,",
        EmailTypeWarehouse:            "Noliktavas telpa",
        EmailTypeMoving:               "Pārvākšanās",
        EmailTypeTrailer:              "Piekabe noma",
        AbandonedSubject:              "Jūsu rezervācija gaida",
        AbandonedGreeting:             "Sveiki",
        AbandonedBody:                 "Mēs pamanījām, ka sākāt rezervāciju, bet vēl neesat veikuši maksājumu.",
        AbandonedService:              "Pakalpojums",
        AbandonedTotal:                "Summa",
        AbandonedCta:                  "Pabeigt rezervāciju",
        ReservationExpiredSubject:     "Rezervācija beigusies",
        ReservationExpiredGreeting:    "Sveiki, {name},",
        ReservationExpiredBody:        "Jūsu rezervācija \"{listing}\" ir beigusies, jo maksājums netika saņemts 24 stundu laikā.",
        ReservationExpiredCta:         "Rezervēt vēlreiz",
        RefundInitiatedTitle:          "Atmaksa uzsākta",
        RefundInitiatedDesc:           "Atmaksa rezervācijai #{bookingRef} ir uzsākta. Summa tiks pārskaitīta uz jūsu kontu 3–5 darba dienu laikā.",
        SupplierWelcomeSubject:        "Laipni lūgti Ruumly!",
        SupplierWelcomeBodyTpl:        "Sveiki, {name}!\n\nJūsu pieteikums ir apstiprināts. Tagad varat pieteikties un sākt pārvaldīt savus sludinājumus.\n\nLaipni lūdzam!\n\nRuumly komanda",
        QuoteReplySubject:             "Ruumly — jūsu cenas piedāvājums",
        QuoteReplyBodyTpl:             "Sveiki, {name}!\n\nPartneris {partner} nosūtīja cenas piedāvājumu pakalpojumam \"{listing}\": {price}.\n\nSazinieties ar partneri tieši vai atbildiet uz šo e-pastu, lai vienotos.\n\nRuumly komanda",
        OfferSubject:                  "Ruumly — jūsu piedāvājumi ir gatavi",
        OfferGreeting:                 "Sveiki!",
        OfferIntro:                    "Mēs izskatījām jūsu pieprasījumu un apkopojām jums piemērotus piedāvājumus. Šeit ir jūsu izvēles iespējas:",
        OfferNoteLabel:                "Mūsu piezīme:",
        OfferCta:                      "Apskatiet piedāvājumus un izvēlieties piemērotāko:",
        OfferQuestions:                "Ja jums ir jautājumi, vienkārši atbildiet uz šo e-pastu.",
        OfferSignature:                "Ruumly komanda\ninfo@ruumly.eu",
        OutreachSubjectTpl:            "{city}: {category} — klienta pieprasījums",
        OutreachGreeting:              "Sveiki!",
        OutreachGreetingTpl:           "Sveiki, {company}!",
        OutreachIntro:                 "Ruumly palīdz cilvēkiem atrast vietējos pakalpojumu sniedzējus. Rakstām jums, jo jūsu uzņēmums sniedz šo pakalpojumu šajā apkaimē un ir iekļauts Ruumly katalogā.",
        OutreachProvenanceTpl:         "Klients iesniedza šo pieprasījumu mūsu vietnē {date}. Atbildēt ir bez maksas un bez saistībām, un cenas nosūtīšanai konts nav vajadzīgs.",
        OutreachAsk:                   "Vai varat uzņemties šo darbu? Pietiek ar īsu atbildi: vai datums jums der un kāda būtu aptuvenā cena. Ja atsūtīsiet cenu, mēs to varēsim nodot klientam; ja klients izvēlēsies jūsu piedāvājumu, mēs jūs savedīsim kopā.",
        OutreachCannotPrice:           "Ja pēc šīs informācijas cenu nosaukt nevarat, tajā pašā lapā norādiet, kas trūkst — mēs pajautāsim klientam.",
        OutreachLabelService:          "Pakalpojums",
        OutreachLabelLocation:         "Atrašanās vieta",
        OutreachLabelDate:             "Vēlamais datums",
        OutreachLabelDetails:          "Detaļas",
        OutreachLabelPhotos:           "Fotoattēli",
        OutreachDateAsap:              "pēc iespējas ātrāk — klients nenorādīja datumu, mēs to precizēsim",
        OutreachDateFlexible:          "klients ir elastīgs ar datumu — piedāvājiet jums ērtu dienu",
        OutreachPhotosTpl:             "Klients pievienoja {count} fotoattēlu(s) — tos var apskatīt cenas iesniegšanas lapā.",
        OutreachDetailsMissing:        "klients nenorādīja — mēs to noskaidrosim",
        OutreachPackingAddOn:          "Klients vēlas arī palīdzību ar iepakošanu — lūdzu, iekļaujiet to savā cenā.",
        OutreachUrgentBadge:           "STEIDZAMI",
        OutreachUrgentTpl:             "STEIDZAMI: klientam pakalpojums nepieciešams līdz {date}",
        OutreachQuoteCta:              "Iesniedziet savu cenu",
        OutreachReplyAlternative:      "Vai vienkārši atbildiet uz šo e-pastu ar savu cenu — tas nonāks tieši pie mūsu komandas.",
        OutreachSignature:             "Ruumly komanda\ninfo@ruumly.eu\nhttps://ruumly.eu",
        OutreachQuestionsTpl:          "Jautājumi? Atbildiet uz šo e-pastu vai rakstiet mums, izmantojot kontaktu lapu: {url}",
        AckSubject:                    "Jūsu pieprasījums ir saņemts — Ruumly",
        AckGreetingTpl:                "Sveiki, {name}!",
        AckGreetingNoName:             "Sveiki!",
        AckReceived:                   "Jūsu pieprasījums ir nonācis pie mums. Mēs to izskatīsim un prasīsim cenas pakalpojumu sniedzējiem.",
        AckSummaryHeading:             "Ko jūs pieprasījāt:",
        AckLabelService:               "Pakalpojums",
        AckLabelCity:                  "Vieta",
        AckLabelDate:                  "Kad",
        AckLabelDetails:               "Precizējumi",
        AckDateAsap:                   "pēc iespējas drīzāk",
        AckWhatNext:                   "Kas notiks tālāk: sazināsimies ar pakalpojumu sniedzējiem jūsu apkaimē un nosūtīsim jūsu piedāvājumus kopā. Ja kaut kas būs neskaidrs, vispirms pajautāsim jums.",
        AckStatusLine:                 "Jūsu pieprasījumam ir sava lapa — tajā redzams, kurā posmā tas ir. Saite ir personiska, lūdzu, nedodiet to tālāk.",
        AckStatusCta:                  "Skatīt savu pieprasījumu",
        AckReply:                      "Ja kaut kas mainās — datums, apjoms, adrese — vienkārši atbildiet uz šo vēstuli. Tā nonāk tieši pie mums.",
        AckContactTpl:                 "Varat rakstīt mums arī šeit: {url}",
        AckSignature:                  "Ar cieņu\nRuumly komanda\ninfo@ruumly.eu",
        IntroSubjectTpl:               "Klientu pieprasījumi no Ruumly — {company}",
        IntroGreeting:                 "Sveiki!",
        IntroOpening:                  "Rakstām no Ruumly, jo jūsu uzņēmums sniedz pakalpojumu, ko meklē mūsu klienti.",
        IntroWhoWeAre:                 "Ruumly palīdz cilvēkiem atrast piemērotus vietējos pakalpojumu sniedzējus. Pie mums nonāk cilvēki ar konkrētu vajadzību — noliktava, pārvākšanās, kravas busiņa vai piekabes noma, uzkopšana vai iepakošana.",
        IntroForwarding:               "Ja pieprasījums atbilst jūsu pakalpojumam un reģionam, mēs to nododam jums.",
        IntroNotTestRequests:          "Tie nav testa pieprasījumi un tas nav mārketinga saraksts. Katrs Ruumly pieprasījums nāk no īsta cilvēka, kuram pakalpojums vajadzīgs tieši tagad.",
        IntroExpectHeading:            "Ko mēs no jums gaidām?",
        IntroExpectIntro:              "Ja varat klientam palīdzēt, atbildiet uz mūsu pieprasījuma vēstuli pēc iespējas vienkāršāk:",
        IntroExpectBullet1:            "vai jums ir iespēja vēlamajā laikā;",
        IntroExpectBullet2:            "kāda būtu aptuvenā vai galīgā cena;",
        IntroExpectBullet3:            "ja nepieciešams — kāds svarīgs nosacījums vai precizējošs jautājums.",
        IntroNoAccount:                "Konts nav jāveido, dalības maksas nav, un atsevišķa sistēma nav jālieto.",
        IntroIfNotSuitable:            "Ja pieprasījums jums neder, pietiek ar īsu atbildi „nav iespējams“.",
        IntroWhyHeading:               "Kāpēc ir svarīgi atbildēt uz pieprasījumu?",
        IntroWhyBody:                  "Klients atnāca uz Ruumly tāpēc, lai pašam nebūtu jāapzvana desmitiem uzņēmumu un jāsalīdzina mājaslapas. Ja saņemam no jums cenu un pieejamību, varam jūsu piedāvājumu klientam nodot konkrēti. Ja atbildē ir tikai saite uz mājaslapu vai atbildes nav vispār, mēs diemžēl nevaram jūsu piedāvājumu parādīt līdzās pārējām iespējām.",
        IntroGoal:                     "Mūsu mērķis ir vienkāršs: atvest jums piemērotu klientu un padarīt nokļūšanu pie jums pēc iespējas vieglāku.",
        IntroVolume:                   "Mēs nesolām noteiktu pieprasījumu skaitu vai ikmēneša pasūtījumu plūsmu. Ruumly vēl aug. Bet katrs pieprasījums, ko nosūtām, ir saistīts ar konkrētu klientu, vietu, laiku un vajadzību.",
        IntroProfileHeading:           "Jūsu uzņēmuma profils Ruumly",
        IntroProfileListedTpl:         "Daudziem pakalpojumu sniedzējiem esam izveidojuši sākotnējo profilu no publiski pieejamas informācijas. Arī {company} mums jau ir sarakstā.",
        IntroPriceList:                "Ja jums ir cenu lapa, tipveida cenas vai vienkārši principi, pēc kuriem veidojat piedāvājumus, tos arī varat mums atsūtīt. Tas palīdzēs turpmāk nodot jums tikai piemērotākos pieprasījumus.",
        IntroVisibilityLater:          "Kad profils ir kārtībā, vēlāk pēc vēlēšanās savu ierakstu var arī izcelt par maksu — priekšroka meklēšanas rezultātos un jūsu reģiona pilsētu lapās, 24–29 € mēnesī. Tas ir pilnīgi brīvprātīgi: jūs palūdzat, mēs ieslēdzam, maksājat ar pārskaitījumu. Automātiski nekad nekas netiek norakstīts, un atrašanās sarakstā ir un paliek bez maksas.",
        IntroFinalAsk:                 "Bet vissvarīgākais lūgums ir vienkāršs: ja saņemat no Ruumly pieprasījumu, kas atbilst jūsu pakalpojumam, lūdzu, atbildiet uz to. Aiz tās vēstules ir īsts klients, kurš gaida risinājumu.",
        IntroQuestionsTpl:             "Ja rodas jautājumi par Ruumly, sadarbību vai pieprasījumiem, vienkārši atbildiet uz šo vēstuli vai rakstiet mums šeit: {url}",
        IntroClaimIntro:               "Ja vēlaties, varat pārņemt savu profilu un pats pārskatīt kontaktus, pakalpojumus un aprakstu. Lai atbildētu uz mūsu klientu pieprasījumiem, tas nav nepieciešams.",
        IntroClaimCta:                 "Pārņemiet savu profilu",
        IntroClaimByEmailTpl:          "Ja vēlaties labot sava profila datus, rakstiet uz {email}, un mēs tos atjaunināsim.",
        IntroOptOutTpl:                "Ja nevēlaties no Ruumly saņemt pieprasījumus un vēstules, atbildiet ar vārdu {keyword}, un mēs izņemsim jūsu uzņēmumu no saraksta.",
        IntroOptOutLinkLabel:          "Izņemt manu uzņēmumu",
        IntroSignature:                "Ar cieņu\nRuumly komanda\ninfo@ruumly.eu\nhttps://ruumly.eu",
        ClaimSubject:                  "Ruumly — apstipriniet sava profila pārņemšanu",
        ClaimGreeting:                 "Sveiki,",
        ClaimBodyTpl:                  "Kāds lūdza pārņemt uzņēmuma {company} Ruumly profilu. Ja tas bijāt jūs, apstipriniet to ar zemāk esošo pogu — pēc tam varēsiet pats labot savus datus.",
        ClaimCta:                      "Apstiprināt un rediģēt manu profilu",
        ClaimExpiryTpl:                "Saite darbojas vienu reizi un ir derīga {hours} stundas.",
        ClaimIgnoreTpl:                "Ja jūs to nelūdzāt, vienkārši ignorējiet šo e-pastu — nekas nemainīsies. Ja rodas jautājumi, rakstiet uz {email}.",
        ApplySignInSubject:            "Ruumly — ar jūsu e-pasta adresi iesniegts partnera pieteikums",
        ApplySignInGreeting:           "Sveiki,",
        ApplySignInBody:
            "Ruumly tika iesniegts partnera pieteikums ar šo e-pasta adresi. Šai adresei jau ir " +
            "Ruumly konts, tāpēc nekas netika izveidots un nekas netika mainīts — pieteikums " +
            "jāiesniedz no paša konta. Ja tas bijāt jūs, pierakstieties un iesniedziet to no sava " +
            "konta: tad uzņēmums uzreiz ir piesaistīts tam un jūs varat to pārvaldīt pats.",
        ApplySignInCta:                "Pierakstīties",
        ApplySignInIgnoreTpl:
            "Ja tas nebijāt jūs, vienkārši ignorējiet šo e-pastu — nekas netika izveidots un jūsu " +
            "kontā nekas netika mainīts. Ja rodas jautājumi, rakstiet uz {email}.",
        CategoryCleaning:              "Uzkopšana",
        CategoryPacking:               "Iepakošana",
        CategoryVanRental:             "Furgona noma",
        CategoryInsurance:             "Apdrošināšana",
        CategoryAny:                   "Pakalpojums",
        ScopeText:                     ScopeLv
    );

    private static readonly EmailStrings Lt = new(
        PasswordResetSubject:       "Ruumly — slaptažodžio atkūrimas",
        PasswordResetGreeting:      "Sveiki,",
        PasswordResetBody1:         "Gavome slaptažodžio atkūrimo užklausą jūsų Ruumly paskyrai",
        PasswordResetBody2:         "Spustelėkite žemiau esantį mygtuką, kad pakeistumėte slaptažodį.",
        PasswordResetExpiry:        "Nuoroda galioja <strong>2 valandas</strong>.",
        PasswordResetButton:        "Keisti slaptažodį",
        PasswordResetCopyLabel:     "Arba nukopijuokite šią nuorodą į naršyklę:",
        PasswordResetSecurityTitle: "⚠ Jei to neprašėte",
        PasswordResetSecurityBody:
            "Nepaisykite šio el. laiško — jūsų slaptažodis liks nepakeistas ir niekas " +
            "negalės pasiekti jūsų paskyros. Jei įtariate, kad kas nors bando pasiekti " +
            "jūsų paskyrą, susisiekite su mumis:",
        PasswordResetContactUs:     "info@ruumly.eu",
        PasswordResetFooter:        "Tai automatinis el. laiškas. Prašome neatsakyti į jį.",
        EmailVerifySubject:         "Ruumly — patvirtinkite savo el. pašto adresą",
        EmailVerifyGreeting:        "Sveiki,",
        EmailVerifyBody:            "Dėkojame už registraciją! Prašome patvirtinti savo el. pašto adresą, kad aktyvuotumėte paskyrą.",
        EmailVerifyButton:          "Patvirtinti el. paštą",
        EmailVerifyExpiry:          "Nuoroda galioja 24 valandas.",
        EmailVerifyFooter:          "Tai automatinis el. laiškas. Prašome neatsakyti į jį.",
        BookingConfirmSubject:      "Ruumly — rezervacijos patvirtinimas",
        BookingConfirmGreeting:     "Sveiki",
        BookingConfirmBody:         "Jūsų rezervacijos užklausa gauta.",
        BookingConfirmService:      "Paslauga",
        BookingConfirmStartDate:    "Pradžios data",
        BookingConfirmPeriod:       "Laikotarpis",
        BookingConfirmTotal:        "Iš viso",
        BookingConfirmVat:          "įskaitant PVM",
        BookingConfirmNext:
            "Partneris susisieks su jumis patvirtinimo metu. Rezervacijos būseną " +
            "galite stebėti savo paskyroje.",
        BookingConfirmViewButton:   "Peržiūrėti rezervaciją",
        BookingConfirmFooter:       "Tai automatinis el. laiškas. Prašome neatsakyti į jį.",
        BookingStatusConfirmedSubject: "Ruumly — rezervacija patvirtinta",
        BookingStatusConfirmedBody:    "Jūsų rezervacija #{id} patvirtinta!",
        BookingStatusRejectedSubject:  "Ruumly — rezervacija atmesta",
        BookingStatusRejectedBody:     "Deja, jūsų rezervacija #{id} buvo atmesta",
        BookingStatusCompletedSubject: "Ruumly — rezervacija užbaigta",
        BookingStatusCompletedBody:    "Jūsų rezervacija #{id} užbaigta",
        BookingStatusCancelledSubject: "Ruumly — rezervacija atšaukta",
        BookingStatusCancelledBody:    "Jūsų rezervacija #{id} atšaukta",
        BookingStatusViewLink:         "Peržiūrėti rezervaciją",
        TimelineBookingCreated:        "Rezervacija sukurta",
        TimelineBookingCancelled:      "Rezervacija atšaukta",
        TimelineOrderApproved:         "Užsakymas patvirtintas",
        TimelinePartnerConfirmed:      "Partneris patvirtino",
        TimelineOrderRejected:         "Užsakymas atmestas",
        TimelineServiceActive:         "Paslauga aktyvi",
        TimelineServiceCompleted:      "Paslauga užbaigta",
        TimelineStatusChanged:         "Būsena pakeista",
        NotifBookingConfirmed:         "Rezervacija patvirtinta",
        NotifBookingRejected:          "Rezervacija atmesta",
        NotifBookingCancelled:         "Rezervacija atšaukta",
        NotifNewMessage:               "Naujas pranešimas rezervacijoje",
        NotifBookingConfirmedBody:     "patvirtinta",
        NotifBookingRejectedBody:      "Jūsų rezervacija buvo atmesta",
        NotifServiceActiveBody:        "paslauga aktyvi",
        NotifServiceCompletedBody:     "užbaigta",
        NotifBookingCancelledBody:     "atšaukta",
        TimelineAwaitingApproval:      "Laukiama patvirtinimo",
        TimelineManualApprovalNeeded:  "Užsakymui reikalingas rankinis patvirtinimas prieš išsiunčiant",
        EmailGreeting:                 "Sveiki",
        EmailNewOrder:                 "Iš Ruumly platformos gautas naujas užsakymas.",
        EmailOrderDetails:             "UŽSAKYMO DUOMENYS",
        EmailOrderNumber:              "Užsakymo nr.",
        EmailService:                  "Paslauga",
        EmailType:                     "Tipas",
        EmailClient:                   "KLIENTAS",
        EmailName:                     "Vardas",
        EmailPhone:                    "Telefonas",
        EmailDetails:                  "DETALĖS",
        EmailStartDate:                "Pradžios data",
        EmailEndDate:                  "Pabaigos data",
        EmailPeriod:                   "Laikotarpis",
        EmailExtras:                   "Papildomos paslaugos",
        EmailPrice:                    "KAINA",
        EmailPartnerPrice:             "Partnerio kaina",
        EmailTotalPartner:             "Iš viso partneriui",
        EmailNotes:                    "PASTABOS",
        EmailConfirmRequest:           "Prašome patvirtinti užsakymą per 2 valandas.",
        EmailConfirmInstructions:      "Norėdami patvirtinti, atsakykite į šį el. laišką raktiniu žodžiu PATVIRTINU\narba prisijunkite prie Ruumly partnerių skydelio.",
        EmailRegards:                  "Pagarbiai,",
        EmailTypeWarehouse:            "Sandėlio patalpa",
        EmailTypeMoving:               "Kraustymasis",
        EmailTypeTrailer:              "Priekabos nuoma",
        AbandonedSubject:              "Jūsų rezervacija laukia",
        AbandonedGreeting:             "Sveiki",
        AbandonedBody:                 "Pastebėjome, kad pradėjote rezervaciją, bet dar neįvykdėte mokėjimo.",
        AbandonedService:              "Paslauga",
        AbandonedTotal:                "Suma",
        AbandonedCta:                  "Užbaigti rezervaciją",
        ReservationExpiredSubject:     "Rezervacija baigėsi",
        ReservationExpiredGreeting:    "Sveiki, {name},",
        ReservationExpiredBody:        "Jūsų rezervacija \"{listing}\" baigėsi, nes mokėjimas negautas per 24 valandas.",
        ReservationExpiredCta:         "Rezervuoti vėl",
        RefundInitiatedTitle:          "Grąžinimas pradėtas",
        RefundInitiatedDesc:           "Grąžinimas rezervacijos #{bookingRef} buvo pradėtas. Suma bus pervesta į jūsų sąskaitą per 3–5 darbo dienas.",
        SupplierWelcomeSubject:        "Sveiki atvykę į Ruumly!",
        SupplierWelcomeBodyTpl:        "Sveiki, {name}!\n\nJūsų paraiška buvo patvirtinta. Dabar galite prisijungti ir pradėti tvarkyti savo skelbimus.\n\nSveikiname prisijungus!\n\nRuumly komanda",
        QuoteReplySubject:             "Ruumly — jūsų kainos pasiūlymas",
        QuoteReplyBodyTpl:             "Sveiki, {name}!\n\nPartneris {partner} atsiuntė kainos pasiūlymą paslaugai \"{listing}\": {price}.\n\nSusisiekite su partneriu tiesiogiai arba atsakykite į šį laišką, kad susitartumėte.\n\nRuumly komanda",
        OfferSubject:                  "Ruumly — jūsų pasiūlymai paruošti",
        OfferGreeting:                 "Sveiki!",
        OfferIntro:                    "Peržiūrėjome jūsų užklausą ir surinkome jums tinkamus pasiūlymus. Štai jūsų pasirinkimai:",
        OfferNoteLabel:                "Mūsų pastaba:",
        OfferCta:                      "Peržiūrėkite pasiūlymus ir išsirinkite tinkamiausią:",
        OfferQuestions:                "Jei turite klausimų, tiesiog atsakykite į šį laišką.",
        OfferSignature:                "Ruumly komanda\ninfo@ruumly.eu",
        OutreachSubjectTpl:            "{city}: {category} — kliento užklausa",
        OutreachGreeting:              "Sveiki!",
        OutreachGreetingTpl:           "Sveiki, {company}!",
        OutreachIntro:                 "Ruumly padeda žmonėms rasti vietos paslaugų teikėjus. Rašome jums, nes jūsų įmonė teikia šią paslaugą šioje vietovėje ir yra įtraukta į Ruumly katalogą.",
        OutreachProvenanceTpl:         "Klientas pateikė šią užklausą mūsų svetainėje {date}. Atsakyti nemokama ir be įsipareigojimų, o kainai pateikti paskyros nereikia.",
        OutreachAsk:                   "Ar galite imtis šio darbo? Pakanka trumpo atsakymo: ar data jums tinka ir kokia būtų apytikslė kaina. Jei atsiųsite kainą, galėsime ją pateikti klientui; jei klientas pasirinks jūsų pasiūlymą, jus sujungsime.",
        OutreachCannotPrice:           "Jei pagal šią informaciją kainos pateikti negalite, tame pačiame puslapyje nurodykite, ko trūksta — paklausime kliento.",
        OutreachLabelService:          "Paslauga",
        OutreachLabelLocation:         "Vieta",
        OutreachLabelDate:             "Pageidaujama data",
        OutreachLabelDetails:          "Detalės",
        OutreachLabelPhotos:           "Nuotraukos",
        OutreachDateAsap:              "kuo greičiau — klientas nenurodė datos, mes ją patikslinsime",
        OutreachDateFlexible:          "klientas lankstus dėl datos — pasiūlykite jums patogią dieną",
        OutreachPhotosTpl:             "Klientas pridėjo {count} nuotrauką(-as) — jas galite peržiūrėti kainos pateikimo puslapyje.",
        OutreachDetailsMissing:        "klientas nenurodė — mes pasitikslinsime",
        OutreachPackingAddOn:          "Klientas taip pat pageidauja pakavimo pagalbos — prašome įtraukti ją į savo kainą.",
        OutreachUrgentBadge:           "SKUBU",
        OutreachUrgentTpl:             "SKUBU: klientui paslauga reikalinga iki {date}",
        OutreachQuoteCta:              "Pateikite savo kainą",
        OutreachReplyAlternative:      "Arba tiesiog atsakykite į šį laišką nurodydami savo kainą — jis pasieks mūsų komandą tiesiogiai.",
        OutreachSignature:             "Ruumly komanda\ninfo@ruumly.eu\nhttps://ruumly.eu",
        OutreachQuestionsTpl:          "Klausimai? Atsakykite į šį laišką arba parašykite mums per kontaktų puslapį: {url}",
        AckSubject:                    "Jūsų užklausa gauta — Ruumly",
        AckGreetingTpl:                "Sveiki, {name}!",
        AckGreetingNoName:             "Sveiki!",
        AckReceived:                   "Jūsų užklausa mus pasiekė. Jà peržiūrėsime ir paprašysime kainų iš paslaugų teikėjų.",
        AckSummaryHeading:             "Ko užsakėte:",
        AckLabelService:               "Paslauga",
        AckLabelCity:                  "Vieta",
        AckLabelDate:                  "Kada",
        AckLabelDetails:               "Patikslinimai",
        AckDateAsap:                   "kuo greičiau",
        AckWhatNext:                   "Kas toliau: susisieksime su jūsų rajono paslaugų teikėjais ir atsiųsime jūsų pasiūlymus kartu. Jei kas nors bus neaišku, pirmiausia paklausime jūsų.",
        AckStatusLine:                 "Jūsų užklausa turi savo puslapį — jame matyti, kokiame etape ji yra. Nuoroda asmeninė, prašome ja nesidalyti.",
        AckStatusCta:                  "Peržiūrėti savo užklausą",
        AckReply:                      "Jei kas nors pasikeis — data, kiekis, adresas — tiesiog atsakykite į šį laišką. Jis ateis tiesiai pas mus.",
        AckContactTpl:                 "Taip pat galite parašyti mums čia: {url}",
        AckSignature:                  "Su geriausiais linkėjimais\nRuumly komanda\ninfo@ruumly.eu",
        IntroSubjectTpl:               "Klientų užklausos iš Ruumly — {company}",
        IntroGreeting:                 "Sveiki!",
        IntroOpening:                  "Rašome iš Ruumly, nes jūsų įmonė teikia paslaugą, kurios ieško mūsų klientai.",
        IntroWhoWeAre:                 "Ruumly padeda žmonėms rasti tinkamus vietos paslaugų teikėjus. Pas mus ateina žmonės su konkrečiu poreikiu — sandėlis, perkraustymas, mikroautobuso ar priekabos nuoma, valymas arba pakavimas.",
        IntroForwarding:               "Jei užklausa atitinka jūsų paslaugą ir regioną, ją persiunčiame jums.",
        IntroNotTestRequests:          "Tai nėra bandomosios užklausos ir tai nėra rinkodaros sąrašas. Kiekviena Ruumly užklausa ateina iš tikro žmogaus, kuriam paslauga reikalinga būtent dabar.",
        IntroExpectHeading:            "Ko iš jūsų tikimės?",
        IntroExpectIntro:              "Jei galite klientui padėti, į mūsų užklausos laišką atsakykite kuo paprasčiau:",
        IntroExpectBullet1:            "ar turite galimybę norimu laiku;",
        IntroExpectBullet2:            "kokia būtų apytikslė ar galutinė kaina;",
        IntroExpectBullet3:            "jei reikia — svarbi sąlyga ar tikslinamasis klausimas.",
        IntroNoAccount:                "Paskyros kurti nereikia, stojimo mokesčio nėra, atskira sistema naudotis nereikės.",
        IntroIfNotSuitable:            "Jei užklausa jums netinka, pakanka trumpo atsakymo „negalime“.",
        IntroWhyHeading:               "Kodėl svarbu atsakyti į užklausą?",
        IntroWhyBody:                  "Klientas atėjo į Ruumly tam, kad pačiam nereikėtų apskambinti dešimčių įmonių ir lyginti svetainių. Jei gauname iš jūsų kainą ir laisvą laiką, galime jūsų pasiūlymą pateikti klientui konkrečiai. Jei atsakyme yra tik nuoroda į svetainę arba atsakymo negauname, jūsų pasiūlymo, deja, negalime parodyti šalia kitų variantų.",
        IntroGoal:                     "Mūsų tikslas paprastas: atvesti jums tinkamą klientą ir padaryti taip, kad jam būtų kuo lengviau jus pasiekti.",
        IntroVolume:                   "Mes nežadame konkretaus užklausų skaičiaus ar mėnesinio užsakymų srauto. Ruumly dar auga. Bet kiekviena mūsų siunčiama užklausa susijusi su konkrečiu klientu, vieta, laiku ir poreikiu.",
        IntroProfileHeading:           "Jūsų įmonės profilis Ruumly",
        IntroProfileListedTpl:         "Daugeliui paslaugų teikėjų pirminį profilį sukūrėme iš viešai prieinamos informacijos. {company} pas mus taip pat jau yra.",
        IntroPriceList:                "Jei turite kainoraštį, tipines kainas ar paprastas taisykles, pagal kurias skaičiuojate pasiūlymus, juos taip pat galite mums atsiųsti. Tai padės ateityje persiųsti jums tik tinkamesnes užklausas.",
        IntroVisibilityLater:          "Kai profilis tvarkingas, vėliau panorėję savo įrašą galite ir išskirti už mokestį — pirmenybė paieškoje ir jūsų regiono miestų puslapiuose, 24–29 € per mėnesį. Tai visiškai savanoriška: jūs paprašote, mes įjungiame, sumokate bankiniu pavedimu. Automatiškai niekada nieko nenuskaitoma, o būti sąraše buvo ir lieka nemokama.",
        IntroFinalAsk:                 "Bet svarbiausias prašymas paprastas: jei gavote iš Ruumly užklausą, kuri tinka jūsų paslaugai, prašome į ją atsakyti. Už to laiško stovi tikras klientas, laukiantis sprendimo.",
        IntroQuestionsTpl:             "Jei kiltų klausimų apie Ruumly, bendradarbiavimą ar užklausas, tiesiog atsakykite į šį laišką arba parašykite mums čia: {url}",
        IntroClaimIntro:               "Panorėję galite perimti savo profilį ir patys peržiūrėti kontaktus, paslaugas bei aprašymą. Atsakyti į mūsų klientų užklausas dėl to nebūtina.",
        IntroClaimCta:                 "Perimkite savo profilį",
        IntroClaimByEmailTpl:          "Jei norite pataisyti savo profilio duomenis, rašykite adresu {email}, ir mes juos atnaujinsime.",
        IntroOptOutTpl:                "Jei nenorite iš Ruumly gauti užklausų ir laiškų, atsakykite žodžiu {keyword}, ir mes išbrauksime jūsų įmonę iš sąrašo.",
        IntroOptOutLinkLabel:          "Išbraukti mano įmonę",
        IntroSignature:                "Su geriausiais linkėjimais\nRuumly komanda\ninfo@ruumly.eu\nhttps://ruumly.eu",
        ClaimSubject:                  "Ruumly — patvirtinkite savo profilio perėmimą",
        ClaimGreeting:                 "Sveiki,",
        ClaimBodyTpl:                  "Kažkas paprašė perimti įmonės {company} Ruumly profilį. Jei tai buvote jūs, patvirtinkite paspaudę mygtuką žemiau — tada galėsite patys pataisyti savo duomenis.",
        ClaimCta:                      "Patvirtinti ir redaguoti mano profilį",
        ClaimExpiryTpl:                "Nuoroda veikia vieną kartą ir galioja {hours} val.",
        ClaimIgnoreTpl:                "Jei to neprašėte, tiesiog nepaisykite šio laiško — niekas nepasikeis. Jei kyla klausimų, rašykite adresu {email}.",
        ApplySignInSubject:            "Ruumly — su jūsų el. pašto adresu pateikta partnerio paraiška",
        ApplySignInGreeting:           "Sveiki,",
        ApplySignInBody:
            "Ruumly buvo pateikta partnerio paraiška su šiuo el. pašto adresu. Šiam adresui jau " +
            "priskirta Ruumly paskyra, todėl nieko nesukūrėme ir nieko nepakeitėme — paraiška turi " +
            "būti pateikta iš pačios paskyros. Jei tai buvote jūs, prisijunkite ir pateikite ją iš " +
            "savo paskyros: tada įmonė iškart bus su ja susieta ir galėsite ją tvarkyti patys.",
        ApplySignInCta:                "Prisijungti",
        ApplySignInIgnoreTpl:
            "Jei tai buvote ne jūs, tiesiog nepaisykite šio laiško — niekas nebuvo sukurta ir jūsų " +
            "paskyroje niekas nepasikeitė. Jei kyla klausimų, rašykite adresu {email}.",
        CategoryCleaning:              "Valymas",
        CategoryPacking:               "Pakavimas",
        CategoryVanRental:             "Furgono nuoma",
        CategoryInsurance:             "Draudimas",
        CategoryAny:                   "Paslauga",
        ScopeText:                     ScopeLt
    );

    public static EmailStrings For(string? lang) =>
        lang switch
        {
            "en" => En,
            "ru" => Ru,
            "lv" => Lv,
            "lt" => Lt,
            _    => Et,   // default Estonian
        };
}
