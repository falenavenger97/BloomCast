using BloomCast.Contracts.Users;
using BloomCast.Models;
using BloomCast.Services.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Security.Claims;

namespace BloomCast.Controllers
{
    [Authorize(Roles = "Administrador")]
    public class UsersController : Controller
    {
        private readonly IUsersApiService
            _usersApi;


        public UsersController(
            IUsersApiService usersApi)
        {
            _usersApi =
                usersApi;
        }


        // =========================================================
        // GET /Users
        // =========================================================

        public async Task<IActionResult> Index(
            string? search,
            int? rolId,
            bool? activo)
        {
            try
            {
                var usuarios =
                    await _usersApi.GetUsersAsync(
                        search,
                        rolId,
                        activo
                    );


                var roles =
                    await _usersApi.GetRolesAsync();


                ViewBag.Search =
                    search;

                ViewBag.RolId =
                    rolId;

                ViewBag.Activo =
                    activo;

                ViewBag.Roles =
                    new SelectList(
                        roles,
                        "Id",
                        "Nombre",
                        rolId
                    );


                return View(
                    usuarios
                );
            }
            catch (HttpRequestException ex)
            {
                TempData["Error"] =
                    $"No se pudieron cargar los usuarios: {ex.Message}";


                ViewBag.Search =
                    search;

                ViewBag.RolId =
                    rolId;

                ViewBag.Activo =
                    activo;


                ViewBag.Roles =
                    new SelectList(
                        Array.Empty<RoleResponse>(),
                        "Id",
                        "Nombre"
                    );


                return View(
                    new List<UserResponse>()
                );
            }
        }


        // =========================================================
        // GET /Users/Create
        // =========================================================

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            try
            {
                await CargarRolesAsync();

                return View(
                    new UserCreateViewModel()
                );
            }
            catch (HttpRequestException ex)
            {
                TempData["Error"] =
                    $"No se pudieron cargar los roles: {ex.Message}";

                return RedirectToAction(
                    nameof(Index)
                );
            }
        }


        // =========================================================
        // POST /Users/Create
        // =========================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            UserCreateViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                await CargarRolesSeguroAsync(
                    vm.RolId
                );

                return View(vm);
            }


            try
            {
                var request =
                    new CreateUserRequest
                    {
                        NombreCompleto =
                            vm.NombreCompleto,

                        Correo =
                            vm.Correo,

                        Password =
                            vm.Password,

                        RolId =
                            vm.RolId,

                        Activo =
                            vm.Activo
                    };


                var usuario =
                    await _usersApi.CreateUserAsync(
                        request
                    );


                TempData["Success"] =
                    $"Usuario <strong>" +
                    $"{usuario.NombreCompleto}" +
                    $"</strong> creado correctamente.";


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


                await CargarRolesSeguroAsync(
                    vm.RolId
                );


                return View(vm);
            }
        }


        // =========================================================
        // GET /Users/Edit/{id}
        // =========================================================

        [HttpGet]
        public async Task<IActionResult> Edit(
            int id)
        {
            try
            {
                var usuario =
                    await _usersApi.GetUserByIdAsync(
                        id
                    );


                if (usuario == null)
                {
                    return NotFound();
                }


                await CargarRolesAsync(
                    usuario.RolId
                );


                var vm =
                    new UserEditViewModel
                    {
                        Id =
                            usuario.Id,

                        NombreCompleto =
                            usuario.NombreCompleto,

                        Correo =
                            usuario.Correo,

                        RolId =
                            usuario.RolId,

                        Activo =
                            usuario.Activo,

                        MfaHabilitado =
                            usuario.MfaHabilitado
                    };


                return View(vm);
            }
            catch (HttpRequestException ex)
            {
                TempData["Error"] =
                    $"No se pudo cargar el usuario: {ex.Message}";

                return RedirectToAction(
                    nameof(Index)
                );
            }
        }


        // =========================================================
        // POST /Users/Edit/{id}
        // =========================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(
            int id,
            UserEditViewModel vm)
        {
            if (id != vm.Id)
            {
                return BadRequest();
            }


            if (!ModelState.IsValid)
            {
                await CargarRolesSeguroAsync(
                    vm.RolId
                );

                return View(vm);
            }


            try
            {
                var request =
                    new UpdateUserRequest
                    {
                        NombreCompleto =
                            vm.NombreCompleto,

                        Correo =
                            vm.Correo,

                        RolId =
                            vm.RolId,

                        Activo =
                            vm.Activo,

                        NuevaPassword =
                            string.IsNullOrWhiteSpace(
                                vm.NuevaPassword
                            )
                                ? null
                                : vm.NuevaPassword,

                        ResetearMfa =
                            vm.ResetearMfa
                    };


                var usuario =
                    await _usersApi.UpdateUserAsync(
                        id,
                        request
                    );


                TempData["Success"] =
                    $"Usuario <strong>" +
                    $"{usuario.NombreCompleto}" +
                    $"</strong> actualizado correctamente.";


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


                await CargarRolesSeguroAsync(
                    vm.RolId
                );


                return View(vm);
            }
        }


        // =========================================================
        // POST /Users/Delete/{id}
        // =========================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(
            int id)
        {
            // =====================================================
            // Protección temporal frontend:
            // impedir desactivar el usuario actualmente autenticado.
            //
            // La API hará esta validación definitivamente cuando
            // implementemos JWT.
            // =====================================================

            var currentUserClaim =
                User.FindFirstValue(
                    ClaimTypes.NameIdentifier
                );


            if (int.TryParse(
                    currentUserClaim,
                    out var currentUserId)
                &&
                currentUserId == id)
            {
                TempData["Error"] =
                    "No puedes desactivar tu propio usuario.";

                return RedirectToAction(
                    nameof(Index)
                );
            }


            try
            {
                await _usersApi.DeactivateUserAsync(
                    id
                );


                TempData["Success"] =
                    "Usuario desactivado correctamente.";


                return RedirectToAction(
                    nameof(Index)
                );
            }
            catch (HttpRequestException ex)
            {
                TempData["Error"] =
                    ex.Message;


                return RedirectToAction(
                    nameof(Index)
                );
            }
        }


        // =========================================================
        // Helpers
        // =========================================================

        private async Task CargarRolesAsync(
            int? selectedId = null)
        {
            var roles =
                await _usersApi.GetRolesAsync();


            ViewBag.Roles =
                new SelectList(
                    roles,
                    "Id",
                    "Nombre",
                    selectedId
                );
        }


        private async Task CargarRolesSeguroAsync(
            int? selectedId = null)
        {
            try
            {
                await CargarRolesAsync(
                    selectedId
                );
            }
            catch
            {
                ViewBag.Roles =
                    new SelectList(
                        Array.Empty<RoleResponse>(),
                        "Id",
                        "Nombre"
                    );
            }
        }
    }
}