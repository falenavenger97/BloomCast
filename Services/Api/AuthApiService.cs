using System.Net.Http.Json;
using System.Text.Json;
using BloomCast.Contracts.Auth;

namespace BloomCast.Services.Api;

public class AuthApiService : IAuthApiService
{
    private readonly HttpClient _httpClient;

    public AuthApiService(
        HttpClient httpClient)
    {
        _httpClient = httpClient;
    }


    // ============================================================
    // POST /api/auth/login
    // ============================================================

    public async Task<LoginResponse> LoginAsync(
        LoginRequest request)
    {
        var response =
            await _httpClient.PostAsJsonAsync(
                "api/auth/login",
                request
            );


        await EnsureSuccessAsync(
            response
        );


        var result =
            await response.Content
                .ReadFromJsonAsync<LoginResponse>();


        if (result == null)
        {
            throw new HttpRequestException(
                "La API no devolvió una respuesta válida de autenticación."
            );
        }


        return result;
    }


    // ============================================================
    // POST /api/auth/mfa
    // ============================================================

    public async Task<AuthenticatedUserResponse> VerifyMfaAsync(
        MfaVerifyRequest request)
    {
        var response =
            await _httpClient.PostAsJsonAsync(
                "api/auth/mfa",
                request
            );


        await EnsureSuccessAsync(
            response
        );


        var result =
            await response.Content
                .ReadFromJsonAsync<AuthenticatedUserResponse>();


        if (result == null)
        {
            throw new HttpRequestException(
                "La API no devolvió el token de autenticación."
            );
        }


        return result;
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