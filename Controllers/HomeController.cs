using BloomCast.Models;
using BloomCast.Services.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BloomCast.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly IDashboardApiService
            _dashboardApi;


        public HomeController(
            IDashboardApiService dashboardApi)
        {
            _dashboardApi =
                dashboardApi;
        }


        // =====================================================
        // GET: /
        // GET: /Home
        // GET: /Home/Index
        // =====================================================

        public async Task<IActionResult> Index()
        {
            try
            {
                var dashboard =
                    await _dashboardApi
                        .GetDashboardAsync();


                var vm =
                    new DashboardViewModel
                    {
                        // Información del usuario:
                        // sigue siendo responsabilidad
                        // del frontend por ahora.

                        NombreUsuario =
                            User.FindFirstValue(
                                ClaimTypes.Name
                            )
                            ?? "Usuario",

                        RolNombre =
                            User.FindFirstValue(
                                "RolNombre"
                            )
                            ?? "",


                        // Datos provenientes de API

                        TotalOrquideas =
                            dashboard.TotalOrquideas,

                        TotalLotes =
                            dashboard.TotalLotes,

                        VentasHoy =
                            dashboard.VentasHoy,

                        LotesCriticos =
                            dashboard.LotesCriticos,

                        Predicciones =
                            dashboard.Predicciones,

                        LotesPorCaducar =
                            dashboard.LotesPorCaducar
                    };


                return View(vm);
            }
            catch (HttpRequestException ex)
            {
                TempData["Error"] =
                    $"No se pudo cargar el dashboard: {ex.Message}";


                var vm =
                    new DashboardViewModel
                    {
                        NombreUsuario =
                            User.FindFirstValue(
                                ClaimTypes.Name
                            )
                            ?? "Usuario",

                        RolNombre =
                            User.FindFirstValue(
                                "RolNombre"
                            )
                            ?? ""
                    };


                return View(vm);
            }
        }
    }
}