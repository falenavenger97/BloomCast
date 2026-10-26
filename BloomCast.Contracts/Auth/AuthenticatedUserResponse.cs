namespace BloomCast.Contracts.Auth;

public class AuthenticatedUserResponse
{
    public string AccessToken { get; set; } = "";

    public DateTime ExpiresUtc { get; set; }

    public int UsuarioId { get; set; }

    public string NombreCompleto { get; set; } = "";

    public string Correo { get; set; } = "";

    public string Rol { get; set; } = "";
}