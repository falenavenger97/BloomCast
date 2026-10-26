using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BloomCast.Contracts.Sales;

public class AvailableSaleLotResponse
{
    public int Id { get; set; }

    public string CodigoLote { get; set; } = "";

    public string Variedad { get; set; } = "";

    public string Tipo { get; set; } = "";

    public string Text { get; set; } = "";

    public decimal PrecioBase { get; set; }

    public int Stock { get; set; }

    public DateTime? FechaCaducidad { get; set; }
}
