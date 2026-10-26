using BloomCast.Api.Data;
using BloomCast.Contracts.Sales;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BloomCast.Api.Models;
using System.Data;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace BloomCast.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/sales")]
public class SalesController : ControllerBase
{
    private readonly BloomCastDbContext _db;
    private readonly ILogger<SalesController> _logger;


    public SalesController(
        BloomCastDbContext db,
        ILogger<SalesController> logger)
    {
        _db = db;
        _logger = logger;
    }


    // ============================================================
    // GET /api/sales
    //
    // GET /api/sales
    // GET /api/sales?search=Arturo
    // GET /api/sales?search=1
    // GET /api/sales?usuarioId=1
    // GET /api/sales?fecha=2026-09-04
    // GET /api/sales?usuarioId=1&fecha=2026-09-04
    // ============================================================

    [HttpGet]
    public async Task<ActionResult<List<SaleResponse>>> GetAll(
        [FromQuery] string? search,
        [FromQuery] int? usuarioId,
        [FromQuery] DateTime? fecha,
        CancellationToken cancellationToken)
    {
        var query = _db.Ventas
            .AsNoTracking()
            .AsQueryable();


        // ========================================================
        // Filtro por usuario ID
        //
        // Prioridad:
        // usuarioId=5
        //
        // Es más eficiente que buscar el ID como texto.
        // ========================================================

        if (usuarioId.HasValue)
        {
            query = query.Where(v =>
                v.UsuarioId == usuarioId.Value
            );
        }


        // ========================================================
        // Búsqueda
        //
        // search=5       -> usuarioId = 5
        // search=Arturo  -> ILIKE '%Arturo%'
        //
        // Se mantiene compatibilidad con el frontend anterior.
        // ========================================================

        else if (!string.IsNullOrWhiteSpace(search))
        {
            var texto = search.Trim();

            if (int.TryParse(texto, out var usuarioIdBuscado))
            {
                query = query.Where(v =>
                    v.UsuarioId == usuarioIdBuscado
                );
            }
            else
            {
                query = query.Where(v =>
                    v.Usuario != null &&
                    EF.Functions.ILike(
                        v.Usuario.NombreCompleto,
                        $"%{texto}%"
                    )
                );
            }
        }


        // ========================================================
        // Filtro fecha
        //
        // Se usa rango:
        //
        // >= 04/09/2026 00:00
        // <  05/09/2026 00:00
        //
        // Esto permite que PostgreSQL aproveche un índice
        // sobre fechaventa.
        // ========================================================

        if (fecha.HasValue)
        {
            var inicio = DateTime.SpecifyKind(
                fecha.Value.Date,
                DateTimeKind.Utc
            );

            var fin = inicio.AddDays(1);

            query = query.Where(v =>
                v.FechaVenta >= inicio &&
                v.FechaVenta < fin
            );
        }


        // ========================================================
        // Proyección directa Entity -> DTO
        //
        // No usamos Include().
        //
        // EF genera únicamente el JOIN y las columnas necesarias.
        // ========================================================

        var ventas = await query

            .OrderByDescending(v => v.Id)

            .Select(v => new SaleResponse
            {
                Id = v.Id,

                UsuarioId = v.UsuarioId,

                UsuarioNombre =
                    v.Usuario != null
                        ? v.Usuario.NombreCompleto
                        : "Usuario",

                FechaVenta = v.FechaVenta,

                Total = v.Total
            })

            .ToListAsync(cancellationToken);


        return Ok(ventas);
    }


    // ============================================================
    // GET /api/sales/{id}
    //
    // Obtiene una venta y todos sus detalles.
    //
    // La proyección se realiza directamente en PostgreSQL,
    // evitando cargar entidades completas con Include().
    // ============================================================

