using BloomCast.Api.Data;
using BloomCast.Contracts.Lots;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BloomCast.Api.Controllers;


[ApiController]
[Route("api/lots")]
public class LotsController : ControllerBase
{
    private readonly BloomCastDbContext _db;


    public LotsController(
        BloomCastDbContext db)
    {
        _db = db;
    }


    // ============================================================
    // GET /api/lots
    //
    // GET /api/lots?search=Mini
    // GET /api/lots?estado=critico
    // GET /api/lots?search=Phalaenopsis&estado=estable
    // ============================================================

    [HttpGet]
    public async Task<ActionResult<List<LotResponse>>> GetAll(
        [FromQuery] string? search,
        [FromQuery] string? estado)
    {
        var hoy = DateTime.UtcNow.Date;

        var limiteCritico =
            hoy.AddDays(8);

        var limiteProximo =
            hoy.AddDays(31);


        var query = _db.Lotes
            .Include(l => l.Orquidea)
            .AsNoTracking()
            .AsQueryable();


        // ========================================================
        // Search
        // ========================================================

        if (!string.IsNullOrWhiteSpace(search))
        {
            search =
                search.Trim().ToLower();

            query = query.Where(l =>

                l.CodigoLote
                    .ToLower()
                    .Contains(search)

                ||

                l.Orquidea!.Variedad
                    .ToLower()
                    .Contains(search)

                ||

                l.Orquidea.Tipo
                    .ToLower()
                    .Contains(search)
            );
        }


        // ========================================================
        // Estado FEFO
        // ========================================================

        if (!string.IsNullOrWhiteSpace(estado))
        {
            switch (estado.ToLower())
            {
                case "agotado":

                    query = query.Where(l =>
                        l.CantidadActual <= 0
                    );

                    break;


                case "vencido":

                    query = query.Where(l =>
                        l.CantidadActual > 0
                        &&
                        l.FechaCaducidad.HasValue
                        &&
                        l.FechaCaducidad.Value < hoy
                    );

                    break;


                case "critico":

                    query = query.Where(l =>
                        l.CantidadActual > 0
                        &&
                        l.FechaCaducidad.HasValue
                        &&
                        l.FechaCaducidad.Value >= hoy
                        &&
                        l.FechaCaducidad.Value < limiteCritico
                    );

                    break;


                case "proximo":

                    query = query.Where(l =>
                        l.CantidadActual > 0
                        &&
                        l.FechaCaducidad.HasValue
                        &&
                        l.FechaCaducidad.Value >= limiteCritico
                        &&
                        l.FechaCaducidad.Value < limiteProximo
                    );

                    break;


                case "estable":

                    query = query.Where(l =>
                        l.CantidadActual > 0
                        &&
                        l.FechaCaducidad.HasValue
                        &&
                        l.FechaCaducidad.Value >= limiteProximo
                    );

                    break;


                case "sinfecha":

                    query = query.Where(l =>
                        l.CantidadActual > 0
                        &&
                        !l.FechaCaducidad.HasValue
                    );

                    break;
            }
        }


        // ========================================================
        // Query
        // ========================================================

        var lotes = await query
            .OrderBy(l => l.FechaCaducidad)
            .ThenByDescending(l => l.FechaIngreso)
            .ToListAsync();


        // ========================================================
        // Entity -> DTO
        // ========================================================

        var result = lotes
            .Select(l => MapLot(l, hoy))
            .ToList();


        return Ok(result);
    }


    // ============================================================
    // Mapper
    // ============================================================

