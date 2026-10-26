using BloomCast.Api.Data;
using BloomCast.Api.Models;
using BloomCast.Contracts.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BloomCast.Api.Controllers;

[Authorize(Roles = "Administrador")]
[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly BloomCastDbContext _db;

    public UsersController(
        BloomCastDbContext db)
    {
        _db = db;
    }


    // ============================================================
    // GET /api/users
    //
    // ?search=claudia
    // ?rolId=1
    // ?activo=true
    // ============================================================

    [HttpGet]
    public async Task<ActionResult<List<UserResponse>>> GetAll(
        [FromQuery] string? search,
        [FromQuery] int? rolId,
        [FromQuery] bool? activo,
        CancellationToken cancellationToken)
    {
        var query = _db.Usuarios
            .Include(u => u.Rol)
            .AsNoTracking()
            .AsQueryable();


        if (!string.IsNullOrWhiteSpace(search))
        {
            var texto =
                search.Trim().ToLower();

            query = query.Where(u =>
                u.NombreCompleto.ToLower().Contains(texto) ||
                u.Correo.ToLower().Contains(texto)
            );
        }


        if (rolId.HasValue)
        {
            query = query.Where(u =>
                u.RolId == rolId.Value
            );
        }


        if (activo.HasValue)
        {
            query = query.Where(u =>
                u.Activo == activo.Value
            );
        }


        var usuarios = await query

            // 1. Activos primero
            .OrderByDescending(u =>
                u.Activo
            )

            // 2. Administradores primero
            .ThenByDescending(u =>
                u.Rol != null &&
                u.Rol.Nombre == "Administrador"
            )

            // 3. Orden alfabético
            .ThenBy(u =>
                u.NombreCompleto
            )

            .Select(u =>
                new UserResponse
                {
                    Id =
                        u.Id,

                    NombreCompleto =
                        u.NombreCompleto,

                    Correo =
                        u.Correo,

                    RolId =
                        u.RolId,

                    RolNombre =
                        u.Rol != null
                            ? u.Rol.Nombre
                            : "",

                    Activo =
                        u.Activo,

                    MfaHabilitado =
                        u.MfaHabilitado
                })

            .ToListAsync(
                cancellationToken
            );


        return Ok(usuarios);
    }


    // ============================================================
    // GET /api/users/{id}
    // ============================================================

    [HttpGet("{id:int}")]
    public async Task<ActionResult<UserResponse>> GetById(
        int id,
        CancellationToken cancellationToken)
    {
        var usuario = await _db.Usuarios
            .Include(u => u.Rol)
            .AsNoTracking()
            .Where(u =>
                u.Id == id
            )
            .Select(u =>
                new UserResponse
                {
                    Id =
                        u.Id,

                    NombreCompleto =
                        u.NombreCompleto,

                    Correo =
                        u.Correo,

                    RolId =
                        u.RolId,

                    RolNombre =
                        u.Rol != null
                            ? u.Rol.Nombre
                            : "",

                    Activo =
                        u.Activo,

                    MfaHabilitado =
                        u.MfaHabilitado
                })
            .FirstOrDefaultAsync(
                cancellationToken
            );


        if (usuario == null)
        {
            return NotFound(new
            {
                message =
                    $"El usuario #{id} no existe."
            });
        }


        return Ok(usuario);
    }


    // ============================================================
    // GET /api/users/roles
    // ============================================================

    [HttpGet("roles")]
    public async Task<ActionResult<List<RoleResponse>>> GetRoles(
        CancellationToken cancellationToken)
    {
        var roles = await _db.Roles
            .AsNoTracking()
            .OrderBy(r =>
                r.Nombre
            )
            .Select(r =>
                new RoleResponse
                {
                    Id =
                        r.Id,

                    Nombre =
                        r.Nombre
                })
            .ToListAsync(
                cancellationToken
            );


        return Ok(roles);
    }


    // ============================================================
    // POST /api/users
    // ============================================================

    [HttpPost]
    public async Task<ActionResult<UserResponse>> Create(
        [FromBody] CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        var nombre =
            request.NombreCompleto.Trim();

        var correo =
            request.Correo.Trim().ToLower();


        if (string.IsNullOrWhiteSpace(nombre))
        {
            return BadRequest(new
            {
                message =
                    "El nombre es requerido."
            });
        }


        var correoExiste =
            await _db.Usuarios.AnyAsync(
                u => u.Correo == correo,
                cancellationToken
            );


        if (correoExiste)
        {
            return Conflict(new
            {
                message =
                    "Ya existe un usuario con ese correo."
            });
        }


        var rol =
            await _db.Roles
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    r => r.Id == request.RolId,
                    cancellationToken
                );


        if (rol == null)
        {
            return BadRequest(new
            {
                message =
                    "El rol seleccionado no existe."
            });
        }


        var usuario =
            new Usuario
            {
                NombreCompleto =
                    nombre,

                Correo =
                    correo,

                PasswordHash =
                    BCrypt.Net.BCrypt.HashPassword(
                        request.Password
                    ),

                RolId =
                    rol.Id,

                Activo =
                    request.Activo,

                MfaHabilitado =
                    false,

                MfaSecret =
                    null
            };


        _db.Usuarios.Add(usuario);

        await _db.SaveChangesAsync(
            cancellationToken
        );


        var response =
            new UserResponse
            {
                Id =
                    usuario.Id,

                NombreCompleto =
                    usuario.NombreCompleto,

                Correo =
                    usuario.Correo,

                RolId =
                    usuario.RolId,

                RolNombre =
                    rol.Nombre,

                Activo =
                    usuario.Activo,

                MfaHabilitado =
                    usuario.MfaHabilitado
            };


        return CreatedAtAction(
            nameof(GetById),
            new
            {
                id = usuario.Id
            },
            response
        );
    }


    // ============================================================
    // PUT /api/users/{id}
    // ============================================================

    [HttpPut("{id:int}")]
    public async Task<ActionResult<UserResponse>> Update(
        int id,
        [FromBody] UpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        var usuario =
            await _db.Usuarios
                .FirstOrDefaultAsync(
                    u => u.Id == id,
                    cancellationToken
                );


        if (usuario == null)
        {
            return NotFound(new
            {
                message =
                    $"El usuario #{id} no existe."
            });
        }


        var nombre =
            request.NombreCompleto.Trim();

        var correo =
            request.Correo.Trim().ToLower();


        var correoDuplicado =
            await _db.Usuarios.AnyAsync(
                u =>
                    u.Correo == correo &&
                    u.Id != id,
                cancellationToken
            );


        if (correoDuplicado)
        {
            return Conflict(new
            {
                message =
                    "Ya existe otro usuario con ese correo."
            });
        }


        var rol =
            await _db.Roles
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    r => r.Id == request.RolId,
                    cancellationToken
                );


        if (rol == null)
        {
            return BadRequest(new
            {
                message =
                    "El rol seleccionado no existe."
            });
        }


        usuario.NombreCompleto =
            nombre;

        usuario.Correo =
            correo;

        usuario.RolId =
            rol.Id;

        usuario.Activo =
            request.Activo;


        if (!string.IsNullOrWhiteSpace(
                request.NuevaPassword))
        {
            usuario.PasswordHash =
                BCrypt.Net.BCrypt.HashPassword(
                    request.NuevaPassword
                );
        }


        if (request.ResetearMfa)
        {
            usuario.MfaSecret =
                null;

            usuario.MfaHabilitado =
                false;
        }


        await _db.SaveChangesAsync(
            cancellationToken
        );


        var response =
            new UserResponse
            {
                Id =
                    usuario.Id,

                NombreCompleto =
                    usuario.NombreCompleto,

                Correo =
                    usuario.Correo,

                RolId =
                    usuario.RolId,

                RolNombre =
                    rol.Nombre,

                Activo =
                    usuario.Activo,

                MfaHabilitado =
                    usuario.MfaHabilitado
            };


        return Ok(response);
    }


    // ============================================================
    // DELETE /api/users/{id}
    //
    // En BloomCast realmente significa DESACTIVAR.
    // ============================================================

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Deactivate(
    int id,
    CancellationToken cancellationToken)
    {
        // ========================================================
        // Identificar al administrador autenticado desde JWT
        // ========================================================

        var currentUserClaim =
            User.FindFirstValue(
                ClaimTypes.NameIdentifier
            );


        if (!int.TryParse(
                currentUserClaim,
                out var currentUserId))
        {
            return Unauthorized(new
            {
                message =
                    "No se pudo identificar al usuario autenticado."
            });
        }


        // ========================================================
        // No permitir auto-desactivación
        // ========================================================

        if (currentUserId == id)
        {
            return BadRequest(new
            {
                message =
                    "No puedes desactivar tu propia cuenta."
            });
        }


        // ========================================================
        // Buscar usuario objetivo
        // ========================================================

        var usuario =
            await _db.Usuarios
                .FirstOrDefaultAsync(
                    u =>
                        u.Id == id,
                    cancellationToken
                );


        if (usuario == null)
        {
            return NotFound(new
            {
                message =
                    $"El usuario #{id} no existe."
            });
        }


        // ========================================================
        // Ya estaba inactivo
        // ========================================================

        if (!usuario.Activo)
        {
            return Ok(new
            {
                message =
                    $"El usuario {usuario.NombreCompleto} " +
                    $"ya estaba desactivado."
            });
        }


        // ========================================================
        // Desactivar
        // ========================================================

        usuario.Activo =
            false;


        await _db.SaveChangesAsync(
            cancellationToken
        );


        return Ok(new
        {
            message =
                $"Usuario {usuario.NombreCompleto} " +
                $"desactivado correctamente."
        });
    }
}