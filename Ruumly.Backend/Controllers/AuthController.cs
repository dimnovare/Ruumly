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
    INotificationService notificationService) : ControllerBase
{
    [HttpPost("register")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var response = await authService.RegisterAsync(request);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var response = await authService.LoginAsync(request);
        return Ok(response);
    }

    [HttpPost("refresh")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request)
    {
        var response = await authService.RefreshAsync(request.RefreshToken);
        return Ok(response);
    }

    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest request)
    {
        await authService.LogoutAsync(request.RefreshToken);
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
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        await authService.ChangePasswordAsync(User.GetUserId(), request);
        return Ok(new { message = "Parool uuendatud." });
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
}

// Inline request DTOs — too small to warrant their own files
public record RefreshTokenRequest(string RefreshToken);
public record VerifyEmailRequest(string Token);
