using System.ComponentModel.DataAnnotations;

namespace BloomCast.Contracts.Users;

public class UpdateUserRequest
{
    [Required]
    public string NombreCompleto { get; set; } = "";

    [Required]
    [EmailAddress]
    public string Correo { get; set; } = "";

    [Range(1, int.MaxValue)]
    public int RolId { get; set; }

    public bool Activo { get; set; }

    public string? NuevaPassword { get; set; }

    public bool ResetearMfa { get; set; }
}