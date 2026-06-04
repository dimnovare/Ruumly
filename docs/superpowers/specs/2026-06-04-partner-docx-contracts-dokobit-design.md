# Partner-tokenized Word contracts → Dokobit signing — Design Spec

**Date:** 2026-06-04
**Status:** Approved (founder: tenant-only signing; Gotenberg for docx→PDF)
**Supersedes:** the 2026-06-03 "Smart-ID identity-only" launch posture for contracts.

## 1. Goal & flow

Partners author their own contract as a Word `.docx`, placing `{{tokens}}` themselves
per a cheat-sheet. We validate the tokens, let them preview a filled sample, then fill +
render to PDF and sign via **Dokobit Documents Gateway** (real qualified e-signature).

**Partner (once, in provider portal):**
upload `contract.docx` → system extracts every `{{token}}` → validates against the known
vocabulary → shows ✅ recognized / ❌ unknown → partner previews a filled sample PDF →
activates it as their supplier's template.

**Tenant (per booking, after payment):**
open contract → system fills the supplier's active docx with this booking's data → renders
to PDF (Gotenberg) → uploads to Dokobit → tenant signs with Smart-ID/Mobile-ID on Dokobit's
hosted page → postback downloads the signed PDF to R2 and records the **verified** identity.

Only the **tenant** signs at launch. Dokobit supports multiple signers (`signers[1]`) — dual
signing is a future extension, out of scope now.

## 2. Token vocabulary (the cheat-sheet)

Maps 1:1 to data already on Booking/Listing/Supplier. Final set (adjust to actual model names):

| Token | Source |
|---|---|
| `{{tenant_name}}` | Booking.CustomerName / User |
| `{{tenant_id_code}}` | Booking customer personal code |
| `{{tenant_email}}` `{{tenant_phone}}` | Booking customer contact |
| `{{listing_name}}` `{{listing_size}}` `{{listing_address}}` `{{listing_city}}` | Listing |
| `{{start_date}}` `{{end_date}}` `{{monthly_price}}` `{{deposit}}` `{{total_price}}` | Booking |
| `{{supplier_name}}` `{{supplier_reg_code}}` | Supplier |
| `{{contract_number}}` `{{today}}` | generated |

Dates formatted `dd.MM.yyyy`; money formatted `N2 €`. Missing/empty → empty string (never crash).

## 3. Validation rules (what makes self-serve safe)

On upload, scan the docx for `{{...}}` tokens — **reassembling tokens split across Word runs**
(a known Open XML gotcha; regex `\{\{\s*([A-Za-z0-9_]+)\s*\}\}` after run-merge).

- **Unknown token** (not in vocabulary) → hard error, blocks activation; response lists offenders + cheat-sheet.
- **Recognized** → ✅.
- **Missing core tokens** (e.g. no `{{tenant_name}}`) → soft warning, does not block.

## 4. Data model (additive — no destructive change)

Extend `ContractTemplate` (already per-supplier, has `SupplierId`, `IsActive`, `IsDefault`):

- `TemplateType` enum `{ Html = 0, Docx = 1 }`, default `Html` (existing rows unaffected).
- `DocxObjectKey` `string?` — R2 key of the uploaded docx.
- `DetectedTokens` `string?` — JSON array of tokens found at upload.
- Keep `HtmlTemplate` nullable (existing admin path remains a fallback).

`SignedContract` needs **no** change — `SignedDocumentUrl`, `VerifiedName`, `VerifiedIdCode`,
`DokobitSigningToken`, `Status` already exist.

Migration: additive columns only.

## 5. docx fill + PDF render

- **Fill:** `OpenXmlContractDocumentService` (port from Rentaro
  `src/Rentaro.Api/Contracts/OpenXmlContractDocumentService.cs`) — fills `{{tokens}}` in the
  docx via DocumentFormat.OpenXml, run-merge aware, headers/footers included.
- **Render:** `GotenbergClient` — `POST {GOTENBERG__URL}/forms/libreoffice/convert` multipart
  with the filled docx → returns PDF bytes. No LibreOffice in the API image; no process spawning.

## 6. Dokobit Documents Gateway — wire format

Confirmed from docs + Rentaro's working impl. **Backend agent MUST smoke-test the live sandbox
to confirm exact paths before trusting them** (docs say `/api/file/upload.json`; Rentaro uses
`/api/upload.json` — verify empirically with the token).

- **Base URL:** sandbox `https://gateway-sandbox.dokobit.com`, prod `https://gateway.dokobit.com`
  — selected by `Signing:Dokobit:Environment` (`test`|`production`, default `test`).
- **Auth:** `?access_token=<token>` query param on **every** request.
- **Wire:** requests are `application/x-www-form-urlencoded` with bracket field names; responses
  are snake_case JSON (parse defensively).

**Lifecycle:**
1. **Upload** `POST /api/file/upload.json` (verify path) — `file[name]`, `file[digest]` (SHA-256
   hex lowercase of the PDF bytes), `file[content]` (base64 of the PDF — use this, NOT a public
   URL, so the contract is never hosted publicly). → `{ status, token }`. Poll upload status if required.
