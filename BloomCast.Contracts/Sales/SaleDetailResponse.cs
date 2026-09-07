using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BloomCast.Contracts.Sales;

public class SaleDetailResponse
{
    public int Id { get; set; }

    public int UsuarioId { get; set; }

    public string UsuarioNombre { get; set; } = "";

    public DateTime FechaVenta { get; set; }

    public decimal Total { get; set; }

    public List<SaleDetailItemResponse> Detalles { get; set; } = new();
}


public class SaleDetailItemResponse
{
    public int LoteId { get; set; }

    public string CodigoLote { get; set; } = "";

    public string Producto { get; set; } = "";

    public int Cantidad { get; set; }

    public decimal PrecioUnitario { get; set; }

    public decimal Subtotal { get; set; }
}
