using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
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
    private CancellationTokenSource? _cancellationTokenSource;
    
    public BotService(
        ITelegramBotClient botClient,
        DatabaseContext context,
        ShuffleService shuffleService,
        MessageService messageService,
        IConfiguration configuration)
    {
        _botClient = botClient;
        _context = context;
        _shuffleService = shuffleService;
        _messageService = messageService;
        _configuration = configuration;
    }
    
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _cancellationTokenSource = new CancellationTokenSource();
        
        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = Array.Empty<UpdateType>()
        };

        await _botClient.SetMyCommandsAsync(
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
            pollingErrorHandler: HandlePollingErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: _cancellationTokenSource.Token
        );
        
        var me = await _botClient.GetMeAsync(cancellationToken);
        Console.WriteLine($"Бот @{me.Username} запущен и готов к работе!");
    }
    
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cancellationTokenSource?.Cancel();
        return Task.CompletedTask;
    }
    
    private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        if (update.Message is not { } message)
            return;
        
        if (message.Text is not { } messageText)
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
            
            try
            {
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
                        await HandleMessageCommand(chatId, userId, args, cancellationToken);
                        break;
                    // Admin commands
                    case "/adduser":
                        if (IsAdmin(userId))
                            await HandleAddUserCommand(chatId, args, cancellationToken);
                        else
                            await botClient.SendTextMessageAsync(chatId, "У вас нет прав администратора.", cancellationToken: cancellationToken);
                        break;
                    case "/shuffle":
                        if (IsAdmin(userId))
                            await HandleShuffleCommand(chatId, cancellationToken);
                        else
                            await botClient.SendTextMessageAsync(chatId, "У вас нет прав администратора.", cancellationToken: cancellationToken);
                        break;
                    case "/sendinfo":
                        if (IsAdmin(userId))
                            await HandleSendInfoCommand(chatId, cancellationToken);
                        else
                            await botClient.SendTextMessageAsync(chatId, "У вас нет прав администратора.", cancellationToken: cancellationToken);
                        break;
                    case "/participants":
                        if (IsAdmin(userId))
                            await HandleParticipantsCommand(chatId, cancellationToken);
                        else
                            await botClient.SendTextMessageAsync(chatId, "У вас нет прав администратора.", cancellationToken: cancellationToken);
                        break;
                    case "/stats":
                        if (IsAdmin(userId))
                            await HandleStatsCommand(chatId, cancellationToken);
                        else
                            await botClient.SendTextMessageAsync(chatId, "У вас нет прав администратора.", cancellationToken: cancellationToken);
                        break;
                    default:
                        await botClient.SendTextMessageAsync(chatId, "Неизвестная команда. Используйте /start для просмотра доступных команд.", cancellationToken: cancellationToken);
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при обработке команды: {ex.Message}");
                await botClient.SendTextMessageAsync(chatId, "Произошла ошибка при обработке команды. Попробуйте позже.", cancellationToken: cancellationToken);
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
                      "/recipientinfo - Посмотреть информацию о том, кому вы дарите" +
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
                       "/participants - Список участников\n" +
                       "/stats - Статистика";
        }

        await _botClient.SendTextMessageAsync(chatId, message, cancellationToken: cancellationToken);
    }
    
    private async Task HandleRegisterCommand(long chatId, long userId, string? username, string? firstName, string? lastName, CancellationToken cancellationToken)
    {
        if (await _shuffleService.HasShuffleHappenedAsync())
        {
            await _botClient.SendTextMessageAsync(chatId, "Регистрация закрыта. Жеребьёвка уже была проведена.", cancellationToken: cancellationToken);
            return;
        }
        
        var existingUser = await _context.Users.FindAsync(userId);
        if (existingUser != null)
        {
            await _botClient.SendTextMessageAsync(chatId, "Вы уже зарегистрированы! Используйте /myinfo для просмотра информации.", cancellationToken: cancellationToken);
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
        
        await _botClient.SendTextMessageAsync(chatId, 
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
            await _botClient.SendTextMessageAsync(chatId, "Вы не зарегистрированы. Используйте /register для регистрации.", cancellationToken: cancellationToken);
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
        
        await _botClient.SendTextMessageAsync(chatId, message, cancellationToken: cancellationToken);
    }

    private async Task HandleRecipientInfoCommand(long chatId, long userId, CancellationToken cancellationToken)
    {
        if (!await _shuffleService.HasShuffleHappenedAsync())
        {
            await _botClient.SendTextMessageAsync(chatId, "Информация о получателе недоступна до жеребьёвки.", cancellationToken: cancellationToken);
            return;
        }

        var user = await _context.Users.FindAsync(userId);
        if (user == null)
        {
            await _botClient.SendTextMessageAsync(chatId, "Вы не зарегистрированы. Используйте /register для регистрации.", cancellationToken: cancellationToken);
            return;
        }

        var assignment = await _shuffleService.GetGifterAssignmentAsync(userId);

        if (assignment is null)
        {
            await _botClient.SendTextMessageAsync(chatId, "Не найдена информация о вашем назначении.", cancellationToken: cancellationToken);
            return;
        }

        var message = GetRecipientInfoString(assignment);
        await _botClient.SendTextMessageAsync(chatId, message, cancellationToken: cancellationToken);
    }
    
    private async Task HandleUpdateWishesCommand(long chatId, long userId, string args, CancellationToken cancellationToken)
    {
        if (await _shuffleService.HasShuffleHappenedAsync())
        {
            await _botClient.SendTextMessageAsync(chatId, "Обновление информации недоступно после жеребьёвки.", cancellationToken: cancellationToken);
            return;
        }
        
        if (string.IsNullOrWhiteSpace(args))
        {
            await _botClient.SendTextMessageAsync(chatId, "Использование: /updatewishes <текст пожеланий>", cancellationToken: cancellationToken);
            return;
        }
        
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
        {
            await _botClient.SendTextMessageAsync(chatId, "Вы не зарегистрированы. Используйте /register для регистрации.", cancellationToken: cancellationToken);
            return;
        }
        
        user.Wishes = args;
        await _context.SaveChangesAsync(cancellationToken);
        
        await _botClient.SendTextMessageAsync(chatId, "✅ Пожелания обновлены!", cancellationToken: cancellationToken);
    }
    
    private async Task HandleUpdateDestinationCommand(long chatId, long userId, string args, CancellationToken cancellationToken)
    {
        if (await _shuffleService.HasShuffleHappenedAsync())
        {
            await _botClient.SendTextMessageAsync(chatId, "Обновление информации недоступно после жеребьёвки.", cancellationToken: cancellationToken);
            return;
        }
        
        if (string.IsNullOrWhiteSpace(args))
        {
            await _botClient.SendTextMessageAsync(chatId, "Использование: /update_destination <текст>", cancellationToken: cancellationToken);
            return;
        }
        
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
        {
            await _botClient.SendTextMessageAsync(chatId, "Вы не зарегистрированы. Используйте /register для регистрации.", cancellationToken: cancellationToken);
            return;
        }
        
        user.Addresses = args;
        await _context.SaveChangesAsync(cancellationToken);
        
        await _botClient.SendTextMessageAsync(chatId, "✅ Адрес доставки обновлён!", cancellationToken: cancellationToken);
    }
    
    private async Task HandleUpdatePhoneCommand(long chatId, long userId, string args, CancellationToken cancellationToken)
    {
        if (await _shuffleService.HasShuffleHappenedAsync())
        {
            await _botClient.SendTextMessageAsync(chatId, "Обновление информации недоступно после жеребьёвки.", cancellationToken: cancellationToken);
            return;
        }
        
        if (string.IsNullOrWhiteSpace(args))
        {
            await _botClient.SendTextMessageAsync(chatId, "Использование: /updatephone <номер телефона>", cancellationToken: cancellationToken);
            return;
        }
        
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
        {
            await _botClient.SendTextMessageAsync(chatId, "Вы не зарегистрированы. Используйте /register для регистрации.", cancellationToken: cancellationToken);
            return;
        }
        
        user.PhoneNumber = args;
        await _context.SaveChangesAsync(cancellationToken);
        
        await _botClient.SendTextMessageAsync(chatId, "✅ Номер телефона обновлён!", cancellationToken: cancellationToken);
    }
    
    private async Task HandleBlacklistCommand(long chatId, long userId, string args, CancellationToken cancellationToken)
    {
        if (await _shuffleService.HasShuffleHappenedAsync())
        {
            await _botClient.SendTextMessageAsync(chatId, "Изменение чёрного списка недоступно после жеребьёвки.", cancellationToken: cancellationToken);
            return;
        }
        
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
        {
            await _botClient.SendTextMessageAsync(chatId, "Вы не зарегистрированы. Используйте /register для регистрации.", cancellationToken: cancellationToken);
            return;
        }
        
        if (string.IsNullOrWhiteSpace(args))
        {
            await _botClient.SendTextMessageAsync(chatId, 
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
                await _botClient.SendTextMessageAsync(chatId, "Ваш чёрный список пуст.", cancellationToken: cancellationToken);
                return;
            }
            
            var message = "Ваш чёрный список:\n";
            foreach (var item in blacklist)
            {
                message += $"- {GetUserDisplayName(item.BlacklistedUser)}\n";
            }
            
            await _botClient.SendTextMessageAsync(chatId, message, cancellationToken: cancellationToken);
            return;
        }
        
        if (parts.Length < 2)
        {
            await _botClient.SendTextMessageAsync(chatId, "Укажите имя пользователя.", cancellationToken: cancellationToken);
            return;
        }
        
        var username = parts[1].TrimStart('@');
        var targetUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == username, cancellationToken);
        
        if (targetUser == null)
        {
            await _botClient.SendTextMessageAsync(chatId, "Пользователь не найден или не зарегистрирован.", cancellationToken: cancellationToken);
            return;
        }
        
        if (targetUser.TelegramUserId == userId)
        {
            await _botClient.SendTextMessageAsync(chatId, "Вы не можете добавить себя в чёрный список.", cancellationToken: cancellationToken);
            return;
        }
        
        if (action == "add")
        {
            var existing = await _context.Blacklist
                .FirstOrDefaultAsync(b => b.UserId == userId && b.BlacklistedUserId == targetUser.TelegramUserId, cancellationToken);
            
            if (existing != null)
            {
                await _botClient.SendTextMessageAsync(chatId, "Пользователь уже в чёрном списке.", cancellationToken: cancellationToken);
                return;
            }
            
            _context.Blacklist.Add(new Blacklist
            {
                UserId = userId,
                BlacklistedUserId = targetUser.TelegramUserId
            });
            
            await _context.SaveChangesAsync(cancellationToken);
            await _botClient.SendTextMessageAsync(chatId, $"✅ {GetUserDisplayName(targetUser)} добавлен в чёрный список.", cancellationToken: cancellationToken);
        }
        else if (action == "remove")
        {
            var existing = await _context.Blacklist
                .FirstOrDefaultAsync(b => b.UserId == userId && b.BlacklistedUserId == targetUser.TelegramUserId, cancellationToken);
            
            if (existing == null)
            {
                await _botClient.SendTextMessageAsync(chatId, "Пользователь не найден в чёрном списке.", cancellationToken: cancellationToken);
                return;
            }
            
            _context.Blacklist.Remove(existing);
            await _context.SaveChangesAsync(cancellationToken);
            await _botClient.SendTextMessageAsync(chatId, $"✅ {GetUserDisplayName(targetUser)} удалён из чёрного списка.", cancellationToken: cancellationToken);
        }
        else
        {
            await _botClient.SendTextMessageAsync(chatId, "Неизвестное действие. Используйте 'add', 'remove' или 'list'.", cancellationToken: cancellationToken);
        }
    }
    
    private async Task HandleMessageCommand(long chatId, long userId, string args, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            await _botClient.SendTextMessageAsync(chatId, 
                "Использование:\n" +
                "/message sender <текст> - Отправить сообщение тому, кто вам дарит\n" +
                "/message recipient <текст> - Отправить сообщение тому, кому вы дарите", 
                cancellationToken: cancellationToken);
            return;
        }
        
        var parts = args.Split(' ', 2);
        if (parts.Length < 2)
        {
            await _botClient.SendTextMessageAsync(chatId, "Укажите получателя (sender/recipient) и текст сообщения.", cancellationToken: cancellationToken);
            return;
        }
        
        var targetType = parts[0];
        var messageText = parts[1];
        
        var (success, errorMessage, targetUser, isFromGifter) = await _messageService.ValidateAndGetTargetAsync(userId, targetType);
        
        if (!success)
        {
            await _botClient.SendTextMessageAsync(chatId, errorMessage, cancellationToken: cancellationToken);
            return;
        }
        
        if (targetUser == null)
        {
            await _botClient.SendTextMessageAsync(chatId, "Получатель не найден.", cancellationToken: cancellationToken);
            return;
        }
        
        // Save message
        await _messageService.SaveMessageAsync(userId, targetUser.TelegramUserId, messageText, isFromGifter);
        
        // Send to recipient
        var label = isFromGifter 
            ? "💬 Сообщение от того, кто вам дарит (sender):"
            : "💬 Сообщение от того, кому вы дарите (recipient):";
        
        await _botClient.SendTextMessageAsync(targetUser.TelegramUserId, $"{label}\n\n{messageText}", cancellationToken: cancellationToken);
        await _botClient.SendTextMessageAsync(chatId, "✅ Сообщение отправлено!", cancellationToken: cancellationToken);
    }
    
    private async Task HandleAddUserCommand(long chatId, string args, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            await _botClient.SendTextMessageAsync(chatId, "Использование: /adduser @username", cancellationToken: cancellationToken);
            return;
        }
        
        var username = args.TrimStart('@');
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username, cancellationToken);
        
        if (user != null)
        {
            await _botClient.SendTextMessageAsync(chatId, "Пользователь уже зарегистрирован.", cancellationToken: cancellationToken);
            return;
        }
        
        // Note: This command requires the user to have started the bot first
        // In a real scenario, you might want to get user info differently
        await _botClient.SendTextMessageAsync(chatId, 
            $"Пользователь @{username} должен сначала написать боту /start, чтобы его можно было добавить. " +
            "Или используйте /register для регистрации через бота.", 
            cancellationToken: cancellationToken);
    }
    
    private async Task HandleShuffleCommand(long chatId, CancellationToken cancellationToken)
    {
        var (_, message) = await _shuffleService.PerformShuffleAsync();
        
        await _botClient.SendTextMessageAsync(chatId, message, cancellationToken: cancellationToken);
    }
    
    private async Task HandleSendInfoCommand(long chatId, CancellationToken cancellationToken)
    {
        if (!await _shuffleService.HasShuffleHappenedAsync())
        {
            await _botClient.SendTextMessageAsync(chatId, "Жеребьёвка ещё не была проведена.", cancellationToken: cancellationToken);
            return;
        }
        
        var assignments = await _context.Shuffle
            .Include(s => s.Recipient)
            .ToListAsync(cancellationToken);
        
        int sentCount = 0;
        int failedCount = 0;
        
        foreach (var assignment in assignments)
        {
            try
            {
                var message = GetRecipientInfoString(assignment);
                
                await _botClient.SendTextMessageAsync(assignment.GifterId, message, cancellationToken: cancellationToken);
                sentCount++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при отправке сообщения пользователю {assignment.GifterId}: {ex.Message}");
                failedCount++;
            }
        }
        
        await _botClient.SendTextMessageAsync(chatId, 
            $"Информация отправлена:\n✅ Успешно: {sentCount}\n❌ Ошибок: {failedCount}", 
            cancellationToken: cancellationToken);
    }

    private string GetRecipientInfoString(Shuffle assignment) =>
        "🎁 Информация о вашем получателе:\n\n" +
        $"Получатель: {GetUserDisplayName(assignment.Recipient)}\n" +
        $"Пожелания: {(string.IsNullOrEmpty(assignment.Recipient.Wishes) ? "не указаны" : assignment.Recipient.Wishes)}\n" +
        $"Адрес доставки: {(string.IsNullOrEmpty(assignment.Recipient.Addresses) ? "не указан" : assignment.Recipient.Addresses)}\n" +
        $"Телефон: {(string.IsNullOrEmpty(assignment.Recipient.PhoneNumber) ? "не указан" : assignment.Recipient.PhoneNumber)}";
    
    private async Task HandleParticipantsCommand(long chatId, CancellationToken cancellationToken)
    {
        var users = await _context.Users.OrderBy(u => u.RegisteredAt).ToListAsync(cancellationToken);
        
        if (!users.Any())
        {
            await _botClient.SendTextMessageAsync(chatId, "Нет зарегистрированных участников.", cancellationToken: cancellationToken);
            return;
        }
        
        var message = $"Участники ({users.Count}):\n\n";
        foreach (var user in users)
        {
            message += $"{GetUserDisplayName(user)}\n";
        }
        
        await _botClient.SendTextMessageAsync(chatId, message, cancellationToken: cancellationToken);
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
        
        await _botClient.SendTextMessageAsync(chatId, message, cancellationToken: cancellationToken);
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
        Console.WriteLine($"Ошибка при опросе Telegram API: {exception.Message}");
        return Task.CompletedTask;
    }
}

