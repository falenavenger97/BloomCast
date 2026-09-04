using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;
using BloomCast.Data;
using BloomCast.Models;
using BloomCast.Services;
using Microsoft.EntityFrameworkCore;

namespace BloomCast.Controllers
{
    public class AccountController : Controller
    {
        private readonly BloomCastDbContext _db;
        private readonly IMfaService _mfa;

        public AccountController(BloomCastDbContext db, IMfaService mfa)
        {
            _db  = db;
            _mfa = mfa;
        }

        // GET /Account/Login
        [HttpGet]
        public IActionResult Login() =>
            User.Identity?.IsAuthenticated == true
                ? RedirectToAction("Index", "Home")
                : View();

        // POST /Account/Login
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel vm)
        {
            if (!ModelState.IsValid) return View(vm);

            var usuario = await _db.Usuarios
                .Include(u => u.Rol)
                .FirstOrDefaultAsync(u => u.Correo == vm.Correo && u.Activo);

            if (usuario == null || !VerificarPassword(vm.Password, usuario.PasswordHash))
            {
                ModelState.AddModelError("", "Credenciales incorrectas o usuario inactivo.");
                return View(vm);
            }

            // Save pending user in session → go to MFA step
            HttpContext.Session.SetInt32("PendingUserId",    usuario.Id);
            HttpContext.Session.SetString("PendingUserEmail", usuario.Correo);

            // First-time MFA setup
            if (!usuario.MfaHabilitado || string.IsNullOrEmpty(usuario.MfaSecret))
            {
                var secret = _mfa.GenerarSecretKey();
                HttpContext.Session.SetString("MfaSetupSecret", secret);
                var qr = _mfa.GenerarQrCode(usuario.Correo, secret);
                return View("MfaSetup", new MfaViewModel { QrCodeBase64 = qr, EsConfiguracion = true });
            }

            return RedirectToAction("Mfa");
        }

        // GET /Account/Mfa
        [HttpGet]
        public IActionResult Mfa()
        {
            if (HttpContext.Session.GetInt32("PendingUserId") == null)
                return RedirectToAction("Login");
            return View(new MfaViewModel());
        }

        // POST /Account/Mfa
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Mfa(MfaViewModel vm)
        {
            var userId = HttpContext.Session.GetInt32("PendingUserId");
            if (userId == null) return RedirectToAction("Login");

            var usuario = await _db.Usuarios.Include(u => u.Rol).FirstOrDefaultAsync(u => u.Id == userId);
            if (usuario == null) return RedirectToAction("Login");

            if (!_mfa.ValidarCodigo(usuario.MfaSecret!, vm.Codigo))
            {
                ModelState.AddModelError("", "Código incorrecto o expirado. Intenta de nuevo.");
                return View(vm);
            }

            await SignInAsync(usuario);
            return RedirectToAction("Index", "Home");
        }

        // POST /Account/MfaSetup  — primera configuración
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MfaSetup(MfaViewModel vm)
        {
            var userId = HttpContext.Session.GetInt32("PendingUserId");
            var secret = HttpContext.Session.GetString("MfaSetupSecret");

            if (userId == null || string.IsNullOrEmpty(secret))
                return RedirectToAction("Login");

            if (!_mfa.ValidarCodigo(secret, vm.Codigo))
            {
                var email = HttpContext.Session.GetString("PendingUserEmail") ?? "";
                var qr    = _mfa.GenerarQrCode(email, secret);
                ModelState.AddModelError("", "Código incorrecto. Vuelve a escanear el QR e intenta de nuevo.");
                return View(new MfaViewModel { QrCodeBase64 = qr, EsConfiguracion = true });
            }

            var usuario = await _db.Usuarios.Include(u => u.Rol).FirstOrDefaultAsync(u => u.Id == userId);
            if (usuario == null) return RedirectToAction("Login");

            usuario.MfaSecret     = secret;
            usuario.MfaHabilitado = true;
            await _db.SaveChangesAsync();

            await SignInAsync(usuario);
            return RedirectToAction("Index", "Home");
        }

        // POST /Account/Logout
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }

        // ── helpers ───────────────────────────────────────────────────────────
        private async Task SignInAsync(Usuario usuario)
        {
            HttpContext.Session.Remove("PendingUserId");
            HttpContext.Session.Remove("PendingUserEmail");
            HttpContext.Session.Remove("MfaSetupSecret");

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
                new(ClaimTypes.Name,           usuario.NombreCompleto),
                new(ClaimTypes.Email,          usuario.Correo),
                new(ClaimTypes.Role,           usuario.Rol?.Nombre ?? "usuario"),
                new("RolNombre",               usuario.Rol?.Nombre ?? "usuario")
            };

            var identity  = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties { IsPersistent = false, ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30) });
        }

        private static bool VerificarPassword(string inputPassword, string storedHash)
        {
            if (storedHash.StartsWith("$2"))
            {
                try { return BCrypt.Net.BCrypt.Verify(inputPassword, storedHash); }
                catch { return false; }
            }
            // Plain-text fallback (legacy)
            return string.Equals(inputPassword, storedHash, StringComparison.Ordinal);
        }
    }
}
