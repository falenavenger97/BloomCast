using BloomCast.Contracts.Auth;
using BloomCast.Models;
using BloomCast.Services.Api;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

using System.Security.Claims;


namespace BloomCast.Controllers
{
    public class AccountController : Controller
    {
        private readonly IAuthApiService
            _authApi;


        public AccountController(
            IAuthApiService authApi)
        {
            _authApi =
                authApi;
        }


        // =========================================================
        // GET /Account/Login
        // =========================================================

        [HttpGet]
        public IActionResult Login()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction(
                    "Index",
                    "Home"
                );
            }


            return View();
        }


        // =========================================================
        // POST /Account/Login
        // =========================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(
            LoginViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                return View(vm);
            }


            try
            {
                var response =
                    await _authApi.LoginAsync(
                        new LoginRequest
                        {
                            Correo =
                                vm.Correo,

                            Password =
                                vm.Password
                        }
                    );


                // =================================================
                // Guardar challenge temporal
                // =================================================

                HttpContext.Session.SetString(
                    "MfaChallengeToken",
                    response.ChallengeToken
                );


                // =================================================
                // Primera configuración MFA
                // =================================================

                if (response.RequiresMfaSetup)
                {
                    if (string.IsNullOrWhiteSpace(
                            response.QrCodeBase64))
                    {
                        ModelState.AddModelError(
                            "",
                            "La API no devolvió el código QR para configurar MFA."
                        );

                        return View(vm);
                    }


                    HttpContext.Session.SetString(
                        "MfaQrCodeBase64",
                        response.QrCodeBase64
                    );


                    return View(
                        "MfaSetup",
                        new MfaViewModel
                        {
                            QrCodeBase64 =
                                response.QrCodeBase64,

                            EsConfiguracion =
                                true
                        }
                    );
                }


                // =================================================
                // MFA ya configurado
                // =================================================

                HttpContext.Session.Remove(
                    "MfaQrCodeBase64"
                );


                return RedirectToAction(
                    nameof(Mfa)
                );
            }
            catch (HttpRequestException ex)
            {
                ModelState.AddModelError(
                    "",
                    ex.Message
                );

                return View(vm);
            }
        }


        // =========================================================
        // GET /Account/Mfa
        // =========================================================

        [HttpGet]
        public IActionResult Mfa()
        {
            var challenge =
                HttpContext.Session.GetString(
                    "MfaChallengeToken"
                );


            if (string.IsNullOrWhiteSpace(
                    challenge))
            {
                return RedirectToAction(
                    nameof(Login)
                );
            }


            return View(
                new MfaViewModel()
            );
        }


        // =========================================================
        // POST /Account/Mfa
        // =========================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Mfa(
            MfaViewModel vm)
        {
            var challenge =
                HttpContext.Session.GetString(
                    "MfaChallengeToken"
                );


            if (string.IsNullOrWhiteSpace(
                    challenge))
            {
                return RedirectToAction(
                    nameof(Login)
                );
            }


            try
            {
                var usuario =
                    await _authApi.VerifyMfaAsync(
                        new MfaVerifyRequest
                        {
                            ChallengeToken =
                                challenge,

                            Codigo =
                                vm.Codigo
                        }
                    );


                await SignInAsync(
                    usuario
                );


                return RedirectToAction(
                    "Index",
                    "Home"
                );
            }
            catch (HttpRequestException ex)
            {
                ModelState.AddModelError(
                    "",
                    ex.Message
                );


                return View(vm);
            }
        }


        // =========================================================
        // POST /Account/MfaSetup
        // =========================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MfaSetup(
            MfaViewModel vm)
        {
            var challenge =
                HttpContext.Session.GetString(
                    "MfaChallengeToken"
                );


            var qrCode =
                HttpContext.Session.GetString(
                    "MfaQrCodeBase64"
                );


            if (string.IsNullOrWhiteSpace(
                    challenge))
            {
                return RedirectToAction(
                    nameof(Login)
                );
            }


            try
            {
                var usuario =
                    await _authApi.VerifyMfaAsync(
                        new MfaVerifyRequest
                        {
                            ChallengeToken =
                                challenge,

                            Codigo =
                                vm.Codigo
                        }
                    );


                await SignInAsync(
                    usuario
                );


                return RedirectToAction(
                    "Index",
                    "Home"
                );
            }
            catch (HttpRequestException ex)
            {
                ModelState.AddModelError(
                    "",
                    ex.Message
                );


                return View(
                    "MfaSetup",
                    new MfaViewModel
                    {
                        Codigo =
                            vm.Codigo,

                        QrCodeBase64 =
                            qrCode ?? "",

                        EsConfiguracion =
                            true
                    }
                );
            }
        }


        // =========================================================
        // POST /Account/Logout
        // =========================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(
                CookieAuthenticationDefaults
                    .AuthenticationScheme
            );


            HttpContext.Session.Clear();


            return RedirectToAction(
                nameof(Login)
            );
        }


        // =========================================================
        // Crear Cookie Web + guardar JWT
        // =========================================================

        private async Task SignInAsync(
            AuthenticatedUserResponse usuario)
        {
            // =====================================================
            // Limpiar estado MFA temporal
            // =====================================================

            HttpContext.Session.Remove(
                "MfaChallengeToken"
            );

            HttpContext.Session.Remove(
                "MfaQrCodeBase64"
            );


            // =====================================================
            // Guardar JWT para las llamadas Web -> API
            // =====================================================

            HttpContext.Session.SetString(
                "AccessToken",
                usuario.AccessToken
            );

            // =====================================================
            // Claims de la Cookie MVC
            // =====================================================

            var claims =
                new List<Claim>
                {
                    new(
                        ClaimTypes.NameIdentifier,
                        usuario.UsuarioId.ToString()
                    ),

                    new(
                        ClaimTypes.Name,
                        usuario.NombreCompleto
                    ),

                    new(
                        ClaimTypes.Email,
                        usuario.Correo
                    ),

                    new(
                        ClaimTypes.Role,
                        usuario.Rol
                    ),

                    new(
                        "RolNombre",
                        usuario.Rol
                    )
                };


            var identity =
                new ClaimsIdentity(
                    claims,
                    CookieAuthenticationDefaults
                        .AuthenticationScheme
                );


            var principal =
                new ClaimsPrincipal(
                    identity
                );


            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults
                    .AuthenticationScheme,

                principal,

                new AuthenticationProperties
                {
                    IsPersistent =
                        false,

                    ExpiresUtc =
                        usuario.ExpiresUtc
                }
            );
        }
    }
}