using BloomCast.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BloomCast.Api.Data;

public class BloomCastDbContext : DbContext
{
    public BloomCastDbContext(
        DbContextOptions<BloomCastDbContext> options)
        : base(options)
    {
    }

    public DbSet<Orquidea> Orquideas => Set<Orquidea>();

    public DbSet<Lote> Lotes => Set<Lote>();


    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Lote>()
            .HasOne(l => l.Orquidea)
            .WithMany()
            .HasForeignKey(l => l.OrquideaId);
    }
}