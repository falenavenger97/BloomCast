using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BloomCast.Contracts.Dashboard;

public class DashboardResponse
{
    public int TotalOrquideas { get; set; }

    public int TotalLotes { get; set; }

    public decimal VentasHoy { get; set; }

    public int LotesCriticos { get; set; }

    public List<DashboardPredictionResponse> Predicciones { get; set; }
        = new();

    public List<DashboardLotResponse> LotesPorCaducar { get; set; }
        = new();
}
