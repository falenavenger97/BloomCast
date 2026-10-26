using System.ComponentModel.DataAnnotations;

namespace BloomCast.Contracts.Sales;

public class CreateSaleRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "El usuario es requerido.")]
    public int UsuarioId { get; set; }


    public DateTime FechaVenta { get; set; }


    [Required]
    [MinLength(1, ErrorMessage = "La venta debe contener al menos un producto.")]
    public List<CreateSaleItemRequest> Items { get; set; } = new();
}


public class CreateSaleItemRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "El lote es requerido.")]
    public int LoteId { get; set; }


    [Range(1, 9999, ErrorMessage = "La cantidad debe ser mayor a cero.")]
    public int Cantidad { get; set; }


    // 0 o null = utilizar PrecioBase de la orquídea.
    [Range(
        typeof(decimal),
        "0",
        "999999999",
        ErrorMessage = "El precio unitario no puede ser negativo."
    )]
    public decimal? PrecioUnitario { get; set; }
}