using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.Filters;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;
using BC = BCrypt.Net.BCrypt;

namespace Ruumly.Backend.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    IAuthService authService,
    RuumlyDbContext db,
    INotificationService notificationService,
    IConfiguration config,
    IWebHostEnvironment env,
    IBackgroundEmailQueue emailQueue) : ControllerBase
{
    // Sets the HttpOnly refresh-token cookie on every successful auth response.
    // SameSite=None because the frontend (ruumly.eu) and API (Railway) are on different origins.
    // CSRF is mitigated by: (a) CORS policy restricts allowed origins, and
    // (b) the paired CsrfToken returned in the JSON body must be sent as X-CSRF-Token on /refresh.
    private void SetRefreshCookie(string refreshToken, int expiryDays)
    {
        var isDev = env.IsDevelopment();
        Response.Cookies.Append("ruumly-refresh", refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure   = !isDev,
            SameSite = isDev ? SameSiteMode.Lax : SameSiteMode.None,
            Path     = "/api/auth",
            MaxAge   = TimeSpan.FromDays(expiryDays),
        });
    }

    private int RefreshTokenExpiryDays =>
        int.TryParse(config["Jwt:RefreshTokenExpiryDays"], out var days) ? days : 7;

    [HttpPost("register")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var response = await authService.RegisterAsync(request);
        SetRefreshCookie(response.RefreshToken, RefreshTokenExpiryDays);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var response = await authService.LoginAsync(request);
        SetRefreshCookie(response.RefreshToken, RefreshTokenExpiryDays);
        return Ok(response);
    }

    [HttpPost("refresh")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest? body)
    {
        var tokenFromCookie = Request.Cookies["ruumly-refresh"];
        var tokenFromBody   = body?.RefreshToken;
        var refreshToken    = tokenFromCookie ?? tokenFromBody;
        if (string.IsNullOrEmpty(refreshToken))
            return Unauthorized();

        var csrfHeader = Request.Headers["X-CSRF-Token"].FirstOrDefault();

        // Cookie-sourced refresh can bootstrap without CSRF (token was set by us,
        // and CORS already prevents cross-origin POST). Body-sourced refresh
        // always needs CSRF because it's readable by JS.
        bool needsCsrf = tokenFromBody is not null && tokenFromCookie is null;

        if (needsCsrf || !string.IsNullOrEmpty(csrfHeader))
        {
            if (string.IsNullOrEmpty(csrfHeader))
                return Unauthorized(new { message = "CSRF token required." });
            var expected = authService.ComputeCsrfToken(refreshToken);
            if (!string.Equals(csrfHeader, expected, StringComparison.Ordinal))
                return Unauthorized(new { message = "Invalid CSRF token." });
        }

        var response = await authService.RefreshAsync(refreshToken);

        // Reuse-grace replay (concurrent multi-tab refresh): the service returns a fresh
        // access token but an EMPTY refresh token to signal "the cookie was already
        // rotated by the first refresh — do not overwrite it". Only (re)set the cookie
        // when a NEW refresh token was actually issued.
        if (!string.IsNullOrEmpty(response.RefreshToken))
            SetRefreshCookie(response.RefreshToken, RefreshTokenExpiryDays);

        return Ok(response);
    }

    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest? request)
    {
        // Accept token from cookie (new flow) or body (backward compat)
        var refreshToken = Request.Cookies["ruumly-refresh"] ?? request?.RefreshToken;
        if (!string.IsNullOrEmpty(refreshToken))
            await authService.LogoutAsync(refreshToken);

        Response.Cookies.Delete("ruumly-refresh", new CookieOptions
        {
            Path     = "/api/auth",
            Secure   = !env.IsDevelopment(),
            SameSite = env.IsDevelopment() ? SameSiteMode.Lax : SameSiteMode.None,
        });
        return Ok(new { message = "Logged out successfully" });
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Me()
    {
        var userId = User.GetUserId();
        var user   = await authService.GetMeAsync(userId);
        return Ok(user);
    }

    [HttpPost("forgot-password")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        await authService.RequestPasswordResetAsync(request.Email);
        // Always 200 — never reveal if email exists
        return Ok(new { message = "If that email exists, a reset link was sent." });
    }

    [HttpPost("reset-password")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        var success = await authService.ResetPasswordAsync(request.Token, request.NewPassword);
        if (!success)
            return BadRequest(new { message = "Invalid or expired reset token." });
        return Ok(new { message = "Password updated successfully." });
    }

    [HttpPost("change-password")]
    [Authorize]
    [EnableRateLimiting("user")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        await authService.ChangePasswordAsync(User.GetUserId(), request);
        return Ok(new { message = "Password updated successfully." });
    }

    /// <summary>Update the authenticated user's own profile (name, phone, company).</summary>
    [HttpPatch("me")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateMyProfile([FromBody] PatchProfileRequest body)
    {
        var userId = User.GetUserId();
        var user = await db.Users.FindAsync(userId);
        if (user is null)
            return NotFound(new { error = "User not found." });

        if (body.Name is not null)
        {
            var trimmed = body.Name.Trim();
            if (trimmed.Length < 2 || trimmed.Length > 100)
                return BadRequest(new { error = "Name must be 2-100 characters." });
            user.Name = trimmed;
        }

        if (body.Phone is not null)
        {
            var trimmed = body.Phone.Trim();
            if (trimmed.Length > 30)
                return BadRequest(new { error = "Phone too long." });
            user.Phone = string.IsNullOrEmpty(trimmed) ? null : trimmed;
        }

        if (body.Company is not null)
        {
            var trimmed = body.Company.Trim();
            if (trimmed.Length > 200)
                return BadRequest(new { error = "Company name too long." });
            user.Company = string.IsNullOrEmpty(trimmed) ? null : trimmed;
        }

        await db.SaveChangesAsync();

        return Ok(await authService.GetMeAsync(userId));
    }

    [HttpPatch("language")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateLanguage([FromBody] UpdateLanguageRequest request)
    {
        var validLangs = new[] { "et", "en", "ru", "lv", "lt" };
        if (!validLangs.Contains(request.Language))
            return BadRequest(new { message = "Invalid language. Use et, en, ru, lv, or lt." });

        await authService.UpdateLanguageAsync(User.GetUserId(), request.Language);
        return NoContent();
    }

    [HttpPost("verify-email")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest request)
    {
        var success = await authService.VerifyEmailAsync(request.Token);
        if (!success)
            return BadRequest(new { message = "Invalid or expired verification token." });
        return Ok(new { message = "Email verified successfully." });
    }

    [HttpPost("google")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginRequest request)
    {
        var response = await authService.GoogleLoginAsync(request.Credential);
        SetRefreshCookie(response.RefreshToken, RefreshTokenExpiryDays);
        return Ok(response);
    }

    [RequireEmailVerified]
    [HttpPost("apply-provider")]
    [Authorize]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ApplyProvider([FromBody] SupplierApplicationRequest request)
    {
        var userId = User.GetUserId();
        var user   = await db.Users.FindAsync(userId)
            ?? throw new KeyNotFoundException("User not found.");

        if (user.Role == UserRole.Provider || user.SupplierId.HasValue)
            return Conflict(new { message = "User is already a provider." });

        var supplier = new Supplier
        {
            Id            = Guid.NewGuid(),
            Name          = request.CompanyName,
            RegistryCode  = request.RegistryCode,
            ContactName   = request.ContactName,
            ContactEmail  = request.ContactEmail,
            ContactPhone  = request.ContactPhone,
            Notes         = BuildNotes(request),
            IsActive      = false,
            CreatedAt     = DateTime.UtcNow,
            UpdatedAt     = DateTime.UtcNow,
        };

        var integrationSettings = new IntegrationSettings
        {
            Id           = Guid.NewGuid(),
            SupplierId   = supplier.Id,
            ApprovalMode = ApprovalMode.Auto,
            PostingMode  = PostingMode.Email,
            IsActive     = false,
            UpdatedAt    = DateTime.UtcNow,
        };

        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            // Re-check inside the transaction to handle concurrent double-apply
            var freshUser = await db.Users
                .Where(u => u.Id == userId)
                .Select(u => new { u.Role, u.SupplierId })
                .FirstOrDefaultAsync();
            if (freshUser?.SupplierId.HasValue == true || freshUser?.Role == UserRole.Provider)
                return Conflict(new { message = "User is already a provider." });

            user.SupplierId = supplier.Id;

            db.Suppliers.Add(supplier);
            db.IntegrationSettings.Add(integrationSettings);

            db.AuditLogs.Add(new AuditLog
            {
                Id        = Guid.NewGuid(),
                Action    = "supplier.application_submitted",
                Actor     = user.Email,
                Target    = supplier.Name,
                Detail    = $"RegistryCode: {supplier.RegistryCode}",
                CreatedAt = DateTime.UtcNow,
            });

            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        // Notify all admins
        var adminIds = await db.Users
            .Where(u => u.Role == UserRole.Admin)
            .Select(u => u.Id)
            .ToListAsync();

        foreach (var adminId in adminIds)
        {
            await notificationService.CreateAsync(
                userId:     adminId,
                type:       NotificationType.Alert,
                title:      "Uus partneriavaldus",
                desc:       $"Uus partneriavaldus: {supplier.Name}",
                actionUrl:  $"/admin/suppliers/{supplier.Id}",
                entityId:   supplier.Id.ToString(),
                entityType: "supplier");
        }

        return StatusCode(StatusCodes.Status201Created, new
        {
            supplierId = supplier.Id,
            name       = supplier.Name,
            isActive   = supplier.IsActive,
            message    = "Avaldus esitatud. Admin vaatab selle läbi.",
        });
    }

    private static string BuildNotes(SupplierApplicationRequest r)
    {
        var parts = new List<string>();
        parts.Add($"BusinessType: {r.BusinessType}");
        if (r.ServiceTypes.Length > 0)
            parts.Add($"ServiceTypes: {string.Join(", ", r.ServiceTypes)}");
        if (r.ServiceAreas.Length > 0)
            parts.Add($"ServiceAreas: {string.Join(", ", r.ServiceAreas)}");
        if (!string.IsNullOrWhiteSpace(r.Notes))
            parts.Add(r.Notes);
        return string.Join("\n", parts);
    }

    [HttpPost("apply-provider-public")]
    [AllowAnonymous]
    // Tighter "public-email" policy (5 / 10 min / IP): this anonymous endpoint sends
    // a verification email to a user-supplied address — limit spam amplification.
    [EnableRateLimiting("public-email")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ApplyProviderPublic([FromBody] SupplierApplicationRequest request)
    {
        // Validate required fields
        if (string.IsNullOrWhiteSpace(request.CompanyName))
            return BadRequest(new { error = "Company name is required." });
        if (!EmailValidation.IsValid(request.ContactEmail))
            return BadRequest(new { error = "Valid contact email is required." });

        var lang = request.Language ?? "et";

        // Idempotency guard: deduplicate by ContactEmail BEFORE creating any rows.
        // If a Supplier already exists for this contact email (or one was just created),
        // return the same success response WITHOUT inserting new User/Supplier rows or
        // re-queuing the verification email. Stops row-spam and email-bombing of a
        // third-party address by anyone replaying the form. Legit first submissions are
        // unaffected because no matching supplier exists yet.
        var existingApplication = await db.Suppliers
            .Where(s => s.ContactEmail == request.ContactEmail)
            .Select(s => new { s.Id })
            .FirstOrDefaultAsync();
        if (existingApplication is not null)
            return Ok(new { applicationId = existingApplication.Id, message = "Application received. Please check your email." });

        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            // Check if user with ContactEmail already exists
            var existingUser = await db.Users
                .Include(u => u.Supplier)
                .FirstOrDefaultAsync(u => u.Email == request.ContactEmail);

            Guid supplierId;
            Guid userId;

            if (existingUser is not null)
            {
                // User exists and already has a supplier
                if (existingUser.SupplierId.HasValue)
                    return Conflict(new { error = "An application for this email already exists." });

                // User exists but no supplier — create supplier linked to existing user
                userId = existingUser.Id;
                var supplier = CreateSupplier(request);
                supplierId = supplier.Id;

                existingUser.SupplierId = supplierId;

                var integrationSettings = CreateIntegrationSettings(supplier.Id);
                db.Suppliers.Add(supplier);
                db.IntegrationSettings.Add(integrationSettings);
            }
            else
            {
                // New user — create User (Customer role, EmailVerified=false) + Supplier
                var supplier = CreateSupplier(request);
                supplierId = supplier.Id;

                var newUser = new User
                {
                    Id            = Guid.NewGuid(),
                    Name          = request.ContactName,
                    Email         = request.ContactEmail,
                    PasswordHash  = BC.HashPassword(Guid.NewGuid().ToString(), workFactor: 4),
                    Role          = UserRole.Customer,
                    Status        = UserStatus.Active,
                    Language      = lang,
                    SupplierId    = supplierId,
                    RegisteredAt  = DateTime.UtcNow,
                    EmailVerified = false,
                };
                userId = newUser.Id;

                var integrationSettings = CreateIntegrationSettings(supplier.Id);
                db.Users.Add(newUser);
                db.Suppliers.Add(supplier);
                db.IntegrationSettings.Add(integrationSettings);
            }

            db.AuditLogs.Add(new AuditLog
            {
                Id        = Guid.NewGuid(),
                Action    = "supplier.public_application_submitted",
                Actor     = request.ContactEmail,
                Target    = request.CompanyName,
                Detail    = $"RegistryCode: {request.RegistryCode}",
                CreatedAt = DateTime.UtcNow,
            });

            await db.SaveChangesAsync();
            await tx.CommitAsync();

            // Queue after commit so Hangfire owns the execution scope and retries.
            emailQueue.EnqueueVerificationEmail(userId);

            // Notify admin
            emailQueue.EnqueueEmail(
                to:       "admin@ruumly.eu",
                subject:  $"New provider application: {request.CompanyName}",
                textBody: $"Company: {request.CompanyName}\nContact: {request.ContactName} <{request.ContactEmail}>\nPhone: {request.ContactPhone}\nRegistry: {request.RegistryCode}");

            return Ok(new { applicationId = supplierId, message = "Application received. Please check your email." });
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    private Supplier CreateSupplier(SupplierApplicationRequest r) => new()
    {
        Id           = Guid.NewGuid(),
        Name         = r.CompanyName,
        RegistryCode = r.RegistryCode,
        ContactName  = r.ContactName,
        ContactEmail = r.ContactEmail,
        ContactPhone = r.ContactPhone,
        Notes        = BuildNotes(r),
        IsActive     = false,
        CreatedAt    = DateTime.UtcNow,
        UpdatedAt    = DateTime.UtcNow,
    };

    private static IntegrationSettings CreateIntegrationSettings(Guid supplierId) => new()
    {
        Id           = Guid.NewGuid(),
        SupplierId   = supplierId,
        ApprovalMode = ApprovalMode.Auto,
        PostingMode  = PostingMode.Email,
        IsActive     = false,
        UpdatedAt    = DateTime.UtcNow,
    };

    [HttpPost("resend-verification")]
    [EnableRateLimiting("auth")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ResendVerification()
    {
        var userId = User.GetUserId();
        await authService.ResendVerificationEmailAsync(userId);
        return Ok(new { message = "Verification email resent." });
    }

    [HttpDelete("account")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteAccount()
    {
        var userId = User.GetUserId();

        var user = await db.Users.FindAsync(userId);
        if (user is null) return NotFound();

        var hasActiveBookings = await db.Bookings.AnyAsync(b =>
            b.UserId == userId &&
            b.Status != BookingStatus.Cancelled &&
            b.Status != BookingStatus.Completed);

        if (hasActiveBookings)
            return BadRequest(new { message = "Cancel active bookings before deleting your account." });

        // Anonymize PII — keep rows for financial record integrity
        user.Name         = "Deleted User";
        user.Email        = $"deleted-{user.Id}@ruumly.eu";
        user.Phone        = null;
        user.Company      = null;
        user.Avatar       = null;
        user.GoogleId     = null;
        user.PasswordHash = "";
        user.Status       = UserStatus.Deleted;
        user.DeletedAt    = DateTime.UtcNow;

        // Revoke all refresh tokens. Session kill: ReplacedByTokenId stays null so the
        // refresh reuse-grace never resurrects these tokens.
        await db.RefreshTokens
            .Where(t => t.UserId == userId && !t.IsRevoked)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.IsRevoked, true)
                .SetProperty(t => t.RevokedAt, DateTime.UtcNow));

        db.AuditLogs.Add(new AuditLog
        {
            Id        = Guid.NewGuid(),
            Action    = "account.deleted",
            Actor     = userId.ToString(),
            Target    = userId.ToString(),
            Detail    = "User requested account deletion — PII anonymized",
            CreatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("account/export")]
    [Authorize]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportData()
    {
        var userId = User.GetUserId();

        var user = await db.Users.FindAsync(userId);
        if (user is null) return NotFound();

        var bookings = await db.Bookings
            .Include(b => b.Timeline)
            .Where(b => b.UserId == userId)
            .ToListAsync();

        var bookingIds = bookings.Select(b => b.Id).ToList();

        var invoices = await db.Invoices
            .Where(i => bookingIds.Contains(i.BookingId))
            .ToListAsync();

        var messages = await db.Messages
            .Where(m => m.UserId == userId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(500)
            .ToListAsync();

        var reviews = await db.Reviews
            .Where(r => r.UserId == userId)
            .ToListAsync();

        var export = new
        {
            exportedAt = DateTime.UtcNow,
            profile = new
            {
                name         = user.Name,
                email        = user.Email,
                phone        = user.Phone,
                company      = user.Company,
                registeredAt = user.RegisteredAt,
            },
            bookings = bookings.Select(b => new
            {
                b.Id,
                listingId  = b.ListingId,
                status     = b.Status.ToString().ToLower(),
                startDate  = b.StartDate.ToString("yyyy-MM-dd"),
                endDate    = b.EndDate?.ToString("yyyy-MM-dd"),
                total      = b.Total,
                createdAt  = b.CreatedAt,
                timeline   = b.Timeline.OrderBy(t => t.CreatedAt).Select(t => new
                {
                    date   = t.CreatedAt.ToString("yyyy-MM-dd"),
                    @event = t.Event,
                    status = t.Status.ToString().ToLower(),
                }),
            }),
            invoices = invoices.Select(i => new
            {
                i.Id,
                bookingId     = i.BookingId,
                amount        = i.Amount,
                status        = i.Status.ToString().ToLower(),
                issuedAt      = i.IssuedAt,
                paidAt        = i.PaidAt,
                paymentMethod = i.PaymentMethod,
            }),
            messages = messages.Select(m => new
            {
                m.Id,
                bookingId  = m.BookingId,
                from       = m.From.ToString().ToLower(),
                text       = m.Text,
                createdAt  = m.CreatedAt,
            }),
            reviews = reviews.Select(r => new
            {
                r.Id,
                listingId = r.ListingId,
                rating    = r.Rating,
                comment   = r.Comment,
                createdAt = r.CreatedAt,
            }),
        };

        Response.Headers.Append("Content-Disposition", "attachment; filename=\"ruumly-data-export.json\"");
        return new JsonResult(export);
    }

    [HttpPost("notify-interest")]
    [AllowAnonymous]
    // Tighter "public-email" policy (5 / 10 min / IP): anonymous endpoint that fires an
    // admin notification email per submission — curb unauthenticated spam amplification.
    [EnableRateLimiting("public-email")]
    public async Task<IActionResult> NotifyInterest([FromBody] NotifyInterestRequest body)
    {
        if (!EmailValidation.IsValid(body.Email))
            return BadRequest(new { error = "Invalid email." });

        var city = (body.City ?? "").Trim();

        // Deduplicate: same email + city in last 7 days
        // Wrap dedup check + insert in transaction to prevent race conditions
        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            var recent = await db.DemandLeads.AnyAsync(d =>
                d.Email == body.Email &&
                d.City == city &&
                d.CreatedAt >= DateTime.UtcNow.AddDays(-7));

            if (recent)
            {
                await tx.RollbackAsync();
                return Ok(new { success = true });
            }

            var lead = new DemandLead
            {
                Id        = Guid.NewGuid(),
                Email     = body.Email,
                City      = city,
                Category  = ParseCategory(body.Category),
                Query     = body.Query,
                Language  = body.Language ?? "et",
                CreatedAt = DateTime.UtcNow,
                Status    = DemandLeadStatus.New,
            };

            db.DemandLeads.Add(lead);
            await db.SaveChangesAsync();
            await tx.CommitAsync();

            // Notify admin (after transaction commits)
            emailQueue.EnqueueEmail(
                to:       "admin@ruumly.eu",
                subject:  $"New demand lead: {body.Email} ({city})",
                textBody: $"Email: {body.Email}\nCity: {city}\nCategory: {lead.Category}\nQuery: {lead.Query}");

            return Ok(new { success = true });
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    private static DemandLeadCategory ParseCategory(string? cat) => cat?.ToLowerInvariant() switch
    {
        "warehouse" => DemandLeadCategory.Warehouse,
        "moving"    => DemandLeadCategory.Moving,
        "trailer"   => DemandLeadCategory.Trailer,
        _           => DemandLeadCategory.Any,
    };

    [HttpPatch("/api/supplier/tier")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ChangeTier([FromBody] ChangeTierRequest body)
    {
        if (!body.SupplierId.HasValue)
            return BadRequest(new { error = "supplierId is required." });

        var supplier = await db.Suppliers.FindAsync(body.SupplierId.Value);
        if (supplier is null) return NotFound();

        if (!Enum.TryParse<SupplierTier>(body.Tier, ignoreCase: true, out var newTier))
            return BadRequest(new { error = "Invalid tier. Use: Starter, Standard, Premium" });

        var oldTier = supplier.Tier;
        supplier.Tier = newTier;
        supplier.MonthlyFee = newTier switch
        {
            SupplierTier.Starter  => 19m,
            SupplierTier.Standard => 49m,
            SupplierTier.Premium  => 99m,
            _                     => 19m,
        };
        supplier.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Ok(new
        {
            oldTier    = oldTier.ToString(),
            newTier    = supplier.Tier.ToString(),
            monthlyFee = supplier.MonthlyFee,
        });
    }
}

// Inline request DTOs — too small to warrant their own files
// RefreshToken is optional: cookie-based refresh sends no body
public record RefreshTokenRequest(string? RefreshToken = null);
public record VerifyEmailRequest(string Token);
public record NotifyInterestRequest(string Email, string City, string? Category = null, string? Query = null, string? Language = null);
