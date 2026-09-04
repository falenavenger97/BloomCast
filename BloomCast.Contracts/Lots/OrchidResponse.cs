using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BloomCast.Contracts.Lots;

public class OrchidResponse
{
    public int Id { get; set; }

    public string Variedad { get; set; } = "";

    public string Tipo { get; set; } = "";

    public decimal PrecioBase { get; set; }

    public int DiasVidaComercial { get; set; }
}
