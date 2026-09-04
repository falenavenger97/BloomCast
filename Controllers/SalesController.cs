using BloomCast.Data;
using BloomCast.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BloomCast.Controllers
{
    [Authorize]
    public class SalesController : Controller
    {
        private readonly BloomCastDbContext _db;

        public SalesController(BloomCastDbContext db)
        {
            _db = db;
        }

        // GET: /Sales
        public async Task<IActionResult> Index(string search, DateTime? fecha)
        {
            var query = _db.Ventas
                .Include(v => v.Usuario)
                .Include(v => v.Detalles)
                    .ThenInclude(d => d.Lote)
                        .ThenInclude(l => l.Orquidea)
                .AsQueryable();

            // filtro por usuario
            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim().ToLower();

                query = query.Where(v =>
                    v.Usuario!.NombreCompleto.ToLower().Contains(search) ||
                    v.UsuarioId.ToString().Contains(search));
            }

            // filtro por fecha
            if (fecha.HasValue)
            {
                var inicio = fecha.Value.Date.ToUniversalTime();
                var fin = inicio.AddDays(1);

                query = query.Where(v =>
                    v.FechaVenta >= inicio &&
                    v.FechaVenta < fin);
            }

            var ventas = await query
                .OrderByDescending(v => v.Id)
                .ToListAsync();

            ViewBag.Search = search;
            ViewBag.Fecha = fecha?.ToString("yyyy-MM-dd");

            return View(ventas);
        }

        // GET: /Sales/Create
        public async Task<IActionResult> Create()
        {
            // Cargar usuarios activos para el select
            ViewBag.Usuarios = new SelectList(
                await _db.Usuarios.Where(u => u.Activo).ToListAsync(),
                "Id", "NombreCompleto");

            // Cargar lotes disponibles
            var lotes = await _db.Lotes
                .Include(l => l.Orquidea)
                .Where(l => l.CantidadActual > 0)
                .Select(l => new
                {
                    l.Id,
                    Text = $"{l.CodigoLote} - {l.Orquidea!.Variedad} - Stock: {l.CantidadActual}",
                    PrecioBase = l.Orquidea!.PrecioBase,
                    Stock = l.CantidadActual
                })
                .OrderBy(l => l.Id)
                .ToListAsync();

            ViewBag.LotesList = lotes; // Guardar la lista completa

            var vm = new SaleCreateViewModel
            {
                Fechaventa = DateTime.Now,
                Items = new List<SaleItemViewModel>
        {
            new SaleItemViewModel { Cantidad = 1 }
        }
            };

            return View(vm);
        }

        // POST: /Sales/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(SaleCreateViewModel vm)
        {
            // Recargar ViewBags
            ViewBag.Usuarios = new SelectList(
                await _db.Usuarios.Where(u => u.Activo).ToListAsync(),
                "Id", "NombreCompleto");

            ViewBag.Lotes = await _db.Lotes
                .Include(l => l.Orquidea)
                .Where(l => l.CantidadActual > 0)
                .Select(l => new {
                    l.Id,
                    DisplayText = $"{l.CodigoLote} - {l.Orquidea!.Variedad}"
                })
                .ToListAsync();

            // Validación más robusta
            vm.Items = vm.Items?.Where(x => x.LoteId > 0 && x.Cantidad > 0).ToList() ?? new();

            if (!vm.Items.Any())
            {
                ModelState.AddModelError("Items", "Debes agregar al menos un producto válido.");
                return View(vm);
            }

            if (!ModelState.IsValid)
            {
                return View(vm);
            }

            using var trx = await _db.Database.BeginTransactionAsync();

            try
            {
                // Obtener usuario autenticado o usar el seleccionado
                var userId = vm.UsuarioId > 0
                    ? vm.UsuarioId
                    : int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

                var venta = new Venta
                {
                    UsuarioId = userId,
                    FechaVenta = vm.Fechaventa.ToUniversalTime(),
                    Total = 0
                };

                _db.Ventas.Add(venta);
                await _db.SaveChangesAsync();

                decimal total = 0;
                var detalles = new List<DetalleVenta>();

                foreach (var item in vm.Items)
                {
                    var lote = await _db.Lotes
                        .Include(l => l.Orquidea)
                        .FirstOrDefaultAsync(l => l.Id == item.LoteId);

                    if (lote == null)
                    {
                        throw new Exception($"Lote con ID {item.LoteId} no encontrado.");
                    }

                    if (lote.CantidadActual < item.Cantidad)
                    {
                        throw new Exception(
                            $"Stock insuficiente para el lote {lote.CodigoLote}. " +
                            $"Disponible: {lote.CantidadActual}, Solicitado: {item.Cantidad}");
                    }

                    // Usar el precio del lote o el enviado
                    var precioUnitario = item.PrecioUnitario > 0
                        ? item.PrecioUnitario
                        : lote.Orquidea!.PrecioBase;

                    var subtotal = item.Cantidad * precioUnitario;

                    var detalle = new DetalleVenta
                    {
                        VentaId = venta.Id,
                        LoteId = lote.Id,
                        Cantidad = item.Cantidad,
                        PrecioUnitario = precioUnitario,
                        Subtotal = subtotal
                    };

                    detalles.Add(detalle);
                    lote.CantidadActual -= item.Cantidad;
                    total += subtotal;
                }

                _db.DetalleVentas.AddRange(detalles);
                venta.Total = total;

                await _db.SaveChangesAsync();
                await trx.CommitAsync();

                TempData["Success"] = $"Venta #{venta.Id} registrada correctamente. Total: Bs {total:N2}";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                await trx.RollbackAsync();
                ModelState.AddModelError("", ex.Message);
                return View(vm);
            }
        }

        // GET: /Sales/DeletePassword/{id}
        [HttpGet]
        public IActionResult DeletePassword(int id)
        {
            // Buscar la venta para mostrar información
            var venta = _db.Ventas
                .Include(v => v.Usuario)
                .FirstOrDefault(v => v.Id == id);

            if (venta == null)
            {
                return NotFound();
            }

            ViewBag.VentaId = id;
            ViewBag.VentaInfo = $"Venta #{id} - {venta.Usuario?.NombreCompleto} - Total: Bs {venta.Total:N2}";
            return PartialView("_DeletePasswordModal");
        }

        // POST: /Sales/DeleteWithPassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteWithPassword(int id, string password)
        {
            try
            {
                var usuarioAdmin = await _db.Usuarios.FindAsync(1);

                if (usuarioAdmin == null)
                {
                    return Json(new { success = false, message = "Usuario no encontrado." });
                }

                // Verificar contraseña con BCrypt
                bool passwordValid = false;

                try
                {
                    if (!string.IsNullOrEmpty(usuarioAdmin.PasswordHash))
                    {
                        passwordValid = BCrypt.Net.BCrypt.Verify(password, usuarioAdmin.PasswordHash);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error verificando BCrypt: {ex.Message}");
                    passwordValid = false;
                }

                if (!passwordValid)
                {
                    return Json(new { success = false, message = "Contraseña incorrecta." });
                }

                // Buscar la venta
                var venta = await _db.Ventas
                    .Include(v => v.Detalles)
                    .FirstOrDefaultAsync(v => v.Id == id);

                if (venta == null)
                {
                    return Json(new { success = false, message = "Venta no encontrada." });
                }

                using var trx = await _db.Database.BeginTransactionAsync();
                try
                {
                    // Restaurar stock de los lotes
                    foreach (var detalle in venta.Detalles)
                    {
                        var lote = await _db.Lotes.FindAsync(detalle.LoteId);
                        if (lote != null)
                        {
                            lote.CantidadActual += detalle.Cantidad;
                        }
                    }

                    // Eliminar detalles y venta
                    _db.DetalleVentas.RemoveRange(venta.Detalles);
                    _db.Ventas.Remove(venta);

                    await _db.SaveChangesAsync();
                    await trx.CommitAsync();

                    return Json(new { success = true, message = $"Venta #{id} eliminada correctamente. Stock restaurado." });
                }
                catch (Exception ex)
                {
                    await trx.RollbackAsync();
                    return Json(new { success = false, message = $"Error al eliminar: {ex.Message}" });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }
        // GET: /Sales/GetVentaDetails/{id}
        [HttpGet]
        public async Task<IActionResult> GetVentaDetails(int id)
        {
            try
            {
                var venta = await _db.Ventas
                    .Include(v => v.Usuario)
                    .Include(v => v.Detalles)
                        .ThenInclude(d => d.Lote)
                            .ThenInclude(l => l.Orquidea)
                    .FirstOrDefaultAsync(v => v.Id == id);

                if (venta == null)
                {
                    return Json(new { success = false, message = "Venta no encontrada" });
                }

                var detalles = venta.Detalles.Select(d => new
                {
                    producto = d.Lote?.Orquidea?.Variedad ?? "Producto no disponible",
                    codigoLote = d.Lote?.CodigoLote ?? "N/A",
                    cantidad = d.Cantidad,
                    precioUnitario = d.PrecioUnitario,
                    subtotal = d.Subtotal
                });

                var result = new
                {
                    success = true,
                    venta = new
                    {
                        id = venta.Id,
                        fechaVenta = venta.FechaVenta,
                        usuario = venta.Usuario?.NombreCompleto ?? "Usuario #" + venta.UsuarioId,
                        total = venta.Total
                    },
                    detalles = detalles
                };

                return Json(result);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }
    }
}