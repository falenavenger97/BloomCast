using BloomCast.Contracts.Auth;

namespace BloomCast.Services.Api;

public interface IAuthApiService
{
    Task<LoginResponse> LoginAsync(
        LoginRequest request);

    Task<AuthenticatedUserResponse> VerifyMfaAsync(
        MfaVerifyRequest request);
}