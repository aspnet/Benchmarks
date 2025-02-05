// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.EntityFrameworkCore;

namespace EFCoreAotQueriesBenchmarks
{ 
    public class EFCoreAotBenchmarkContext : DbContext
    {
        public static bool OnModelCreatingRan { get; set; } = false;

        public DbSet<MyEntity0> Entities0 { get; set; } = null!;
        public DbSet<MyEntity1> Entities1 { get; set; } = null!;
        public DbSet<MyEntity2> Entities2 { get; set; } = null!;
        public DbSet<MyEntity3> Entities3 { get; set; } = null!;
        public DbSet<MyEntity4> Entities4 { get; set; } = null!;
        public DbSet<MyEntity5> Entities5 { get; set; } = null!;
        public DbSet<MyEntity6> Entities6 { get; set; } = null!;
        public DbSet<MyEntity7> Entities7 { get; set; } = null!;
        public DbSet<MyEntity8> Entities8 { get; set; } = null!;
        public DbSet<MyEntity9> Entities9 { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            OnModelCreatingRan = true;

            modelBuilder.Entity<MyEntity0>().OwnsMany(x => x.Owned, b => { b.ToJson(); });
            modelBuilder.Entity<MyEntity1>().OwnsMany(x => x.Owned, b => { b.ToJson(); });
            modelBuilder.Entity<MyEntity2>().OwnsMany(x => x.Owned, b => { b.ToJson(); });
            modelBuilder.Entity<MyEntity3>().OwnsMany(x => x.Owned, b => { b.ToJson(); });
            modelBuilder.Entity<MyEntity4>().OwnsMany(x => x.Owned, b => { b.ToJson(); });
            modelBuilder.Entity<MyEntity5>().OwnsMany(x => x.Owned, b => { b.ToJson(); });
            modelBuilder.Entity<MyEntity6>().OwnsMany(x => x.Owned, b => { b.ToJson(); });
            modelBuilder.Entity<MyEntity7>().OwnsMany(x => x.Owned, b => { b.ToJson(); });
            modelBuilder.Entity<MyEntity8>().OwnsMany(x => x.Owned, b => { b.ToJson(); });
            modelBuilder.Entity<MyEntity9>().OwnsMany(x => x.Owned, b => { b.ToJson(); });
        }

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseSqlite($"Data Source=efcoreaotbenchmark.db");
    }
}