    [HttpGet("{id:int}")]
    public async Task<ActionResult<SaleDetailResponse>> GetById(
        int id,
        CancellationToken cancellationToken)
    {
        var venta = await _db.Ventas

            .AsNoTracking()

            .Where(v =>
                v.Id == id
            )

            .Select(v => new SaleDetailResponse
            {
                Id = v.Id,

                UsuarioId = v.UsuarioId,

                UsuarioNombre =
                    v.Usuario != null
                        ? v.Usuario.NombreCompleto
                        : "Usuario",

                FechaVenta = v.FechaVenta,

                Total = v.Total,

                Detalles = v.Detalles

                    .OrderBy(d => d.Id)

                    .Select(d =>
                        new SaleDetailItemResponse
                        {
                            LoteId = d.LoteId,

                            CodigoLote =
                                d.Lote != null
                                    ? d.Lote.CodigoLote
                                    : "N/A",

                            Producto =
                                d.Lote != null &&
                                d.Lote.Orquidea != null
                                    ? d.Lote.Orquidea.Variedad
                                    : "Producto no disponible",

                            Cantidad = d.Cantidad,

                            PrecioUnitario =
                                d.PrecioUnitario,

                            Subtotal =
                                d.Subtotal
                        })

                    .ToList()
            })

            .FirstOrDefaultAsync(cancellationToken);


        if (venta == null)
        {
            return NotFound(new
            {
                message =
                    $"La venta #{id} no existe."
            });
        }


        return Ok(venta);
    }


    // ============================================================
    // GET /api/sales/users
    //
    // Usuarios activos disponibles para registrar ventas.
    // ============================================================

    [HttpGet("users")]
    public async Task<ActionResult<List<SaleUserResponse>>> GetUsers(
        CancellationToken cancellationToken)
    {
        var usuarios = await _db.Usuarios

            .AsNoTracking()

            .Where(u =>
                u.Activo
            )

            .OrderBy(u =>
                u.NombreCompleto
            )

            .Select(u =>
                new SaleUserResponse
                {
                    Id = u.Id,

                    NombreCompleto =
                        u.NombreCompleto
                }
            )

            .ToListAsync(cancellationToken);


        return Ok(usuarios);
    }


    // ============================================================
    // GET /api/sales/available-lots
    //
    // Lotes disponibles ordenados con prioridad FEFO.
    //
    // Primero:
    // - menor fecha de caducidad
    //
    // Después:
    // - menor fecha de ingreso
    //
    // Finalmente:
    // - menor ID
    //
    // ============================================================

    [HttpGet("available-lots")]
    public async Task<
    ActionResult<List<AvailableSaleLotResponse>>
> GetAvailableLots()
    {
        var hoy = DateTime.UtcNow.Date;

        var lotes =
            await _db.Lotes

                .Include(l => l.Orquidea)

                .AsNoTracking()

                .Where(l =>
                    l.CantidadActual > 0
                    &&
                    l.FechaCaducidad.HasValue
                    &&
                    l.FechaCaducidad.Value >= hoy
                )

                // FEFO:
                // primero sale el que vence antes
                .OrderBy(l =>
                    l.FechaCaducidad)

                .ThenBy(l =>
                    l.FechaIngreso)

                .Select(l =>
                    new
                    {
                        l.Id,

                        l.CodigoLote,

                        Variedad =
                            l.Orquidea != null
                                ? l.Orquidea.Variedad
                                : "",

                        Tipo =
                            l.Orquidea != null
                                ? l.Orquidea.Tipo
                                : "",

                        PrecioBase =
                            l.Orquidea != null
                                ? l.Orquidea.PrecioBase
                                : 0,

                        Stock =
                            l.CantidadActual,

                        l.FechaCaducidad
                    })

                .ToListAsync();


        var response =
            lotes
                .Select(l =>
                    new AvailableSaleLotResponse
                    {
                        Id =
                            l.Id,

                        CodigoLote =
                            l.CodigoLote,

                        Variedad =
                            l.Variedad,

                        Tipo =
                            l.Tipo,

                        PrecioBase =
                            l.PrecioBase,

                        Stock =
                            l.Stock,

                        FechaCaducidad =
                            l.FechaCaducidad,

                        Text =
                            $"{l.CodigoLote} - " +
                            $"{l.Variedad} - " +
                            $"Stock: {l.Stock}"
                    })

                .ToList();


        return Ok(response);
    }
    // ============================================================
    // POST /api/sales
    //
    // Registra una venta de forma transaccional.
    //
    // 1. Valida usuario.
    // 2. Bloquea los lotes involucrados.
    // 3. Valida stock.
    // 4. Calcula precios.
    // 5. Descuenta inventario.
    // 6. Crea venta y detalles.
    // 7. COMMIT.
    //
    // Si cualquier paso falla:
    // ROLLBACK.
    // ============================================================

