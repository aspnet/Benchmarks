using BlazorSSR.Models;
using Microsoft.EntityFrameworkCore;

namespace BlazorSSR.Database;

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
        => modelBuilder.Entity<Fortune>(b =>
        {
            b.ToTable("fortune");
            b.Property(f => f.Id).HasColumnName("id");
            b.Property(f => f.Message).HasColumnName("message");
        });

    public required DbSet<Fortune> Fortunes { get; set; }
}
