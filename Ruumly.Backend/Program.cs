using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using FluentValidation;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Ruumly.Backend.Data;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Middleware;
// SeedData is in Ruumly.Backend.Data namespace — already covered
using Ruumly.Backend.Models;
using Resend;
using Ruumly.Backend.Jobs;
using Ruumly.Backend.Services.Implementations;
using Ruumly.Backend.Services.Interfaces;
// BookingService, OrderRoutingService, IntegrationDispatchService are in same namespace
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.Identity;
using Ruumly.Backend.Identity.SmartId;
using Ruumly.Backend.Identity.MobileId;
using Asp.Versioning;
using Asp.Versioning.ApiExplorer;
using Hangfire;
using Hangfire.Dashboard;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Serilog;
using Swashbuckle.AspNetCore.SwaggerGen;

var logConfig = new LoggerConfiguration()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .MinimumLevel.Information()
    .MinimumLevel.Override(
        "Microsoft.EntityFrameworkCore.Database.Command",
        Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext();

// Only write to file in development — Railway containers are ephemeral
if (!Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
        ?.Equals("Production", StringComparison.OrdinalIgnoreCase) ?? true)
{
    logConfig = logConfig.WriteTo.File(
        "logs/ruumly-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        outputTemplate:
            "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}");
}

Log.Logger = logConfig.CreateLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog();

// ─── Database ───
// Railway injects DATABASE_URL as a postgres:// URI; fall back to appsettings for local dev.
var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL") is { Length: > 0 } dbUrl
    ? ParseDatabaseUrl(dbUrl)
    : builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddDbContext<RuumlyDbContext>(options =>
    options.UseNpgsql(connectionString));

// ─── JWT Authentication ───
var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtSecret = jwtSection["Secret"]!;

if (string.IsNullOrWhiteSpace(jwtSecret))
    throw new InvalidOperationException(
        "[Ruumly] FATAL: Jwt:Secret is not configured. " +
        "Set it via the JWT__SECRET environment variable in Railway. " +
        "The app cannot start safely without a signing key.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidAudience = jwtSection["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<RuumlyDbContext>()
    .SetApplicationName("ruumly");
builder.Services.AddSingleton<TokenProtector>();

// ─── Google OAuth config validation ───
var googleClientId = builder.Configuration["Google:ClientId"];
if (string.IsNullOrWhiteSpace(googleClientId))
{
    Console.WriteLine(
        "[Ruumly] WARNING: Google:ClientId not configured. " +
        "Google login will be unavailable.");
}

// ─── Montonio config validation ───
// Keys are empty strings by default; they must be set via Railway env vars:
//   MONTONIO__ACCESSKEY, MONTONIO__SECRETKEY
// For sandbox testing set MONTONIO__USESANDBOX=true and use sandbox key pair.
var montonioAccessKey = builder.Configuration["Montonio:AccessKey"];
var montonioSecretKey = builder.Configuration["Montonio:SecretKey"];
if (string.IsNullOrWhiteSpace(montonioAccessKey) || string.IsNullOrWhiteSpace(montonioSecretKey))
{
    Console.WriteLine(
        "[Ruumly] WARNING: Montonio:AccessKey or Montonio:SecretKey not configured. " +
        "Payment initiation and webhook verification will fail. " +
        "Set MONTONIO__ACCESSKEY and MONTONIO__SECRETKEY in Railway env vars.");
}

// ─── Distributed cache (Redis in prod, in-memory fallback for dev) ───
var redisConn = Environment.GetEnvironmentVariable("REDIS_URL") ?? "";
if (!string.IsNullOrEmpty(redisConn))
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConn;
        options.InstanceName  = "ruumly:";
    });
}
else
{
    builder.Services.AddDistributedMemoryCache();
}
builder.Services.AddResponseCaching();

