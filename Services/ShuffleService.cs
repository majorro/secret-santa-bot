using Microsoft.EntityFrameworkCore;
using SecretSantaBot.Data;
using SecretSantaBot.Models;

namespace SecretSantaBot.Services;

public class ShuffleService
{
    private readonly DatabaseContext _context;
    
    public ShuffleService(DatabaseContext context)
    {
        _context = context;
    }
    
    public async Task<bool> HasShuffleHappenedAsync()
    {
        return await _context.Shuffle.AnyAsync();
    }
    
    public async Task<(bool Success, string Message)> PerformShuffleAsync()
    {
        var users = await _context.Users.ToListAsync();
        
        if (users.Count < 2)
        {
            return (false, "Недостаточно участников для жеребьёвки. Нужно минимум 2 человека.");
        }
        
        // Check if shuffle already happened
        if (await HasShuffleHappenedAsync())
        {
            _context.RemoveRange(await _context.Shuffle.ToArrayAsync());
            await _context.SaveChangesAsync();
            // return (false, "Жеребьёвка уже была проведена.");
        }
        
        // Get all blacklists
        var blacklists = await _context.Blacklist.ToListAsync();
        var blacklistDict = blacklists
            .GroupBy(b => b.UserId)
            .ToDictionary(g => g.Key, g => g.Select(b => b.BlacklistedUserId).ToHashSet());
        
        // Validate that each user has at least one possible recipient
        foreach (var user in users)
        {
            var possibleRecipients = users
                .Where(u => u.TelegramUserId != user.TelegramUserId)
                .Where(u => !blacklistDict.ContainsKey(user.TelegramUserId) || 
                           !blacklistDict[user.TelegramUserId].Contains(u.TelegramUserId))
                .ToList();
            
            if (possibleRecipients.Count == 0)
            {
                return (false, $"Пользователь {GetUserDisplayName(user)} не может получить получателя из-за чёрного списка.");
            }
        }
        
        // Perform shuffle
        var assignments = new Dictionary<long, long>();
        var availableRecipients = users.ToList();
        var random = new Random();
        
        foreach (var gifter in users.OrderBy(_ => random.Next()))
        {
            var possibleRecipients = availableRecipients
                .Where(r => r.TelegramUserId != gifter.TelegramUserId)
                .Where(r => !blacklistDict.ContainsKey(gifter.TelegramUserId) || 
                           !blacklistDict[gifter.TelegramUserId].Contains(r.TelegramUserId))
                .ToList();
            
            if (possibleRecipients.Count == 0)
            {
                // This shouldn't happen due to validation, but handle it
                return (false, "Ошибка при жеребьёвке. Попробуйте ещё раз.");
            }
            
            var recipient = possibleRecipients[random.Next(possibleRecipients.Count)];
            assignments[gifter.TelegramUserId] = recipient.TelegramUserId;
            availableRecipients.Remove(recipient);
        }
        
        // Save to database
        var shuffleEntries = assignments.Select(a => new Shuffle
        {
            GifterId = a.Key,
            RecipientId = a.Value,
            ShuffledAt = DateTime.UtcNow
        }).ToList();
        
        _context.Shuffle.AddRange(shuffleEntries);
        await _context.SaveChangesAsync();
        
        return (true, "Жеребьёвка успешно проведена!");
    }
    
    public async Task<Shuffle?> GetGifterAssignmentAsync(long userId)
    {
        return await _context.Shuffle
            .Include(s => s.Recipient)
            .FirstOrDefaultAsync(s => s.GifterId == userId);
    }
    
    public async Task<Shuffle?> GetRecipientAssignmentAsync(long userId)
    {
        return await _context.Shuffle
            .Include(s => s.Gifter)
            .FirstOrDefaultAsync(s => s.RecipientId == userId);
    }
    
    public async Task<int> GetParticipantCountAsync()
    {
        return await _context.Users.CountAsync();
    }
    
    public async Task<int> GetShuffledCountAsync()
    {
        return await _context.Shuffle.CountAsync();
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
}

