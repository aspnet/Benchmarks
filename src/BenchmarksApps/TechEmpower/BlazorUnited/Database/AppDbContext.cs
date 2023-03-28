using BlazorUnited.Models;
using Microsoft.EntityFrameworkCore;

namespace BlazorUnited.Database;

public class AppDbContext : DbContext
{
    public static readonly Func<AppDbContext, IAsyncEnumerable<Fortune>> FortunesQuery =
        EF.CompileAsyncQuery((AppDbContext context) => context.Fortunes);

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
        ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        ChangeTracker.AutoDetectChangesEnabled = false;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var fortune = modelBuilder.Entity<Fortune>()
            .ToTable("fortune");
        fortune.Property(f => f.Id).HasColumnName("id");
        fortune.Property(f => f.Message).HasColumnName("message");
    }

    public required DbSet<Fortune> Fortunes { get; set; }
}
