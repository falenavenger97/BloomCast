using BloomCast.Api.Data;
using BloomCast.Api.Services;
using BloomCast.Contracts.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BloomCast.Api.Controllers;


[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly BloomCastDbContext _db;

    private readonly IMfaService _mfa;

    private readonly ITokenService _tokens;

    private readonly ILogger<AuthController>
        _logger;


    public AuthController(
        BloomCastDbContext db,
        IMfaService mfa,
        ITokenService tokens,
        ILogger<AuthController> logger)
    {
        _db =
            db;

        _mfa =
            mfa;

        _tokens =
            tokens;

        _logger =
            logger;
    }


    // ============================================================
    // POST /api/auth/login
    //
    // PASO 1
    // Correo + password.
    //
    // NO devuelve todavía el Access Token.
    // Devuelve un challenge temporal para MFA.
    // ============================================================

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var correo =
            request.Correo
                .Trim()
                .ToLower();


        // ========================================================
        // Usuario
        // ========================================================

        var usuario =
            await _db.Usuarios

                .Include(u =>
                    u.Rol)

                .FirstOrDefaultAsync(
                    u =>
                        u.Correo == correo
                        &&
                        u.Activo,

                    cancellationToken
                );


        // ========================================================
        // Credenciales
        //
        // Mismo mensaje independientemente de si falla correo
        // o contraseña para no revelar usuarios existentes.
        // ========================================================

        if (usuario == null ||
            !VerificarPassword(
                request.Password,
                usuario.PasswordHash
            ))
        {
            return Unauthorized(new
            {
                message =
                    "Credenciales incorrectas o usuario inactivo."
            });
        }


        // ========================================================
        // Challenge MFA
        // ========================================================

        var challengeToken =
            _tokens.CreateMfaChallengeToken(
                usuario
            );


        // ========================================================
        // Primera configuración MFA
        //
        // Guardamos temporalmente el secreto en el usuario,
        // pero NO marcamos MFA como habilitado hasta que
        // el código sea verificado correctamente.
        // ========================================================

        if (!usuario.MfaHabilitado ||
            string.IsNullOrWhiteSpace(
                usuario.MfaSecret
            ))
        {
            var secret =
                _mfa.GenerarSecretKey();


            usuario.MfaSecret =
                secret;

            usuario.MfaHabilitado =
                false;


            await _db.SaveChangesAsync(
                cancellationToken
            );


            var qrCode =
                _mfa.GenerarQrCode(
                    usuario.Correo,
                    secret
                );


            return Ok(
                new LoginResponse
                {
                    RequiresMfa =
                        true,

                    RequiresMfaSetup =
                        true,

                    ChallengeToken =
                        challengeToken,

                    QrCodeBase64 =
                        qrCode
                }
            );
        }


        // ========================================================
        // MFA ya configurado
        // ========================================================

        return Ok(
            new LoginResponse
            {
                RequiresMfa =
                    true,

                RequiresMfaSetup =
                    false,

                ChallengeToken =
                    challengeToken,

                QrCodeBase64 =
                    null
            }
        );
    }


    // ============================================================
    // POST /api/auth/mfa
    //
    // PASO 2
    // Challenge + código TOTP
    //
    // Si es correcto:
    //      → habilita MFA si era setup
    //      → devuelve JWT
    // ============================================================

    [HttpPost("mfa")]
    public async Task<ActionResult<AuthenticatedUserResponse>>
        VerifyMfa(
            [FromBody] MfaVerifyRequest request,
            CancellationToken cancellationToken)
    {
        // ========================================================
        // Validar challenge
        // ========================================================

        var userId =
            _tokens.ValidateMfaChallengeToken(
                request.ChallengeToken
            );


        if (!userId.HasValue)
        {
            return Unauthorized(new
            {
                message =
                    "La verificación MFA expiró o no es válida. " +
                    "Inicia sesión nuevamente."
            });
        }


        // ========================================================
        // Usuario
        // ========================================================

        var usuario =
            await _db.Usuarios

                .Include(u =>
                    u.Rol)

                .FirstOrDefaultAsync(
                    u =>
                        u.Id == userId.Value
                        &&
                        u.Activo,

                    cancellationToken
                );


        if (usuario == null)
        {
            return Unauthorized(new
            {
                message =
                    "El usuario ya no está disponible."
            });
        }


        // ========================================================
        // Secret
        // ========================================================

        if (string.IsNullOrWhiteSpace(
                usuario.MfaSecret
            ))
        {
            return BadRequest(new
            {
                message =
                    "El usuario no tiene una configuración MFA válida."
            });
        }


        // ========================================================
        // Código TOTP
        // ========================================================

        var codigoValido =
            _mfa.ValidarCodigo(
                usuario.MfaSecret,
                request.Codigo
            );


        if (!codigoValido)
        {
            return Unauthorized(new
            {
                message =
                    "Código incorrecto o expirado."
            });
        }


        // ========================================================
        // Si era primera configuración, ahora sí habilitamos MFA
        // ========================================================

        if (!usuario.MfaHabilitado)
        {
            usuario.MfaHabilitado =
                true;


            await _db.SaveChangesAsync(
                cancellationToken
            );
        }


        // ========================================================
        // Generar Access Token JWT
        // ========================================================

        var result =
            _tokens.CreateAccessToken(
                usuario
            );


        _logger.LogInformation(
            "Usuario {UsuarioId} autenticado correctamente.",
            usuario.Id
        );


        return Ok(result);
    }


    // ============================================================
    // Password compatibility
    // ============================================================

    private static bool VerificarPassword(
        string inputPassword,
        string storedHash)
    {
        if (string.IsNullOrWhiteSpace(
                storedHash
            ))
        {
            return false;
        }


        // ========================================================
        // BCrypt
        // ========================================================

        if (storedHash.StartsWith("$2"))
        {
            try
            {
                return BCrypt.Net.BCrypt.Verify(
                    inputPassword,
                    storedHash
                );
            }
            catch
            {
                return false;
            }
        }


        // ========================================================
        // Compatibilidad temporal con usuarios legacy
        // ========================================================

        return string.Equals(
            inputPassword,
            storedHash,
            StringComparison.Ordinal
        );
    }
}