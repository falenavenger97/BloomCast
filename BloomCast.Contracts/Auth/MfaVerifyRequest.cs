using System.ComponentModel.DataAnnotations;

namespace BloomCast.Contracts.Auth;

public class MfaVerifyRequest
{
    [Required]
    public string ChallengeToken { get; set; } = "";

    [Required]
    public string Codigo { get; set; } = "";
}