// ─── Forwarded headers (Cloudflare → Railway proxy chain) ───
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Trust ALL upstream proxies — Cloudflare's egress IPs rotate constantly;
    // an IP allowlist would break on every Cloudflare expansion.
    // Real client IP is read from CF-Connecting-IP (set by Cloudflare, not forgeable downstream).
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// ─── Cloudflare egress ranges ───
// Source: https://www.cloudflare.com/ips/  (IPv4: /ips-v4, IPv6: /ips-v6)
// Snapshot date: 2026-06-18. Cloudflare changes these rarely; refresh if edge
// connectivity ever fails the IsCloudflareIp check. These are the networks
// Cloudflare connects to our origin FROM, so a request whose RemoteIpAddress is
// in one of these ranges genuinely transited Cloudflare and its CF-Connecting-IP
// / X-Forwarded-For headers can be trusted. Parsed once at startup.
var cloudflareNetworks = new[]
{
    // IPv4
    "173.245.48.0/20",
    "103.21.244.0/22",
    "103.22.200.0/22",
    "103.31.4.0/22",
    "141.101.64.0/18",
    "108.162.192.0/18",
    "190.93.240.0/20",
    "188.114.96.0/20",
    "197.234.240.0/22",
    "198.41.128.0/17",
    "162.158.0.0/15",
    "104.16.0.0/13",
    "104.24.0.0/14",
    "172.64.0.0/13",
    "131.0.72.0/22",
    // IPv6
    "2400:cb00::/32",
    "2606:4700::/32",
    "2803:f800::/32",
    "2405:b500::/32",
    "2405:8100::/32",
    "2a06:98c0::/29",
    "2c0f:f248::/32",
}
.Select(System.Net.IPNetwork.Parse)
.ToArray();

static bool IsCloudflareIp(System.Net.IPAddress? remote, System.Net.IPNetwork[] cfNetworks)
{
    if (remote is null) return false;
    // Normalise IPv4-mapped IPv6 (e.g. ::ffff:104.16.0.1) so it matches the IPv4 CIDRs.
    if (remote.IsIPv4MappedToIPv6) remote = remote.MapToIPv4();
    foreach (var net in cfNetworks)
        if (net.Contains(remote)) return true;
    return false;
}

// ─── Rate limiting (per-client partitioned) ───
string IpKey(HttpContext ctx)
{
    // Only trust client-supplied forwarding headers when the request actually reached
    // us THROUGH Cloudflare (RemoteIpAddress is a CF edge IP). A direct-to-origin
    // attacker can set CF-Connecting-IP / X-Forwarded-For to anything to rotate around
    // per-IP login/email limits, so we ignore those headers unless the socket peer is
    // inside Cloudflare's published egress ranges.
    var remote = ctx.Connection.RemoteIpAddress;
    if (IsCloudflareIp(remote, cloudflareNetworks))
    {
        var cfIp = ctx.Request.Headers["CF-Connecting-IP"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(cfIp)) return cfIp;
        var xff = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(xff))
            // X-Forwarded-For is a comma-separated chain; the left-most entry is the
            // original client as recorded by Cloudflare.
            return xff.Split(',')[0].Trim();
    }
    return remote?.ToString() ?? "unknown";
}

string UserOrIpKey(HttpContext ctx) =>
    ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
    ?? IpKey(ctx);

builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("auth", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(IpKey(ctx), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit          = 10,
            Window               = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit           = 0,
        }));

    options.AddPolicy("search", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(IpKey(ctx), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window      = TimeSpan.FromMinutes(1),
            QueueLimit  = 0,
        }));

    // Anonymous endpoints that send email to third-party / unverified addresses
    // (apply-provider-public, notify-interest). Tighter than "auth" (10/min) to
    // curb unauthenticated email-spam amplification — no CAPTCHA in front of these.
    options.AddPolicy("public-email", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(IpKey(ctx), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window      = TimeSpan.FromMinutes(10),
            QueueLimit  = 0,
        }));

    options.AddPolicy("upload", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(IpKey(ctx), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window      = TimeSpan.FromMinutes(1),
            QueueLimit  = 0,
        }));

    options.AddPolicy("booking", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(UserOrIpKey(ctx), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window      = TimeSpan.FromMinutes(1),
            QueueLimit  = 0,
        }));

    options.AddPolicy("payment", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(UserOrIpKey(ctx), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window      = TimeSpan.FromMinutes(1),
            QueueLimit  = 0,
        }));

    options.AddPolicy("user", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(UserOrIpKey(ctx), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window      = TimeSpan.FromMinutes(1),
            QueueLimit  = 0,
        }));

    // Anonymous Montonio payment webhook (payments/webhook). Partitioned by IP since the
    // caller is unauthenticated (Montonio's server, verified by JWT signature, not auth).
    // Generous cap so legit Montonio delivery/retry bursts are never dropped, while still
    // bounding the JWT-crypto + DB-lookup cost of a direct-to-origin flood.
    options.AddPolicy("webhook", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(IpKey(ctx), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120,
            Window      = TimeSpan.FromMinutes(1),
            QueueLimit  = 0,
        }));

    // Anonymous Dokobit signing postback (dokobit/callback). Partitioned by IP since the
    // caller is unauthenticated (Dokobit's server). The endpoint always 200s and re-fetches
    // status server-to-server, so a modest cap curbs abuse without dropping legitimate
    // per-signature callbacks.
    options.AddPolicy("dokobit", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(IpKey(ctx), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window      = TimeSpan.FromMinutes(1),
            QueueLimit  = 0,
        }));

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();

        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync(
            """{"error":"Too many requests. Please try again later."}""", cancellationToken);
    };
});

// ─── CORS ───
var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>()!;
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
        policy.SetIsOriginAllowed(origin =>
        {
            if (allowedOrigins.Contains(origin)) return true;

            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
            if (!uri.Host.EndsWith(".vercel.app")) return false;

            // Only allow exact Ruumly Vercel project slugs.
            // Preview URLs follow the pattern: {projectName}-{hash}-{teamSlug}.vercel.app
            // Using a broad prefix like "ruumly-" would let any attacker register
            // "ruumly-evil.vercel.app" and make credentialed cross-origin requests.
            var host = uri.Host;
            return host.StartsWith("estonia-space-hub-") ||
                   host == "estonia-space-hub.vercel.app";
            // Add future project slugs explicitly — never use a short prefix alone.
        })
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

// ─── FluentValidation ───
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

// The sanitized 400 factory is wired via AddControllers().AddSanitizedValidationErrors()
// below — it MUST run after the framework's ApiBehaviorOptionsSetup (registered by
// AddControllers), so it cannot be configured here (a Configure<ApiBehaviorOptions>
// before AddControllers is silently overwritten by the framework default).

// ─── API Versioning ───
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion                   = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions                   = true;
    options.ApiVersionReader                    = ApiVersionReader.Combine(
        new UrlSegmentApiVersionReader(),
        new HeaderApiVersionReader("X-Api-Version"));
})
.AddApiExplorer(options =>
{
    options.GroupNameFormat           = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});

builder.Services.AddTransient<IConfigureOptions<SwaggerGenOptions>, ConfigureSwaggerOptions>();

// ─── Hangfire ───
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(c => c.UseNpgsqlConnection(connectionString)));

builder.Services.AddHangfireServer();
builder.Services.AddScoped<BackgroundOrderDispatchService>();
builder.Services.AddScoped<BackgroundEmailService>();
builder.Services.AddScoped<BackgroundCleanupService>();

