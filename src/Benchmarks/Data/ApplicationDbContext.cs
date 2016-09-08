// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using Benchmarks.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Benchmarks.Data
{
    public class ApplicationDbContext : DbContext
    {
        private readonly AppSettings _appSettings;

        public ApplicationDbContext(IOptions<AppSettings> appSettings)
        {
            _appSettings = appSettings.Value;
            Database.AutoTransactionsEnabled = false;
        }

        public DbSet<World> World { get; set; }

        public DbSet<Fortune> Fortune { get; set; }

        public bool UseBatchUpdate 
        { 
            get
            {
                return _appSettings.Database != DatabaseServer.PostgreSql;
            }
        } 

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (_appSettings.Database == DatabaseServer.PostgreSql)
            {
                optionsBuilder.UseNpgsql(_appSettings.ConnectionString);
            }
            else
            {
                optionsBuilder.UseSqlServer(_appSettings.ConnectionString);
            }
        }
    }
}
