using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BloomCast.Api.Models;


// ============================================================
// ORQUIDEA
// ============================================================

[Table("orquideas")]
public class Orquidea
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("variedad")]
    public string Variedad { get; set; } = "";

    [Column("tipo")]
    public string Tipo { get; set; } = "";

    [Column("preciobase")]
    public decimal PrecioBase { get; set; }

    [Column("diasvidacomercial")]
    public int DiasVidaComercial { get; set; }
}


// ============================================================
// LOTE
// ============================================================

[Table("lotes")]
public class Lote
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("orquideaid")]
    public int OrquideaId { get; set; }

    [Column("codigolote")]
    public string CodigoLote { get; set; } = "";

    [Column("cantidadinicial")]
    public int CantidadInicial { get; set; }

    [Column("cantidadactual")]
    public int CantidadActual { get; set; }

    [Column("fechaingreso")]
    public DateTime FechaIngreso { get; set; }

    [Column("fechafloracion")]
    public DateTime? FechaFloracion { get; set; }

    [Column("fechacaducidad")]
    public DateTime? FechaCaducidad { get; set; }

    [ForeignKey(nameof(OrquideaId))]
    public Orquidea? Orquidea { get; set; }
}