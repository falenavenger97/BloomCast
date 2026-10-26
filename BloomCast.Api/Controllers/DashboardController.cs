using BloomCast.Api.Data;
using BloomCast.Contracts.Dashboard;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;

namespace BloomCast.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly BloomCastDbContext _db;


    public DashboardController(
        BloomCastDbContext db)
    {
        _db = db;
    }


    // ============================================================
    // GET /api/dashboard
    // ============================================================

    [HttpGet]
    public async Task<ActionResult<DashboardResponse>> Get(
        CancellationToken cancellationToken)
    {
        var hoy =
            DateTime.UtcNow.Date;

        var limite30Dias =
            hoy.AddDays(31);

        var limiteCritico =
            hoy.AddDays(8);


        // ========================================================
        // Predicciones
        // ========================================================

        var predicciones =
            await _db.PrediccionesDemanda

                .Include(p =>
                    p.Orquidea)

                .AsNoTracking()

                .OrderBy(p =>
                    p.FechaObjetivo)

                .Select(p =>
                    new DashboardPredictionResponse
                    {
                        Id =
                            p.Id,

                        OrquideaId =
                            p.OrquideaId,

                        Variedad =
                            p.Orquidea != null
                                ? p.Orquidea.Variedad
                                : "",

                        FechaObjetivo =
                            p.FechaObjetivo,

                        CantidadEstimada =
                            p.CantidadEstimada
                    })

                .ToListAsync(
                    cancellationToken
                );


        // ========================================================
        // Lotes próximos a caducar
        // ========================================================

        var lotes =
            await _db.Lotes

                .Include(l =>
                    l.Orquidea)

                .AsNoTracking()

                .Where(l =>
                    l.CantidadActual > 0
                    &&
                    l.FechaCaducidad.HasValue
                    &&
                    l.FechaCaducidad.Value >= hoy
                    &&
                    l.FechaCaducidad.Value < limite30Dias
                )

                .OrderBy(l =>
                    l.FechaCaducidad)

                .Take(12)

                .Select(l =>
                    new
                    {
                        l.Id,

                        l.CodigoLote,

                        l.OrquideaId,

                        Variedad =
                            l.Orquidea != null
                                ? l.Orquidea.Variedad
                                : "",
                        Tipo =
                            l.Orquidea != null
                                ? l.Orquidea.Tipo
                                : "",

                        l.CantidadActual,

                        l.FechaCaducidad
                    })

                .ToListAsync(
                    cancellationToken
                );


        var lotesPorCaducar =
            lotes.Select(l =>
            {
                var dias =
                    l.FechaCaducidad.HasValue
                        ? (l.FechaCaducidad.Value.Date - hoy).Days
                        : (int?)null;


                return new DashboardLotResponse
                {
                    Id =
                        l.Id,

                    CodigoLote =
                        l.CodigoLote,

                    OrquideaId =
                        l.OrquideaId,

                    Variedad =
                        l.Variedad,

                    CantidadActual =
                        l.CantidadActual,

                    FechaCaducidad =
                        l.FechaCaducidad,

                    DiasRestantes =
                        dias,

                    EsCritico =
                        dias.HasValue &&
                        dias.Value >= 0 &&
                        dias.Value <= 7
                };
            })
            .ToList();


        // ========================================================
        // Métricas
        // ========================================================

        var totalOrquideas =
            await _db.Orquideas
                .AsNoTracking()
                .CountAsync(
                    cancellationToken
                );


        var totalLotes =
            await _db.Lotes
                .AsNoTracking()
                .CountAsync(
                    l =>
                        l.CantidadActual > 0
                        &&
                        l.FechaCaducidad.HasValue
                        &&
                        l.FechaCaducidad.Value >= hoy,
                    cancellationToken
                );


        var manana =
            hoy.AddDays(1);


        var ventasHoy =
            await _db.Ventas
                .AsNoTracking()
                .Where(v =>
                    v.FechaVenta >= hoy
                    &&
                    v.FechaVenta < manana
                )
                .SumAsync(
                    v => (decimal?)v.Total,
                    cancellationToken
                )
            ?? 0;


        var lotesCriticos =
            await _db.Lotes
                .AsNoTracking()
                .CountAsync(
                    l =>
                        l.CantidadActual > 0
                        &&
                        l.FechaCaducidad.HasValue
                        &&
                        l.FechaCaducidad.Value >= hoy
                        &&
                        l.FechaCaducidad.Value < limiteCritico,
                    cancellationToken
                );


        // ========================================================
        // Response
        // ========================================================

        var response =
            new DashboardResponse
            {
                TotalOrquideas =
                    totalOrquideas,

                TotalLotes =
                    totalLotes,

                VentasHoy =
                    ventasHoy,

                LotesCriticos =
                    lotesCriticos,

                Predicciones =
                    predicciones,

                LotesPorCaducar =
                    lotesPorCaducar
            };


        return Ok(response);
    }
}