2. **Create signing** `POST /api/signing/create.json` — `type=pdf`, `name`, `signers[0][id]=1`,
   `signers[0][name]`, `signers[0][surname]`, `signers[0][code]` (tenant personal code),
   `signers[0][country_code]` (`EE`), `signers[0][signing_options][]=smartid` &
   `signers[0][signing_options][]=mobile`, optional `signers[0][phone]`, `files[0][token]`,
   `postback_url`. → `{ status, token, signers: { "1": signerAccessToken } }`.
3. **Signing URL** (build locally, not an API call):
   `{base}/signing/{signingToken}?access_token={signerAccessToken}`.
4. **Status** `GET /api/signing/{token}/status.json` → `{ status, signer_info: { code, phone,
   country_code, signing_option, signing_time, type }, file/files }`. Map: `signed|completed`→Signed;
   `pending|ok|waiting`→Pending; `declined|rejected`→Declined; `expired`→Expired; `failed|error`→Failed;
   unknown→Pending (never flip a real signature to Failed).
5. **Download signed PDF** — inline base64 `file.content`/`files[0].content`, else `download_url`/`url`,
   else `GET /api/signing/{token}/files.json`.
6. **Identity capture** — on Signed, set `VerifiedIdCode = signer_info.code` (the **verified**
   personal code — fixes the prior form-sourced bug), `SigningMethod = signer_info.signing_option`.

**Verified live against the sandbox (2026-06-04):**
- Upload path `/api/file/upload.json` works (Rentaro's `/api/upload.json` also works); upload is
  `uploaded` immediately, so the status poll is best-effort.
- `signers[0][signing_purpose]` is **REQUIRED** (gateway returns 400 `code 10000` without it) —
  not optional as the public docs imply.
- Per-signer info in status is nested under `signers["1"]` (NOT a top-level `signer_info`) —
  the parser handles both shapes. Read `code`/`signing_option`/`name`/`surname` from there.
- `signers[0][code]` (personal code) is optional and may be omitted; the tenant supplies it on
  the hosted Smart-ID/Mobile-ID page and we capture the VERIFIED value from status afterward.

## 7. API endpoints (backend ↔ frontend contract)

**Provider templating** (`[Authorize(Roles="Provider,Admin")]`, scoped to caller's supplier):
- `GET  /api/provider/contract-template` → `{ templates:[...], activeId, vocabulary:[{token,label}] }`
- `POST /api/provider/contract-template` (multipart `file`=docx) → upload to R2, extract+validate →
  `{ templateId, detectedTokens:[{token,recognized}], unknownTokens:[...], warnings:[...] }` (not yet active)
- `POST /api/provider/contract-template/{id}/preview` → fill with sample data → Gotenberg PDF →
  returns `application/pdf` (or `{ url }`)
- `POST /api/provider/contract-template/{id}/activate` → set active for supplier (deactivate others);
  **409 if unknownTokens exist**

**Signing** (existing, rewired — keep routes):
- `POST /api/contracts/dokobit/initiate` `{ bookingId }` → load supplier active docx → fill →
  Gotenberg PDF → Dokobit upload+create → persist pending `SignedContract` → `{ signingUrl, signingToken }`
- `GET  /api/contracts/dokobit/{token}/status` → poll fallback; on signed: capture identity,
  download PDF→R2, mark completed → `{ status, signedDocumentUrl? }`
- `POST /api/contracts/dokobit/callback` **`[AllowAnonymous]`** → postback. Read token from
  query/form; re-fetch status server-to-server (don't trust body); idempotent via the contract's
  terminal-status guard (no WebhookEvent table needed); download+store PDF; capture identity; always `200`.

## 8. Config / env

- `Signing:Dokobit:AccessToken` (env `SIGNING__DOKOBIT__ACCESSTOKEN`) — sandbox token now.
- `Signing:Dokobit:Environment` (`SIGNING__DOKOBIT__ENVIRONMENT`) — `test` | `production`.
- `Gotenberg:Url` (`GOTENBERG__URL`) — sidecar service URL.
- Reuse existing `IStorageService` (R2) for signed-PDF storage.
- Env-gated: if `Signing:Dokobit:AccessToken` absent → `IDokobitService.IsEnabled == false`,
  signing endpoints return a clear "not configured" error (existing pattern).

## 9. Build order

1. **Dokobit transport rewrite + live sandbox smoke test** (prove signing with a throwaway test PDF).
2. **docx fill (Open XML) + Gotenberg client.**
3. **Provider portal: upload + token validation + preview UI.**
4. **Wire `dokobit/initiate` to fill the supplier's docx → PDF → sign.**

## 10. Out of scope (YAGNI)

Dual-signer (tenant+partner), PDF coordinate overlay, AcroForm field mapping, ASiC-E/BDOC
containers (we use PAdES PDF), conditional clauses, partner template versioning history.

## 11. Ops / founder actions (not code)

- Deploy **Gotenberg** as a Railway service (`gotenberg/gotenberg:8`); set `GOTENBERG__URL`.
- Set `SIGNING__DOKOBIT__ACCESSTOKEN` (sandbox now; production token only after Dokobit's
  integration review + signed contract — see their onboarding docs).
