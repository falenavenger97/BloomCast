namespace BloomCast.Contracts.Auth;

public class LoginResponse
{
    public bool RequiresMfa { get; set; }

    public bool RequiresMfaSetup { get; set; }

    public string ChallengeToken { get; set; } = "";

    public string? QrCodeBase64 { get; set; }
}