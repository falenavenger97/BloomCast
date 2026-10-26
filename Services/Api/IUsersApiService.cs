using BloomCast.Contracts.Users;

namespace BloomCast.Services.Api;

public interface IUsersApiService
{
    Task<List<UserResponse>> GetUsersAsync(
        string? search = null,
        int? rolId = null,
        bool? activo = null);

    Task<UserResponse?> GetUserByIdAsync(int id);

    Task<List<RoleResponse>> GetRolesAsync();

    Task<UserResponse> CreateUserAsync(
        CreateUserRequest request);

    Task<UserResponse> UpdateUserAsync(
        int id,
        UpdateUserRequest request);

    Task DeactivateUserAsync(int id);
}