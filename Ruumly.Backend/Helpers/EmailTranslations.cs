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
        string OutreachSubjectTpl,
        string OutreachGreeting,
        // One-line value proposition for a COLD recipient who has never heard of
        // Ruumly: a real customer request, free to answer, no commitment, no
        // signup needed to quote.
        string OutreachIntro,
        string OutreachAsk,
        // Discrete field labels — the request facts are rendered as a table in
        // HTML and as "Label: value" lines in the text fallback.
        string OutreachLabelService,
        string OutreachLabelLocation,
        string OutreachLabelDate,
        string OutreachLabelDetails,
        // Shown INSTEAD of a bare "—" when the customer gave no date / no
        // details: a provider must never be asked to price a dash.
        string OutreachDateAsap,
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
        // Service-category labels missing from the legacy EmailType* trio.
        // CategoryPacking / CategoryInsurance are STILL REFERENCED and must not be
        // deleted: we stopped selling those two as consumer services in 2026-08, but
        // DemandLead rows created before that are still worked in the admin CRM and
        // still generate provider outreach, which renders CategoryLabel(lead.Category).
        string CategoryCleaning,
        string CategoryPacking,
        string CategoryVanRental,
        string CategoryInsurance,
        string CategoryAny
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

        public string OutreachSubject(string category, string city) =>
            OutreachSubjectTpl
                .Replace("{category}", category)
                .Replace("{city}", city);

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
    }

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
        OutreachSubjectTpl:            "Ruumly — kliendipäring: {category}, {city}",
        OutreachGreeting:              "Tere!",
        OutreachIntro:                 "Ruumly viib kliendid kokku kohalike teenusepakkujatega. See on päris kliendi päring — vastamine on tasuta ja mittesiduv ning hinna esitamiseks ei ole kontot vaja.",
        OutreachAsk:                   "Kas saate selle töö vastu võtta? Esitage oma hind allolevalt lingilt ja viime teid kliendiga kokku.",
        OutreachLabelService:          "Teenus",
        OutreachLabelLocation:         "Asukoht",
        OutreachLabelDate:             "Soovitud aeg",
        OutreachLabelDetails:          "Lisainfo",
        OutreachDateAsap:              "esimesel võimalusel — klient kuupäeva ei märkinud, täpsustame selle üle",
        OutreachDetailsMissing:        "klient ei täpsustanud — küsime tema käest üle",
        OutreachPackingAddOn:          "Klient soovib lisaks pakkimisabi — palun arvestage see oma hinna sisse.",
        OutreachUrgentBadge:           "KIIRE",
        OutreachUrgentTpl:             "KIIRE: klient vajab teenust {date}",
        OutreachQuoteCta:              "Esitage oma hind",
        OutreachReplyAlternative:      "Või vastake lihtsalt sellele e-kirjale koos hinnaga — kiri jõuab otse meie meeskonnani.",
        OutreachSignature:             "Ruumly meeskond\ninfo@ruumly.eu",
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
        CategoryCleaning:              "Koristus",
        CategoryPacking:               "Pakkimine",
        CategoryVanRental:             "Kaubiku rent",
        CategoryInsurance:             "Kindlustus",
        CategoryAny:                   "Teenus"
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
        OutreachSubjectTpl:            "Ruumly — customer request: {category}, {city}",
        OutreachGreeting:              "Hello!",
        OutreachIntro:                 "Ruumly connects customers with local providers. This is a real customer request — answering is free, there is no commitment, and you don't need an account to send a price.",
        OutreachAsk:                   "Can you take this job? Submit your price using the link below and we'll connect you with the customer.",
        OutreachLabelService:          "Service",
        OutreachLabelLocation:         "Location",
        OutreachLabelDate:             "Preferred date",
        OutreachLabelDetails:          "Details",
        OutreachDateAsap:              "as soon as possible — the customer gave no date, we'll confirm it",
        OutreachDetailsMissing:        "not specified — we'll check with the customer",
        OutreachPackingAddOn:          "The customer also wants packing help — please include it in your price.",
        OutreachUrgentBadge:           "URGENT",
        OutreachUrgentTpl:             "URGENT: the customer needs this by {date}",
        OutreachQuoteCta:              "Submit your price",
        OutreachReplyAlternative:      "Or simply reply to this email with your price — it reaches our team directly.",
        OutreachSignature:             "The Ruumly team\ninfo@ruumly.eu",
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
        CategoryCleaning:              "Cleaning",
        CategoryPacking:               "Packing",
        CategoryVanRental:             "Van rental",
        CategoryInsurance:             "Insurance",
        CategoryAny:                   "Service"
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
        OutreachSubjectTpl:            "Ruumly — запрос клиента: {category}, {city}",
        OutreachGreeting:              "Здравствуйте!",
        OutreachIntro:                 "Ruumly соединяет клиентов с местными исполнителями. Это запрос реального клиента — ответ бесплатный, ни к чему не обязывает, и для отправки цены не нужен аккаунт.",
        OutreachAsk:                   "Можете взять этот заказ? Отправьте вашу цену по ссылке ниже, и мы свяжем вас с клиентом.",
        OutreachLabelService:          "Услуга",
        OutreachLabelLocation:         "Местоположение",
        OutreachLabelDate:             "Желаемая дата",
        OutreachLabelDetails:          "Детали",
        OutreachDateAsap:              "как можно скорее — клиент не указал дату, мы её уточним",
        OutreachDetailsMissing:        "клиент не указал — мы уточним у него",
        OutreachPackingAddOn:          "Клиент также хочет помощь с упаковкой — пожалуйста, включите её в свою цену.",
        OutreachUrgentBadge:           "СРОЧНО",
        OutreachUrgentTpl:             "СРОЧНО: услуга нужна клиенту к {date}",
        OutreachQuoteCta:              "Отправьте вашу цену",
        OutreachReplyAlternative:      "Или просто ответьте на это письмо, указав свою цену — оно придёт напрямую нашей команде.",
        OutreachSignature:             "Команда Ruumly\ninfo@ruumly.eu",
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
        CategoryCleaning:              "Уборка",
        CategoryPacking:               "Упаковка",
        CategoryVanRental:             "Аренда фургона",
        CategoryInsurance:             "Страхование",
        CategoryAny:                   "Услуга"
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
        OutreachSubjectTpl:            "Ruumly — klienta pieprasījums: {category}, {city}",
        OutreachGreeting:              "Sveiki!",
        OutreachIntro:                 "Ruumly savieno klientus ar vietējiem pakalpojumu sniedzējiem. Šis ir īsta klienta pieprasījums — atbildēt ir bez maksas un bez saistībām, un cenas nosūtīšanai konts nav vajadzīgs.",
        OutreachAsk:                   "Vai varat uzņemties šo darbu? Iesniedziet savu cenu, izmantojot zemāk esošo saiti, un mēs jūs savienosim ar klientu.",
        OutreachLabelService:          "Pakalpojums",
        OutreachLabelLocation:         "Atrašanās vieta",
        OutreachLabelDate:             "Vēlamais datums",
        OutreachLabelDetails:          "Detaļas",
        OutreachDateAsap:              "pēc iespējas ātrāk — klients nenorādīja datumu, mēs to precizēsim",
        OutreachDetailsMissing:        "klients nenorādīja — mēs to noskaidrosim",
        OutreachPackingAddOn:          "Klients vēlas arī palīdzību ar iepakošanu — lūdzu, iekļaujiet to savā cenā.",
        OutreachUrgentBadge:           "STEIDZAMI",
        OutreachUrgentTpl:             "STEIDZAMI: klientam pakalpojums nepieciešams līdz {date}",
        OutreachQuoteCta:              "Iesniedziet savu cenu",
        OutreachReplyAlternative:      "Vai vienkārši atbildiet uz šo e-pastu ar savu cenu — tas nonāks tieši pie mūsu komandas.",
        OutreachSignature:             "Ruumly komanda\ninfo@ruumly.eu",
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
        CategoryCleaning:              "Uzkopšana",
        CategoryPacking:               "Iepakošana",
        CategoryVanRental:             "Furgona noma",
        CategoryInsurance:             "Apdrošināšana",
        CategoryAny:                   "Pakalpojums"
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
        OutreachSubjectTpl:            "Ruumly — kliento užklausa: {category}, {city}",
        OutreachGreeting:              "Sveiki!",
        OutreachIntro:                 "Ruumly sujungia klientus su vietos paslaugų teikėjais. Tai tikro kliento užklausa — atsakyti nemokama, be įsipareigojimų, o kainai pateikti paskyros nereikia.",
        OutreachAsk:                   "Ar galite imtis šio darbo? Pateikite savo kainą naudodami žemiau esančią nuorodą ir mes sujungsime jus su klientu.",
        OutreachLabelService:          "Paslauga",
        OutreachLabelLocation:         "Vieta",
        OutreachLabelDate:             "Pageidaujama data",
        OutreachLabelDetails:          "Detalės",
        OutreachDateAsap:              "kuo greičiau — klientas nenurodė datos, mes ją patikslinsime",
        OutreachDetailsMissing:        "klientas nenurodė — mes pasitikslinsime",
        OutreachPackingAddOn:          "Klientas taip pat pageidauja pakavimo pagalbos — prašome įtraukti ją į savo kainą.",
        OutreachUrgentBadge:           "SKUBU",
        OutreachUrgentTpl:             "SKUBU: klientui paslauga reikalinga iki {date}",
        OutreachQuoteCta:              "Pateikite savo kainą",
        OutreachReplyAlternative:      "Arba tiesiog atsakykite į šį laišką nurodydami savo kainą — jis pasieks mūsų komandą tiesiogiai.",
        OutreachSignature:             "Ruumly komanda\ninfo@ruumly.eu",
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
        CategoryCleaning:              "Valymas",
        CategoryPacking:               "Pakavimas",
        CategoryVanRental:             "Furgono nuoma",
        CategoryInsurance:             "Draudimas",
        CategoryAny:                   "Paslauga"
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
