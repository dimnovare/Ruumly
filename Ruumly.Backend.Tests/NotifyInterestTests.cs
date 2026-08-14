using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Ruumly.Backend.Controllers;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Microsoft.AspNetCore.Hosting;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Tests;

public class NotifyInterestTests
{
    // ─── Test infrastructure ───────────────────────────────────────────────

    private static RuumlyDbContext CreateDb() => TestDbContext.Create();

    private static AuthController MakeController(
        RuumlyDbContext db,
        IBackgroundEmailQueue? emailQueue = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"]                   = "test-secret-key-minimum-sixteen-chars",
                ["Jwt:Issuer"]                   = "ruumly-api",
                ["Jwt:Audience"]                 = "ruumly-frontend",
                ["Jwt:AccessTokenExpiryMinutes"] = "15",
                ["Jwt:RefreshTokenExpiryDays"]   = "7",
                ["AppUrl"]                       = "https://test.ruumly.eu",
            })
            .Build();

        var controller = new AuthController(
            authService:         new NoOpAuthService(),
            db:                  db,
            notificationService: new NoOpNotificationService(),
            config:              config,
            env:                 new NoOpWebHostEnvironment(),
            emailQueue:          emailQueue ?? new NoOpBackgroundEmailQueue());

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }

    private static AdminLeadsController MakeAdminController(RuumlyDbContext db, bool withUser = false)
    {
        var httpContext = new DefaultHttpContext();
        if (withUser)
        {
            httpContext.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) },
                    "test"));
        }

        return new AdminLeadsController(db)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    // ─── NoOp stubs ────────────────────────────────────────────────────────

    private sealed class NoOpAuthService : IAuthService
    {
        public Task<AuthResponse> RegisterAsync(RegisterRequest request) => Task.FromResult<AuthResponse>(null!);
        public Task<AuthResponse> LoginAsync(LoginRequest request) => Task.FromResult<AuthResponse>(null!);
        public Task<AuthResponse> RefreshAsync(string refreshToken) => Task.FromResult<AuthResponse>(null!);
        public Task LogoutAsync(string refreshToken) => Task.CompletedTask;
        public Task<UserDto> GetMeAsync(Guid userId) => Task.FromResult<UserDto>(null!);
        public Task RequestPasswordResetAsync(string email) => Task.CompletedTask;
        public Task<bool> ResetPasswordAsync(string token, string newPassword) => Task.FromResult(false);
        public Task<AuthResponse> GoogleLoginAsync(string credential) => Task.FromResult<AuthResponse>(null!);
        public Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request) => Task.CompletedTask;
        public Task UpdateLanguageAsync(Guid userId, string language) => Task.CompletedTask;
        public Task<bool> VerifyEmailAsync(string token) => Task.FromResult(false);
        public Task ResendVerificationEmailAsync(Guid userId) => Task.CompletedTask;
        public string ComputeCsrfToken(string rawRefreshToken) => string.Empty;
    }

    private sealed class NoOpNotificationService : INotificationService
    {
        public Task<PaginatedResult<NotificationDto>> GetAllAsync(Guid userId, int page = 1, int limit = 50)
            => Task.FromResult(new PaginatedResult<NotificationDto>([], 0, page, limit, false));

        public Task MarkReadAsync(Guid id, Guid userId) => Task.CompletedTask;
        public Task MarkAllReadAsync(Guid userId) => Task.CompletedTask;

        public Task CreateAsync(
            Guid userId, NotificationType type, string title, string desc,
            string? actionUrl = null, string? entityId = null, string? entityType = null)
            => Task.CompletedTask;
    }

    private sealed class NoOpWebHostEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = string.Empty;
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
        public string ApplicationName { get; set; } = "Test";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Development";
    }

    private sealed class NoOpBackgroundEmailQueue : IBackgroundEmailQueue
    {
        public void EnqueueEmail(string to, string subject, string textBody, string? htmlBody = null) { }
        public void EnqueueVerificationEmail(Guid userId) { }
    }

    private sealed class CapturingBackgroundEmailQueue : IBackgroundEmailQueue
    {
        public List<(string To, string Subject, string TextBody)> Emails { get; } = [];
        public List<Guid> VerificationUsers { get; } = [];

        public void EnqueueEmail(string to, string subject, string textBody, string? htmlBody = null) =>
            Emails.Add((to, subject, textBody));

        public void EnqueueVerificationEmail(Guid userId) =>
            VerificationUsers.Add(userId);
    }

    // ─── Tests ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task NotifyInterest_NewLead_IsCreated()
    {
        var db         = CreateDb();
        var controller = MakeController(db);

        var result = await controller.NotifyInterest(
            new NotifyInterestRequest("test@example.com", "Tallinn"));

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var value = ok.Value!;
        ((bool)value.GetType().GetProperty("success")!.GetValue(value)!).Should().BeTrue();

        db.DemandLeads.Count().Should().Be(1);

        var lead = db.DemandLeads.First();
        lead.Email.Should().Be("test@example.com");
        lead.City.Should().Be("Tallinn");
        lead.Status.Should().Be(DemandLeadStatus.New);
    }

    [Fact]
    public async Task ApplyProviderPublic_QueuesApplicantAndAdminEmailsAfterPersisting()
    {
        var db = CreateDb();
        var queue = new CapturingBackgroundEmailQueue();
        var controller = MakeController(db, queue);

        var result = await controller.ApplyProviderPublic(new SupplierApplicationRequest
        {
            CompanyName = "Test Storage",
            RegistryCode = "12345678",
            ContactName = "Test Partner",
            ContactEmail = "partner@example.com",
            ContactPhone = "+3725555555",
            Language = "en",
        });

        result.Should().BeOfType<OkObjectResult>();
        var user = db.Users.Single(u => u.Email == "partner@example.com");
        db.Suppliers.Should().ContainSingle(s => s.Id == user.SupplierId);
        queue.VerificationUsers.Should().Equal(user.Id);
        queue.Emails.Should().ContainSingle(e =>
            e.To == "admin@ruumly.eu" &&
            e.Subject.Contains("Test Storage"));
    }

    [Fact]
    public async Task ApplyProviderPublic_PersistsServiceTypesIntoServiceTypesJson_NormalizedAndValidated()
    {
        var db         = CreateDb();
        var controller = MakeController(db);

        var result = await controller.ApplyProviderPublic(new SupplierApplicationRequest
        {
            CompanyName  = "Clean & Move OÜ",
            RegistryCode = "87654321",
            ContactName  = "Applicant",
            ContactEmail = "apply@example.com",
            ContactPhone = "+3725550000",
            // Mixed case + whitespace + a duplicate + an unknown slug.
            ServiceTypes = ["Cleaning", " moving ", "cleaning", "teleportation"],
            Language     = "en",
        });

        result.Should().BeOfType<OkObjectResult>();

        var supplier = db.Suppliers.Single(s => s.ContactEmail == "apply@example.com");
        supplier.ServiceTypesJson.Should().NotBeNull(
            "structured service coverage must land in the queryable column, not only the Notes blob");

        var slugs = System.Text.Json.JsonSerializer.Deserialize<List<string>>(supplier.ServiceTypesJson!)!;
        slugs.Should().BeEquivalentTo(["cleaning", "moving"],
            "slugs are normalized (trim + lowercase + dedupe) and unknown slugs are dropped");

        // Backward compat: the human-readable Notes blob still carries the raw list.
        supplier.Notes.Should().Contain("ServiceTypes:");
    }

    [Fact]
    public async Task ApplyProviderPublic_StoresTheLoginAddressLowercased()
    {
        var db         = CreateDb();
        var controller = MakeController(db);

        var result = await controller.ApplyProviderPublic(new SupplierApplicationRequest
        {
            CompanyName  = "Yard OÜ",
            RegistryCode = "11223344",
            ContactName  = "Yard Owner",
            ContactEmail = "  Info@Yard.ee ",
            ContactPhone = "+3725551111",
            Language     = "en",
        });

        result.Should().BeOfType<OkObjectResult>();
        db.Users.Single().Email.Should().Be("info@yard.ee",
            "Users.Email is the login identity and its unique index is on the raw column — "
            + "storing it as typed lets one mailbox own two accounts");
    }

    [Fact]
    public async Task ApplyProviderPublic_ReplayedWithDifferentCapitalisation_IsTheSameApplication()
    {
        // The dedupe used to compare the address as typed, so re-submitting the
        // form with the shift key held minted a second Supplier and a second
        // User for one mailbox — a duplicate junk row in the admin match queue.
        var db         = CreateDb();
        var controller = MakeController(db);

        static SupplierApplicationRequest Application(string email) => new()
        {
            CompanyName  = "Yard OÜ",
            RegistryCode = "11223344",
            ContactName  = "Yard Owner",
            ContactEmail = email,
            ContactPhone = "+3725551111",
            Language     = "en",
        };

        await controller.ApplyProviderPublic(Application("info@yard.ee"));
        var replay = await controller.ApplyProviderPublic(Application("Info@Yard.EE"));

        replay.Should().BeOfType<OkObjectResult>();
        db.Suppliers.Should().ContainSingle();
        db.Users.Should().ContainSingle();
    }

    [Fact]
    public async Task ApplyProviderPublic_ExistingAccountInDifferentCapitalisation_IsFoundNotDuplicated()
    {
        // A customer who registered before applying already has a User row. Missing
        // it on casing created a SECOND row for the same address, which then made
        // login ambiguous (the lookup lowercases both sides but has no ORDER BY)
        // and left a stray account in the way of the claim → account flow.
        var db = CreateDb();
        db.Users.Add(new User
        {
            Id           = Guid.NewGuid(),
            Name         = "Yard Owner",
            Email        = "Info@Yard.ee",
            PasswordHash = "x",
            Role         = UserRole.Customer,
            Status       = UserStatus.Active,
            Language     = "et",
            RegisteredAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var result = await MakeController(db).ApplyProviderPublic(new SupplierApplicationRequest
        {
            CompanyName  = "Yard OÜ",
            RegistryCode = "11223344",
            ContactName  = "Yard Owner",
            ContactEmail = "info@yard.ee",
            ContactPhone = "+3725551111",
            Language     = "en",
        });

        result.Should().BeOfType<OkObjectResult>();
        db.Users.Should().ContainSingle("one mailbox is one account, however it was capitalised");
    }

    // ─── The public form must never link an account it cannot prove is the
    //     caller's. See AuthController.ExistingAccountAsync. ─────────────────

    private static User ExistingAccount(
        string email, bool verified = false, string language = "et", Guid? supplierId = null) => new()
    {
        Id            = Guid.NewGuid(),
        Name          = "Yard Owner",
        Email         = email,
        PasswordHash  = "x",
        Role          = supplierId is null ? UserRole.Customer : UserRole.Provider,
        Status        = UserStatus.Active,
        Language      = language,
        SupplierId    = supplierId,
        EmailVerified = verified,
        RegisteredAt  = DateTime.UtcNow,
    };

    private static SupplierApplicationRequest Application(
        string email, string company = "Yard OÜ", string language = "en") => new()
    {
        CompanyName  = company,
        RegistryCode = "11223344",
        ContactName  = "Someone Else",
        ContactEmail = email,
        ContactPhone = "+3725551111",
        Language     = language,
    };

    [Fact]
    public async Task ApplyProviderPublic_ExistingAccount_IsNeverLinkedToASupplier()
    {
        // The griefing hole: an anonymous caller who typed a stranger's address
        // got User.SupplierId written for them. That alone locked the real owner
        // out of applying — the authenticated route answers 409 "User is already
        // a provider" the moment SupplierId is set.
        var db    = CreateDb();
        var queue = new CapturingBackgroundEmailQueue();
        db.Users.Add(ExistingAccount("owner@yard.ee"));
        await db.SaveChangesAsync();

        var result = await MakeController(db, queue).ApplyProviderPublic(Application("owner@yard.ee"));

        result.Should().BeOfType<OkObjectResult>();
        db.Users.Single().SupplierId.Should().BeNull(
            "an anonymous form submission proves nothing about who owns the mailbox");
        db.Suppliers.Should().BeEmpty("nothing is created for a caller who proved nothing");
        db.IntegrationSettings.Should().BeEmpty();
        queue.VerificationUsers.Should().BeEmpty(
            "the account already exists — a verification mail would say nothing about what happened");

        queue.Emails.Should().ContainSingle(e => e.To == "owner@yard.ee")
            .Which.Subject.Should().Be(EmailTranslations.For("et").ApplySignInSubject);
        queue.Emails.Should().ContainSingle(e => e.To == "admin@ruumly.eu",
            "ops still has to see a genuine application that could not be self-served");
    }

    [Fact]
    public async Task ApplyProviderPublic_ExistingVerifiedAccount_IsTold_NotSilentlyLinked()
    {
        // The worst case of the old behaviour: BackgroundEmailService skips the
        // verification mail for an already-verified user, so the victim was linked
        // to a business they never applied for and heard nothing at all.
        var db    = CreateDb();
        var queue = new CapturingBackgroundEmailQueue();
        db.Users.Add(ExistingAccount("verified@yard.ee", verified: true));
        await db.SaveChangesAsync();

        await MakeController(db, queue).ApplyProviderPublic(Application("verified@yard.ee"));

        db.Users.Single().SupplierId.Should().BeNull();
        queue.Emails.Should().ContainSingle(e => e.To == "verified@yard.ee",
            "a verified account holder must hear about it, not be linked in silence");
    }

    [Fact]
    public async Task ApplyProviderPublic_ExistingProviderAccount_AnswersLikeAnyOtherSubmission()
    {
        // This used to be a 409 "An application for this email already exists" —
        // an account-existence oracle handed to anyone with the form open.
        var db         = CreateDb();
        var supplierId = Guid.NewGuid();
        db.Users.Add(ExistingAccount("partner@yard.ee", supplierId: supplierId));
        await db.SaveChangesAsync();

        var result = await MakeController(db).ApplyProviderPublic(Application("partner@yard.ee"));

        result.Should().BeOfType<OkObjectResult>(
            "the reply must not tell an anonymous caller whether an address has an account");
        db.Users.Single().SupplierId.Should().Be(supplierId, "the existing link is untouched");
        db.Suppliers.Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyProviderPublic_ExistingAccount_ReplayedSubmissions_SendOneEmail()
    {
        // No Supplier row is created for these submissions, so the dedupe that
        // used to cap the mail at one per address no longer applies. Without the
        // audit-row throttle, the form becomes a mail-bombing tool aimed at the
        // very account it is meant to protect.
        var db    = CreateDb();
        var queue = new CapturingBackgroundEmailQueue();
        db.Users.Add(ExistingAccount("owner@yard.ee"));
        await db.SaveChangesAsync();

        var controller = MakeController(db, queue);
        await controller.ApplyProviderPublic(Application("owner@yard.ee"));
        var replay = await controller.ApplyProviderPublic(Application("Owner@Yard.EE"));

        replay.Should().BeOfType<OkObjectResult>("a throttled replay is still an accepted submission");
        queue.Emails.Should().ContainSingle(e => e.To == "owner@yard.ee",
            "the throttle keys off the canonical address, so capitalisation cannot bypass it");
        queue.Emails.Should().ContainSingle(e => e.To == "admin@ruumly.eu");
        db.AuditLogs.Should().ContainSingle();
    }

    [Fact]
    public async Task ApplyProviderPublic_ExistingAccount_EmailsInTheAccountLanguage()
    {
        // The recipient is the account holder, not the submitter, so their stored
        // language decides — and one more thing stays out of a stranger's hands.
        var db    = CreateDb();
        var queue = new CapturingBackgroundEmailQueue();
        db.Users.Add(ExistingAccount("owner@yard.ee", language: "ru"));
        await db.SaveChangesAsync();

        await MakeController(db, queue)
            .ApplyProviderPublic(Application("owner@yard.ee", language: "en"));

        queue.Emails.Single(e => e.To == "owner@yard.ee")
            .Subject.Should().Be(EmailTranslations.For("ru").ApplySignInSubject);
    }

    [Fact]
    public async Task ApplyProviderPublic_ExistingAccount_EmailCarriesNothingFromTheSubmission()
    {
        // Whoever filled the form is unauthenticated and may not be the recipient.
        // If their text reached this email, the endpoint would be a delivery
        // channel for a stranger's message to a chosen address.
        const string Planted = "URGENT confirm your bank details at evil.example";

        var db    = CreateDb();
        var queue = new CapturingBackgroundEmailQueue();
        db.Users.Add(ExistingAccount("owner@yard.ee"));
        await db.SaveChangesAsync();

        await MakeController(db, queue)
            .ApplyProviderPublic(Application("owner@yard.ee", company: Planted));

        var nudge = queue.Emails.Single(e => e.To == "owner@yard.ee");
        nudge.Subject.Should().NotContain("evil.example");
        nudge.TextBody.Should().NotContain("evil.example",
            "the mail is built from translated copy and a configured URL only");

        queue.Emails.Single(e => e.To == "admin@ruumly.eu").TextBody.Should().Contain(Planted,
            "the submitted details still reach the internal inbox, where a human reads them");
    }

    [Fact]
    public async Task NotifyInterest_DuplicateWithin7Days_IsSilentlyIgnored()
    {
        var db = CreateDb();
        db.DemandLeads.Add(new DemandLead
        {
            Id        = Guid.NewGuid(),
            Email     = "test@example.com",
            City      = "Tallinn",
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            Status    = DemandLeadStatus.New,
            Language  = "et",
        });
        await db.SaveChangesAsync();

        var controller = MakeController(db);

        var result = await controller.NotifyInterest(
            new NotifyInterestRequest("test@example.com", "Tallinn"));

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var value = ok.Value!;
        ((bool)value.GetType().GetProperty("success")!.GetValue(value)!).Should().BeTrue();

        db.DemandLeads.Count().Should().Be(1);
    }

    [Fact]
    public async Task AdminGetLeads_Returns_AllRows()
    {
        var db = CreateDb();
        db.DemandLeads.AddRange(
            new DemandLead
            {
                Id        = Guid.NewGuid(),
                Email     = "a@test.com",
                City      = "Tallinn",
                Status    = DemandLeadStatus.New,
                Language  = "et",
                CreatedAt = DateTime.UtcNow,
            },
            new DemandLead
            {
                Id        = Guid.NewGuid(),
                Email     = "b@test.com",
                City      = "Tartu",
                Status    = DemandLeadStatus.Contacted,
                Language  = "en",
                CreatedAt = DateTime.UtcNow,
            });
        await db.SaveChangesAsync();

        var controller = MakeAdminController(db);

        var result = await controller.GetLeads(status: null, page: 1, limit: 50);

        var ok    = result.Should().BeOfType<OkObjectResult>().Subject;
        var value = ok.Value!;
        var total = (int)value.GetType().GetProperty("total")!.GetValue(value)!;
        var page = (int)value.GetType().GetProperty("page")!.GetValue(value)!;
        var limit = (int)value.GetType().GetProperty("limit")!.GetValue(value)!;
        var items = (System.Collections.IEnumerable)value.GetType().GetProperty("items")!.GetValue(value)!;

        total.Should().Be(2);
        page.Should().Be(1);
        limit.Should().Be(50);
        items.Cast<object>().Should().HaveCount(2);
    }

    [Fact]
    public async Task AdminUpdateLead_UpdatesStatus()
    {
        var db = CreateDb();
        var lead = new DemandLead
        {
            Id        = Guid.NewGuid(),
            Email     = "lead@test.com",
            City      = "Tallinn",
            Status    = DemandLeadStatus.New,
            Language  = "et",
            CreatedAt = DateTime.UtcNow,
        };
        db.DemandLeads.Add(lead);
        await db.SaveChangesAsync();

        var controller = MakeAdminController(db, withUser: true);

        var result = await controller.UpdateLead(lead.Id, new UpdateLeadRequest("contacted", null));

        result.Should().BeOfType<OkObjectResult>();
        db.DemandLeads.First().Status.Should().Be(DemandLeadStatus.Contacted);
    }
}
