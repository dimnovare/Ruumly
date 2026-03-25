using System.Text;
using FluentValidation;
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
using Ruumly.Backend.Services.Implementations;
using Ruumly.Backend.Services.Interfaces;
// BookingService, OrderRoutingService, IntegrationDispatchService are in same namespace
using Ruumly.Backend.DTOs.Requests;
using Asp.Versioning;
using Asp.Versioning.ApiExplorer;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.Extensions.Options;
using Serilog;
using Swashbuckle.AspNetCore.SwaggerGen;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/ruumly-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        outputTemplate:
            "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command",
        Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .CreateLogger();

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
builder.Services.AddDataProtection();
builder.Services.AddSingleton<TokenProtector>();

// ─── Google OAuth config validation ───
var googleClientId = builder.Configuration["Google:ClientId"];
if (string.IsNullOrWhiteSpace(googleClientId))
{
    Console.WriteLine(
        "[Ruumly] WARNING: Google:ClientId not configured. " +
        "Google login will be unavailable.");
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

// ─── Rate limiting ───
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("auth", limiterOptions =>
    {
        limiterOptions.PermitLimit           = 10;
        limiterOptions.Window                = TimeSpan.FromMinutes(1);
        limiterOptions.QueueProcessingOrder  = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit            = 0;
    });
    options.AddFixedWindowLimiter("search", limiterOptions =>
    {
        limiterOptions.PermitLimit = 60;
        limiterOptions.Window      = TimeSpan.FromMinutes(1);
        limiterOptions.QueueLimit  = 0;
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// ─── CORS ───
var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>()!;
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

// ─── FluentValidation ───
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

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

// ─── Application services ───
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IListingService, ListingService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IInvoiceService, InvoiceService>();
builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddScoped<IOrderRoutingService, OrderRoutingService>();
builder.Services.AddScoped<IIntegrationDispatchService, IntegrationDispatchService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IMessageService, MessageService>();
builder.Services.AddScoped<IPaymentService, MontonioPaymentService>();
builder.Services.AddHttpClient();

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
        tags: new[] { "db", "ready" });

builder.Services.AddHttpContextAccessor();

// ─── Controllers ───
builder.Services.AddControllers()
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
app.UseMiddleware<ApiVersionRewriteMiddleware>();

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

app.UseCors("Frontend");
app.UseSentryTracing();
app.UseAuthentication();
app.UseAuthorization();
// Attach user id/email/role to Sentry scope after auth resolves the principal
app.UseMiddleware<SentryUserContextMiddleware>();
app.UseRateLimiter();

// ─── Static file serving for uploaded images ───
var uploadsPath = app.Configuration["Storage:BasePath"] ?? "/app/uploads";
Directory.CreateDirectory(uploadsPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
        Path.GetFullPath(uploadsPath)),
    RequestPath = "/uploads",
});

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [new HangfireAdminAuthFilter()],
});

app.MapControllers();

app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = System.Text.Json.JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name     = e.Key,
                status   = e.Value.Status.ToString(),
                duration = e.Value.Duration.TotalMilliseconds,
            }),
        });
        await context.Response.WriteAsync(result);
    }
});

if (app.Environment.IsProduction())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<RuumlyDbContext>();
    await db.Database.MigrateAsync();
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

var port = Environment.GetEnvironmentVariable("PORT") ?? "3000";
app.Urls.Add($"http://+:{port}");
Console.WriteLine($"[Ruumly] Starting on http://localhost:{port}");
Console.WriteLine($"[Ruumly] Swagger: http://localhost:{port}/swagger");
Console.WriteLine($"[Ruumly] Health:  http://localhost:{port}/health");
app.Run();

static string ParseDatabaseUrl(string databaseUrl)
{
    var uri = new Uri(databaseUrl);
    var userInfo = uri.UserInfo.Split(':');
    var username = userInfo[0];
    var password = userInfo.Length > 1 ? userInfo[1] : string.Empty;
    return $"Host={uri.Host};Port={uri.Port};Database={uri.AbsolutePath.TrimStart('/')};Username={username};Password={password};SSL Mode=Require;Trust Server Certificate=true";
}