    [HttpPost]
    public async Task<ActionResult<SaleDetailResponse>> Create(
        [FromBody] CreateSaleRequest request,
        CancellationToken cancellationToken)
    {
        // ========================================================
        // Validación inicial
        // ========================================================

        if (request.Items == null || request.Items.Count == 0)
        {
            return BadRequest(new
            {
                message =
                    "La venta debe contener al menos un producto."
            });
        }

        if (request.Items.Any(i =>
            i.LoteId <= 0 ||
            i.Cantidad <= 0))
        {
            return BadRequest(new
            {
                message =
                    "Todos los productos deben tener un lote válido " +
                    "y una cantidad mayor a cero."
            });
        }


        // ========================================================
        // Usuario
        // ========================================================

        var usuario = await _db.Usuarios
            .AsNoTracking()
            .Where(u =>
                u.Id == request.UsuarioId &&
                u.Activo
            )
            .Select(u => new
            {
                u.Id,
                u.NombreCompleto
            })
            .FirstOrDefaultAsync(
                cancellationToken
            );


        if (usuario == null)
        {
            return BadRequest(new
            {
                message =
                    "El usuario seleccionado no existe o está inactivo."
            });
        }


        // ========================================================
        // Fecha de venta
        // ========================================================

        var fechaVentaUtc =
            ConvertirFechaUtc(
                request.FechaVenta
            );


        // ========================================================
        // IDs distintos de lotes
        // ========================================================

        var loteIds = request.Items
            .Select(i => i.LoteId)
            .Distinct()
            .OrderBy(id => id)
            .ToList();


        await using var transaction =
            await _db.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken
            );


