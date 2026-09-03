using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using TradeTitans.Core.Data;
using TradeTitans.Core.Interfaces;
using TradeTitans.Core.RiskRules;
using TradeTitans.Core.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Trade Titans API Tower", Version = "v1" });
});

// Configure CORS - support multiple origins from configuration
var allowedOriginsConfig = builder.Configuration["Cors:AllowedOrigins"];
var allowedOrigins = !string.IsNullOrWhiteSpace(allowedOriginsConfig)
    ? allowedOriginsConfig.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    : new[] { "http://localhost:4200", "https://localhost:4200" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// SQLite Database - use configurable path
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=tradetitans.db";
builder.Services.AddDbContext<TradeTitansDbContext>(options =>
    options.UseSqlite(connectionString));

// Register HTTP Clients
var pythonBaseUrl = builder.Configuration["PythonService:BaseUrl"] ?? "https://trade-titan-seven.vercel.app";
var pythonTimeoutSeconds = builder.Configuration.GetValue<int?>("PythonService:TimeoutSeconds") ?? 120;
builder.Services.AddHttpClient<IPythonAnalyticsClient, PythonAnalyticsClient>(client =>
{
    client.BaseAddress = new Uri(pythonBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(pythonTimeoutSeconds);
});

var alpacaBaseUrl = builder.Configuration["Alpaca:BaseUrl"] ?? "https://paper-api.alpaca.markets";
var apiKey = builder.Configuration["Alpaca:ApiKey"] ?? "";
var secretKey = builder.Configuration["Alpaca:SecretKey"] ?? "";

builder.Services.AddHttpClient<IAlpacaPaperService, AlpacaPaperService>(client =>
{
    client.BaseAddress = new Uri(alpacaBaseUrl);
    if (!string.IsNullOrEmpty(apiKey))
    {
        client.DefaultRequestHeaders.Add("APCA-API-KEY-ID", apiKey);
        client.DefaultRequestHeaders.Add("APCA-API-SECRET-KEY", secretKey);
    }
});

// Register Deterministic Risk Rules
builder.Services.AddScoped<IRiskRule, MaxPositionSizeRule>();
builder.Services.AddScoped<IRiskRule, MinimumCashReserveRule>();
builder.Services.AddScoped<IRiskRule, OptionsDteLiquidityRule>();
builder.Services.AddScoped<IRiskRule, DataQualityRule>();

// Register Domain Services
builder.Services.AddScoped<IRiskGuardianService, RiskGuardianService>();
builder.Services.AddScoped<IChiefTraderService, ChiefTraderService>();
builder.Services.AddScoped<ITradeCouncilOrchestrator, TradeCouncilOrchestrator>();

// Configure forwarded headers for reverse proxy scenarios (e.g., MonsterASP.NET)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                             | ForwardedHeaders.XForwardedProto;
});

var app = builder.Build();

// Ensure DB is created on startup
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<TradeTitansDbContext>();
    dbContext.Database.EnsureCreated();

    // Lightweight idempotent schema migration: databases created before audit-phase tracking
    // lack the RiskLogs.Phase column. EnsureCreated never alters existing tables, so the
    // additive change is applied directly — SQLite ALTER TABLE ADD COLUMN with a constant
    // default is instant and keeps every historical risk log intact (labeled as the initial
    // evaluation pass).
    var dbConnection = dbContext.Database.GetDbConnection();
    if (dbConnection.State != System.Data.ConnectionState.Open)
    {
        await dbConnection.OpenAsync();
    }
    using (var checkCmd = dbConnection.CreateCommand())
    {
        checkCmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RiskLogs') WHERE name = 'Phase'";
        var phaseColumnExists = Convert.ToInt64(checkCmd.ExecuteScalar()) > 0;
        if (!phaseColumnExists)
        {
            using var alterCmd = dbConnection.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE RiskLogs ADD COLUMN Phase TEXT NOT NULL DEFAULT 'INITIAL_EVALUATION'";
            await alterCmd.ExecuteNonQueryAsync();
        }
    }
}

// Configure the HTTP request pipeline.
// Enable Swagger in both Development and when explicitly configured
var swaggerEnabled = builder.Configuration.GetValue<bool>("Swagger:Enabled") || app.Environment.IsDevelopment();
if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Trade Titans API Tower v1");
        c.RoutePrefix = "swagger";
    });
}

// Use forwarded headers before other middleware
app.UseForwardedHeaders();
app.UseCors("AllowAngular");
app.UseAuthorization();
app.MapControllers();

// Add a simple health endpoint for deployment verification
app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    timestamp = DateTime.UtcNow,
    environment = app.Environment.ApplicationName
}));

app.Run();
