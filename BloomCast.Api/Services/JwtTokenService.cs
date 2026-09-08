using BloomCast.Api.Models;
using BloomCast.Contracts.Auth;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace BloomCast.Api.Services;

public class JwtTokenService : ITokenService
{
    private readonly IConfiguration _configuration;

    public JwtTokenService(
        IConfiguration configuration)
    {
        _configuration = configuration;
    }


    public AuthenticatedUserResponse CreateAccessToken(
        Usuario usuario)
    {
        var expirationMinutes =
            _configuration.GetValue<int>(
                "Jwt:ExpirationMinutes",
                30
            );

        var expiresUtc =
            DateTime.UtcNow.AddMinutes(
                expirationMinutes
            );


        var claims =
            new List<Claim>
            {
                new(
                    ClaimTypes.NameIdentifier,
                    usuario.Id.ToString()
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
                    usuario.Rol?.Nombre
                    ?? "usuario"
                ),

                new(
                    "RolNombre",
                    usuario.Rol?.Nombre
                    ?? "usuario"
                ),

                new(
                    "token_type",
                    "access"
                )
            };


        var token =
            CreateToken(
                claims,
                expiresUtc
            );


        return new AuthenticatedUserResponse
        {
            AccessToken =
                token,

            ExpiresUtc =
                expiresUtc,

            UsuarioId =
                usuario.Id,

            NombreCompleto =
                usuario.NombreCompleto,

            Correo =
                usuario.Correo,

            Rol =
                usuario.Rol?.Nombre
                ?? "usuario"
        };
    }


    public string CreateMfaChallengeToken(
        Usuario usuario)
    {
        var claims =
            new List<Claim>
            {
                new(
                    ClaimTypes.NameIdentifier,
                    usuario.Id.ToString()
                ),

                new(
                    "token_type",
                    "mfa_challenge"
                )
            };


        return CreateToken(
            claims,
            DateTime.UtcNow.AddMinutes(5)
        );
    }


    public int? ValidateMfaChallengeToken(
        string token)
    {
        try
        {
            var handler =
                new JwtSecurityTokenHandler();


            var principal =
                handler.ValidateToken(
                    token,
                    GetValidationParameters(),
                    out _
                );


            var tokenType =
                principal.FindFirst(
                    "token_type"
                )?.Value;


            if (tokenType !=
                "mfa_challenge")
            {
                return null;
            }


            var userIdClaim =
                principal.FindFirst(
                    ClaimTypes.NameIdentifier
                )?.Value;


            if (!int.TryParse(
                    userIdClaim,
                    out var userId))
            {
                return null;
            }


            return userId;
        }
        catch
        {
            return null;
        }
    }


    private string CreateToken(
        IEnumerable<Claim> claims,
        DateTime expiresUtc)
    {
        var key =
            GetSigningKey();


        var credentials =
            new SigningCredentials(
                key,
                SecurityAlgorithms.HmacSha256
            );


        var token =
            new JwtSecurityToken(
                issuer:
                    _configuration["Jwt:Issuer"],

                audience:
                    _configuration["Jwt:Audience"],

                claims:
                    claims,

                expires:
                    expiresUtc,

                signingCredentials:
                    credentials
            );


        return new JwtSecurityTokenHandler()
            .WriteToken(token);
    }


    private SymmetricSecurityKey GetSigningKey()
    {
        var jwtKey =
            _configuration["Jwt:Key"];


        if (string.IsNullOrWhiteSpace(jwtKey))
        {
            throw new InvalidOperationException(
                "No se encontró Jwt:Key."
            );
        }


        return new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(
                jwtKey
            )
        );
    }


    private TokenValidationParameters
        GetValidationParameters()
    {
        return new TokenValidationParameters
        {
            ValidateIssuer =
                true,

            ValidateAudience =
                true,

            ValidateLifetime =
                true,

            ValidateIssuerSigningKey =
                true,

            ValidIssuer =
                _configuration["Jwt:Issuer"],

            ValidAudience =
                _configuration["Jwt:Audience"],

            IssuerSigningKey =
                GetSigningKey(),

            ClockSkew =
                TimeSpan.FromSeconds(30)
        };
    }
}