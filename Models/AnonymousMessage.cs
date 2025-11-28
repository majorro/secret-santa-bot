using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SecretSantaBot.Models;

[Table("AnonymousMessages")]
public class AnonymousMessage
{
    [Key]
    public int Id { get; set; }
    
    public long FromUserId { get; set; }
    
    public long ToUserId { get; set; }
    
    public string Message { get; set; } = string.Empty;
    
    public DateTime SentAt { get; set; }
    
    public bool IsFromGifter { get; set; }
    
    [ForeignKey(nameof(FromUserId))]
    public virtual User FromUser { get; set; } = null!;
    
    [ForeignKey(nameof(ToUserId))]
    public virtual User ToUser { get; set; } = null!;
}

