using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecretSantaBot.Data;
using SecretSantaBot.Models;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using User = SecretSantaBot.Models.User;

namespace SecretSantaBot.Services;

public class BotService : IHostedService
{
    private readonly ITelegramBotClient _botClient;
    private readonly DatabaseContext _context;
    private readonly ShuffleService _shuffleService;
    private readonly MessageService _messageService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BotService> _logger;
    private CancellationTokenSource? _cancellationTokenSource;
    
    public BotService(ITelegramBotClient botClient,
        DatabaseContext context,
        ShuffleService shuffleService,
        MessageService messageService,
        IConfiguration configuration, ILogger<BotService> logger)
    {
        _botClient = botClient;
        _context = context;
        _shuffleService = shuffleService;
        _messageService = messageService;
        _configuration = configuration;
        _logger = logger;
    }
    
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Telegram polling");
        _cancellationTokenSource = new CancellationTokenSource();
        
        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = Array.Empty<UpdateType>()
        };

        await _botClient.SetMyCommands(
            [
                new BotCommand { Command = "help", Description = "Показать список доступных команд" },
                new BotCommand { Command = "register", Description = "Зарегистрироваться" },
                new BotCommand { Command = "myinfo", Description = "Посмотреть свою информацию" },
                new BotCommand { Command = "recipientinfo", Description = "Посмотреть информацию получателя" },
                new BotCommand { Command = "updatewishes", Description = "Обновить пожелания" },
                new BotCommand { Command = "update_destination", Description = "Обновить адрес доставки" },
                new BotCommand { Command = "updatephone", Description = "Обновить номер телефона" },
                new BotCommand { Command = "blacklist", Description = "Управление чёрным списком" },
                new BotCommand { Command = "message", Description = "Отправить анонимное сообщение" }
            ],
            new BotCommandScopeAllPrivateChats());

        _botClient.StartReceiving(
            updateHandler: HandleUpdateAsync,
            errorHandler: HandlePollingErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: _cancellationTokenSource.Token
        );
        
        var me = await _botClient.GetMe(cancellationToken);
        _logger.LogInformation("Telegram bot {BotUsername} ({BotId}) is ready", me.Username, me.Id);
    }
    
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cancellationTokenSource?.Cancel();
        _logger.LogInformation("Telegram polling cancellation requested");
        return Task.CompletedTask;
    }
    
    private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        if (update.Message is not { } message)
            return;

        string? messageText;
        if (message.Text is not null)
        {
            messageText = message.Text;
        }
        else if (message.Caption is not null)
        {
            messageText = message.Caption;
        }
        else
            return;
        
        var chatId = message.Chat.Id;
        var userId = message.From?.Id ?? 0;
        var username = message.From?.Username;
        var firstName = message.From?.FirstName;
        var lastName = message.From?.LastName;
        
        // Handle commands
        if (messageText.StartsWith('/'))
        {
            var commandParts = messageText.Split(' ', 2);
            var command = commandParts[0].ToLower();
            var args = commandParts.Length > 1 ? commandParts[1] : "";

            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["UpdateId"] = update.Id,
                ["ChatId"] = chatId,
                ["UserId"] = userId,
                ["Command"] = command
            });
            var startedAt = Stopwatch.GetTimestamp();
            
            try
            {
                _logger.LogDebug("Handling command");
                if (command is "/adduser" or "/shuffle" or "/sendinfo" or "/messageall" or "/participants" or "/stats"
                    && !IsAdmin(userId))
                {
                    _logger.LogWarning("Admin command denied");
                    await botClient.SendMessage(chatId, "У вас нет прав администратора.", cancellationToken: cancellationToken);
                    return;
                }

                switch (command)
                {
                    case "/start" or "/help":
                        await HandleStartCommand(chatId, userId, cancellationToken);
                        break;
                    case "/register":
                        await HandleRegisterCommand(chatId, userId, username, firstName, lastName, cancellationToken);
                        break;
                    case "/myinfo":
                        await HandleMyInfoCommand(chatId, userId, cancellationToken);
                        break;
                    case "/recipientinfo":
                        await HandleRecipientInfoCommand(chatId, userId, cancellationToken);
                        break;
                    case "/updatewishes":
                        await HandleUpdateWishesCommand(chatId, userId, args, cancellationToken);
                        break;
                    case "/update_destination":
                        await HandleUpdateDestinationCommand(chatId, userId, args, cancellationToken);
                        break;
                    case "/updatephone":
                        await HandleUpdatePhoneCommand(chatId, userId, args, cancellationToken);
                        break;
                    case "/blacklist":
                        await HandleBlacklistCommand(chatId, userId, args, cancellationToken);
                        break;
                    case "/message":
                        await HandleMessageCommand(chatId, userId, args, message, cancellationToken);
                        break;
                    // Admin commands
                    case "/adduser":
                        await HandleAddUserCommand(chatId, args, cancellationToken);
                        break;
                    case "/shuffle":
                        await HandleShuffleCommand(chatId, cancellationToken);
                        break;
                    case "/sendinfo":
                        await HandleSendInfoCommand(chatId, cancellationToken);
                        break;
                    case "/messageall":
                        await HandleMessageAllCommand(chatId, args, cancellationToken);
                        break;
                    case "/participants":
                        await HandleParticipantsCommand(chatId, cancellationToken);
                        break;
                    case "/stats":
                        await HandleStatsCommand(chatId, cancellationToken);
                        break;
                    default:
                        _logger.LogWarning("Unknown command received");
                        await botClient.SendMessage(chatId, "Неизвестная команда. Используйте /start для просмотра доступных команд.", cancellationToken: cancellationToken);
                        break;
                }

                _logger.LogInformation("Command handled in {ElapsedMilliseconds} ms", Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug("Command cancelled during shutdown");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Command handling failed after {ElapsedMilliseconds} ms", Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
                await botClient.SendMessage(chatId, "Произошла ошибка при обработке команды. Попробуйте позже.", cancellationToken: cancellationToken);
            }
        }
    }

    private async Task HandleStartCommand(long chatId, long userId, CancellationToken cancellationToken)
    {
        var message = "🎄 Добро пожаловать в бота Тайного Санты!\n\n" +
                      "Доступные команды:\n" +
                      "/help - Показать это сообщение\n" +
                      "/register - Зарегистрироваться\n" +
                      "/myinfo - Посмотреть свою информацию\n" +
                      "/recipientinfo - Посмотреть информацию о том, кому вы дарите\n" +
                      "/updatewishes <текст> - Обновить пожелания\n" +
                      "/update_destination <текст> - Обновить адреса доставки\n" +
                      "/updatephone <номер> - Обновить номер телефона\n" +
                      "/blacklist add @username - Добавить пользователя в чёрный список\n" +
                      "/blacklist remove @username - Удалить пользователя из чёрного списка\n" +
                      "/blacklist list - Показать чёрный список\n" +
                      "/message sender <текст> - Отправить анонимное сообщение тому, кто вам дарит\n" +
                      "/message recipient <текст> - Отправить анонимное сообщение тому, кому вы дарите\n";
        if (IsAdmin(userId))
        {
            message += "\nКоманды администратора:\n" +
                       "/adduser @username - Добавить участника\n" +
                       "/shuffle - Провести жеребьёвку\n" +
                       "/sendinfo - Отправить информацию получателям\n" +
                       "/messageall - Написать всем участникам\n" +
                       "/participants - Список участников\n" +
                       "/stats - Статистика";
        }

        await _botClient.SendMessage(chatId, message, cancellationToken: cancellationToken);
    }
    
    private async Task HandleRegisterCommand(long chatId, long userId, string? username, string? firstName, string? lastName, CancellationToken cancellationToken)
    {
        if (await _shuffleService.HasShuffleHappenedAsync())
        {
            await _botClient.SendMessage(chatId, "Регистрация закрыта. Жеребьёвка уже была проведена.", cancellationToken: cancellationToken);
            return;
        }
        
        var existingUser = await _context.Users.FindAsync(userId);
        if (existingUser != null)
        {
            await _botClient.SendMessage(chatId, "Вы уже зарегистрированы! Используйте /myinfo для просмотра информации.", cancellationToken: cancellationToken);
            return;
        }
        
        var user = new User
        {
            TelegramUserId = userId,
            Username = username,
            FirstName = firstName,
            LastName = lastName,
            RegisteredAt = DateTime.UtcNow
        };
        
        _context.Users.Add(user);
        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Participant registered");
        
        await _botClient.SendMessage(chatId, 
            "✅ Вы успешно зарегистрированы!\n\n" +
            "Теперь вы можете:\n" +
            "- Добавить пожелания: /updatewishes <текст>\n" +
            "- Добавить адрес доставки: /update_destination <текст>\n" +
            "- Добавить номер телефона: /updatephone <номер>\n" +
            "- Настроить чёрный список: /blacklist add @username", 
            cancellationToken: cancellationToken);
    }
    
    private async Task HandleMyInfoCommand(long chatId, long userId, CancellationToken cancellationToken)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
        {
            await _botClient.SendMessage(chatId, "Вы не зарегистрированы. Используйте /register для регистрации.", cancellationToken: cancellationToken);
            return;
        }
        
        var message = "📋 Ваша информация:\n\n" +
                     $"Пользователь: {GetUserDisplayName(user)}\n" +
                     $"Пожелания: {(string.IsNullOrEmpty(user.Wishes) ? "не указаны" : user.Wishes)}\n" +
                     $"Адрес доставки: {(string.IsNullOrEmpty(user.Addresses) ? "не указан" : user.Addresses)}\n" +
                     $"Телефон: {(string.IsNullOrEmpty(user.PhoneNumber) ? "не указан" : user.PhoneNumber)}\n\n";
        
        var blacklist = await _context.Blacklist
            .Where(b => b.UserId == userId)
            .Include(b => b.BlacklistedUser)
            .ToListAsync(cancellationToken);
        
        if (blacklist.Any())
        {
            message += "Чёрный список:\n";
            foreach (var item in blacklist)
            {
                message += $"- {GetUserDisplayName(item.BlacklistedUser)}\n";
            }
        }
        else
        {
            message += "Чёрный список пуст.";
        }
        
        await _botClient.SendMessage(chatId, message, cancellationToken: cancellationToken);
    }

    private async Task HandleRecipientInfoCommand(long chatId, long userId, CancellationToken cancellationToken)
    {
        if (!await _shuffleService.HasShuffleHappenedAsync())
        {
            await _botClient.SendMessage(chatId, "Информация о получателе недоступна до жеребьёвки.", cancellationToken: cancellationToken);
            return;
        }

        var user = await _context.Users.FindAsync(userId);
        if (user == null)
        {
            await _botClient.SendMessage(chatId, "Вы не зарегистрированы. Используйте /register для регистрации.", cancellationToken: cancellationToken);
            return;
        }

        var assignment = await _shuffleService.GetGifterAssignmentAsync(userId);

        if (assignment is null)
        {
            await _botClient.SendMessage(chatId, "Не найдена информация о вашем назначении.", cancellationToken: cancellationToken);
            return;
        }

        var message = GetRecipientInfoString(assignment);
        await _botClient.SendMessage(chatId, message, cancellationToken: cancellationToken);
    }
    
    private async Task HandleUpdateWishesCommand(long chatId, long userId, string args, CancellationToken cancellationToken)
    {
        if (await _shuffleService.HasShuffleHappenedAsync())
        {
            await _botClient.SendMessage(chatId, "Обновление информации недоступно после жеребьёвки.", cancellationToken: cancellationToken);
            return;
        }
        
        if (string.IsNullOrWhiteSpace(args))
        {
            await _botClient.SendMessage(chatId, "Использование: /updatewishes <текст пожеланий>", cancellationToken: cancellationToken);
            return;
        }
        
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
        {
            await _botClient.SendMessage(chatId, "Вы не зарегистрированы. Используйте /register для регистрации.", cancellationToken: cancellationToken);
            return;
        }
        
        user.Wishes = args;
        await _context.SaveChangesAsync(cancellationToken);
        
        _logger.LogInformation("Profile field {ProfileField} updated", "Wishes");
        await _botClient.SendMessage(chatId, "✅ Пожелания обновлены!", cancellationToken: cancellationToken);
    }
    
    private async Task HandleUpdateDestinationCommand(long chatId, long userId, string args, CancellationToken cancellationToken)
    {
        if (await _shuffleService.HasShuffleHappenedAsync())
        {
            await _botClient.SendMessage(chatId, "Обновление информации недоступно после жеребьёвки.", cancellationToken: cancellationToken);
            return;
        }
        
        if (string.IsNullOrWhiteSpace(args))
        {
            await _botClient.SendMessage(chatId, "Использование: /update_destination <текст>", cancellationToken: cancellationToken);
            return;
        }
        
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
        {
            await _botClient.SendMessage(chatId, "Вы не зарегистрированы. Используйте /register для регистрации.", cancellationToken: cancellationToken);
            return;
        }
        
        user.Addresses = args;
        await _context.SaveChangesAsync(cancellationToken);
        
        _logger.LogInformation("Profile field {ProfileField} updated", "Addresses");
        await _botClient.SendMessage(chatId, "✅ Адрес доставки обновлён!", cancellationToken: cancellationToken);
    }
    
    private async Task HandleUpdatePhoneCommand(long chatId, long userId, string args, CancellationToken cancellationToken)
    {
        if (await _shuffleService.HasShuffleHappenedAsync())
        {
            await _botClient.SendMessage(chatId, "Обновление информации недоступно после жеребьёвки.", cancellationToken: cancellationToken);
            return;
        }
        
        if (string.IsNullOrWhiteSpace(args))
        {
            await _botClient.SendMessage(chatId, "Использование: /updatephone <номер телефона>", cancellationToken: cancellationToken);
            return;
        }
        
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
        {
            await _botClient.SendMessage(chatId, "Вы не зарегистрированы. Используйте /register для регистрации.", cancellationToken: cancellationToken);
            return;
        }
        
        user.PhoneNumber = args;
        await _context.SaveChangesAsync(cancellationToken);
        
        _logger.LogInformation("Profile field {ProfileField} updated", "PhoneNumber");
        await _botClient.SendMessage(chatId, "✅ Номер телефона обновлён!", cancellationToken: cancellationToken);
    }
    
    private async Task HandleBlacklistCommand(long chatId, long userId, string args, CancellationToken cancellationToken)
    {
        if (await _shuffleService.HasShuffleHappenedAsync())
        {
            await _botClient.SendMessage(chatId, "Изменение чёрного списка недоступно после жеребьёвки.", cancellationToken: cancellationToken);
            return;
        }
        
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
        {
            await _botClient.SendMessage(chatId, "Вы не зарегистрированы. Используйте /register для регистрации.", cancellationToken: cancellationToken);
            return;
        }
        
        if (string.IsNullOrWhiteSpace(args))
        {
            await _botClient.SendMessage(chatId, 
                "Использование:\n" +
                "/blacklist add @username - Добавить в чёрный список\n" +
                "/blacklist remove @username - Удалить из чёрного списка\n" +
                "/blacklist list - Показать чёрный список", 
                cancellationToken: cancellationToken);
            return;
        }
        
        var parts = args.Split(' ', 2);
        var action = parts[0].ToLower();
        
        if (action == "list")
        {
            var blacklist = await _context.Blacklist
                .Where(b => b.UserId == userId)
                .Include(b => b.BlacklistedUser)
                .ToListAsync(cancellationToken);
            
            if (!blacklist.Any())
            {
                await _botClient.SendMessage(chatId, "Ваш чёрный список пуст.", cancellationToken: cancellationToken);
                return;
            }
            
            var message = "Ваш чёрный список:\n";
            foreach (var item in blacklist)
            {
                message += $"- {GetUserDisplayName(item.BlacklistedUser)}\n";
            }
            
            await _botClient.SendMessage(chatId, message, cancellationToken: cancellationToken);
            return;
        }
        
        if (parts.Length < 2)
        {
            await _botClient.SendMessage(chatId, "Укажите имя пользователя.", cancellationToken: cancellationToken);
            return;
        }
        
        var username = parts[1].TrimStart('@');
        var targetUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == username, cancellationToken);
        
        if (targetUser == null)
        {
            await _botClient.SendMessage(chatId, "Пользователь не найден или не зарегистрирован.", cancellationToken: cancellationToken);
            return;
        }
        
        if (targetUser.TelegramUserId == userId)
        {
            await _botClient.SendMessage(chatId, "Вы не можете добавить себя в чёрный список.", cancellationToken: cancellationToken);
            return;
        }
        
        if (action == "add")
        {
            var existing = await _context.Blacklist
                .FirstOrDefaultAsync(b => b.UserId == userId && b.BlacklistedUserId == targetUser.TelegramUserId, cancellationToken);
            
            if (existing != null)
            {
                await _botClient.SendMessage(chatId, "Пользователь уже в чёрном списке.", cancellationToken: cancellationToken);
                return;
            }
            
            _context.Blacklist.Add(new Blacklist
            {
                UserId = userId,
                BlacklistedUserId = targetUser.TelegramUserId
            });
            
            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Blacklist entry added");
            await _botClient.SendMessage(chatId, $"✅ {GetUserDisplayName(targetUser)} добавлен в чёрный список.", cancellationToken: cancellationToken);
        }
        else if (action == "remove")
        {
            var existing = await _context.Blacklist
                .FirstOrDefaultAsync(b => b.UserId == userId && b.BlacklistedUserId == targetUser.TelegramUserId, cancellationToken);
            
            if (existing == null)
            {
                await _botClient.SendMessage(chatId, "Пользователь не найден в чёрном списке.", cancellationToken: cancellationToken);
                return;
            }
            
            _context.Blacklist.Remove(existing);
            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Blacklist entry removed");
            await _botClient.SendMessage(chatId, $"✅ {GetUserDisplayName(targetUser)} удалён из чёрного списка.", cancellationToken: cancellationToken);
        }
        else
        {
            await _botClient.SendMessage(chatId, "Неизвестное действие. Используйте 'add', 'remove' или 'list'.", cancellationToken: cancellationToken);
        }
    }

    private async Task HandleMessageCommand(long chatId, long userId, string args, Message message, CancellationToken cancellationToken)
{
    // Check if message contains photo
    if (message.Photo?.Length > 0)
    {
        var file = message.Photo.First();
        var fileId = file.FileId;
        var caption = message.Caption ?? "";

        var prts = caption.Split(' ', 3);
        if (prts.Length < 3)
        {
            await _botClient.SendMessage(chatId, "Укажите получателя (sender/recipient) и текст сообщения.", cancellationToken: cancellationToken);
            return;
        }

        var targType = prts[1];
        var msgText = prts[2];

        var (success, errorMessage, targetUser, isFromGifter) = await _messageService.ValidateAndGetTargetAsync(userId, targType);

        if (!success)
        {
            await _botClient.SendMessage(chatId, errorMessage, cancellationToken: cancellationToken);
            return;
        }

        if (targetUser == null)
        {
            await _botClient.SendMessage(chatId, "Получатель не найден.", cancellationToken: cancellationToken);
            return;
        }

        // Save media message info to database (you would need to extend your message model)
        await _messageService.SaveMessageAsync(userId, targetUser.TelegramUserId, $"{fileId} - {msgText}", isFromGifter);

        // Forward media to recipient
        await _botClient.SendPhoto(targetUser.TelegramUserId, new InputFileId(fileId),
            caption: $"💬 Сообщение от {(isFromGifter ? "того, кто вам дарит (sender)" : "того, кому вы дарите (recipient)")}:\n\n{msgText}",
            cancellationToken: cancellationToken);

        _logger.LogInformation("Anonymous {MessageKind} message delivered", "Photo");
        await _botClient.SendMessage(chatId, "✅ Изображение отправлено!", cancellationToken: cancellationToken);
        return;
    }

    // Existing text message handling
    if (string.IsNullOrWhiteSpace(args))
    {
        await _botClient.SendMessage(chatId,
            "Использование:\n" +
            "/message sender <текст> - Отправить сообщение тому, кто вам дарит\n" +
            "/message recipient <текст> - Отправить сообщение тому, кому вы дарите",
            cancellationToken: cancellationToken);
        return;
    }

    var parts = args.Split(' ', 2);
    if (parts.Length < 2)
    {
        await _botClient.SendMessage(chatId, "Укажите получателя (sender/recipient) и текст сообщения.", cancellationToken: cancellationToken);
        return;
    }

    var targetType = parts[0];
    var messageText = parts[1];

    var (success2, errorMessage2, targetUser2, isFromGifter2) = await _messageService.ValidateAndGetTargetAsync(userId, targetType);

    if (!success2)
    {
        await _botClient.SendMessage(chatId, errorMessage2, cancellationToken: cancellationToken);
        return;
    }

    if (targetUser2 == null)
    {
        await _botClient.SendMessage(chatId, "Получатель не найден.", cancellationToken: cancellationToken);
        return;
    }

    // Save text message
    await _messageService.SaveMessageAsync(userId, targetUser2.TelegramUserId, messageText, isFromGifter2);

    // Send to recipient
    var label = isFromGifter2
        ? "💬 Сообщение от того, кто вам дарит (sender):"
        : "💬 Сообщение от того, кому вы дарите (recipient):";

    await _botClient.SendMessage(targetUser2.TelegramUserId, $"{label}\n\n{messageText}", cancellationToken: cancellationToken);
    _logger.LogInformation("Anonymous {MessageKind} message delivered", "Text");
    await _botClient.SendMessage(chatId, "✅ Сообщение отправлено!", cancellationToken: cancellationToken);
}
    
    private async Task HandleAddUserCommand(long chatId, string args, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            await _botClient.SendMessage(chatId, "Использование: /adduser @username", cancellationToken: cancellationToken);
            return;
        }
        
        var username = args.TrimStart('@');
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username, cancellationToken);
        
        if (user != null)
        {
            await _botClient.SendMessage(chatId, "Пользователь уже зарегистрирован.", cancellationToken: cancellationToken);
            return;
        }
        
        // Note: This command requires the user to have started the bot first
        // In a real scenario, you might want to get user info differently
        await _botClient.SendMessage(chatId, 
            $"Пользователь @{username} должен сначала написать боту /start, чтобы его можно было добавить. " +
            "Или используйте /register для регистрации через бота.", 
            cancellationToken: cancellationToken);
    }
    
    private async Task HandleShuffleCommand(long chatId, CancellationToken cancellationToken)
    {
        var (_, message) = await _shuffleService.PerformShuffleAsync();
        
        await _botClient.SendMessage(chatId, message, cancellationToken: cancellationToken);
    }
    
    private async Task HandleSendInfoCommand(long chatId, CancellationToken cancellationToken)
    {
        if (!await _shuffleService.HasShuffleHappenedAsync())
        {
            await _botClient.SendMessage(chatId, "Жеребьёвка ещё не была проведена.", cancellationToken: cancellationToken);
            return;
        }
        
        var assignments = await _context.Shuffle
            .Include(s => s.Recipient)
            .ToListAsync(cancellationToken);
        
        int sentCount = 0;
        int failedCount = 0;

        _logger.LogInformation("Sending recipient information to {ParticipantCount} participants", assignments.Count);
        
        foreach (var assignment in assignments)
        {
            try
            {
                var message = GetRecipientInfoString(assignment);
                
                await _botClient.SendMessage(assignment.GifterId, message, cancellationToken: cancellationToken);
                sentCount++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Recipient information delivery failed for user {DeliveryUserId}", assignment.GifterId);
                failedCount++;
            }
        }
        
        _logger.LogInformation("Recipient information delivery finished; sent: {SentCount}, failed: {FailedCount}", sentCount, failedCount);
        await _botClient.SendMessage(chatId, 
            $"Информация отправлена:\n✅ Успешно: {sentCount}\n❌ Ошибок: {failedCount}", 
            cancellationToken: cancellationToken);
    }

    private string GetRecipientInfoString(Shuffle assignment) =>
        "🎁 Информация о вашем получателе:\n\n" +
        $"Получатель: {GetUserDisplayName(assignment.Recipient)}\n" +
        $"Пожелания: {(string.IsNullOrEmpty(assignment.Recipient.Wishes) ? "не указаны" : assignment.Recipient.Wishes)}\n" +
        $"Адрес доставки: {(string.IsNullOrEmpty(assignment.Recipient.Addresses) ? "не указан" : assignment.Recipient.Addresses)}\n" +
        $"Телефон: {(string.IsNullOrEmpty(assignment.Recipient.PhoneNumber) ? "не указан" : assignment.Recipient.PhoneNumber)}";

    private async Task HandleMessageAllCommand(long chatId, string args, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            await _botClient.SendMessage(chatId, "Укажите текст сообщения.", cancellationToken: cancellationToken);
            return;
        }

        var msg = $"Сообщение от админа:\n\n{args}";

        var sendToIds = await _messageService.GetAllParticipantIds();
        _logger.LogInformation("Broadcast started for {ParticipantCount} participants", sendToIds.Length);
        var sentCount = 0;
        var failedCount = 0;
        foreach (var id in sendToIds)
        {
            try
            {
                await _botClient.SendMessage(id, msg, cancellationToken: cancellationToken);
                ++sentCount;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Broadcast delivery failed for user {DeliveryUserId}", id);
                ++failedCount;
            }
        }

        _logger.LogInformation("Broadcast finished; sent: {SentCount}, failed: {FailedCount}", sentCount, failedCount);
        await _botClient.SendMessage(chatId,
            $"Сообщение отправлено:\n✅ Успешно: {sentCount}\n❌ Ошибок: {failedCount}",
            cancellationToken: cancellationToken);
    }

    private async Task HandleParticipantsCommand(long chatId, CancellationToken cancellationToken)
    {
        var users = await _context.Users.OrderBy(u => u.RegisteredAt).ToListAsync(cancellationToken);
        
        if (!users.Any())
        {
            await _botClient.SendMessage(chatId, "Нет зарегистрированных участников.", cancellationToken: cancellationToken);
            return;
        }
        
        var message = $"Участники ({users.Count}):\n\n";
        foreach (var user in users)
        {
            message += $"{GetUserDisplayName(user)}\n";
        }
        
        await _botClient.SendMessage(chatId, message, cancellationToken: cancellationToken);
    }
    
    private async Task HandleStatsCommand(long chatId, CancellationToken cancellationToken)
    {
        var participantCount = await _shuffleService.GetParticipantCountAsync();
        var shuffledCount = await _shuffleService.GetShuffledCountAsync();
        var hasShuffled = await _shuffleService.HasShuffleHappenedAsync();
        
        var message = "📊 Статистика:\n\n" +
                     $"Участников: {participantCount}\n" +
                     $"Жеребьёвка: {(hasShuffled ? "проведена" : "не проведена")}\n" +
                     $"Назначений: {shuffledCount}";
        
        await _botClient.SendMessage(chatId, message, cancellationToken: cancellationToken);
    }
    
    private bool IsAdmin(long userId)
    {
        var adminIds = _configuration.GetSection("AdminUserIds").Get<long[]>() ?? Array.Empty<long>();
        return adminIds.Contains(userId);
    }
    
    private string GetUserDisplayName(User user)
    {
        if (!string.IsNullOrEmpty(user.Username))
            return $"@{user.Username}";
        
        var name = user.FirstName ?? "";
        if (!string.IsNullOrEmpty(user.LastName))
            name += " " + user.LastName;
        
        return string.IsNullOrEmpty(name) ? $"ID: {user.TelegramUserId}" : name;
    }
    
    private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
            _logger.LogDebug("Telegram polling cancelled during shutdown");
        else
            _logger.LogError(exception, "Telegram polling failed");
        return Task.CompletedTask;
    }
}

