using System.Net.Http.Json;
using System.Text.Json;
using BloomCast.Contracts.Dashboard;

namespace BloomCast.Services.Api;

public class DashboardApiService
    : IDashboardApiService
{
    private readonly HttpClient _httpClient;


    public DashboardApiService(
        HttpClient httpClient)
    {
        _httpClient = httpClient;
    }


    public async Task<DashboardResponse>
        GetDashboardAsync()
    {
        var response =
            await _httpClient.GetAsync(
                "api/dashboard"
            );


        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                await ObtenerMensajeErrorAsync(
                    response
                )
            );
        }


        var dashboard =
            await response.Content
                .ReadFromJsonAsync<DashboardResponse>();


        if (dashboard == null)
        {
            throw new HttpRequestException(
                "La API no devolvió información del dashboard."
            );
        }


        return dashboard;
    }


    private static async Task<string>
        ObtenerMensajeErrorAsync(
            HttpResponseMessage response)
    {
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
                    return message.GetString()
                        ?? "Error en BloomCast API.";
                }


                if (root.TryGetProperty(
                    "detail",
                    out var detail))
                {
                    return detail.GetString()
                        ?? "Error en BloomCast API.";
                }


                if (root.TryGetProperty(
                    "title",
                    out var title))
                {
                    return title.GetString()
                        ?? "Error en BloomCast API.";
                }
            }
            catch (JsonException)
            {
            }


            return content;
        }


        return
            $"BloomCast API respondió con código " +
            $"{(int)response.StatusCode}.";
    }
}