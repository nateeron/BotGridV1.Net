using BotGridV1.Models.Login;
using BotGridV1.Models.SQLite;
using BotGridV1.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReact", policy =>
    {
        policy.WithOrigins(
            "http://139.180.128.104",
            "https://139.180.128.104",
            "https://cayoshibot.com",
            "https://www.cayoshibot.com",
            "https://api.cayoshibot.com",
            "http://localhost:5173",
            "http://localhost:5174"
        )
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials(); // Required for SignalR
    });
});

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();

// Add SignalR for real-time order updates and alerts
builder.Services.AddSignalR();

// Register AlertLogService with IServiceProvider
builder.Services.AddSingleton<AlertLogService>(serviceProvider =>
{
    var hubContext = serviceProvider.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<BotGridV1.Hubs.AlertHub>>();
    return new AlertLogService(hubContext, serviceProvider);
});

// Configure SQLite Database
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Data Source=botgrid.db";

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));

// Register HttpClient for DiscordService with AlertLogService dependency
// Note: DiscordService is Singleton because BotWorkerService (Singleton) depends on it
builder.Services.AddHttpClient();
builder.Services.AddSingleton<DiscordService>(serviceProvider =>
{
    var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
    var httpClient = httpClientFactory.CreateClient();
    var alertLogService = serviceProvider.GetRequiredService<AlertLogService>();
    var logger = serviceProvider.GetRequiredService<ILogger<DiscordService>>();
    return new DiscordService(httpClient, logger, alertLogService);
});

// Register CurrencyService
builder.Services.AddHttpClient<CurrencyService>((serviceProvider, client) =>
{
    // HttpClient is configured by AddHttpClient, logger will be injected
});
builder.Services.AddScoped<CurrencyService>();

// Register JwtService
builder.Services.AddScoped<JwtService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"])
        )
    };
});

// Register BotWorkerService as a hosted service
builder.Services.AddSingleton<BotWorkerService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<BotWorkerService>());

var app = builder.Build();

// Add custom logger provider to capture application logs (after app is built to access services)
var alertLogService = app.Services.GetRequiredService<AlertLogService>();
var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
loggerFactory.AddProvider(new AlertLoggerProvider(alertLogService, LogLevel.Warning));

await DefaultDataSeeder.EnsureSeedDataAsync(app.Services);

// Initialize UserLogin tables
await UserLoginTableInitializer.EnsureUserLoginTablesAsync(app.Services);

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
}
app.UseCors("AllowReact");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
// Map SignalR Hubs
app.MapHub<BotGridV1.Hubs.OrderHub>("/hubs/orders");
app.MapHub<BotGridV1.Hubs.AlertHub>("/hubs/alerts");

app.Run();