    private static LotResponse MapLot(
        BloomCast.Api.Models.Lote lote,
        DateTime hoy)
    {
        int? diasRestantes = null;

        string estado;


        if (lote.CantidadActual <= 0)
        {
            estado = "Agotado";
        }
        else if (!lote.FechaCaducidad.HasValue)
        {
            estado = "Sin fecha";
        }
        else
        {
            diasRestantes =
                (lote.FechaCaducidad.Value.Date - hoy)
                .Days;


            if (diasRestantes < 0)
            {
                estado = "Vencido";
            }
            else if (diasRestantes <= 7)
            {
                estado = "Crítico";
            }
            else if (diasRestantes <= 30)
            {
                estado = "Próximo";
            }
            else
            {
                estado = "Estable";
            }
        }


        return new LotResponse
        {
            Id =
                lote.Id,

            CodigoLote =
                lote.CodigoLote,

            OrquideaId =
                lote.OrquideaId,

            Variedad =
                lote.Orquidea?.Variedad ?? "",

            Tipo =
                lote.Orquidea?.Tipo ?? "",

            CantidadInicial =
                lote.CantidadInicial,

            CantidadActual =
                lote.CantidadActual,

            FechaIngreso =
                lote.FechaIngreso,

            FechaFloracion =
                lote.FechaFloracion,

            FechaCaducidad =
                lote.FechaCaducidad,

            DiasRestantes =
                diasRestantes,

            Estado =
                estado
        };
    }
    // ============================================================
    // GET /api/lots/orchids
    // ============================================================

    [HttpGet("orchids")]
    public async Task<ActionResult<List<OrchidResponse>>> GetOrchids()
    {
        var orquideas = await _db.Orquideas
            .AsNoTracking()
            .OrderBy(o => o.Variedad)
            .Select(o => new OrchidResponse
            {
                Id = o.Id,
                Variedad = o.Variedad,
                Tipo = o.Tipo,
                PrecioBase = o.PrecioBase,
                DiasVidaComercial = o.DiasVidaComercial
            })
            .ToListAsync();

        return Ok(orquideas);
    }
    // ============================================================
    // POST /api/lots
    // ============================================================

    [HttpPost]
    public async Task<ActionResult<LotResponse>> Create(
        [FromBody] CreateLotRequest request)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var orquidea = await _db.Orquideas
            .FirstOrDefaultAsync(o => o.Id == request.OrquideaId);

        if (orquidea == null)
        {
            return BadRequest(new
            {
                message = "La orquídea seleccionada no existe."
            });
        }

        if (orquidea.DiasVidaComercial <= 0)
        {
            return BadRequest(new
            {
                message =
                    "La orquídea seleccionada no tiene una vida comercial válida."
            });
        }

        var fechaIngreso = DateTime.UtcNow;

        var codigoLote = await GenerarCodigoLoteAsync(
            orquidea.Id,
            fechaIngreso
        );

        var fechaCaducidad =
            fechaIngreso.AddDays(
                orquidea.DiasVidaComercial
            );

        var lote = new BloomCast.Api.Models.Lote
        {
            OrquideaId = orquidea.Id,

            CodigoLote = codigoLote,

            CantidadInicial =
                request.CantidadActual,

            CantidadActual =
                request.CantidadActual,

            FechaIngreso =
                fechaIngreso,

            FechaFloracion =
                fechaIngreso,

            FechaCaducidad =
                fechaCaducidad
        };

        _db.Lotes.Add(lote);

        await _db.SaveChangesAsync();

        // Para poder construir el DTO completo
        lote.Orquidea = orquidea;

        var response =
            MapLot(lote, DateTime.UtcNow.Date);

        return CreatedAtAction(
            nameof(GetAll),
            new { },
            response
        );
    }
    private async Task<string> GenerarCodigoLoteAsync(
    int orquideaId,
    DateTime fecha)
    {
        var prefijo =
            $"LOT-{fecha:yyyy-MM}-{orquideaId}-";

        var codigosExistentes =
            await _db.Lotes
                .Where(l =>
                    l.CodigoLote.StartsWith(prefijo))
                .Select(l => l.CodigoLote)
                .ToListAsync();

        var ultimoCorrelativo =
            codigosExistentes
                .Select(codigo =>
                {
                    var partes =
                        codigo.Split('-');

                    if (partes.Length == 5 &&
                        int.TryParse(
                            partes[4],
                            out var numero))
                    {
                        return numero;
                    }

                    return -1;
                })
                .DefaultIfEmpty(-1)
                .Max();

        var siguienteCorrelativo =
            ultimoCorrelativo + 1;

        return
            $"{prefijo}{siguienteCorrelativo}";
    }
}