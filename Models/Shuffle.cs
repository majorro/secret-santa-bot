using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SecretSantaBot.Models;

[Table("Shuffle")]
public class Shuffle
{
    [Key]
    public long GifterId { get; set; }
    
    public long RecipientId { get; set; }
    
    public DateTime ShuffledAt { get; set; }
    
    [ForeignKey(nameof(GifterId))]
    public virtual User Gifter { get; set; } = null!;
    
    [ForeignKey(nameof(RecipientId))]
    public virtual User Recipient { get; set; } = null!;
}

