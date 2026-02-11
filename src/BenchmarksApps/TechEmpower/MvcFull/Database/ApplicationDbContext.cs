using System.Data.Entity;
using MvcFull.Models;

namespace MvcFull.Database
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext() : base("DefaultConnection")
        {
            Configuration.AutoDetectChangesEnabled = false;
            Configuration.LazyLoadingEnabled = false;
        }

        public DbSet<Fortune> Fortunes { get; set; }

        public DbSet<World> Worlds { get; set; }

        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.HasDefaultSchema("public");

            // Configure tables
            modelBuilder.Entity<Fortune>().ToTable("fortune");
            modelBuilder.Entity<World>().ToTable("world");
        }
    }
}
