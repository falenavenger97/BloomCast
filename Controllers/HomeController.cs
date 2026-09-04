using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BloomCast.Data;
using BloomCast.Models;
using System.Security.Claims;

namespace BloomCast.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly BloomCastDbContext _db;

        public HomeController(BloomCastDbContext db) => _db = db;

        public async Task<IActionResult> Index()
        {
            var hoy    = DateTime.UtcNow;
            var limite = hoy.AddDays(30);

            // Predicciones con orquídea incluida para mostrar variedad
            var predicciones = await _db.PrediccionesDemanda
                .Include(p => p.Orquidea)
                .OrderBy(p => p.FechaObjetivo)
                .ToListAsync();

            // Lotes que caducan en los próximos 30 días
            var lotesPorCaducar = await _db.Lotes
                .Include(l => l.Orquidea)
                .Where(l => l.FechaCaducidad.HasValue
                         && l.FechaCaducidad.Value <= limite
                         && l.CantidadActual > 0)
                .OrderBy(l => l.FechaCaducidad)
                .Take(12)
                .ToListAsync();

            // Stats
            var totalOrquideas = await _db.Orquideas.CountAsync();
            var totalLotes     = await _db.Lotes.Where(l => l.CantidadActual > 0 && l.FechaCaducidad > hoy.Date).CountAsync(); ;
            var ventasHoy      = await _db.Ventas
                .Where(v => v.FechaVenta.Date == hoy.Date)
                .SumAsync(v => (decimal?)v.Total) ?? 0;
            var lotesCriticos  = lotesPorCaducar.Count(l =>
                (l.FechaCaducidad!.Value - hoy).TotalDays <= 7 && (l.FechaCaducidad!.Value - hoy).TotalDays > 0);

            var vm = new DashboardViewModel
            {
                NombreUsuario  = User.FindFirstValue(ClaimTypes.Name) ?? "Usuario",
                RolNombre      = User.FindFirstValue("RolNombre")     ?? "",
                Predicciones   = predicciones,
                LotesPorCaducar= lotesPorCaducar,
                TotalOrquideas = totalOrquideas,
                TotalLotes     = totalLotes,
                VentasHoy      = ventasHoy,
                LotesCriticos  = lotesCriticos
            };

            return View(vm);
        }
    }
}
