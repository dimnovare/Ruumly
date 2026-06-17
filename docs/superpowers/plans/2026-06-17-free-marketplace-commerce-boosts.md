# Free Marketplace Commerce Boosts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reposition Ruumly as a free partner marketplace while keeping booking, contract, payment, and paid promotional features available as supplier/admin-controlled optional capabilities.

**Architecture:** Add supplier-level commerce flags for core transactional gates, then add a separate paid-feature catalog and activation model for boosts. Existing booking/payment/contract systems stay in place and are guarded by the new flags rather than deleted.

**Tech Stack:** ASP.NET Core 8, EF Core, PostgreSQL, React, TypeScript, TanStack Query, Vite, Cloudflare Worker for crawler previews.

---

## File Structure

Backend:

- Modify `Ruumly.Backend/Models/Supplier.cs`: add commerce capability booleans.
- Create `Ruumly.Backend/Models/PaidFeature.cs`: catalog item.
- Create `Ruumly.Backend/Models/SupplierPaidFeature.cs`: active/granted feature.
- Create `Ruumly.Backend/Models/PaidFeatureRequest.cs`: partner request lifecycle.
- Create `Ruumly.Backend/Models/Enums/PaidFeature*.cs`: feature category/scope/status enums.
- Modify `Ruumly.Backend/Data/RuumlyDbContext.cs`: add DbSets and indexes.
- Add migration `AddSupplierCommerceAndPaidFeatures`.
- Modify `Ruumly.Backend/DTOs/Responses/SupplierDto.cs`: expose commerce flags.
- Modify `Ruumly.Backend/DTOs/Responses/ListingDto.cs`: expose listing commerce capability summary.
- Modify `Ruumly.Backend/Controllers/AdminMappers.cs`: map flags.
- Modify `Ruumly.Backend/DTOs/Requests/SupplierRequests.cs`: accept admin flag updates.
- Modify `Ruumly.Backend/Controllers/AdminSuppliersController.cs`: update flags and paid feature admin endpoints.
- Modify `Ruumly.Backend/Services/Implementations/BookingService.cs`: reject booking when disabled.
- Modify `Ruumly.Backend/Services/Implementations/MontonioPaymentService.cs`: reject Ruumly payment when disabled.
- Modify `Ruumly.Backend/Services/Implementations/ListingService.cs`: ranking joins active paid features.
- Add backend tests in `Ruumly.Backend.Tests`.

Frontend:

- Modify `estonia-space-hub/src/services/types.ts`: supplier/listing capability fields.
- Modify `estonia-space-hub/src/pages/DetailPages.tsx`: contact CTA default, booking CTA when enabled.
- Modify `estonia-space-hub/src/pages/BookingPage.tsx`: defensive disabled state if supplier no longer allows booking.
- Modify `estonia-space-hub/src/components/admin/AdminSuppliers.tsx`: Commerce section.
- Create `estonia-space-hub/src/components/provider/ProviderBoosts.tsx`: provider request UI.
- Modify `estonia-space-hub/src/pages/ProviderDashboardPage.tsx`: add Boosts tab, hide old billing pressure.
- Modify `estonia-space-hub/src/pages/ProviderPage.tsx`: free listing first, paid boosts later.
- Modify `estonia-space-hub/src/i18n/translations.ts`: new copy in five languages.

SEO:

- Modify `estonia-space-hub/index.html`: remove stale discount claim and align static copy.
- Later task: extend `workers/social-preview/src/index.ts` or create SEO Worker behavior for Googlebot if approved.

---

### Task 1: Supplier Commerce Flags Backend

**Files:**
- Modify: `Ruumly.Backend/Models/Supplier.cs`
- Modify: `Ruumly.Backend/DTOs/Responses/SupplierDto.cs`
- Modify: `Ruumly.Backend/Controllers/AdminMappers.cs`
- Modify: `Ruumly.Backend/DTOs/Requests/SupplierRequests.cs`
- Modify: `Ruumly.Backend/Controllers/AdminSuppliersController.cs`
- Modify: `Ruumly.Backend/Data/RuumlyDbContext.cs`
- Add migration: `Ruumly.Backend/Migrations/*_AddSupplierCommerceFlags.cs`
- Test: `Ruumly.Backend.Tests/SupplierCommerceFlagsTests.cs`

- [ ] **Step 1: Write failing tests for default flags and admin mapping**

