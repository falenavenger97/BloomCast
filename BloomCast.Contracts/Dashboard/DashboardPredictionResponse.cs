using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BloomCast.Contracts.Dashboard;

public class DashboardPredictionResponse
{
    public int Id { get; set; }

    public int OrquideaId { get; set; }

    public string Variedad { get; set; } = "";

    public DateTime FechaObjetivo { get; set; }

    public int CantidadEstimada { get; set; }
}
