using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SecretSantaBot.Models;

[Table("Blacklist")]
public class Blacklist
{
    [Key]
    [Column(Order = 0)]
    public long UserId { get; set; }
    
    [Key]
    [Column(Order = 1)]
    public long BlacklistedUserId { get; set; }
    
    [ForeignKey(nameof(UserId))]
    public virtual User User { get; set; } = null!;
    
    [ForeignKey(nameof(BlacklistedUserId))]
    public virtual User BlacklistedUser { get; set; } = null!;
}

