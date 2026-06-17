# Free Marketplace, Optional Commerce, and Paid Boosts Design

## Goal

Shift Ruumly from a transaction-first marketplace to a partner-acquisition marketplace: partners can list for free, while bookings, contract signing, payments, and paid promotional services are opt-in capabilities controlled by admin and later purchasable by partners.

## Product Positioning

Ruumly is free for partners to join and list. The immediate value is visibility: searchable listings, map placement, partner pages, city/category SEO pages, and lead generation.

The existing booking, contract, invoice, payout, and payment systems stay in the product, but they become supplier-level capabilities rather than the default expectation. A partner can operate as a simple directory listing, or admin can enable booking and commerce when the partner wants a fuller workflow.

## Supplier Modes

Every supplier starts in directory mode:

- Public partner profile can be published.
- Listings can appear in search, map, sitemap, and partner pages.
- Customers can contact or request information.
- Billing, commissions, packages, payout details, and contract signing are not required to onboard.

Admin can enable commerce capabilities per supplier:

- `BookingEnabled`: listing detail pages can show a booking CTA and backend booking creation is allowed.
- `ContractSigningEnabled`: booking flow requires or offers the existing Ruumly signature system.
- `DirectPaymentEnabled`: customer can choose a direct/provider-pay-later option.
- `RuumlyPaymentEnabled`: customer can pay through Ruumly/Montonio and Ruumly later settles with the supplier.

Existing `BillingModel` remains meaningful:

- `Marketplace`: Ruumly collects payment and pays supplier later.
- `Rebate`: customer pays supplier directly and Ruumly may later invoice fees if enabled.

## Paid Feature Catalog

Paid features are add-ons, not mandatory packages. They can be manually activated by admin first and later become self-serve purchases.

Visibility boosts:

- Promoted search placement.
- Highlighted map pin.
- Featured homepage placement.
- Featured city/category placement.
- Listing bump or refresh.
- Recommended badge.
- Newsletter or social feature.

Trust and conversion:

- Verified Partner badge.
- Ruumly-written partner profile.
- SEO landing page for the partner.
- Photo/video/virtual tour package.
- Review collection support.

Operational tools:

- Calendar import/export/sync.
- Occupancy timeline.
- Bulk import assistance.
- Google Places sync.
- Analytics dashboard.
- Lead inbox export.
- Contract template support.

Commerce tools:

- Booking enablement.
- Ruumly signature enablement.
- Future Dokobit enablement.
- Direct payment enablement.
- Ruumly payment enablement.

## Ordering Locations

The first version is a request-and-approve workflow, not checkout:

- Provider Dashboard: new Growth or Boosts tab showing available paid features and active features.
- Provider Listings: per-listing actions like Promote, Highlight on map, Bump listing.
- Partner profile editor: request verified badge, SEO profile, or better content.
- Admin Supplier Detail: approve, activate, schedule, expire, price, and revoke features.
- Public Provider Page: explains free listing and optional paid boosts without forcing pricing choices during onboarding.

Self-serve payment for boosts is deferred until partner supply and traffic justify it.

## Data Model

Add supplier commerce flags directly to `Supplier` because they gate core behavior.

Add paid feature records separately because boosts have type, scope, dates, price, and approval lifecycle.

Core entities:

- `PaidFeature`: catalog item, category, scope, base price, active flag.
- `SupplierPaidFeature`: active/granted feature for a supplier or listing, with start/end dates and metadata.
- `PaidFeatureRequest`: partner request for a feature, reviewed by admin.

## Behavior Rules

- Directory mode is the default for new suppliers and public applications.
- Public listing pages show contact/lead CTA when booking is disabled.
- Backend `POST /api/bookings` rejects booking attempts when the supplier has booking disabled.
- Payment initiation rejects Ruumly payment when `RuumlyPaymentEnabled` is false.
- Contract signing is only required when `ContractSigningEnabled` is true; otherwise booking can complete as a lead/reservation depending on payment mode.
- Search ranking can use active paid features, but disabled/expired features must never affect ranking.
- Existing subscription tiers remain in code but should be hidden from public/provider UX while the marketplace is in free-acquisition mode.

## Admin Controls

Admin supplier detail gets a Commerce section:

- Booking enabled.
- Contract signing enabled.
- Direct payment enabled.
- Ruumly payment enabled.
- Billing model.
- Notes for payment/contract setup.

Admin supplier detail or a new Boosts admin tab gets feature management:

- Active features.
- Pending requests.
- Start/end dates.
- Manual price.
- Scope: supplier, listing, city, category, homepage, search, map.

## Frontend UX

Public:

- Provider page sells free listing first.
- Listing detail page shows contact/request CTA by default.
- Booking CTA appears only for commerce-enabled suppliers.
- Pricing/package cards move out of public onboarding.

Provider:

- Dashboard emphasizes listings, profile completeness, leads, map visibility, and optional growth tools.
- Billing tab becomes Commerce & Boosts or hides unless a supplier has active commerce or paid features.
- Calendar tools are free during acquisition mode, even if later monetized.

Admin:

- Supplier edit remains the control plane.
- Paid feature requests are reviewed manually.

## SEO and Indexing

The sitemap is live and returns XML. The current weakness is that normal Googlebot receives the Vite HTML shell with generic static meta, while per-page Helmet tags render client-side. Short term:

- Keep sitemap and robots routed to API.
- Update static `index.html` copy.
- Keep slash/no-slash redirects consistent with sitemap.
- Add lightweight crawler-rendered HTML for Googlebot for city, listing, partner, and blog routes before considering full Next.js SSR.

## Testing Strategy

Backend tests:

- Booking disabled supplier cannot receive booking.
- Booking enabled supplier can receive booking.
- Payment initiation rejects Ruumly payment when disabled.
- Paid feature activation affects listing ranking only while active.
- Expired paid features do not affect ranking.

Frontend tests/type checks:

- Listing detail hides booking CTA when booking disabled.
- Listing detail shows booking CTA when booking enabled.
- Provider dashboard shows boosts without requiring payment setup.
- Admin supplier commerce flags are editable.

Manual live smoke:

- Create partner, approve, add listing, verify it appears on map/search as directory listing.
- Enable booking and verify booking path appears.
- Disable booking and verify backend rejects direct API attempts.
- Activate promoted feature and verify listing placement/styling.

