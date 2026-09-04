using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using BloomCast.Data;
using BloomCast.Models;

namespace BloomCast.Controllers
{
    [Authorize(Roles = "Administrador")]
    public class UsersController : Controller
    {
        private readonly BloomCastDbContext _db;

        public UsersController(BloomCastDbContext db)
        {
            _db = db;
        }

        // GET /Users
        public async Task<IActionResult> Index(string? search, int? rolId, bool? activo)
        {
            var query = _db.Usuarios.Include(u => u.Rol).AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(u =>
                    u.NombreCompleto.ToLower().Contains(search.ToLower()) ||
                    u.Correo.ToLower().Contains(search.ToLower()));

            if (rolId.HasValue)
                query = query.Where(u => u.RolId == rolId.Value);

            if (activo.HasValue)
                query = query.Where(u => u.Activo == activo.Value);

            var usuarios = await query.OrderBy(u => u.NombreCompleto).ToListAsync();
            var roles = await _db.Roles.OrderBy(r => r.Nombre).ToListAsync();

            ViewBag.Search = search;
            ViewBag.RolId = rolId;
            ViewBag.Activo = activo;
            ViewBag.Roles = new SelectList(roles, "Id", "Nombre", rolId);

            return View(usuarios);
        }

        // GET /Users/Create
        public async Task<IActionResult> Create()
        {
            await CargarRolesAsync();
            return View(new UserCreateViewModel());
        }

        // POST /Users/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(UserCreateViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                await CargarRolesAsync();
                return View(vm);
            }

            if (await _db.Usuarios.AnyAsync(u => u.Correo == vm.Correo))
            {
                ModelState.AddModelError("Correo", "Ya existe un usuario con ese correo.");
                await CargarRolesAsync();
                return View(vm);
            }

            var usuario = new Usuario
            {
                NombreCompleto = vm.NombreCompleto.Trim(),
                Correo = vm.Correo.Trim().ToLower(),
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(vm.Password),
                RolId = vm.RolId,
                Activo = vm.Activo,
                MfaHabilitado = false
            };

            _db.Usuarios.Add(usuario);
            await _db.SaveChangesAsync();

            TempData["Success"] = $"Usuario <strong>{usuario.NombreCompleto}</strong> creado correctamente.";
            return RedirectToAction(nameof(Index));
        }

        // GET /Users/Edit/5
        public async Task<IActionResult> Edit(int id)
        {
            var usuario = await _db.Usuarios.Include(u => u.Rol).FirstOrDefaultAsync(u => u.Id == id);
            if (usuario == null) return NotFound();

            await CargarRolesAsync(usuario.RolId);

            var vm = new UserEditViewModel
            {
                Id = usuario.Id,
                NombreCompleto = usuario.NombreCompleto,
                Correo = usuario.Correo,
                RolId = usuario.RolId,
                Activo = usuario.Activo,
                MfaHabilitado = usuario.MfaHabilitado
            };

            return View(vm);
        }

        // POST /Users/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, UserEditViewModel vm)
        {
            if (id != vm.Id) return BadRequest();

            if (!ModelState.IsValid)
            {
                await CargarRolesAsync(vm.RolId);
                return View(vm);
            }

            var usuario = await _db.Usuarios.FirstOrDefaultAsync(u => u.Id == id);
            if (usuario == null) return NotFound();

            // Check correo duplicado (excluyendo al mismo usuario)
            if (await _db.Usuarios.AnyAsync(u => u.Correo == vm.Correo.Trim().ToLower() && u.Id != id))
            {
                ModelState.AddModelError("Correo", "Ya existe otro usuario con ese correo.");
                await CargarRolesAsync(vm.RolId);
                return View(vm);
            }

            usuario.NombreCompleto = vm.NombreCompleto.Trim();
            usuario.Correo = vm.Correo.Trim().ToLower();
            usuario.RolId = vm.RolId;
            usuario.Activo = vm.Activo;

            // Solo cambiar password si se proporcionó una nueva
            if (!string.IsNullOrWhiteSpace(vm.NuevaPassword))
                usuario.PasswordHash = BCrypt.Net.BCrypt.HashPassword(vm.NuevaPassword);

            // Resetear MFA si se solicitó
            if (vm.ResetearMfa)
            {
                usuario.MfaSecret = null;
                usuario.MfaHabilitado = false;
            }

            await _db.SaveChangesAsync();

            TempData["Success"] = $"Usuario <strong>{usuario.NombreCompleto}</strong> actualizado correctamente.";
            return RedirectToAction(nameof(Index));
        }

        // POST /Users/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var usuario = await _db.Usuarios.FirstOrDefaultAsync(u => u.Id == id);

            if (usuario == null)
                return NotFound();

            var currentUserId = int.Parse(
                User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

            if (usuario.Id == currentUserId)
            {
                TempData["Error"] = "No puedes eliminar tu propio usuario.";
                return RedirectToAction(nameof(Index));
            }

            usuario.Activo = false;

            await _db.SaveChangesAsync();

            TempData["Success"] =
                $"Usuario <strong>{usuario.NombreCompleto}</strong> desactivado correctamente.";

            return RedirectToAction(nameof(Index));
        }

        // ── helpers ──────────────────────────────────────────────────────────
        private async Task CargarRolesAsync(int? selectedId = null)
        {
            var roles = await _db.Roles.OrderBy(r => r.Nombre).ToListAsync();
            ViewBag.Roles = new SelectList(roles, "Id", "Nombre", selectedId);
        }
    }
}