// ─── Application services ───
builder.Services.AddScoped<IPricingConfigService, PricingConfigService>();
builder.Services.AddScoped<IBackgroundEmailQueue, HangfireBackgroundEmailQueue>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IListingService, ListingService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IInvoiceService, InvoiceService>();
builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddScoped<IOrderRoutingService, OrderRoutingService>();
builder.Services.AddScoped<IIntegrationDispatchService, IntegrationDispatchService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IMessageService, MessageService>();
builder.Services.AddScoped<IFeaturedPartnersService, FeaturedPartnersService>();
builder.Services.AddScoped<ISupplierProfileService, SupplierProfileService>();
builder.Services.AddScoped<IPaymentService, MontonioPaymentService>();
builder.Services.AddScoped<IPlacesService, PlacesService>();
builder.Services.AddScoped<ISupplierPollingService, SupplierPollingService>();
builder.Services.AddScoped<IContractService, ContractService>();
builder.Services.AddScoped<SupplierPollingDispatcherJob>();
builder.Services.AddHttpClient();
// Dokobit — typed HttpClient; IsEnabled gated on Signing:Dokobit:AccessToken presence.
// Dokobit requires its access token in the query string. Remove the framework HTTP
// loggers for this client so request URLs cannot leak that credential into Railway logs.
builder.Services.AddHttpClient<IDokobitService, DokobitService>().RemoveAllLoggers();
// Contract docx fill (Open XML) + Gotenberg docx→PDF render.
builder.Services.AddSingleton<IContractDocumentService, OpenXmlContractDocumentService>();
builder.Services.AddHttpClient<GotenbergClient>(c => c.Timeout = TimeSpan.FromSeconds(90));
builder.Services.AddScoped<IGotenbergClient, GotenbergClient>();

// ─── Identity verification (Smart-ID / Mobile-ID) ────────────────────────────
// Env-gated: only registered when SmartId:RelyingPartyUuid is set.
// In Railway: set SMARTID__RELYINGPARTYUUID and SMARTID__RELYINGPARTYNAME env vars.
var smartIdUuid = builder.Configuration["SmartId:RelyingPartyUuid"];
if (!string.IsNullOrWhiteSpace(smartIdUuid))
{
    builder.Services.Configure<SmartIdConfig>(builder.Configuration.GetSection("SmartId"));
    builder.Services.Configure<MobileIdConfig>(builder.Configuration.GetSection("MobileId"));
    builder.Services.AddHttpClient<SmartIdProvider>();
    builder.Services.AddHttpClient<MobileIdProvider>();
    builder.Services.AddScoped<IIdentityVerificationProvider, SmartIdProvider>();
    builder.Services.AddScoped<IIdentityVerificationProvider, MobileIdProvider>();
    builder.Services.AddScoped<IdentityVerificationService>();
    Console.WriteLine("[Ruumly] Smart-ID / Mobile-ID identity verification: ENABLED.");
}
else
{
    Console.WriteLine("[Ruumly] Smart-ID / Mobile-ID identity verification: DISABLED (SmartId:RelyingPartyUuid not set).");
}

// ─── Storage service ───
if (builder.Environment.IsProduction())
    builder.Services.AddScoped<IStorageService, CloudflareR2StorageService>();
else
    builder.Services.AddScoped<IStorageService, LocalDiskStorageService>();

if (builder.Environment.IsProduction())
{
    builder.Services.AddOptions();
    builder.Services.AddHttpClient<ResendClient>();
    builder.Services.Configure<ResendClientOptions>(o =>
    {
        o.ApiToken = builder.Configuration["Resend:ApiKey"]
            ?? throw new InvalidOperationException(
                "Resend:ApiKey is required in production. Set it via RESEND__APIKEY environment variable.");
    });
    builder.Services.AddTransient<IResend, ResendClient>();
    builder.Services.AddTransient<IEmailSender, ResendEmailSender>();
}
else
{
    builder.Services.AddTransient<IEmailSender, DevConsoleEmailSender>();
}

// ─── Health checks ───
builder.Services.AddHealthChecks()
    .AddNpgSql(
        connectionString!,
        name: "postgres",
        tags: new[] { "db", "ready" })
    .AddHangfire(options =>
    {
        options.MinimumAvailableServers = 0;
    }, name: "hangfire", tags: new[] { "jobs", "ready" });

builder.Services.AddHttpContextAccessor();

// ─── Controllers ───
builder.Services.AddControllers()
    // Sanitized 400 body for validation + malformed-JSON failures. Chained here
    // (after AddControllers) so it wins over the framework's leaky default factory.
    .AddSanitizedValidationErrors()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

