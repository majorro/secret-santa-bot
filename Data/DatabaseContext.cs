using Microsoft.EntityFrameworkCore;
using SecretSantaBot.Models;

namespace SecretSantaBot.Data;

public class DatabaseContext : DbContext
{
    public DbSet<User> Users { get; set; }
    public DbSet<Blacklist> Blacklist { get; set; }
    public DbSet<Shuffle> Shuffle { get; set; }
    public DbSet<AnonymousMessage> AnonymousMessages { get; set; }
    
    public DatabaseContext(DbContextOptions<DatabaseContext> options) : base(options)
    {
    }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        modelBuilder.Entity<Blacklist>()
            .HasKey(b => new { b.UserId, b.BlacklistedUserId });
        
        modelBuilder.Entity<Blacklist>()
            .HasOne(b => b.User)
            .WithMany(u => u.BlacklistedUsers)
            .HasForeignKey(b => b.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        
        modelBuilder.Entity<Blacklist>()
            .HasOne(b => b.BlacklistedUser)
            .WithMany(u => u.BlacklistedBy)
            .HasForeignKey(b => b.BlacklistedUserId)
            .OnDelete(DeleteBehavior.Restrict);
        
        modelBuilder.Entity<Shuffle>()
            .HasKey(s => s.GifterId);
        
        modelBuilder.Entity<Shuffle>()
            .HasOne(s => s.Gifter)
            .WithOne(u => u.GiftingTo)
            .HasForeignKey<Shuffle>(s => s.GifterId)
            .OnDelete(DeleteBehavior.Restrict);
        
        modelBuilder.Entity<Shuffle>()
            .HasOne(s => s.Recipient)
            .WithOne(u => u.GiftedBy)
            .HasForeignKey<Shuffle>(s => s.RecipientId)
            .OnDelete(DeleteBehavior.Restrict);
        
        modelBuilder.Entity<AnonymousMessage>()
            .HasOne(m => m.FromUser)
            .WithMany(u => u.SentMessages)
            .HasForeignKey(m => m.FromUserId)
            .OnDelete(DeleteBehavior.Restrict);
        
        modelBuilder.Entity<AnonymousMessage>()
            .HasOne(m => m.ToUser)
            .WithMany(u => u.ReceivedMessages)
            .HasForeignKey(m => m.ToUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

