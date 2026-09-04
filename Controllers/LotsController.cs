using BloomCast.Data;
using BloomCast.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace BloomCast.Controllers
{
    [Authorize(Roles = "Administrador")]
    public class LotsController : Controller
    {
        private readonly BloomCastDbContext _db;

        public LotsController(BloomCastDbContext db)
        {
            _db = db;
        }

        public async Task<IActionResult> Index(
            string? search,
            string? estado)
        {
            var hoy = DateTime.UtcNow.Date;

            var limiteCritico = hoy.AddDays(8);
            var limiteProximo = hoy.AddDays(31);

            var query = _db.Lotes
                .Include(l => l.Orquidea)
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim().ToLower();

                query = query.Where(l =>
                    l.CodigoLote.ToLower().Contains(search) ||
                    l.Orquidea!.Variedad.ToLower().Contains(search) ||
                    l.Orquidea.Tipo.ToLower().Contains(search));
            }

            if (!string.IsNullOrWhiteSpace(estado))
            {
                switch (estado.ToLower())
                {
                    case "agotado":

                        query = query.Where(l =>
                            l.CantidadActual <= 0);

                        break;


                    case "vencido":

                        query = query.Where(l =>
                            l.CantidadActual > 0 &&
                            l.FechaCaducidad.HasValue &&
                            l.FechaCaducidad.Value < hoy);

                        break;


                    case "critico":

                        query = query.Where(l =>
                            l.CantidadActual > 0 &&
                            l.FechaCaducidad.HasValue &&
                            l.FechaCaducidad.Value >= hoy &&
                            l.FechaCaducidad.Value < limiteCritico);

                        break;


                    case "proximo":

                        query = query.Where(l =>
                            l.CantidadActual > 0 &&
                            l.FechaCaducidad.HasValue &&
                            l.FechaCaducidad.Value >= limiteCritico &&
                            l.FechaCaducidad.Value < limiteProximo);

                        break;


                    case "estable":

                        query = query.Where(l =>
                            l.CantidadActual > 0 &&
                            l.FechaCaducidad.HasValue &&
                            l.FechaCaducidad.Value >= limiteProximo);

                        break;


                    case "sinfecha":

                        query = query.Where(l =>
                            l.CantidadActual > 0 &&
                            !l.FechaCaducidad.HasValue);

                        break;
                }
            }


            var lotes = await query
                .OrderBy(l => l.FechaCaducidad)
                .ThenByDescending(l => l.FechaIngreso)
                .ToListAsync();


            ViewBag.Search = search;
            ViewBag.Estado = estado;

            return View(lotes);
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            await CargarOrquideasAsync();

            return View(new LoteCreateViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(LoteCreateViewModel vm)
        {
            await CargarOrquideasAsync(vm.OrquideaId);

            if (!ModelState.IsValid)
            {
                return View(vm);
            }

            var orquidea = await _db.Orquideas
                .FirstOrDefaultAsync(o => o.Id == vm.OrquideaId);

            if (orquidea == null)
            {
                ModelState.AddModelError(
                    nameof(vm.OrquideaId),
                    "La orquídea seleccionada no existe."
                );

                return View(vm);
            }

            if (orquidea.DiasVidaComercial <= 0)
            {
                ModelState.AddModelError(
                    nameof(vm.OrquideaId),
                    "La orquídea seleccionada no tiene configurada una vida comercial válida."
                );

                return View(vm);
            }

            var fechaIngreso = DateTime.UtcNow;

            try
            {
                var codigoLote = await GenerarCodigoLoteAsync(
                    orquidea.Id,
                    fechaIngreso
                );

                var fechaCaducidad = fechaIngreso
                    .AddDays(orquidea.DiasVidaComercial);

                var lote = new Lote
                {
                    OrquideaId = orquidea.Id,

                    CodigoLote = codigoLote,

                    CantidadInicial = vm.CantidadActual,
                    CantidadActual = vm.CantidadActual,

                    FechaIngreso = fechaIngreso,

                    FechaFloracion = fechaIngreso,

                    FechaCaducidad = fechaCaducidad
                };

                _db.Lotes.Add(lote);

                await _db.SaveChangesAsync();

                TempData["Success"] =
                    $"Lote {lote.CodigoLote} registrado correctamente. " +
                    $"Caducidad estimada: {lote.FechaCaducidad:dd/MM/yyyy}.";

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(
                    "",
                    $"No se pudo registrar el lote: {ex.Message}"
                );

                return View(vm);
            }
        }
        private async Task<string> GenerarCodigoLoteAsync(
            int orquideaId,
            DateTime fecha)
        {
            var prefijo =
                $"LOT-{fecha:yyyy-MM}-{orquideaId}-";

            var codigosExistentes = await _db.Lotes
                .Where(l => l.CodigoLote.StartsWith(prefijo))
                .Select(l => l.CodigoLote)
                .ToListAsync();

            var ultimoCorrelativo = codigosExistentes
                .Select(codigo =>
                {
                    var partes = codigo.Split('-');

                    if (partes.Length == 5 &&
                        int.TryParse(partes[4], out var numero))
                    {
                        return numero;
                    }

                    return -1;
                })
                .DefaultIfEmpty(-1)
                .Max();

            var siguienteCorrelativo =
                ultimoCorrelativo + 1;

            return $"{prefijo}{siguienteCorrelativo}";
        }
        private async Task CargarOrquideasAsync(
            int? seleccionado = null)
        {
            var orquideas = await _db.Orquideas
                .AsNoTracking()
                .OrderBy(o => o.Variedad)
                .ToListAsync();

            ViewBag.Orquideas = new SelectList(
                orquideas,
                "Id",
                "Variedad",
                seleccionado
            );
        }
    }
}