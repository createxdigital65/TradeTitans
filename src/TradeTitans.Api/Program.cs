using System.Text.Json.Serialization;
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

// Configure CORS for Angular Frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
    {
        policy.WithOrigins("http://localhost:4200", "https://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// SQLite Database
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Data Source=tradetitans.db";
builder.Services.AddDbContext<TradeTitansDbContext>(options =>
    options.UseSqlite(connectionString));

// Register HTTP Clients
var pythonBaseUrl = builder.Configuration["PythonService:BaseUrl"] ?? "https://trade-titan-seven.vercel.app";
builder.Services.AddHttpClient<IPythonAnalyticsClient, PythonAnalyticsClient>(client =>
{
    client.BaseAddress = new Uri(pythonBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(120); // Council LLM chain on serverless Python can be slow; avoids challenger timeout fallbacks.
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
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAngular");
app.UseAuthorization();
app.MapControllers();

app.Run();
