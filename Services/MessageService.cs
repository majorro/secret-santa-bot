using Microsoft.EntityFrameworkCore;
using SecretSantaBot.Data;
using SecretSantaBot.Models;

namespace SecretSantaBot.Services;

public class MessageService
{
    private readonly DatabaseContext _context;
    private readonly ShuffleService _shuffleService;
    
    public MessageService(DatabaseContext context, ShuffleService shuffleService)
    {
        _context = context;
        _shuffleService = shuffleService;
    }
    
    public async Task<(bool Success, string Message, User? TargetUser, bool IsFromGifter)> 
        ValidateAndGetTargetAsync(long fromUserId, string targetType)
    {
        // Check if shuffle happened
        if (!await _shuffleService.HasShuffleHappenedAsync())
        {
            return (false, "Жеребьёвка ещё не была проведена.", null, false);
        }
        
        // Get user's assignments
        var giftingTo = await _shuffleService.GetGifterAssignmentAsync(fromUserId);
        var giftedBy = await _shuffleService.GetRecipientAssignmentAsync(fromUserId);
        
        if (giftingTo == null || giftedBy == null)
        {
            return (false, "Не найдена информация о вашем назначении.", null, false);
        }
        
        User? targetUser = null;
        bool isFromGifter = false;
        
        if (targetType.ToLower() == "sender" || targetType.ToLower() == "отправитель")
        {
            // Message to the one who gifts to this user
            targetUser = await _context.Users.FindAsync(giftedBy.GifterId);
            isFromGifter = false; // This user is the recipient, so they're messaging their gifter
        }
        else if (targetType.ToLower() == "recipient" || targetType.ToLower() == "получатель")
        {
            // Message to the one this user gifts to
            targetUser = await _context.Users.FindAsync(giftingTo.RecipientId);
            isFromGifter = true; // This user is the gifter, so they're messaging their recipient
        }
        else
        {
            return (false, "Неверный тип получателя. Используйте 'sender' или 'recipient'.", null, false);
        }
        
        if (targetUser == null)
        {
            return (false, "Получатель не найден.", null, false);
        }
        
        return (true, "", targetUser, isFromGifter);
    }
    
    public async Task SaveMessageAsync(long fromUserId, long toUserId, string message, bool isFromGifter)
    {
        var anonymousMessage = new AnonymousMessage
        {
            FromUserId = fromUserId,
            ToUserId = toUserId,
            Message = message,
            SentAt = DateTime.UtcNow,
            IsFromGifter = isFromGifter
        };
        
        _context.AnonymousMessages.Add(anonymousMessage);
        await _context.SaveChangesAsync();
    }

    public async Task<long[]> GetAllParticipantIds() => await _context.Users.Select(x => x.TelegramUserId).ToArrayAsync();
}

