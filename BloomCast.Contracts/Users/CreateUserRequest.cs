using System.ComponentModel.DataAnnotations;

namespace BloomCast.Contracts.Users;

public class CreateUserRequest
{
    [Required]
    public string NombreCompleto { get; set; } = "";

    [Required]
    [EmailAddress]
    public string Correo { get; set; } = "";

    [Required]
    [MinLength(8)]
    public string Password { get; set; } = "";

    [Range(1, int.MaxValue)]
    public int RolId { get; set; }

    public bool Activo { get; set; } = true;
}