        try
        {
            // ====================================================
            // Bloqueo pesimista de lotes
            // SELECT ... FOR UPDATE
            // ====================================================

            var lotes =
                new Dictionary<int, Lote>();


            foreach (var loteId in loteIds)
            {
                var lote = await _db.Lotes
                    .FromSqlInterpolated(
                        $"""
                    SELECT *
                    FROM lotes
                    WHERE id = {loteId}
                    FOR UPDATE
                    """
                    )
                    .FirstOrDefaultAsync(
                        cancellationToken
                    );


                if (lote == null)
                {
                    await transaction.RollbackAsync(
                        cancellationToken
                    );

                    return BadRequest(new
                    {
                        message =
                            $"El lote con ID {loteId} no existe."
                    });
                }


                lotes[loteId] = lote;
            }


            // ====================================================
            // Validar disponibilidad comercial de lotes
            // ====================================================

            var hoy =
                DateTime.UtcNow.Date;


            foreach (var lote in lotes.Values)
            {
                // Sin fecha de caducidad
                if (!lote.FechaCaducidad.HasValue)
                {
                    await transaction.RollbackAsync(
                        cancellationToken
                    );

                    return BadRequest(new
                    {
                        message =
                            $"El lote {lote.CodigoLote} no tiene " +
                            $"fecha de caducidad configurada y no puede venderse."
                    });
                }


                // Vencido
                if (lote.FechaCaducidad.Value.Date < hoy)
                {
                    await transaction.RollbackAsync(
                        cancellationToken
                    );

                    return BadRequest(new
                    {
                        message =
                            $"El lote {lote.CodigoLote} está vencido " +
                            $"desde el {lote.FechaCaducidad.Value:dd/MM/yyyy} " +
                            $"y no puede venderse."
                    });
                }


                // Sin stock
                if (lote.CantidadActual <= 0)
                {
                    await transaction.RollbackAsync(
                        cancellationToken
                    );

                    return Conflict(new
                    {
                        message =
                            $"El lote {lote.CodigoLote} no tiene stock disponible."
                    });
                }
            }


            // ====================================================
            // Orquídeas necesarias
            // ====================================================

            var orquideaIds =
                lotes.Values
                    .Select(l =>
                        l.OrquideaId
                    )
                    .Distinct()
                    .ToList();


            var orquideas =
                await _db.Orquideas
                    .AsNoTracking()
                    .Where(o =>
                        orquideaIds.Contains(o.Id)
                    )
                    .ToDictionaryAsync(
                        o => o.Id,
                        cancellationToken
                    );


            // ====================================================
            // Cantidad total requerida por lote
            // ====================================================

            var cantidadesRequeridas =
                request.Items
                    .GroupBy(i =>
                        i.LoteId
                    )
                    .ToDictionary(
                        g => g.Key,
                        g => g.Sum(i =>
                            i.Cantidad
                        )
                    );


            foreach (var requerido in cantidadesRequeridas)
            {
                var lote =
                    lotes[requerido.Key];


                if (lote.CantidadActual <
                    requerido.Value)
                {
                    await transaction.RollbackAsync(
                        cancellationToken
                    );


                    return Conflict(new
                    {
                        message =
                            $"Stock insuficiente para el lote " +
                            $"{lote.CodigoLote}. " +
                            $"Disponible: {lote.CantidadActual}, " +
                            $"Solicitado: {requerido.Value}."
                    });
                }
            }


            // ====================================================
            // Crear venta
            // ====================================================

            var venta =
                new Venta
                {
                    UsuarioId =
                        usuario.Id,

                    FechaVenta =
                        fechaVentaUtc,

                    Total =
                        0
                };


            decimal total =
                0;


            // ====================================================
            // Crear detalles y descontar inventario
            // ====================================================

            foreach (var item in request.Items)
            {
                var lote =
                    lotes[item.LoteId];


                if (!orquideas.TryGetValue(
                        lote.OrquideaId,
                        out var orquidea))
                {
                    throw new InvalidOperationException(
                        $"La orquídea del lote " +
                        $"{lote.CodigoLote} no existe."
                    );
                }


                // =================================================
                // Precio
                // =================================================

                var precioUnitario =
                    item.PrecioUnitario.HasValue &&
                    item.PrecioUnitario.Value > 0

                        ? item.PrecioUnitario.Value

                        : orquidea.PrecioBase;


                if (precioUnitario <= 0)
                {
                    await transaction.RollbackAsync(
                        cancellationToken
                    );

                    return BadRequest(new
                    {
                        message =
                            $"El precio del lote " +
                            $"{lote.CodigoLote} no es válido."
                    });
                }


                var subtotal =
                    item.Cantidad *
                    precioUnitario;


                var detalle =
                    new DetalleVenta
                    {
                        LoteId =
                            lote.Id,

                        Cantidad =
                            item.Cantidad,

                        PrecioUnitario =
                            precioUnitario,

                        Subtotal =
                            subtotal
                    };


                venta.Detalles.Add(
                    detalle
                );


                // =================================================
                // Descontar stock
                // =================================================

                lote.CantidadActual -=
                    item.Cantidad;


                total +=
                    subtotal;
            }


            venta.Total =
                total;


            // ====================================================
            // Guardar venta, detalles y stock
            // ====================================================

            _db.Ventas.Add(
                venta
            );


            await _db.SaveChangesAsync(
                cancellationToken
            );


            // ====================================================
            // COMMIT
            // ====================================================

            await transaction.CommitAsync(
                cancellationToken
            );


            // ====================================================
            // Response
            // ====================================================

            var response =
                new SaleDetailResponse
                {
                    Id =
                        venta.Id,

                    UsuarioId =
                        usuario.Id,

                    UsuarioNombre =
                        usuario.NombreCompleto,

                    FechaVenta =
                        venta.FechaVenta,

                    Total =
                        venta.Total,

                    Detalles =
                        venta.Detalles
                            .Select(d =>
                            {
                                var lote =
                                    lotes[d.LoteId];

                                var orquidea =
                                    orquideas[
                                        lote.OrquideaId
                                    ];


                                return new SaleDetailItemResponse
                                {
                                    LoteId =
                                        lote.Id,

                                    CodigoLote =
                                        lote.CodigoLote,

                                    Producto =
                                        orquidea.Variedad,

                                    Cantidad =
                                        d.Cantidad,

                                    PrecioUnitario =
                                        d.PrecioUnitario,

                                    Subtotal =
                                        d.Subtotal
                                };
                            })
                            .ToList()
                };


            return CreatedAtAction(
                nameof(GetById),
                new
                {
                    id = venta.Id
                },
                response
            );
        }
        catch (OperationCanceledException)
        {
            await transaction.RollbackAsync(
                CancellationToken.None
            );

            throw;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(
                cancellationToken
            );


            _logger.LogError(
                ex,
                "Error registrando venta para usuario {UsuarioId}",
                request.UsuarioId
            );


            return Problem(
                title:
                    "No se pudo registrar la venta.",

                detail:
                    "Ocurrió un error interno mientras se procesaba la venta.",

                statusCode:
                    StatusCodes.Status500InternalServerError
            );
        }
    }
    // ============================================================
    // Conversión segura a UTC
    // ============================================================

    private static DateTime ConvertirFechaUtc(
        DateTime fecha)
    {
        // Si el frontend no envía fecha,
        // usamos la fecha actual.
        if (fecha == default)
        {
            return DateTime.UtcNow;
        }


        return fecha.Kind switch
        {
            DateTimeKind.Utc =>
                fecha,

            DateTimeKind.Local =>
                fecha.ToUniversalTime(),

            // Mantiene comportamiento compatible con el MVC actual,
            // que convertía Fechaventa a UTC.
            DateTimeKind.Unspecified =>
                fecha.ToUniversalTime(),

            _ =>
                fecha.ToUniversalTime()
        };
    }
    // ============================================================
    // DELETE /api/sales/{id}
    //
    // TEMPORAL:
    // Mantiene la validación administrativa existente.
    // Será reemplazada por autorización mediante token cuando
    // migremos Auth/MFA.
    // ============================================================
    [Authorize(Roles = "Administrador")]
    [HttpPost("{id:int}/delete-with-password")]
    public async Task<IActionResult> Delete(
        int id,
        [FromBody] DeleteSaleRequest request,
        CancellationToken cancellationToken)
    {
        // ========================================================
        // Password requerido
        // ========================================================

        if (string.IsNullOrWhiteSpace(
            request.Password))
        {
            return BadRequest(new
            {
                message =
                    "La contraseña es requerida."
            });
        }


        // ========================================================
        // Obtener usuario autenticado desde JWT
        // ========================================================

        var userIdClaim =
            User.FindFirstValue(
                ClaimTypes.NameIdentifier
            );


        if (!int.TryParse(
                userIdClaim,
                out var currentUserId))
        {
            return Unauthorized(new
            {
                message =
                    "No se pudo identificar al usuario autenticado."
            });
        }


        // ========================================================
        // Buscar administrador autenticado
        //
        // Ya NO usamos ID = 1.
        // ========================================================

        var usuarioAdmin =
            await _db.Usuarios

                .AsNoTracking()

                .FirstOrDefaultAsync(
                    u =>
                        u.Id == currentUserId
                        &&
                        u.Activo,

                    cancellationToken
                );


        if (usuarioAdmin == null)
        {
            return Unauthorized(new
            {
                message =
                    "El usuario autenticado no existe o está inactivo."
            });
        }


        // ========================================================
        // Validar nuevamente contraseña
        //
        // Esto funciona como reautenticación para una operación
        // sensible.
        // ========================================================

        var passwordValido =
            VerificarPassword(
                request.Password,
                usuarioAdmin.PasswordHash
            );


        if (!passwordValido)
        {
            return Unauthorized(new
            {
                message =
                    "Contraseña incorrecta."
            });
        }


        // ========================================================
        // Transacción
        // ========================================================

        await using var transaction =
            await _db.Database
                .BeginTransactionAsync(
                    IsolationLevel.ReadCommitted,
                    cancellationToken
                );


        try
        {
            // ====================================================
            // Buscar venta
            // ====================================================

            var venta =
                await _db.Ventas

                    .Include(v =>
                        v.Detalles)

                    .FirstOrDefaultAsync(
                        v =>
                            v.Id == id,

                        cancellationToken
                    );


            if (venta == null)
            {
                await transaction.RollbackAsync(
                    cancellationToken
                );


                return NotFound(new
                {
                    message =
                        $"La venta #{id} no existe."
                });
            }


            // ====================================================
            // IDs de lotes involucrados
            // ====================================================

            var loteIds =
                venta.Detalles

                    .Select(d =>
                        d.LoteId)

                    .Distinct()

                    .OrderBy(x =>
                        x)

                    .ToList();


            var lotes =
                new Dictionary<int, Lote>();


            // ====================================================
            // Bloquear lotes
            //
            // SELECT FOR UPDATE evita inconsistencias si otra
            // operación intenta modificar los mismos lotes.
            // ====================================================

            foreach (var loteId in loteIds)
            {
                var lote =
                    await _db.Lotes

                        .FromSqlInterpolated(
                            $"""
                        SELECT *
                        FROM lotes
                        WHERE id = {loteId}
                        FOR UPDATE
                        """
                        )

                        .FirstOrDefaultAsync(
                            cancellationToken
                        );


                if (lote == null)
                {
                    throw new InvalidOperationException(
                        $"No se encontró el lote {loteId} " +
                        $"asociado a la venta #{id}."
                    );
                }


                lotes[loteId] =
                    lote;
            }


            // ====================================================
            // Restaurar stock
            // ====================================================

            foreach (var detalle in venta.Detalles)
            {
                lotes[
                    detalle.LoteId
                ].CantidadActual +=
                    detalle.Cantidad;
            }


            // ====================================================
            // Eliminar detalles
            // ====================================================

            _db.DetalleVentas.RemoveRange(
                venta.Detalles
            );


            // ====================================================
            // Eliminar venta
            // ====================================================

            _db.Ventas.Remove(
                venta
            );


            // ====================================================
            // Guardar
            // ====================================================

            await _db.SaveChangesAsync(
                cancellationToken
            );


            // ====================================================
            // COMMIT
            // ====================================================

            await transaction.CommitAsync(
                cancellationToken
            );


            // ====================================================
            // Log
            // ====================================================

            _logger.LogInformation(
                "Administrador {UsuarioId} eliminó la venta {VentaId}.",
                currentUserId,
                id
            );


            return Ok(new
            {
                success =
                    true,

                message =
                    $"Venta #{id} eliminada correctamente. " +
                    $"Stock restaurado."
            });
        }
        catch (OperationCanceledException)
        {
            await transaction.RollbackAsync(
                CancellationToken.None
            );

            throw;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(
                cancellationToken
            );


            _logger.LogError(
                ex,
                "Error eliminando venta {VentaId} por usuario {UsuarioId}.",
                id,
                currentUserId
            );


            return Problem(
                title:
                    "No se pudo eliminar la venta.",

                detail:
                    "Ocurrió un error interno al eliminar la venta.",

                statusCode:
                    StatusCodes
                        .Status500InternalServerError
            );
        }
    }
    // ============================================================
    // Validación de contraseña
    //
    // Soporta BCrypt y temporalmente passwords legacy.
    // ============================================================

    private static bool VerificarPassword(
        string inputPassword,
        string storedHash)
    {
        if (string.IsNullOrWhiteSpace(
            storedHash))
        {
            return false;
        }


        // BCrypt
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


        // Compatibilidad temporal legacy
        return string.Equals(
            inputPassword,
            storedHash,
            StringComparison.Ordinal
        );
    }
}