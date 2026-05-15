using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    IAuthService authService,
    RuumlyDbContext db,
    INotificationService notificationService,
    IConfiguration config,
    IWebHostEnvironment env) : ControllerBase
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
        int.Parse(config["Jwt:RefreshTokenExpiryDays"] ?? "7");

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
        return Ok(new { message = "Parool uuendatud." });
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
        var validLangs = new[] { "et", "en", "ru" };
        if (!validLangs.Contains(request.Language))
            return BadRequest(new { message = "Invalid language. Use et, en, or ru." });

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

    [HttpPost("apply-provider")]
    [Authorize]
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

        user.Role       = UserRole.Provider;
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

    [HttpPost("resend-verification")]
    [EnableRateLimiting("auth")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ResendVerification()
    {
        var userId = User.GetUserId();
        await authService.ResendVerificationEmailAsync(userId);
        return Ok(new { message = "Kinnitusmeil on uuesti saadetud." });
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

        // Revoke all refresh tokens
        await db.RefreshTokens
            .Where(t => t.UserId == userId && !t.IsRevoked)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsRevoked, true));

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
    public IActionResult NotifyInterest([FromBody] NotifyInterestRequest body)
    {
        // Log for now — can implement email notification later
        return Ok(new { success = true });
    }

    [HttpPatch("/api/supplier/tier")]
    [Authorize(Roles = "Provider,Admin")]
    public async Task<IActionResult> ChangeTier([FromBody] ChangeTierRequest body)
    {
        var userId = User.GetUserId();
        var user   = await db.Users.FindAsync(userId);

        Guid supplierId;
        if (body.SupplierId.HasValue && User.GetUserRole() == UserRole.Admin)
            supplierId = body.SupplierId.Value;
        else if (user?.SupplierId is not null)
            supplierId = user.SupplierId.Value;
        else
            return BadRequest(new { error = "No supplier linked." });

        var supplier = await db.Suppliers.FindAsync(supplierId);
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
public record NotifyInterestRequest(string Email, string City);
