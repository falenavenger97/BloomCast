using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BloomCast.Contracts.Dashboard;

public class DashboardLotResponse
{
    public int Id { get; set; }

    public string CodigoLote { get; set; } = "";

    public int OrquideaId { get; set; }

    public string Variedad { get; set; } = "";

    public string Tipo { get; set; } = "";

    public int CantidadActual { get; set; }

    public DateTime? FechaCaducidad { get; set; }

    public int? DiasRestantes { get; set; }

    public bool EsCritico { get; set; }
}