// ─── Swagger / OpenAPI ───
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // Per-version docs are added by ConfigureSwaggerOptions (runs after IApiVersionDescriptionProvider is ready).
    var jwtScheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT access token",
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
    };

    options.AddSecurityDefinition("Bearer", jwtScheme);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { jwtScheme, Array.Empty<string>() }
    });
});

// ─── Sentry ───
builder.WebHost.UseSentry(options =>
{
    options.Dsn                = builder.Configuration["Sentry:Dsn"] ?? "";
    options.TracesSampleRate   = 0.1;   // capture 10 % of transactions for performance monitoring
    options.MinimumEventLevel  = LogLevel.Error;
    options.Environment        = builder.Environment.EnvironmentName;
    // Silence Sentry when no DSN is configured (local dev without a project)
    options.InitializeSdk      = !string.IsNullOrWhiteSpace(builder.Configuration["Sentry:Dsn"]);
});

// ─── Build ───
var app = builder.Build();

// ─── Middleware pipeline ───
app.UseMiddleware<ExceptionMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
// Optional origin lock — no-op unless SECURITY__ORIGINSECRET is set (see middleware doc).
app.UseMiddleware<OriginAuthMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        var provider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();
        foreach (var desc in provider.ApiVersionDescriptions)
        {
            options.SwaggerEndpoint(
                $"/swagger/{desc.GroupName}/swagger.json",
                $"Ruumly API {desc.GroupName.ToUpperInvariant()}");
        }
    });
}

app.UseForwardedHeaders();   // must be before UseRouting, UseAuthentication, UseRateLimiter
app.UseCors("Frontend");
app.UseResponseCaching();
app.UseSentryTracing();
app.UseAuthentication();
app.UseAuthorization();
// Attach user id/email/role to Sentry scope after auth resolves the principal
app.UseMiddleware<SentryUserContextMiddleware>();
app.UseRateLimiter();

// ─── Static file serving for uploaded images ───
if (app.Environment.IsDevelopment())
{
    var uploadsPath = app.Configuration["Storage:BasePath"] ?? "/app/uploads";
    Directory.CreateDirectory(uploadsPath);
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
            Path.GetFullPath(uploadsPath)),
        RequestPath = "/uploads",
    });
}

if (app.Environment.IsDevelopment())
{
    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = [new LocalRequestsOnlyAuthorizationFilter()],
    });
}

using (var scope = app.Services.CreateScope())
{
    var recurringJobManager = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
    recurringJobManager.AddOrUpdate<BackgroundCleanupService>(
        "cleanup-tokens",
        x => x.CleanupStaleRefreshTokensAsync(),
        Cron.Daily);

    recurringJobManager.AddOrUpdate<StaleBookingCleanupJob>(
        "stale-booking-cleanup",
        x => x.ExecuteAsync(),
        Cron.Hourly);

    recurringJobManager.AddOrUpdate<BackgroundCleanupService>(
        "expire-reservations",
        x => x.ExpireReservationsAsync(),
        "*/15 * * * *");

    recurringJobManager.AddOrUpdate<AbandonedBookingReminderJob>(
        "abandoned-booking-reminder",
        x => x.ExecuteAsync(),
        "*/15 * * * *");

    recurringJobManager.AddOrUpdate<SupplierPollingDispatcherJob>(
        "supplier-polling-dispatcher",
        x => x.ExecuteAsync(),
        "*/5 * * * *");   // every 5 minutes — per-supplier interval governs actual cadence

    recurringJobManager.AddOrUpdate<BackgroundCleanupService>(
        "prune-polling-logs",
        x => x.PruneOldPollingLogsAsync(),
        Cron.Weekly);
}

app.MapControllers();

// ─── Health endpoints ───
// Public /health — minimal response for uptime monitors (Railway, UptimeRobot, etc.)
// Returns only HTTP 200 ("ok") or 503 ("unhealthy"). No dependency names, no timings.
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "text/plain";
        await context.Response.WriteAsync(
            report.Status == Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy
                ? "ok"
                : "unhealthy");
    }
});

