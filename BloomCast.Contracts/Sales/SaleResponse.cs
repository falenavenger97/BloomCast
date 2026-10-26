using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BloomCast.Contracts.Sales;

public class SaleResponse
{
    public int Id { get; set; }

    public int UsuarioId { get; set; }

    public string UsuarioNombre { get; set; } = "";

    public DateTime FechaVenta { get; set; }

    public decimal Total { get; set; }
}