Create tests that instantiate a supplier with default values and assert:

```csharp
Assert.False(dto.BookingEnabled);
Assert.False(dto.ContractSigningEnabled);
Assert.False(dto.DirectPaymentEnabled);
Assert.False(dto.RuumlyPaymentEnabled);
```

Run:

```powershell
dotnet test Ruumly.Backend.Tests --filter SupplierCommerceFlagsTests
```

Expected: fail because fields do not exist.

- [ ] **Step 2: Add Supplier properties**

Add to `Supplier`:

```csharp
public bool BookingEnabled { get; set; } = false;
public bool ContractSigningEnabled { get; set; } = false;
public bool DirectPaymentEnabled { get; set; } = false;
public bool RuumlyPaymentEnabled { get; set; } = false;
```

- [ ] **Step 3: Expose fields in DTOs and mappers**

Add the four booleans to `SupplierDto` and map them in `AdminMappers.MapSupplier`.

- [ ] **Step 4: Accept admin updates**

Extend supplier update request with nullable booleans and apply them only when provided.

- [ ] **Step 5: Create migration**

Run from `Ruumly.Backend`:

```powershell
dotnet ef migrations add AddSupplierCommerceFlags
```

Expected: migration adds four non-null boolean columns with default `false`.

- [ ] **Step 6: Verify**

Run:

```powershell
dotnet test Ruumly.Backend.Tests --filter SupplierCommerceFlagsTests
dotnet build Ruumly.Backend
```

Expected: tests and build pass.

---

### Task 2: Booking and Payment Backend Gates

**Files:**
- Modify: `Ruumly.Backend/Services/Implementations/BookingService.cs`
- Modify: `Ruumly.Backend/Services/Implementations/MontonioPaymentService.cs`
- Modify: `Ruumly.Backend/Helpers/ErrorMessages.cs`
- Test: `Ruumly.Backend.Tests/SupplierCommerceGateTests.cs`

- [ ] **Step 1: Write failing booking-disabled test**

Create a test with active supplier/listing where `BookingEnabled=false`, then call booking creation.

Expected exception:

```csharp
Assert.Equal("BOOKING_DISABLED", ex.Message or error key);
```

Run targeted test and verify it fails because booking currently succeeds.

- [ ] **Step 2: Add localized error key**

Add `BOOKING_DISABLED` and `RUUMLY_PAYMENT_DISABLED` to all five backend languages.

- [ ] **Step 3: Gate booking creation**

After listing and supplier are loaded:

```csharp
if (!supplier.BookingEnabled)
    throw new ForbiddenException(Msg("BOOKING_DISABLED"));
```

- [ ] **Step 4: Write failing payment-disabled test**

Create booking/invoice for supplier with `RuumlyPaymentEnabled=false`; call Montonio payment initiation for bank/card.

Expected: forbidden/business exception before outbound Montonio call.

- [ ] **Step 5: Gate Montonio payment**

Load booking supplier before outbound request and reject when `RuumlyPaymentEnabled=false` and payment method is not `later`.

- [ ] **Step 6: Verify**

Run:

```powershell
dotnet test Ruumly.Backend.Tests --filter SupplierCommerceGateTests
dotnet test Ruumly.Backend.Tests
```

Expected: new tests pass; note any pre-existing unrelated failures.

---

### Task 3: Listing Capability Summary

**Files:**
- Modify: `Ruumly.Backend/DTOs/Responses/ListingDto.cs`
- Modify: `Ruumly.Backend/Controllers/AdminMappers.cs`
- Modify: `Ruumly.Backend/Services/Implementations/ListingService.cs`
- Modify: `Ruumly.Backend/Controllers/LocationsController.cs`
- Test: `Ruumly.Backend.Tests/ListingCommerceCapabilitiesTests.cs`

- [ ] **Step 1: Write failing listing DTO test**

Assert a public listing DTO includes:

```csharp
BookingEnabled
ContractSigningEnabled
DirectPaymentEnabled
RuumlyPaymentEnabled
```

- [ ] **Step 2: Add fields to `ListingDto`**

Add four booleans near supplier metadata.

- [ ] **Step 3: Map from supplier**

Map the fields in all listing DTO construction sites.

- [ ] **Step 4: Verify**

Run listing tests and backend build.

---

### Task 4: Paid Feature Data Model

