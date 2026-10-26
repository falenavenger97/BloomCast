using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BloomCast.Api.Models;


// ============================================================
// USUARIO
// ============================================================

[Table("usuarios")]
public class Usuario
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("nombrecompleto")]
    public string NombreCompleto { get; set; } = "";

    [Column("correo")]
    public string Correo { get; set; } = "";

    [Column("passwordhash")]
    public string PasswordHash { get; set; } = "";

    [Column("rolid")]
    public int RolId { get; set; }

    [Column("activo")]
    public bool Activo { get; set; }

    [Column("mfa_secret")]
    public string? MfaSecret { get; set; }

    [Column("mfa_habilitado")]
    public bool MfaHabilitado { get; set; }

    [ForeignKey(nameof(RolId))]
    public Rol? Rol { get; set; }
}


// ============================================================
// VENTA
// ============================================================

[Table("ventas")]
public class Venta
{
    [Key]
    [Column("id")]
    public int Id { get; set; }


    [Column("usuarioid")]
    public int UsuarioId { get; set; }


    [Column("fechaventa")]
    public DateTime FechaVenta { get; set; }


    [Column("total")]
    public decimal Total { get; set; }


    [ForeignKey(nameof(UsuarioId))]
    public Usuario? Usuario { get; set; }


    public List<DetalleVenta> Detalles { get; set; } = new();
}


// ============================================================
// DETALLE VENTA
// ============================================================

[Table("detalle_ventas")]
public class DetalleVenta
{
    [Key]
    [Column("id")]
    public int Id { get; set; }


    [Column("ventaid")]
    public int VentaId { get; set; }


    [Column("loteid")]
    public int LoteId { get; set; }


    [Column("cantidad")]
    public int Cantidad { get; set; }


    [Column("preciounitario")]
    public decimal PrecioUnitario { get; set; }


    [Column("subtotal")]
    public decimal Subtotal { get; set; }


    [ForeignKey(nameof(VentaId))]
    public Venta? Venta { get; set; }


    [ForeignKey(nameof(LoteId))]
    public Lote? Lote { get; set; }
}