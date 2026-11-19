using Majorro.Common.Setup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecretSantaBot.Data;
using SecretSantaBot.Services;
using Telegram.Bot;

DotEnv.TryLoad(".env");

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);

var botToken = builder.Configuration["BotToken"];
if (string.IsNullOrEmpty(botToken))
{
    using var loggerFactory = LoggerFactory.Create(logging => logging.AddJsonConsole());
    loggerFactory.CreateLogger("SecretSantaBot.Startup")
        .LogCritical("BotToken is missing; set it in .env or environment variables");
    Environment.ExitCode = 1;
    return;
}

// Register Telegram Bot Client
builder.Services.AddHttpClient("telegram_bot_client")
    .AddTypedClient<ITelegramBotClient>((httpClient, sp) =>
    {
        var botConfig = new TelegramBotClientOptions(botToken);
        return new TelegramBotClient(botConfig, httpClient);
    });

// Register Database
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<DatabaseContext>(options => options.UseSqlite(connectionString));

// Register Services
builder.Services.AddScoped<ShuffleService>();
builder.Services.AddScoped<MessageService>();
builder.Services.AddHostedService<BotService>();

using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("SecretSantaBot.Startup");

// Initialize Database
using (var scope = host.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
    try
    {
        var databaseCreated = context.Database.EnsureCreated();
        logger.LogInformation("Database initialized; created: {DatabaseCreated}", databaseCreated);
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Database initialization failed");
        Environment.ExitCode = 1;
        return;
    }
}

logger.LogInformation("Starting Secret Santa bot");
await host.RunAsync();
logger.LogInformation("Secret Santa bot stopped");