**Files:**
- Create: `Ruumly.Backend/Models/PaidFeature.cs`
- Create: `Ruumly.Backend/Models/SupplierPaidFeature.cs`
- Create: `Ruumly.Backend/Models/PaidFeatureRequest.cs`
- Create: `Ruumly.Backend/Models/Enums/PaidFeatureCategory.cs`
- Create: `Ruumly.Backend/Models/Enums/PaidFeatureScope.cs`
- Create: `Ruumly.Backend/Models/Enums/PaidFeatureRequestStatus.cs`
- Modify: `Ruumly.Backend/Data/RuumlyDbContext.cs`
- Add migration: `Ruumly.Backend/Migrations/*_AddPaidFeatures.cs`
- Test: `Ruumly.Backend.Tests/PaidFeatureModelTests.cs`

- [ ] **Step 1: Write failing model persistence test**

Test inserting a catalog feature, a supplier active feature, and a pending request.

- [ ] **Step 2: Add models and enums**

Feature categories:

```csharp
Visibility, Trust, Operations, Commerce
```

Scopes:

```csharp
Supplier, Listing, City, Category, Homepage, Search, Map
```

Request statuses:

```csharp
Pending, Approved, Rejected, Cancelled, Completed
```

- [ ] **Step 3: Configure EF**

Indexes:

- `PaidFeatures.Code` unique.
- `SupplierPaidFeatures.SupplierId, FeatureId, StartsAt, EndsAt`.
- `SupplierPaidFeatures.ListingId` for listing-scoped boosts.
- `PaidFeatureRequests.SupplierId, Status`.

- [ ] **Step 4: Migration**

Run:

```powershell
dotnet ef migrations add AddPaidFeatures
```

- [ ] **Step 5: Verify**

Run model tests and backend build.

---

### Task 5: Admin Paid Feature Endpoints

**Files:**
- Create: `Ruumly.Backend/Controllers/AdminPaidFeaturesController.cs`
- Create/modify DTOs under `Ruumly.Backend/DTOs/Requests` and `Responses`
- Test: `Ruumly.Backend.Tests/AdminPaidFeaturesTests.cs`

- [ ] **Step 1: Write failing admin list/activate tests**

Tests:

- Admin can list catalog features.
- Admin can activate feature for supplier/listing with date range.
- Admin can expire active feature.

- [ ] **Step 2: Add endpoints**

Routes:

```http
GET    /api/admin/paid-features
POST   /api/admin/suppliers/{supplierId}/paid-features
PATCH  /api/admin/supplier-paid-features/{id}
DELETE /api/admin/supplier-paid-features/{id}
GET    /api/admin/paid-feature-requests
PATCH  /api/admin/paid-feature-requests/{id}
```

- [ ] **Step 3: Add audit logs**

Audit actions:

- `paid_feature.activated`
- `paid_feature.expired`
- `paid_feature.request_reviewed`

- [ ] **Step 4: Verify**

Run targeted tests and backend build.

---

### Task 6: Paid Feature Ranking and Styling Signals

**Files:**
- Modify: `Ruumly.Backend/Services/Implementations/ListingService.cs`
- Modify: `Ruumly.Backend/DTOs/Responses/ListingDto.cs`
- Test: `Ruumly.Backend.Tests/PaidFeatureRankingTests.cs`

- [ ] **Step 1: Write failing ranking tests**

Tests:

- Active promoted search feature ranks before normal listing.
- Expired promoted feature does not rank before normal listing.
- Hidden moving/trailer toggles still override boosts.

- [ ] **Step 2: Add listing DTO active feature codes**

Add:

```csharp
List<string> ActivePaidFeatures
```

- [ ] **Step 3: Join active features in search**

Apply only features active at `DateTime.UtcNow` and matching supplier/listing scope.

- [ ] **Step 4: Ranking order**

In default search sort:

1. Active search promotion.
2. Existing supplier tier/fallback priority.
3. Rating.
4. Recency.

- [ ] **Step 5: Verify**

Run ranking tests and backend build.

---

### Task 7: Frontend Types and Listing CTA

**Files:**
- Modify: `estonia-space-hub/src/services/types.ts`
- Modify: `estonia-space-hub/src/pages/DetailPages.tsx`
- Modify: `estonia-space-hub/src/pages/BookingPage.tsx`
- Modify: `estonia-space-hub/src/i18n/translations.ts`

- [ ] **Step 1: Add TypeScript fields**

Add supplier/listing capability fields and active paid feature codes.

