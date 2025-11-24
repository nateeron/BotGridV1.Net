using BotGridV1.Models.SQLite;
using BotGridV1.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReact", policy =>
    {
        policy.WithOrigins(
            "http://139.180.128.104",        // React UI
            "http://localhost:5173",         // dev
            "http://localhost:5174"
        )
        .AllowAnyHeader()
        .AllowAnyMethod();
    });
});

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();

// Configure SQLite Database
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Data Source=botgrid.db";

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));

// Register HttpClient for DiscordService
builder.Services.AddHttpClient<DiscordService>();

// Register BotWorkerService as a hosted service
builder.Services.AddSingleton<BotWorkerService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<BotWorkerService>());

var app = builder.Build();

await DefaultDataSeeder.EnsureSeedDataAsync(app.Services);

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
}
app.UseCors("AllowReact");
app.UseAuthorization();

app.MapControllers();

app.Run();
