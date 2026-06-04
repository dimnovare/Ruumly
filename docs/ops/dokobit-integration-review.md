# Dokobit Documents Gateway — Integration Review Submission

> **Purpose:** the document to send to Dokobit (developers@dokobit.com) to request the
> **integration review** that unlocks production access + the production access token.
> Per Dokobit's onboarding: *"Access to the production environment is granted only after a
> successful integration review and after signing the contract."*
>
> **Status:** sandbox integration complete and validated end-to-end (see §6). Awaiting review.
> **Fill in the [BRACKETS]** before sending (company/legal details, contact).

---

## 1. Merchant / integrator

| Field | Value |
|---|---|
| Service name | **Ruumly** — storage-rental marketplace (Estonia / Baltics) |
| Website | https://ruumly.eu |
| Legal entity | [Ruumly OÜ — reg. nr. ____ once registered] |
| Country | Estonia |
| Use case | Tenants digitally **sign a storage rental contract** with a verified eID identity after booking a unit. One signer (the tenant) per document. |
| Technical contact | [name / email] |
| Sandbox access token in use | `testgw_…` (issued [date], 90-day) |
| Environments | Sandbox `gateway-sandbox.dokobit.com` (live), Production `gateway.dokobit.com` (requested) |

## 2. What we sign

- **Document type:** `pdf` (PAdES). The contract is a partner-provided Word (`.docx`) template
  with `{{placeholder}}` tokens, filled per booking and rendered to PDF (via a Gotenberg/LibreOffice
  service) before signing. One A4 PDF per signing.
- **Signers:** exactly **one** — the tenant — signing via **Smart-ID** or **Mobile-ID**
  (`signing_options[] = smartid, mobile`), `signing_purpose = signature`.

## 3. API integration (Documents Gateway)

Environment-selected base URL; access token sent as `?access_token=` query parameter on every call;
requests `application/x-www-form-urlencoded`, responses parsed defensively.

1. **Upload** — `POST /api/file/upload.json` with `file[name]`, `file[digest]` (SHA-256 hex of the PDF),
   `file[content]` (base64 of the PDF). We upload the **content directly (base64)** — the contract is
   never hosted at a public URL, protecting tenant personal data.
2. **Create signing** — `POST /api/signing/create.json` with `type=pdf`, `name`, `signers[0][id]=1`,
   `signers[0][name]`, `signers[0][surname]`, `signers[0][code]`, `signers[0][country_code]`,
   `signers[0][signing_purpose]=signature`, `signers[0][signing_options][]=smartid` &
   `=mobile`, `files[0][token]`, `postback_url`. We read back the signing token and the per-signer
   access token.
3. **Signer redirect** — the tenant is sent to `{base}/signing/{signingToken}?access_token={signerToken}`
   (Dokobit-hosted signing page; all Smart-ID/Mobile-ID interaction happens there).
4. **Completion** — handled by our **postback** (§4), with client polling of
   `GET /api/signing/{token}/status.json` only as a fallback.
5. **Download** — on `signed`, the signed PDF is retrieved (inline base64 / download URL /
   `…/files.json`) and stored in our private object storage (Cloudflare R2).

## 4. Callback / error handling / idempotency

- **Postback URL:** `https://api.ruumly.eu/api/contracts/dokobit/callback` (public, unauthenticated by
  design, antiforgery-exempt — it is a server-to-server provider callback).
- **We do not trust the callback body.** On callback we **re-fetch authoritative status** from
  `…/status.json` before acting.
- **Idempotent:** a contract that is already in a terminal state (`signed`/`declined`/`expired`) is a
  no-op on replay; the endpoint always returns `200` to avoid provider retry storms.
- **Failure branches handled:** failed init → user-facing error + alert; declined/expired → contract
  marked accordingly; signature/validation failure → rejected; network/timeout → caught, retried/surfaced.

## 5. Security

- The **access token is stored only as a Railway environment variable** (`SIGNING__DOKOBIT__ACCESSTOKEN`)
  — never in client code, never in the git repository, never sent to the browser.
- All signing is initiated **server-side**; the browser only opens the Dokobit-hosted signing URL.
- We persist a tamper-evidence hash of the rendered document and capture the **verified** national
  ID code from `signer_info.code` (not from user input).
- Transport is HTTPS end-to-end (TLS terminates at Cloudflare → our backend).

## 6. Smart-ID / Mobile-ID branding compliance

We follow SK ID Solutions' brand-placement guidelines
(https://www.smart-id.com/e-service-providers/smart-id-branding/ and
https://www.mobile-id.lt/en/logos-and-branding/):
- The actual Smart-ID/Mobile-ID authentication & signing UI is rendered on **Dokobit's hosted page**.
- In our own UI, the method choices are presented as **"Smart-ID"** / **"Mobile-ID"** using the
  official names and (where shown) official logos with required clear-space; no modification of the
  marks. [Confirm/screenshot before submitting.]

## 7. Sandbox validation evidence (already passing)

Validated the full chain end-to-end against `gateway-sandbox.dokobit.com`:
- `docx → PDF (Gotenberg) → upload.json` → `{"status":"ok","token":…}`
- `signing/create.json` (PDF, one Smart-ID/Mobile-ID signer, `signing_purpose=signature`) →
  `{"status":"ok","token":…,"signers":{"1":…}}`
- Signer signing URL reachable (`200`).
- Empirically confirmed: `signers[0][signing_purpose]` is **required**; per-signer status nests under
  `signers["1"]`. Our parser handles both that and a flat `signer_info`.

*(Attach request/response samples or a screen-recording of a sandbox signing on request.)*

## 8. Request

We request an **integration review** and, on approval, **production access + a production access token**,
and the **production agreement/contract** to sign. We are ready to walk through a live sandbox signing
at your convenience.

— [Name], Ruumly · [email] · [date]