- [ ] **Step 2: Listing detail CTA**

Directory mode:

- Primary CTA: contact/request information.
- Secondary: view partner page when available.

Commerce mode:

- Show booking CTA only when `bookingEnabled=true`.

- [ ] **Step 3: Booking defensive guard**

If a listing loads with `bookingEnabled=false`, show friendly disabled state and do not submit.

- [ ] **Step 4: Translations**

Add five-language keys for directory CTA and booking disabled state.

- [ ] **Step 5: Verify**

Run:

```powershell
npx tsc --noEmit
```

from `estonia-space-hub`.

---

### Task 8: Admin Supplier Commerce UI

**Files:**
- Modify: `estonia-space-hub/src/components/admin/AdminSuppliers.tsx`
- Modify: `estonia-space-hub/src/services/types.ts`
- Modify: `estonia-space-hub/src/i18n/translations.ts`

- [ ] **Step 1: Add Commerce section**

Controls:

- Booking enabled.
- Contract signing enabled.
- Direct payment enabled.
- Ruumly payment enabled.
- Billing model.

- [ ] **Step 2: Save flags**

Patch existing supplier update route or new focused route.

- [ ] **Step 3: Visual safety**

Show a short helper:

```text
Free directory listings stay public even when all commerce options are off.
```

- [ ] **Step 4: Verify**

Run TypeScript check.

---

### Task 9: Provider Boosts Tab

**Files:**
- Create: `estonia-space-hub/src/components/provider/ProviderBoosts.tsx`
- Modify: `estonia-space-hub/src/pages/ProviderDashboardPage.tsx`
- Modify: `estonia-space-hub/src/services/index.ts`
- Modify: `estonia-space-hub/src/services/queryKeys.ts`
- Modify: `estonia-space-hub/src/i18n/translations.ts`

- [ ] **Step 1: Catalog list**

Show grouped features:

- Visibility
- Trust
- Operations
- Commerce

- [ ] **Step 2: Request workflow**

Button creates a paid feature request.

- [ ] **Step 3: Active features**

Show active features and expiry dates.

- [ ] **Step 4: Verify**

Run TypeScript check.

---

### Task 10: Provider Page Copy Rebuild

**Files:**
- Modify: `estonia-space-hub/src/pages/ProviderPage.tsx`
- Modify: `estonia-space-hub/src/i18n/translations.ts`
- Modify: `estonia-space-hub/index.html`

- [ ] **Step 1: Remove package pressure**

Replace subscription cards with:

- Free listing.
- Optional boosts.
- Optional commerce tools.

- [ ] **Step 2: Keep future paid features visible**

Mention paid boosts without forcing checkout.

- [ ] **Step 3: Static HTML meta**

Remove stale “10% cheaper” copy and align with free marketplace positioning.

- [ ] **Step 4: Verify**

Run TypeScript check and frontend build.

---

### Task 11: SEO Follow-Up

**Files:**
- Modify: `workers/social-preview/src/index.ts` or create dedicated crawler route handling if approved.
- Test manually with curl.

- [ ] **Step 1: Verify current sitemap**

Run:

```powershell
curl.exe -I https://ruumly.eu/sitemap.xml
curl.exe -s https://ruumly.eu/sitemap.xml
```

- [ ] **Step 2: Decide crawler rendering**

If keeping SPA, add Worker-rendered HTML for Googlebot on key public routes.

- [ ] **Step 3: Verify**

Run:

```powershell
curl.exe -A "Googlebot" https://ruumly.eu/et/storage/tallinn
```

Expected after implementation: route-specific title/canonical/description in HTML response.

---

## Verification Commands

Backend:

```powershell
dotnet build Ruumly.Backend
dotnet test Ruumly.Backend.Tests
```

Frontend:

```powershell
cd estonia-space-hub
npx tsc --noEmit
npm run build
```

Worker:

```powershell
cd workers/social-preview
npm run typecheck
```

Live smoke after deploy:

```powershell
curl.exe -I https://ruumly.eu/sitemap.xml
curl.exe -A "Googlebot" https://ruumly.eu/et/storage/tallinn
```

Manual:

- Create public partner application.
- Approve supplier.
- Add directory listing.
- Verify public map/search listing without booking CTA.
- Enable booking.
- Verify booking CTA appears.
- Disable booking.
- Verify direct API booking returns localized disabled error.
- Activate promoted listing feature.
- Verify ranking/styling changes.

