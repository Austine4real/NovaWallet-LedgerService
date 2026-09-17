using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using NovaWallet.Api.Auth;
using NovaWallet.Api.Middleware;
using NovaWallet.Application.Interfaces;
using NovaWallet.Application.Services;
using NovaWallet.Infrastructure;
using NovaWallet.Infrastructure.Outbox;
using NovaWallet.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// --- Configuration -----------------------------------------------------
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()!;

// --- Persistence ---------------------------------------------------------
builder.Services.AddDbContext<NovaWalletDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("NovaWalletDb")));
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddSingleton<IClock, SystemClock>();

// --- Outbox publisher (stretch goal): polls for unpublished
// --- TransferCompleted events and "publishes" them (logged, for this
// --- exercise - see OutboxPublisherService for what a real broker
// --- integration would replace). ------------------------------------------
builder.Services.AddHostedService<OutboxPublisherService>();

// --- Application services -------------------------------------------------
builder.Services.AddScoped<WalletService>();
builder.Services.AddScoped<TransferService>();
builder.Services.AddScoped<JwtTokenService>();

// --- Auth ------------------------------------------------------------------
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey))
        };
    });
builder.Services.AddAuthorization();

// --- Rate limiting on the transfer endpoint (stretch goal) -----------------
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("transfers", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.User.Identity?.Name ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromSeconds(10),
                QueueLimit = 0
            }));
});

// --- Problem Details / exception handling -----------------------------------
builder.Services.AddExceptionHandler<NovaWalletExceptionHandler>();
builder.Services.AddProblemDetails();

// --- API surface -------------------------------------------------------------
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "NovaWallet Ledger Service", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new()
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Paste the access token returned by POST /auth/token"
    });
    c.AddSecurityRequirement(new()
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// --- Apply migrations on startup so `docker compose up` is genuinely
// --- single-command: no manual `dotnet ef database update` step required.
// ---
// --- Wrapped in a retry loop because the SQL Server container's healthcheck
// --- can report "healthy" (it can accept a basic connection) before it has
// --- actually finished recovering a pre-existing user database from a prior
// --- run - a transient race, not a real failure. Without retrying here, that
// --- race crashes the whole API container on startup. A fresh, truly empty
// --- database does not hit this: the race only shows up when the volume
// --- already has data to recover, which takes SQL Server noticeably longer. ---
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<NovaWalletDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    const int maxAttempts = 10;
    var delay = TimeSpan.FromSeconds(3);

    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            db.Database.Migrate();
            break;
        }
        catch (Exception ex) when (attempt < maxAttempts)
        {
            logger.LogWarning(
                ex, "Migration attempt {Attempt}/{MaxAttempts} failed - SQL Server may still be starting. Retrying in {Delay}s.",
                attempt, maxAttempts, delay.TotalSeconds);
            Thread.Sleep(delay);
        }
    }
}

app.UseSwagger();
app.UseSwaggerUI();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", async (NovaWalletDbContext db) =>
{
    try
    {
        await db.Database.ExecuteSqlRawAsync("SELECT 1");
        return Results.Ok(new { status = "ready" });
    }
    catch
    {
        return Results.Json(new { status = "not-ready" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.Run();

// Exposed for WebApplicationFactory-based integration tests.
public partial class Program { }
