using System;
using Microsoft.Data.Entity;
using Microsoft.Data.Entity.Infrastructure;
using Microsoft.Data.Entity.Metadata;
using Microsoft.Data.Entity.Migrations;
using Benchmarks.Data;

namespace Benchmarks.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20151124205054_Fortunue")]
    partial class Fortunue
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
            modelBuilder
                .HasAnnotation("ProductVersion", "7.0.0-rc2-16413")
                .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

            modelBuilder.Entity("Benchmarks.Data.Fortune", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd();

                    b.Property<string>("Message")
                        .HasAnnotation("MaxLength", 2048);

                    b.HasKey("Id");
                });

            modelBuilder.Entity("Benchmarks.Data.World", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd();

                    b.Property<int>("RandomNumber");

                    b.HasKey("Id");
                });
        }
    }
}
