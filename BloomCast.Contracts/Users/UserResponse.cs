namespace BloomCast.Contracts.Users;

public class UserResponse
{
    public int Id { get; set; }

    public string NombreCompleto { get; set; } = "";

    public string Correo { get; set; } = "";

    public int RolId { get; set; }

    public string RolNombre { get; set; } = "";

    public bool Activo { get; set; }

    public bool MfaHabilitado { get; set; }
}