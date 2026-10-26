using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BloomCast.Contracts.Users;

namespace BloomCast.Services.Api;

public class UsersApiService : IUsersApiService
{
    private readonly HttpClient _httpClient;


    public UsersApiService(
        HttpClient httpClient)
    {
        _httpClient = httpClient;
    }


    // ============================================================
    // GET /api/users
    // ============================================================

    public async Task<List<UserResponse>> GetUsersAsync(
        string? search = null,
        int? rolId = null,
        bool? activo = null)
    {
        var parameters =
            new List<string>();


        if (!string.IsNullOrWhiteSpace(search))
        {
            parameters.Add(
                $"search={Uri.EscapeDataString(search)}"
            );
        }


        if (rolId.HasValue)
        {
            parameters.Add(
                $"rolId={rolId.Value}"
            );
        }


        if (activo.HasValue)
        {
            parameters.Add(
                $"activo={activo.Value.ToString().ToLowerInvariant()}"
            );
        }


        var url =
            "api/users";


        if (parameters.Count > 0)
        {
            url +=
                "?" +
                string.Join("&", parameters);
        }


        var response =
            await _httpClient.GetAsync(url);


        await EnsureSuccessAsync(
            response
        );


        return await response.Content
            .ReadFromJsonAsync<List<UserResponse>>()
            ?? new List<UserResponse>();
    }


    // ============================================================
    // GET /api/users/{id}
    // ============================================================

    public async Task<UserResponse?> GetUserByIdAsync(
        int id)
    {
        var response =
            await _httpClient.GetAsync(
                $"api/users/{id}"
            );


        if (response.StatusCode ==
            HttpStatusCode.NotFound)
        {
            return null;
        }


        await EnsureSuccessAsync(
            response
        );


        return await response.Content
            .ReadFromJsonAsync<UserResponse>();
    }


    // ============================================================
    // GET /api/users/roles
    // ============================================================

    public async Task<List<RoleResponse>> GetRolesAsync()
    {
        var response =
            await _httpClient.GetAsync(
                "api/users/roles"
            );


        await EnsureSuccessAsync(
            response
        );


        return await response.Content
            .ReadFromJsonAsync<List<RoleResponse>>()
            ?? new List<RoleResponse>();
    }


    // ============================================================
    // POST /api/users
    // ============================================================

    public async Task<UserResponse> CreateUserAsync(
        CreateUserRequest request)
    {
        var response =
            await _httpClient.PostAsJsonAsync(
                "api/users",
                request
            );


        await EnsureSuccessAsync(
            response
        );


        var usuario =
            await response.Content
                .ReadFromJsonAsync<UserResponse>();


        if (usuario == null)
        {
            throw new HttpRequestException(
                "La API no devolvió el usuario creado."
            );
        }


        return usuario;
    }


    // ============================================================
    // PUT /api/users/{id}
    // ============================================================

    public async Task<UserResponse> UpdateUserAsync(
        int id,
        UpdateUserRequest request)
    {
        var response =
            await _httpClient.PutAsJsonAsync(
                $"api/users/{id}",
                request
            );


        await EnsureSuccessAsync(
            response
        );


        var usuario =
            await response.Content
                .ReadFromJsonAsync<UserResponse>();


        if (usuario == null)
        {
            throw new HttpRequestException(
                "La API no devolvió el usuario actualizado."
            );
        }


        return usuario;
    }


    // ============================================================
    // DELETE /api/users/{id}
    // ============================================================

    public async Task DeactivateUserAsync(
        int id)
    {
        var response =
            await _httpClient.DeleteAsync(
                $"api/users/{id}"
            );


        await EnsureSuccessAsync(
            response
        );
    }


    // ============================================================
    // Error parser
    // ============================================================

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new HttpRequestException(
                "Solo una cuenta de administrador puede realizar esta acción."
            );
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new HttpRequestException(
                "Tu sesión ha expirado. Inicia sesión nuevamente."
            );
        }

        var content =
            await response.Content.ReadAsStringAsync();


        if (!string.IsNullOrWhiteSpace(content))
        {
            try
            {
                using var document =
                    JsonDocument.Parse(content);

                var root =
                    document.RootElement;


                if (root.TryGetProperty(
                        "message",
                        out var message))
                {
                    throw new HttpRequestException(
                        message.GetString()
                        ?? "Error en BloomCast API."
                    );
                }


                if (root.TryGetProperty(
                        "detail",
                        out var detail))
                {
                    throw new HttpRequestException(
                        detail.GetString()
                        ?? "Error en BloomCast API."
                    );
                }


                if (root.TryGetProperty(
                        "title",
                        out var title))
                {
                    throw new HttpRequestException(
                        title.GetString()
                        ?? "Error en BloomCast API."
                    );
                }
            }
            catch (JsonException)
            {
            }


            throw new HttpRequestException(
                content
            );
        }


        throw new HttpRequestException(
            $"BloomCast API respondió con código " +
            $"{(int)response.StatusCode}."
        );
    }
}