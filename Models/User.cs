using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SecretSantaBot.Models;

[Table("Users")]
public class User
{
    [Key]
    public long TelegramUserId { get; set; }
    
    public string? Username { get; set; }
    
    public string? FirstName { get; set; }
    
    public string? LastName { get; set; }
    
    public string? Wishes { get; set; }
    
    public string? Addresses { get; set; }
    
    public string? PhoneNumber { get; set; }
    
    public DateTime RegisteredAt { get; set; }
    
    public virtual ICollection<Blacklist> BlacklistedUsers { get; set; } = new List<Blacklist>();
    
    public virtual ICollection<Blacklist> BlacklistedBy { get; set; } = new List<Blacklist>();
    
    public virtual Shuffle? GiftingTo { get; set; }
    
    public virtual Shuffle? GiftedBy { get; set; }
    
    public virtual ICollection<AnonymousMessage> SentMessages { get; set; } = new List<AnonymousMessage>();
    
    public virtual ICollection<AnonymousMessage> ReceivedMessages { get; set; } = new List<AnonymousMessage>();
}

