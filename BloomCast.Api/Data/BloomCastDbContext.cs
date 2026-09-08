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

    public DbSet<PrediccionDemanda> PrediccionesDemanda =>
    Set<PrediccionDemanda>();
    // ============================================================
    // Inventario
    // ============================================================

    public DbSet<Orquidea> Orquideas =>
        Set<Orquidea>();


    public DbSet<Lote> Lotes =>
        Set<Lote>();
    public DbSet<Rol> Roles =>
    Set<Rol>();

    // ============================================================
    // Ventas
    // ============================================================

    public DbSet<Usuario> Usuarios =>
        Set<Usuario>();


    public DbSet<Venta> Ventas =>
        Set<Venta>();


    public DbSet<DetalleVenta> DetalleVentas =>
        Set<DetalleVenta>();


    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<PrediccionDemanda>()
            .HasOne(p => p.Orquidea)
            .WithMany()
            .HasForeignKey(p => p.OrquideaId);

        modelBuilder.Entity<Usuario>()
            .HasOne(u => u.Rol)
            .WithMany()
            .HasForeignKey(u => u.RolId);
        // ========================================================
        // Lote -> Orquídea
        // ========================================================

        modelBuilder.Entity<Lote>()
            .HasOne(l => l.Orquidea)
            .WithMany()
            .HasForeignKey(l => l.OrquideaId);


        // ========================================================
        // Venta -> Usuario
        // ========================================================

        modelBuilder.Entity<Venta>()
            .HasOne(v => v.Usuario)
            .WithMany()
            .HasForeignKey(v => v.UsuarioId);


        // ========================================================
        // Venta -> Detalles
        // ========================================================

        modelBuilder.Entity<Venta>()
            .HasMany(v => v.Detalles)
            .WithOne(d => d.Venta)
            .HasForeignKey(d => d.VentaId);


        // ========================================================
        // Detalle -> Lote
        // ========================================================

        modelBuilder.Entity<DetalleVenta>()
            .HasOne(d => d.Lote)
            .WithMany()
            .HasForeignKey(d => d.LoteId);
    }
}