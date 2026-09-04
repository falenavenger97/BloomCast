using Microsoft.EntityFrameworkCore;
using BloomCast.Models;

namespace BloomCast.Data
{
    public class BloomCastDbContext : DbContext
    {
        public BloomCastDbContext(DbContextOptions<BloomCastDbContext> options) : base(options) { }

        public DbSet<Rol>             Roles              { get; set; }
        public DbSet<Usuario>         Usuarios           { get; set; }
        public DbSet<Orquidea>        Orquideas          { get; set; }
        public DbSet<Insumo>          Insumos            { get; set; }
        public DbSet<RecetaArmado>    RecetasArmado      { get; set; }
        public DbSet<Lote>            Lotes              { get; set; }
        public DbSet<PrediccionDemanda> PrediccionesDemanda { get; set; }
        public DbSet<Venta>           Ventas             { get; set; }
        public DbSet<DetalleVenta>    DetalleVentas      { get; set; }

        protected override void OnModelCreating(ModelBuilder mb)
        {
            base.OnModelCreating(mb);

            mb.Entity<Usuario>()
              .HasOne(u => u.Rol)
              .WithMany()
              .HasForeignKey(u => u.RolId);

            mb.Entity<RecetaArmado>()
              .HasOne(r => r.Orquidea).WithMany().HasForeignKey(r => r.OrquideaId);
            mb.Entity<RecetaArmado>()
              .HasOne(r => r.Insumo).WithMany().HasForeignKey(r => r.InsumoId);

            mb.Entity<Lote>()
              .HasOne(l => l.Orquidea).WithMany().HasForeignKey(l => l.OrquideaId);

            mb.Entity<PrediccionDemanda>()
              .HasOne(p => p.Orquidea).WithMany().HasForeignKey(p => p.OrquideaId);

            mb.Entity<Venta>()
              .HasOne(v => v.Usuario).WithMany().HasForeignKey(v => v.UsuarioId);
            mb.Entity<Venta>()
              .HasMany(v => v.Detalles).WithOne(d => d.Venta).HasForeignKey(d => d.VentaId);

            mb.Entity<DetalleVenta>()
              .HasOne(d => d.Lote).WithMany().HasForeignKey(d => d.LoteId);

            // Tell EF Core about the extra MFA columns (added via ALTER TABLE)
            mb.Entity<Usuario>()
              .Property(u => u.MfaSecret).HasColumnName("mfa_secret").IsRequired(false);
            mb.Entity<Usuario>()
              .Property(u => u.MfaHabilitado).HasColumnName("mfa_habilitado").HasDefaultValue(false);
        }
    }
}
