using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SecretSantaBot.Data;
using SecretSantaBot.Services;
using Telegram.Bot;

var builder = Host.CreateApplicationBuilder(args);

// Configuration
builder.Configuration
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables();

var botToken = builder.Configuration["BotToken"];
if (string.IsNullOrEmpty(botToken))
{
    Console.WriteLine("ОШИБКА: BotToken не указан в appsettings.json!");
    Console.WriteLine("Пожалуйста, добавьте ваш токен бота в файл appsettings.json");
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
builder.Services.AddDbContext<DatabaseContext>(options =>
    options.UseSqlite("Data Source=secret_santa.db"));

// Register Services
builder.Services.AddScoped<ShuffleService>();
builder.Services.AddScoped<MessageService>();
builder.Services.AddHostedService<BotService>();

var host = builder.Build();

// Initialize Database
using (var scope = host.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
    try
    {
        context.Database.EnsureCreated();
        Console.WriteLine("База данных инициализирована.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Ошибка при инициализации базы данных: {ex.Message}");
        return;
    }
}

Console.WriteLine("Запуск бота...");
await host.RunAsync();

