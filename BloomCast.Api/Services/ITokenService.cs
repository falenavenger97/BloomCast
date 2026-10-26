using BloomCast.Api.Models;
using BloomCast.Contracts.Auth;

namespace BloomCast.Api.Services;

public interface ITokenService
{
    AuthenticatedUserResponse CreateAccessToken(
        Usuario usuario);

    string CreateMfaChallengeToken(
        Usuario usuario);

    int? ValidateMfaChallengeToken(
        string token);
}