using System.ComponentModel.DataAnnotations;

namespace BloomCast.Contracts.Auth;

public class LoginRequest
{
    [Required]
    [EmailAddress]
    public string Correo { get; set; } = "";

    [Required]
    public string Password { get; set; } = "";
}