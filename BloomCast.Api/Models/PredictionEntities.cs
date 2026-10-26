using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BloomCast.Api.Models;

[Table("predicciones_demanda")]
public class PrediccionDemanda
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("fechageneracion")]
    public DateTime FechaGeneracion { get; set; }

    [Column("fechaobjetivo")]
    public DateTime FechaObjetivo { get; set; }

    [Column("orquideaid")]
    public int OrquideaId { get; set; }

    [Column("cantidadestimada")]
    public int CantidadEstimada { get; set; }

    [ForeignKey(nameof(OrquideaId))]
    public Orquidea? Orquidea { get; set; }
}