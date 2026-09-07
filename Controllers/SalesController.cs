using BloomCast.Contracts.Sales;
using BloomCast.Models;
using BloomCast.Services.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BloomCast.Controllers
{
    [Authorize]
    public class SalesController : Controller
    {
        private readonly ISalesApiService _salesApi;

        public SalesController(
            ISalesApiService salesApi)
        {
            _salesApi = salesApi;
        }


        // =========================================================
        // GET: /Sales
        // =========================================================

        public async Task<IActionResult> Index(
            string? search,
            DateTime? fecha)
        {
            try
            {
                var ventas =
                    await _salesApi.GetSalesAsync(
                        search,
                        fecha
                    );

                ViewBag.Search = search;

                ViewBag.Fecha =
                    fecha?.ToString("yyyy-MM-dd");

                return View(ventas);
            }
            catch (HttpRequestException ex)
            {
                TempData["Error"] =
                    $"No se pudieron cargar las ventas: {ex.Message}";

                ViewBag.Search = search;

                ViewBag.Fecha =
                    fecha?.ToString("yyyy-MM-dd");

                return View(
                    new List<SaleResponse>()
                );
            }
        }


        // =========================================================
        // GET: /Sales/Create
        // =========================================================

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            try
            {
                await CargarDatosVentaAsync();

                var vm =
                    new SaleCreateViewModel
                    {
                        Fechaventa =
                            DateTime.Now,

                        Items =
                            new List<SaleItemViewModel>
                            {
                                new SaleItemViewModel
                                {
                                    Cantidad = 1
                                }
                            }
                    };

                return View(vm);
            }
            catch (HttpRequestException ex)
            {
                TempData["Error"] =
                    $"No se pudieron cargar los datos de la venta: {ex.Message}";

                return RedirectToAction(
                    nameof(Index)
                );
            }
        }


        // =========================================================
        // POST: /Sales/Create
        // =========================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            SaleCreateViewModel vm)
        {
            vm.Items =
                vm.Items?
                    .Where(x =>
                        x.LoteId > 0 &&
                        x.Cantidad > 0)
                    .ToList()
                ?? new List<SaleItemViewModel>();


            if (!vm.Items.Any())
            {
                ModelState.AddModelError(
                    "Items",
                    "Debes agregar al menos un producto válido."
                );
            }


            if (!ModelState.IsValid)
            {
                await CargarDatosVentaSeguroAsync();

                return View(vm);
            }


            try
            {
                var request =
                    new CreateSaleRequest
                    {
                        UsuarioId =
                            vm.UsuarioId,

                        FechaVenta =
                            vm.Fechaventa,

                        Items =
                            vm.Items
                                .Select(item =>
                                    new CreateSaleItemRequest
                                    {
                                        LoteId =
                                            item.LoteId,

                                        Cantidad =
                                            item.Cantidad,

                                        PrecioUnitario =
                                            item.PrecioUnitario
                                    })
                                .ToList()
                    };


                var venta =
                    await _salesApi.CreateSaleAsync(
                        request
                    );


                TempData["Success"] =
                    $"Venta #{venta.Id} registrada correctamente. " +
                    $"Total: Bs {venta.Total:N2}";


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

                await CargarDatosVentaSeguroAsync();

                return View(vm);
            }
        }


        // =========================================================
        // POST: /Sales/DeleteWithPassword
        // =========================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteWithPassword(
            int id,
            string password)
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                return Json(new
                {
                    success = false,
                    message =
                        "Debes ingresar la contraseña de administrador."
                });
            }


            try
            {
                await _salesApi.DeleteSaleAsync(
                    id,
                    password
                );


                return Json(new
                {
                    success = true,

                    message =
                        $"Venta #{id} eliminada correctamente. " +
                        $"Stock restaurado."
                });
            }
            catch (HttpRequestException ex)
            {
                return Json(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }


        // =========================================================
        // GET: /Sales/GetVentaDetails/{id}
        // =========================================================

        [HttpGet]
        public async Task<IActionResult> GetVentaDetails(
            int id)
        {
            try
            {
                var venta =
                    await _salesApi.GetSaleByIdAsync(id);


                if (venta == null)
                {
                    return Json(new
                    {
                        success = false,
                        message =
                            "Venta no encontrada."
                    });
                }


                return Json(new
                {
                    success = true,

                    venta = new
                    {
                        id =
                            venta.Id,

                        fechaVenta =
                            venta.FechaVenta,

                        usuario =
                            venta.UsuarioNombre,

                        total =
                            venta.Total
                    },

                    detalles =
                        venta.Detalles.Select(d => new
                        {
                            producto =
                                d.Producto,

                            codigoLote =
                                d.CodigoLote,

                            cantidad =
                                d.Cantidad,

                            precioUnitario =
                                d.PrecioUnitario,

                            subtotal =
                                d.Subtotal
                        })
                });
            }
            catch (HttpRequestException ex)
            {
                return Json(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }


        // =========================================================
        // Helpers
        // =========================================================

        private async Task CargarDatosVentaAsync()
        {
            var usuarios =
                await _salesApi.GetUsersAsync();

            var lotes =
                await _salesApi.GetAvailableLotsAsync();


            ViewBag.Usuarios =
                new SelectList(
                    usuarios,
                    "Id",
                    "NombreCompleto"
                );


            ViewBag.LotesList =
                lotes;
        }


        private async Task CargarDatosVentaSeguroAsync()
        {
            try
            {
                await CargarDatosVentaAsync();
            }
            catch
            {
                ViewBag.Usuarios =
                    new SelectList(
                        Array.Empty<SaleUserResponse>(),
                        "Id",
                        "NombreCompleto"
                    );

                ViewBag.LotesList =
                    new List<AvailableSaleLotResponse>();
            }
        }
    }
}