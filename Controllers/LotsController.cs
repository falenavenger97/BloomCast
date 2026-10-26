using BloomCast.Contracts.Lots;
using BloomCast.Models;
using BloomCast.Services.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BloomCast.Controllers
{
    [Authorize(Roles = "Administrador")]
    public class LotsController : Controller
    {
        private readonly ILotsApiService _lotsApi;


        public LotsController(
            ILotsApiService lotsApi)
        {
            _lotsApi = lotsApi;
        }


        // =========================================================
        // GET /Lots
        // =========================================================

        public async Task<IActionResult> Index(
            string? search,
            string? estado)
        {
            try
            {
                var lotes =
                    await _lotsApi.GetLotsAsync(
                        search,
                        estado
                    );


                ViewBag.Search =
                    search;

                ViewBag.Estado =
                    estado;


                return View(lotes);
            }
            catch (HttpRequestException ex)
            {
                TempData["Error"] =
                    $"No se pudo obtener los lotes: {ex.Message}";


                ViewBag.Search =
                    search;

                ViewBag.Estado =
                    estado;


                return View(
                    new List<LotResponse>()
                );
            }
        }


        // =========================================================
        // GET /Lots/Create
        // =========================================================

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            try
            {
                await CargarOrquideasAsync();

                return View(
                    new LoteCreateViewModel()
                );
            }
            catch (HttpRequestException ex)
            {
                TempData["Error"] =
                    $"No se pudieron cargar las orquídeas: {ex.Message}";

                return RedirectToAction(
                    nameof(Index)
                );
            }
        }


        // =========================================================
        // POST /Lots/Create
        // =========================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            LoteCreateViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                await CargarOrquideasSeguroAsync(
                    vm.OrquideaId
                );

                return View(vm);
            }


            try
            {
                var request =
                    new CreateLotRequest
                    {
                        OrquideaId =
                            vm.OrquideaId,

                        CantidadActual =
                            vm.CantidadActual
                    };


                var lote =
                    await _lotsApi.CreateLotAsync(
                        request
                    );


                TempData["Success"] =
                    $"Lote {lote.CodigoLote} registrado correctamente. " +
                    $"Caducidad estimada: " +
                    $"{lote.FechaCaducidad:dd/MM/yyyy}.";


                return RedirectToAction(
                    nameof(Index)
                );
            }
            catch (HttpRequestException ex)
            {
                ModelState.AddModelError(
                    "",
                    ex.Message
                );


                await CargarOrquideasSeguroAsync(
                    vm.OrquideaId
                );


                return View(vm);
            }
        }


        // =========================================================
        // Orquídeas desde API
        // =========================================================

        private async Task CargarOrquideasAsync(
            int? seleccionado = null)
        {
            var orquideas =
                await _lotsApi.GetOrchidsAsync();


            ViewBag.Orquideas =
                new SelectList(
                    orquideas,
                    "Id",
                    "Variedad",
                    seleccionado
                );
        }


        // =========================================================
        // Recarga segura cuando falla POST
        // =========================================================

        private async Task CargarOrquideasSeguroAsync(
            int? seleccionado = null)
        {
            try
            {
                await CargarOrquideasAsync(
                    seleccionado
                );
            }
            catch
            {
                ViewBag.Orquideas =
                    new SelectList(
                        Array.Empty<OrchidResponse>(),
                        "Id",
                        "Variedad"
                    );
            }
        }
    }
}