// Authenticated /health/details — full diagnostic report for admin / on-call
// Returns dependency names, individual check status, and timing.
app.MapHealthChecks("/health/details", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = System.Text.Json.JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            totalDuration = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name        = e.Key,
                status      = e.Value.Status.ToString(),
                duration    = e.Value.Duration.TotalMilliseconds,
                description = e.Value.Description,
                exception   = e.Value.Exception?.Message,
            }),
        });
        await context.Response.WriteAsync(result);
    }
}).RequireAuthorization(new Microsoft.AspNetCore.Authorization.AuthorizeAttribute { Roles = "Admin" });

if (app.Environment.IsProduction())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<RuumlyDbContext>();
    await db.Database.MigrateAsync();
    // Required by SearchVector trigger and ListingService for diacritic-folding.
    // Idempotent — no-op if already installed.
    await db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS unaccent;");
    await db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
}

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<RuumlyDbContext>();
    try
    {
        var pending = await db.Database.GetPendingMigrationsAsync();
        if (pending.Any())
        {
            Console.WriteLine($"[Startup] Applying {pending.Count()} pending migration(s)...");
            await db.Database.MigrateAsync();
        }
        await db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS unaccent;");
    await db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
        await SeedData.SeedAsync(db);
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex,
            "Startup seed/migration failed. App will continue " +
            "but data may be incomplete. Check your database connection.");
    }
}

using (var scope = app.Services.CreateScope())
{
    var db     = scope.ServiceProvider.GetRequiredService<RuumlyDbContext>();
    var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await DemoSeeder.SeedIfRequestedAsync(db, config, logger);
}

// After migrations + seed, flush the featured-listings cache so we don't serve
// stale pre-migration responses (e.g. empty results when synthetic-Location
// listings were excluded). Search keys are hashed and can't be enumerated;
// they expire on TTL within minutes.
using (var scope = app.Services.CreateScope())
{
    var cache = scope.ServiceProvider.GetService<Microsoft.Extensions.Caching.Distributed.IDistributedCache>();
    if (cache is not null)
    {
        try
        {
            foreach (var lang in new[] { "_", "et", "en", "ru", "lv", "lt" })
                await cache.RemoveAsync($"listings:featured:{lang}");
        }
        catch { /* swallow — cache flush is best-effort */ }
    }
}

var port = Environment.GetEnvironmentVariable("PORT") ?? "3000";
app.Urls.Add($"http://+:{port}");
Console.WriteLine($"[Ruumly] Starting on http://localhost:{port}");
Console.WriteLine($"[Ruumly] Swagger: http://localhost:{port}/swagger");
Console.WriteLine($"[Ruumly] Health (public):  http://localhost:{port}/health");
Console.WriteLine($"[Ruumly] Health (details): http://localhost:{port}/health/details (admin auth required)");
app.Run();

// NOTE: top-level local function — not directly unit-testable from the test
// project. If this needs test coverage, promote it to a static method on a
// testable type; do not restructure Program.cs just for that.
//
// Parses a Railway-style postgres://user:password@host:port/db URI into an
// Npgsql connection string. UserInfo is split on the FIRST ':' only so that
// passwords containing ':' survive, and both user/password are URL-decoded
// (e.g. %40 -> '@', %3A -> ':') via Uri.UnescapeDataString. UnescapeDataString
// is a no-op for inputs without '%' sequences, so the produced string is
// identical to the previous implementation for normal (unencoded) URLs.
static string ParseDatabaseUrl(string databaseUrl)
{
    var uri = new Uri(databaseUrl);
    var userInfo = uri.UserInfo.Split(':', 2);
    var username = Uri.UnescapeDataString(userInfo[0]);
    var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;
    var database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
    return $"Host={uri.Host};Port={uri.Port};Database={database};Username={username};Password={password};SSL Mode=Require;Trust Server Certificate=true;Maximum Pool Size=100;Minimum Pool Size=2;Connection Idle Lifetime=300;Connection Pruning Interval=10";
}
