using System.Net.Http.Json;
using System.Text.Json;
using BloomCast.Contracts.Lots;

namespace BloomCast.Services.Api;

public class LotsApiService : ILotsApiService
{
    private readonly HttpClient _httpClient;

    public LotsApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }


    // ============================================================
    // GET /api/lots
    // ============================================================

    public async Task<List<LotResponse>> GetLotsAsync(
        string? search = null,
        string? estado = null)
    {
        var parameters = new List<string>();

        if (!string.IsNullOrWhiteSpace(search))
        {
            parameters.Add(
                $"search={Uri.EscapeDataString(search)}"
            );
        }

        if (!string.IsNullOrWhiteSpace(estado))
        {
            parameters.Add(
                $"estado={Uri.EscapeDataString(estado)}"
            );
        }

        var url = "api/lots";

        if (parameters.Count > 0)
        {
            url += "?" + string.Join("&", parameters);
        }


        var response =
            await _httpClient.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                await ObtenerMensajeErrorAsync(response)
            );
        }


        return
            await response.Content
                .ReadFromJsonAsync<List<LotResponse>>()
            ?? new List<LotResponse>();
    }


    // ============================================================
    // GET /api/lots/orchids
    // ============================================================

    public async Task<List<OrchidResponse>> GetOrchidsAsync()
    {
        var response =
            await _httpClient.GetAsync(
                "api/lots/orchids"
            );


        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                await ObtenerMensajeErrorAsync(response)
            );
        }


        return
            await response.Content
                .ReadFromJsonAsync<List<OrchidResponse>>()
            ?? new List<OrchidResponse>();
    }


    // ============================================================
    // POST /api/lots
    // ============================================================

    public async Task<LotResponse> CreateLotAsync(
        CreateLotRequest request)
    {
        var response =
            await _httpClient.PostAsJsonAsync(
                "api/lots",
                request
            );


        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                await ObtenerMensajeErrorAsync(response)
            );
        }


        var lote =
            await response.Content
                .ReadFromJsonAsync<LotResponse>();


        if (lote == null)
        {
            throw new HttpRequestException(
                "La API no devolvió información del lote creado."
            );
        }


        return lote;
    }


    // ============================================================
    // API Error Parser
    // ============================================================

    private static async Task<string> ObtenerMensajeErrorAsync(
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
                        "title",
                        out var title))
                {
                    return title.GetString()
                           ?? "Error en BloomCast API.";
                }
            }
            catch
            {
                // Si no es JSON usamos el contenido original.
            }


            return content;
        }


        return
            $"BloomCast API respondió con código " +
            $"{(int)response.StatusCode}.";
    }
}