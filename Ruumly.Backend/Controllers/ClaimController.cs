using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Ruumly.Backend.Constants;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Controllers;

/// <summary>
/// Public "claim your profile" flow for DIRECTORY suppliers — the 1,186 rows
/// Ruumly created from public research, each with a scraped ContactEmail and no
/// user account. The introduction campaign tells those providers they can claim
/// their profile to correct their details; this controller is what makes that
/// sentence true.
///
/// Shape: the provider lands on /{lang}/claim/{slug}, types their business
/// email, and — only if it matches the address already on the row — gets a
/// single-use magic link AT THAT STORED ADDRESS. Redeeming it mints a claim
/// session scoped to one supplier id, which is the only credential the edit
/// endpoints accept.
///
/// THE TWO INVARIANTS, both of which have tests:
/// <list type="number">
/// <item>NO DISCLOSURE. Nothing an unauthenticated visitor can reach ever
/// reveals the stored contact email, not even as a masked hint or a differing
/// status code — <see cref="RequestLink"/> returns the identical body whether
/// the address matched, missed, or belongs to a different supplier. The
/// directory's contact data is the asset a scraper would want back, and a
/// probe-by-guessing endpoint would hand it over one address at a time.</item>
/// <item>NO CROSS-TENANT WRITES. A claim session carries its supplier id;
/// nothing in any request body does. A session minted for supplier A that is
/// presented on supplier B's URL is refused, not silently redirected.</item>
/// </list>
/// </summary>
[ApiController]
[Route("api/claim")]
public partial class ClaimController(
    RuumlyDbContext db,
    IBackgroundEmailQueue emailQueue,
    IConfiguration config,
    IDistributedCache cache,
    IAuthService authService,
    IWebHostEnvironment env,
    ILogger<ClaimController> logger) : ControllerBase
{
    /// <summary>Header carrying the claim session token on the edit endpoints.</summary>
    public const string SessionHeader = "X-Claim-Session";

    private const int MaxNameLength        = 200;
    private const int MaxPhoneLength       = 40;
    private const int MaxWebsiteLength     = 500;
    private const int MaxAddressLength     = 200;
    private const int MaxDescriptionLength = 160;
    private const int MaxServiceTypes      = 8;
    private const int MaxPriceUnitLength   = 40;
    private const int MaxPriceNoteLength   = 500;

    /// <summary>
    /// Upper bound on a claimed "from" price. Not a business rule — a typo guard.
    /// A provider meaning 45 EUR/h who types 45000 would otherwise publish it, and
    /// the figure goes straight in front of customers.
    /// </summary>
    private const decimal MaxPriceFrom = 100_000m;

    [GeneratedRegex("^[a-z0-9-]{2,80}$")]
    private static partial Regex SlugShape();

    // ─── Public: what an anonymous visitor may see ────────────────────────────

    /// <summary>
    /// The claim landing page's data: only facts already printed on the public
    /// partner page, plus whether the profile is already claimed. Never the
    /// contact email, never the phone, never a hint about either.
    /// </summary>
    [HttpGet("{slug}")]
    [EnableRateLimiting("search")]
    [ProducesResponseType(typeof(PublicClaimProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetClaimProfile(string slug, CancellationToken ct)
    {
        var supplier = await FindClaimableAsync(slug, ct);
        if (supplier is null) return NotFound(new { error = "Profile not found." });

        var city = await PrimaryCityAsync(supplier.Id, ct);

        return Ok(new PublicClaimProfileDto(
            Slug:         supplier.Slug!,
            Name:         supplier.Name,
            City:         city,
            Country:      supplier.Country,
            ServiceTypes: ParseServiceTypes(supplier.ServiceTypesJson),
            IsClaimed:    supplier.ClaimedAt is not null));
    }

    /// <summary>
    /// "Send me the link." Mints and emails a magic link ONLY when the submitted
    /// address equals the supplier's stored ContactEmail (case-insensitively),
    /// and sends it to the STORED address rather than the submitted one — the
    /// two are equal ignoring case whenever a send happens at all, so preferring
    /// the stored value costs nothing and removes any chance that a crafted
    /// input steers delivery.
    ///
    /// Anything else — wrong address, blank stored address, already-claimed
    /// profile — writes an ops-review row and alerts the ops inbox, and returns
    /// the SAME body as a success. The caller cannot tell the cases apart.
    ///
    /// Rate limit "public-email": the existing bucket for anonymous endpoints
    /// that send mail to unverified third-party addresses (5 per 10 min per IP).
    /// </summary>
    [HttpPost("{slug}/request")]
    [EnableRateLimiting("public-email")]
    [ProducesResponseType(typeof(ClaimRequestAcceptedDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RequestLink(
        string slug, [FromBody] ClaimRequestLinkRequest? body, CancellationToken ct)
    {
        var supplier = await FindClaimableAsync(slug, ct);
        // The slug is public (it is in the URL of a public page), so a 404 for an
        // unknown or non-directory profile leaks nothing.
        if (supplier is null) return NotFound(new { error = "Profile not found." });

        var typed = body?.Email?.Trim() ?? "";
        // Shape validation is safe to surface: it is a fact about the caller's
        // OWN input and says nothing about what is on file.
        if (!EmailValidation.IsValid(typed))
            return BadRequest(new { error = "Please enter a valid email address." });

        var language = ResolveLanguage(body?.Language, supplier);
        var stored   = supplier.ContactEmail?.Trim() ?? "";
        var matched  = stored.Length > 0 &&
                       string.Equals(stored, typed, StringComparison.OrdinalIgnoreCase);

        var now   = DateTime.UtcNow;
        var claim = new SupplierClaim
        {
            Id             = Guid.NewGuid(),
            SupplierId     = supplier.Id,
            RequestedEmail = typed.ToLowerInvariant(),
            EmailMatched   = matched,
            Language       = language,
            RequestIp      = HttpContext.Connection.RemoteIpAddress?.ToString(),
            CreatedAt      = now,
        };

        string? rawToken = null;
        if (matched)
        {
            // A fresh request supersedes any link still outstanding for this
            // supplier: only the newest email can be redeemed, so a link left in
            // a shared inbox stops working the moment the owner asks for another.
            var superseded = await db.SupplierClaims
                .Where(c => c.SupplierId == supplier.Id
                         && c.TokenHash != null
                         && c.VerifiedAt == null
                         && c.TokenExpiresAt > now)
                .ToListAsync(ct);
            foreach (var old in superseded) old.TokenExpiresAt = now;

            rawToken             = ClaimToken.Generate();
            claim.TokenHash      = ClaimToken.Hash(rawToken);
            claim.TokenExpiresAt = now.Add(ClaimToken.LinkLifetime);
        }

        db.SupplierClaims.Add(claim);
        await db.SaveChangesAsync(ct);

        var opsInbox = await OpsInbox.ResolveAsync(db);

        if (matched && rawToken is not null)
        {
            var url = SupplierClaimComposer.ClaimUrl(
                config["AppUrl"], language, supplier.Slug!, rawToken);
            var message = SupplierClaimComposer.Compose(
                language, supplier.Name, url, (int)ClaimToken.LinkLifetime.TotalHours, opsInbox);

            // To the STORED address. Never to `typed`.
            emailQueue.EnqueueEmail(
                stored, message.Subject, message.TextBody, message.HtmlBody, opsInbox);
            logger.LogInformation(
                "Claim link sent for supplier {SupplierId} ({Slug}).", supplier.Id, supplier.Slug);
        }
        else
        {
            // No link, no hint — a human reviews it instead. The ops inbox is
            // internal, so it is the one place the stored address may appear.
            emailQueue.EnqueueEmail(
                opsInbox,
                $"Ruumly — profile claim needs review ({supplier.Name})",
                $"Someone tried to claim the directory profile for {supplier.Name} " +
                $"(/{language}/claim/{supplier.Slug}).\n\n" +
                $"They entered: {typed}\n" +
                $"On file:      {(stored.Length == 0 ? "— (no contact email stored)" : stored)}\n" +
                $"Requested at: {now:u} from {claim.RequestIp ?? "unknown IP"}\n\n" +
                "No link was sent and nothing was changed. If this is genuinely the " +
                "business, verify them by hand (phone, registry, website) and update " +
                "the supplier's contact email in admin — they can then claim it themselves.");
            logger.LogInformation(
                "Claim request for supplier {SupplierId} did not match the stored contact; ops notified.",
                supplier.Id);
        }

        // ONE body for every path.
        return Accepted(new ClaimRequestAcceptedDto(
            Received: true,
            Message:  "If that address is the one we have on file for this business, " +
                      "a confirmation link is on its way to it. Otherwise our team will review the request."));
    }

    // ─── Redeeming the magic link ─────────────────────────────────────────────

    /// <summary>
    /// Redeems the emailed link and mints a claim session. Single-use: the row
    /// is stamped Verified, so a replay of the same URL is indistinguishable
    /// from an unknown token (404). Expired links 404 the same way.
    ///
    /// This is also the moment the profile counts as CLAIMED — proving control
    /// of the inbox is the claim; whether they go on to edit anything is a
    /// separate question.
    /// </summary>
    [HttpPost("verify")]
    [EnableRateLimiting("search")]
    [ProducesResponseType(typeof(ClaimSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Verify([FromBody] ClaimVerifyRequest? body, CancellationToken ct)
    {
        var raw = body?.Token?.Trim();
        if (string.IsNullOrEmpty(raw))
            return NotFound(new { error = "This link is no longer valid." });

        var hash = ClaimToken.Hash(raw);
        var claim = await db.SupplierClaims.FirstOrDefaultAsync(c => c.TokenHash == hash, ct);

        var now = DateTime.UtcNow;
        // One indistinguishable failure for unknown / already-used / expired.
        // Telling them apart would let a holder of a spent link learn that it
        // was once real.
        if (claim is null || claim.VerifiedAt is not null
            || claim.TokenExpiresAt is null || claim.TokenExpiresAt <= now)
        {
            return NotFound(new { error = "This link is no longer valid." });
        }

        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == claim.SupplierId, ct);
        if (supplier is null || !IsClaimable(supplier))
            return NotFound(new { error = "This link is no longer valid." });

        var sessionToken = ClaimToken.Generate();
        claim.VerifiedAt        = now;
        claim.SessionTokenHash  = ClaimToken.Hash(sessionToken);
        claim.SessionExpiresAt  = now.Add(ClaimToken.SessionLifetime);

        // First claim wins the timestamp — it is the campaign's uptake number and
        // must not be reset by the same owner coming back a month later.
        supplier.ClaimedAt ??= now;
        supplier.ClaimedByEmail = claim.RequestedEmail;
        supplier.UpdatedAt = now;

        await db.SaveChangesAsync(ct);
        await InvalidateProfileCacheAsync(supplier.Slug, ct);

        var opsInbox = await OpsInbox.ResolveAsync(db);
        emailQueue.EnqueueEmail(
            opsInbox,
            $"Ruumly — profile claimed ({supplier.Name})",
            $"{supplier.Name} claimed their directory profile from {claim.RequestedEmail}.\n\n" +
            $"Profile: /{claim.Language}/partner/{supplier.Slug}\n" +
            $"Claimed at: {now:u}\n\n" +
            "They can now edit their own name, contact details, address, services and description.");

        return Ok(new ClaimSessionDto(
            SessionToken: sessionToken,
            ExpiresAt:    claim.SessionExpiresAt!.Value,
            Profile:      await BuildEditableAsync(supplier, ct)));
    }

    // ─── Editing, scoped to the claimed supplier ──────────────────────────────

    /// <summary>
    /// Re-reads the editable values for a live session — lets the page survive a
    /// refresh without re-sending the (already spent) magic link.
    /// </summary>
    [HttpGet("{slug}/profile")]
    [EnableRateLimiting("search")]
    [ProducesResponseType(typeof(ClaimEditableProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEditableProfile(string slug, CancellationToken ct)
    {
        var (supplier, failure) = await ResolveSessionAsync(slug, ct);
        if (failure is not null) return failure;

        return Ok(await BuildEditableAsync(supplier!, ct));
    }

    /// <summary>
    /// Saves the claimant's corrections. The supplier is resolved from the
    /// SESSION, never from the payload, and the slug in the route must be that
    /// same supplier's — a session for supplier A presented on supplier B's URL
    /// is a 403, not a write to either row.
    /// </summary>
    [HttpPut("{slug}/profile")]
    [EnableRateLimiting("user")]
    [ProducesResponseType(typeof(ClaimEditableProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateProfile(
        string slug, [FromBody] ClaimUpdateProfileRequest? body, CancellationToken ct)
    {
        var (supplier, failure) = await ResolveSessionAsync(slug, ct);
        if (failure is not null) return failure;
        if (body is null) return BadRequest(new { error = "A profile update is required." });

        var name         = Clean(body.Name);
        var contactPhone = Clean(body.ContactPhone);
        var contactEmail = Clean(body.ContactEmail);
        var websiteUrl   = Clean(body.WebsiteUrl);
        var address      = Clean(body.Address);
        var description  = Clean(body.Description);

        // This text renders on public pages (partner page, map popups), so the
        // same anti-markup hardening the admin bulk import applies to scraped
        // rows applies to provider-authored input.
        if (name is null || name.Length < 2)
            return BadRequest(new { error = "Business name is required." });
        if (name.Length > MaxNameLength)
            return BadRequest(new { error = $"Business name must be at most {MaxNameLength} characters." });
        foreach (var (label, value) in new[]
                 { ("Business name", name), ("Phone", contactPhone),
                   ("Address", address), ("Description", description) })
        {
            if (value is not null && (value.Contains('<') || value.Contains('>')))
                return BadRequest(new { error = $"{label} must not contain '<' or '>'." });
        }
        if (contactPhone is { Length: > MaxPhoneLength })
            return BadRequest(new { error = $"Phone must be at most {MaxPhoneLength} characters." });
        // Never allowed to go blank: it is the address every request email — and
        // any future claim of this profile — depends on.
        if (contactEmail is null || !EmailValidation.IsValid(contactEmail))
            return BadRequest(new { error = "A valid contact email is required." });
        if (websiteUrl is not null &&
            !(websiteUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
              websiteUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
        {
            return BadRequest(new { error = "Website must start with http:// or https://." });
        }
        if (websiteUrl is { Length: > MaxWebsiteLength })
            return BadRequest(new { error = "Website address is too long." });
        if (address is { Length: > MaxAddressLength })
            return BadRequest(new { error = $"Address must be at most {MaxAddressLength} characters." });
        if (description is { Length: > MaxDescriptionLength })
            return BadRequest(new { error = $"Description must be at most {MaxDescriptionLength} characters." });

        var serviceTypes = (body.ServiceTypes ?? [])
            .Select(s => s?.Trim().ToLowerInvariant() ?? "")
            .Where(s => s.Length > 0)
            .Distinct()
            .ToList();
        if (serviceTypes.Count == 0)
            return BadRequest(new { error = "Pick at least one service." });
        if (serviceTypes.Count > MaxServiceTypes)
            return BadRequest(new { error = $"Pick at most {MaxServiceTypes} services." });
        var unknown = serviceTypes.Where(s => !ServiceCategories.BySlug.ContainsKey(s)).ToList();
        if (unknown.Count > 0)
            return BadRequest(new { error = $"Unknown services: {string.Join(", ", unknown)}." });

        // Price. The one field on this form only the provider can fill in, so it
        // gets the same anti-markup and length treatment as the rest, plus a
        // sanity bound: this number is shown to customers verbatim.
        var priceUnit = Clean(body.PriceUnit);
        var priceNote = Clean(body.PriceNote);
        if (body.PriceFrom is { } price && (price < 0 || price > MaxPriceFrom))
            return BadRequest(new { error = $"Price must be between 0 and {MaxPriceFrom:N0}." });
        if (priceUnit is { Length: > MaxPriceUnitLength })
            return BadRequest(new { error = $"Price unit must be at most {MaxPriceUnitLength} characters." });
        if (priceNote is { Length: > MaxPriceNoteLength })
            return BadRequest(new { error = $"Price details must be at most {MaxPriceNoteLength} characters." });
        foreach (var (label, value) in new[] { ("Price unit", priceUnit), ("Price details", priceNote) })
        {
            if (value is not null && (value.Contains('<') || value.Contains('>')))
                return BadRequest(new { error = $"{label} must not contain '<' or '>'." });
        }

        var now = DateTime.UtcNow;
        supplier!.Name             = name;
        supplier.ContactPhone      = contactPhone ?? "";
        supplier.ContactEmail      = contactEmail;
        supplier.WebsiteUrl        = websiteUrl;
        supplier.Tagline           = description;
        supplier.ServiceTypesJson  = JsonSerializer.Serialize(serviceTypes);
        // Only touched when the field is present in the payload, so a client that
        // does not render the price inputs cannot silently wipe a price the
        // provider already gave us. An explicit empty string still clears.
        if (body.PriceFrom is not null) supplier.PriceFrom = body.PriceFrom;
        if (body.PriceUnit is not null) supplier.PriceUnit = priceUnit;
        if (body.PriceNote is not null) supplier.PriceNote = priceNote;
        supplier.UpdatedAt         = now;
        // Everything NOT listed above is deliberately untouched: tier, verified
        // badge, publish state, slug, commerce flags, rating, discounts. A claim
        // proves control of an inbox, not entitlement to any of those.

        if (address is not null)
        {
            var location = await PrimaryLocationQuery(supplier.Id).FirstOrDefaultAsync(ct);
            if (location is not null)
            {
                location.Address   = address;
                location.UpdatedAt = now;
            }
        }

        await db.SaveChangesAsync(ct);
        await InvalidateProfileCacheAsync(supplier.Slug, ct);

        return Ok(await BuildEditableAsync(supplier, ct));
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the claim session from the <c>X-Claim-Session</c> header and
    /// checks it belongs to the supplier the route names. Returns the supplier,
    /// or the exact failure result to send back.
    ///
    /// The two failures are kept distinct ON PURPOSE: 401 means "your session is
    /// gone, ask for a new link", 403 means "this session is valid but not for
    /// this business". Collapsing them would leave a provider whose session
    /// expired staring at a permissions error.
    /// </summary>
    /// <summary>
    /// Turns a verified claim into a real provider login, so the business can
    /// come back later and manage prices, services and their own page instead of
    /// getting one throwaway edit session.
    ///
    /// Deliberately OPTIONAL. The introduction letter tells 754 businesses there
    /// is "no account to create", and that stays true: claiming without ever
    /// touching this endpoint keeps working exactly as before. The account is
    /// what a provider gets for engaging, not a toll on the way in.
    ///
    /// Why this needs no admin approval: the magic link was delivered to the
    /// address already on file, and redeeming it proved control of that mailbox.
    /// That is strictly more evidence than a signup form gathers, so making an
    /// admin re-approve it would add delay and no safety.
    ///
    /// Three rules keep it honest:
    /// <list type="number">
    /// <item>The login is bound to <c>SupplierClaim.RequestedEmail</c> — the
    /// address the link actually went to. NEVER to Supplier.ContactEmail, which
    /// this same session may have rewritten a moment ago, and never to anything
    /// in the request body.</item>
    /// <item>A supplier already linked to a user is refused, so a second claimant
    /// cannot quietly take over a live account.</item>
    /// <item>An address that already has a Ruumly user is refused rather than
    /// silently adopted. Proving control of a mailbox is grounds for a session,
    /// not for seizing an existing account that may hold bookings and bank
    /// details.</item>
    /// </list>
    /// </summary>
    [HttpPost("{slug}/account")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAccount(
        string slug, [FromBody] ClaimCreateAccountRequest? body, CancellationToken ct)
    {
        var (supplier, claim, failure) = await ResolveSessionWithClaimAsync(slug, ct);
        if (failure is not null) return failure;

        var password = body?.Password ?? "";
        if (password.Length < MinPasswordLength)
            return BadRequest(new { error = $"Password must be at least {MinPasswordLength} characters." });

        // The proof this whole endpoint rests on. EmailMatched is false when the
        // visitor typed an address that did not match the stored one — in that
        // case no link was ever sent, so no mailbox was proved.
        if (!claim!.EmailMatched || string.IsNullOrWhiteSpace(claim.RequestedEmail))
            return StatusCode(StatusCodes.Status403Forbidden,
                new { error = "This claim did not verify an email address." });

        var email = claim.RequestedEmail.Trim().ToLowerInvariant();

        if (await db.Users.AnyAsync(u => u.SupplierId == supplier!.Id, ct))
            return Conflict(new
            {
                error   = "This profile already has an account.",
                message = "Sign in with it, or use the password reset if you have lost access.",
            });

        if (await db.Users.AnyAsync(u => u.Email.ToLower() == email, ct))
            return Conflict(new
            {
                error   = "An account already exists for this email address.",
                message = "Sign in with it — we will link it to this profile once you are signed in.",
            });

        var now  = DateTime.UtcNow;
        var user = new User
        {
            Id            = Guid.NewGuid(),
            Name          = supplier!.Name,
            Email         = email,
            PasswordHash  = BCrypt.Net.BCrypt.HashPassword(password),
            Role          = Models.Enums.UserRole.Provider,
            SupplierId    = supplier.Id,
            // Not a shortcut: the magic link went to this address and was
            // redeemed, which is exactly what the verification mail proves.
            EmailVerified = true,
            Status        = Models.Enums.UserStatus.Active,
            Language      = claim.Language,
            RegisteredAt  = now,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Provider account created from claim for supplier {SupplierId} ({Slug}).",
            supplier.Id, supplier.Slug);

        // Log them straight in — a provider who just set a password should not be
        // asked for it again on the next screen.
        var auth = await authService.LoginAsync(new LoginRequest(email, password));
        SetRefreshCookie(auth.RefreshToken);
        return StatusCode(StatusCodes.Status201Created, auth);
    }

    /// <summary>
    /// Mirrors AuthController's cookie exactly — same name, path and flags — so
    /// the session issued here refreshes through the ordinary /api/auth flow and
    /// is indistinguishable from one obtained by signing in.
    /// </summary>
    private void SetRefreshCookie(string refreshToken)
    {
        var isDev = env.IsDevelopment();
        var days  = int.TryParse(config["Jwt:RefreshTokenExpiryDays"], out var d) ? d : 7;
        Response.Cookies.Append("ruumly-refresh", refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure   = !isDev,
            SameSite = isDev ? SameSiteMode.Lax : SameSiteMode.None,
            Path     = "/api/auth",
            MaxAge   = TimeSpan.FromDays(days),
        });
    }

    /// <summary>
    /// Same as AuthService enforces on registration and password reset. Kept in
    /// step deliberately: a weaker rule here would be a side door into the same
    /// account system.
    /// </summary>
    private const int MinPasswordLength = 8;

    /// <summary>
    /// Same guard as <see cref="ResolveSessionAsync"/>, but also hands back the
    /// claim row — needed wherever the VERIFIED address matters rather than the
    /// supplier's current one. Account creation is the case: the session may
    /// legitimately have rewritten Supplier.ContactEmail seconds earlier, and
    /// binding a login to that value would tie it to an unproved mailbox.
    /// </summary>
    private async Task<(Supplier? Supplier, SupplierClaim? Claim, IActionResult? Failure)>
        ResolveSessionWithClaimAsync(string slug, CancellationToken ct)
    {
        var raw = Request.Headers[SessionHeader].ToString().Trim();
        if (string.IsNullOrEmpty(raw))
            return (null, null, Unauthorized(new { error = "Your claim session has expired. Request a new link." }));

        var hash = ClaimToken.Hash(raw);
        var claim = await db.SupplierClaims.FirstOrDefaultAsync(c => c.SessionTokenHash == hash, ct);
        if (claim?.SessionExpiresAt is null || claim.SessionExpiresAt <= DateTime.UtcNow)
            return (null, null, Unauthorized(new { error = "Your claim session has expired. Request a new link." }));

        var (supplier, failure) = await ResolveSessionAsync(slug, ct);
        return failure is not null ? (null, null, failure) : (supplier, claim, null);
    }

    private async Task<(Supplier? Supplier, IActionResult? Failure)> ResolveSessionAsync(
        string slug, CancellationToken ct)
    {
        var raw = Request.Headers[SessionHeader].ToString().Trim();
        if (string.IsNullOrEmpty(raw))
            return (null, Unauthorized(new { error = "Your claim session has expired. Request a new link." }));

        var hash = ClaimToken.Hash(raw);
        var claim = await db.SupplierClaims.FirstOrDefaultAsync(c => c.SessionTokenHash == hash, ct);
        if (claim?.SessionExpiresAt is null || claim.SessionExpiresAt <= DateTime.UtcNow)
            return (null, Unauthorized(new { error = "Your claim session has expired. Request a new link." }));

        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == claim.SupplierId, ct);
        if (supplier is null || !IsClaimable(supplier))
            return (null, NotFound(new { error = "Profile not found." }));

        // The session's supplier is the ONLY supplier it can act on. The route
        // slug has to agree — a token for A on B's URL never reaches a write.
        // Case-insensitive so a hand-retyped URL is not a baffling 403; slugs
        // are minted lower-case and unique, so this cannot match a second row.
        if (!string.Equals(supplier.Slug, slug, StringComparison.OrdinalIgnoreCase))
            return (null, StatusCode(StatusCodes.Status403Forbidden,
                new { error = "This claim session belongs to a different profile." }));

        return (supplier, null);
    }

    private async Task<Supplier?> FindClaimableAsync(string slug, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(slug) || !SlugShape().IsMatch(slug)) return null;

        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Slug == slug, ct);
        return supplier is not null && IsClaimable(supplier) ? supplier : null;
    }

    /// <summary>
    /// Only published, active DIRECTORY rows are claimable. A supplier that
    /// already has a real account is edited by logging in, never by proving
    /// control of a contact address — otherwise this flow would be a second,
    /// weaker door into a live partner's profile.
    /// </summary>
    private static bool IsClaimable(Supplier s) =>
        s.Slug is not null && s.IsActive && s.IsPartnerPagePublished && s.IsDirectoryListing;

    private IQueryable<SupplierLocation> PrimaryLocationQuery(Guid supplierId) =>
        db.SupplierLocations
            .Where(l => l.SupplierId == supplierId && l.IsActive && !l.IsSynthetic)
            .OrderBy(l => l.CreatedAt).ThenBy(l => l.Id);

    private async Task<string?> PrimaryCityAsync(Guid supplierId, CancellationToken ct) =>
        await PrimaryLocationQuery(supplierId).Select(l => l.City).FirstOrDefaultAsync(ct);

    private async Task<ClaimEditableProfileDto> BuildEditableAsync(Supplier s, CancellationToken ct)
    {
        var location = await PrimaryLocationQuery(s.Id)
            .Select(l => new { l.Address, l.City })
            .FirstOrDefaultAsync(ct);

        return new ClaimEditableProfileDto(
            Slug:         s.Slug!,
            Name:         s.Name,
            ContactPhone: NullIfBlank(s.ContactPhone),
            ContactEmail: NullIfBlank(s.ContactEmail),
            WebsiteUrl:   s.WebsiteUrl,
            Address:      location?.Address,
            City:         location?.City,
            Country:      s.Country,
            ServiceTypes: ParseServiceTypes(s.ServiceTypesJson),
            Description:  s.Tagline,
            PriceFrom:    s.PriceFrom,
            PriceUnit:    NullIfBlank(s.PriceUnit),
            PriceNote:    NullIfBlank(s.PriceNote),
            ClaimedAt:    s.ClaimedAt);
    }

    private async Task InvalidateProfileCacheAsync(string? slug, CancellationToken ct)
    {
        if (slug is not null) await cache.RemoveAsync($"supplier:profile:{slug}", ct);
    }

    /// <summary>
    /// Language of the magic-link email: the page the provider is standing on
    /// when it is one we speak, otherwise the supplier's own country language —
    /// the same mapping the introduction campaign used to pick the mail that
    /// sent them here. Never a bare "et" fallback, which would answer a Latvian
    /// provider's Latvian page in Estonian.
    /// </summary>
    private static string ResolveLanguage(string? requested, Supplier supplier)
    {
        var lang = requested?.Trim().ToLowerInvariant();
        return lang is "et" or "en" or "ru" or "lv" or "lt"
            ? lang
            : SupplierIntroComposer.LanguageFor(supplier);
    }

    private static List<string> ParseServiceTypes(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    private static string? Clean(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static string? NullIfBlank(string? value) => Clean(value);
}
