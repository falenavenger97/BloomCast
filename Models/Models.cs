using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BloomCast.Models
{
    // ── roles ─────────────────────────────────────────────────────────────────
    [Table("roles")]
    public class Rol
    {
        [Key][Column("id")]
        public int Id { get; set; }

        [Column("nombre")]
        public string Nombre { get; set; } = "";
    }

    // ── usuarios ──────────────────────────────────────────────────────────────
    [Table("usuarios")]
    public class Usuario
    {
        [Key][Column("id")]
        public int Id { get; set; }

        [Column("nombrecompleto")]
        public string NombreCompleto { get; set; } = "";

        [Column("correo")]
        public string Correo { get; set; } = "";

        [Column("passwordhash")]
        public string PasswordHash { get; set; } = "";

        [Column("rolid")]
        public int RolId { get; set; }

        [Column("activo")]
        public bool Activo { get; set; } = true;

        [Column("mfa_secret")]
        public string? MfaSecret { get; set; }

        [Column("mfa_habilitado")]
        public bool MfaHabilitado { get; set; }

        [ForeignKey("RolId")]
        public Rol? Rol { get; set; }
    }

    // ── orquideas ─────────────────────────────────────────────────────────────
    [Table("orquideas")]
    public class Orquidea
    {
        [Key][Column("id")]
        public int Id { get; set; }

        [Column("variedad")]
        public string Variedad { get; set; } = "";

        [Column("tipo")]
        public string Tipo { get; set; } = "";

        [Column("preciobase")]
        public decimal PrecioBase { get; set; }

        [Column("diasvidacomercial")]
        public int DiasVidaComercial { get; set; }
    }

    // ── insumos ───────────────────────────────────────────────────────────────
    [Table("insumos")]
    public class Insumo
    {
        [Key][Column("id")]
        public int Id { get; set; }

        [Column("nombre")]
        public string Nombre { get; set; } = "";

        [Column("unidadmedida")]
        public string UnidadMedida { get; set; } = "";

        [Column("stockactual")]
        public decimal StockActual { get; set; }
    }

    // ── recetas_armado ────────────────────────────────────────────────────────
    [Table("recetas_armado")]
    public class RecetaArmado
    {
        [Key][Column("id")]
        public int Id { get; set; }

        [Column("orquideaid")]
        public int OrquideaId { get; set; }

        [Column("insumoid")]
        public int InsumoId { get; set; }

        [Column("cantidadrequerida")]
        public decimal CantidadRequerida { get; set; }

        [ForeignKey("OrquideaId")]
        public Orquidea? Orquidea { get; set; }

        [ForeignKey("InsumoId")]
        public Insumo? Insumo { get; set; }
    }

    // ── lotes ─────────────────────────────────────────────────────────────────
    [Table("lotes")]
    public class Lote
    {
        [Key][Column("id")]
        public int Id { get; set; }

        [Column("orquideaid")]
        public int OrquideaId { get; set; }

        [Column("codigolote")]
        public string CodigoLote { get; set; } = "";

        [Column("cantidadinicial")]
        public int CantidadInicial { get; set; }

        [Column("cantidadactual")]
        public int CantidadActual { get; set; }

        [Column("fechaingreso")]
        public DateTime FechaIngreso { get; set; }

        [Column("fechafloracion")]
        public DateTime? FechaFloracion { get; set; }

        [Column("fechacaducidad")]
        public DateTime? FechaCaducidad { get; set; }

        [ForeignKey("OrquideaId")]
        public Orquidea? Orquidea { get; set; }
    }

    // ── predicciones_demanda ──────────────────────────────────────────────────
    [Table("predicciones_demanda")]
    public class PrediccionDemanda
    {
        [Key][Column("id")]
        public int Id { get; set; }

        [Column("fechageneracion")]
        public DateTime FechaGeneracion { get; set; }

        [Column("fechaobjetivo")]
        public DateOnly FechaObjetivo { get; set; }

        [Column("orquideaid")]
        public int OrquideaId { get; set; }

        [Column("cantidadestimada")]
        public int CantidadEstimada { get; set; }

        [ForeignKey("OrquideaId")]
        public Orquidea? Orquidea { get; set; }
    }

    // ── ventas ────────────────────────────────────────────────────────────────
    [Table("ventas")]
    public class Venta
    {
        [Key][Column("id")]
        public int Id { get; set; }

        [Column("usuarioid")]
        public int UsuarioId { get; set; }

        [Column("fechaventa")]
        public DateTime FechaVenta { get; set; }

        [Column("total")]
        public decimal Total { get; set; }

        [ForeignKey("UsuarioId")]
        public Usuario? Usuario { get; set; }

        public List<DetalleVenta> Detalles { get; set; } = new();
    }

    // ── detalle_ventas ────────────────────────────────────────────────────────
    [Table("detalle_ventas")]
    public class DetalleVenta
    {
        [Key][Column("id")]
        public int Id { get; set; }

        [Column("ventaid")]
        public int VentaId { get; set; }

        [Column("loteid")]
        public int LoteId { get; set; }

        [Column("cantidad")]
        public int Cantidad { get; set; }

        [Column("preciounitario")]
        public decimal PrecioUnitario { get; set; }

        [Column("subtotal")]
        public decimal Subtotal { get; set; }

        [ForeignKey("VentaId")]
        public Venta? Venta { get; set; }

        [ForeignKey("LoteId")]
        public Lote? Lote { get; set; }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // View Models
    // ═════════════════════════════════════════════════════════════════════════

    public class LoginViewModel
    {
        [Required(ErrorMessage = "El correo es requerido")]
        [EmailAddress(ErrorMessage = "Correo inválido")]
        public string Correo { get; set; } = "";

        [Required(ErrorMessage = "La contraseña es requerida")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";
    }

    public class MfaViewModel
    {
        [Required(ErrorMessage = "El código es requerido")]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "El código debe tener 6 dígitos")]
        public string Codigo { get; set; } = "";

        public string? QrCodeBase64 { get; set; }
        public bool EsConfiguracion { get; set; }
    }

    public class DashboardViewModel
    {
        public string NombreUsuario { get; set; } = "";

        public string RolNombre { get; set; } = "";
        public int TotalOrquideas { get; set; }

        public int TotalLotes { get; set; }

        public decimal VentasHoy { get; set; }

        public int LotesCriticos { get; set; }


        public List<
            BloomCast.Contracts.Dashboard.DashboardPredictionResponse
        > Predicciones
        { get; set; } = new();


        public List<
            BloomCast.Contracts.Dashboard.DashboardLotResponse
        > LotesPorCaducar
        { get; set; } = new();
    }
    public class UserCreateViewModel
    {
        [Required(ErrorMessage = "El nombre es requerido")]
        [StringLength(120, ErrorMessage = "Máximo 120 caracteres")]
        [Display(Name = "Nombre completo")]
        public string NombreCompleto { get; set; } = "";

        [Required(ErrorMessage = "El correo es requerido")]
        [EmailAddress(ErrorMessage = "Correo inválido")]
        [StringLength(200)]
        [Display(Name = "Correo electrónico")]
        public string Correo { get; set; } = "";

        [Required(ErrorMessage = "La contraseña es requerida")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Mínimo 8 caracteres")]
        [DataType(DataType.Password)]
        [Display(Name = "Contraseña")]
        public string Password { get; set; } = "";

        [Required(ErrorMessage = "Confirma la contraseña")]
        [DataType(DataType.Password)]
        [Compare("Password", ErrorMessage = "Las contraseñas no coinciden")]
        [Display(Name = "Confirmar contraseña")]
        public string ConfirmarPassword { get; set; } = "";

        [Required(ErrorMessage = "Selecciona un rol")]
        [Display(Name = "Rol")]
        public int RolId { get; set; }

        [Display(Name = "Usuario activo")]
        public bool Activo { get; set; } = true;
    }

    public class UserEditViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "El nombre es requerido")]
        [StringLength(120, ErrorMessage = "Máximo 120 caracteres")]
        [Display(Name = "Nombre completo")]
        public string NombreCompleto { get; set; } = "";

        [Required(ErrorMessage = "El correo es requerido")]
        [EmailAddress(ErrorMessage = "Correo inválido")]
        [StringLength(200)]
        [Display(Name = "Correo electrónico")]
        public string Correo { get; set; } = "";

        [StringLength(100, MinimumLength = 8, ErrorMessage = "Mínimo 8 caracteres")]
        [DataType(DataType.Password)]
        [Display(Name = "Nueva contraseña")]
        public string? NuevaPassword { get; set; }

        [DataType(DataType.Password)]
        [Compare("NuevaPassword", ErrorMessage = "Las contraseñas no coinciden")]
        [Display(Name = "Confirmar nueva contraseña")]
        public string? ConfirmarPassword { get; set; }

        [Required(ErrorMessage = "Selecciona un rol")]
        [Display(Name = "Rol")]
        public int RolId { get; set; }

        [Display(Name = "Usuario activo")]
        public bool Activo { get; set; } = true;

        [Display(Name = "MFA habilitado")]
        public bool MfaHabilitado { get; set; }

        [Display(Name = "Resetear MFA")]
        public bool ResetearMfa { get; set; }
    }
    public class SaleCreateViewModel
    {
        [Required]
        public List<SaleItemViewModel> Items { get; set; } = new();
        public int UsuarioId { get; set; }
        public DateTime Fechaventa { get; set; }

        public decimal Total =>
            Items.Sum(x => x.Subtotal);
    }
    public class SaleItemViewModel
    {
        [Required]
        public int LoteId { get; set; }

        public string? CodigoLote { get; set; }

        public string? OrquideaNombre { get; set; }

        public decimal PrecioUnitario { get; set; }

        [Required]
        [Range(1, 9999)]
        public int Cantidad { get; set; }

        public decimal Subtotal => PrecioUnitario * Cantidad;
    }
    public class LoteCreateViewModel
    {
        [Required(ErrorMessage = "Selecciona una orquídea")]
        [Display(Name = "Orquídea")]
        public int OrquideaId { get; set; }

        [Required(ErrorMessage = "La cantidad es requerida")]
        [Range(1, 9999, ErrorMessage = "La cantidad debe ser mayor a 0")]
        [Display(Name = "Cantidad actual")]
        public int CantidadActual { get; set; }
    }
    public class PasswordVerificationModel
    {
        public string Password { get; set; }
    }
}
