using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BloomCast.Contracts.Sales;
using System.Net;

namespace BloomCast.Services.Api;

public class SalesApiService : ISalesApiService
{
    private readonly HttpClient _httpClient;

    public SalesApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }


    public async Task<List<SaleResponse>> GetSalesAsync(
        string? search = null,
        DateTime? fecha = null)
    {
        var parameters = new List<string>();

        if (!string.IsNullOrWhiteSpace(search))
        {
            parameters.Add(
                $"search={Uri.EscapeDataString(search)}"
            );
        }

        if (fecha.HasValue)
        {
            parameters.Add(
                $"fecha={fecha.Value:yyyy-MM-dd}"
            );
        }

        var url = "api/sales";

        if (parameters.Count > 0)
            url += "?" + string.Join("&", parameters);


        var response =
            await _httpClient.GetAsync(url);

        await EnsureSuccessAsync(response);


        return await response.Content
            .ReadFromJsonAsync<List<SaleResponse>>()
            ?? new();
    }


    public async Task<SaleDetailResponse?> GetSaleByIdAsync(
        int id)
    {
        var response =
            await _httpClient.GetAsync(
                $"api/sales/{id}"
            );

        if (response.StatusCode ==
            HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response);


        return await response.Content
            .ReadFromJsonAsync<SaleDetailResponse>();
    }


    public async Task<List<SaleUserResponse>> GetUsersAsync()
    {
        var response =
            await _httpClient.GetAsync(
                "api/sales/users"
            );

        await EnsureSuccessAsync(response);


        return await response.Content
            .ReadFromJsonAsync<List<SaleUserResponse>>()
            ?? new();
    }


    public async Task<List<AvailableSaleLotResponse>>
        GetAvailableLotsAsync()
    {
        var response =
            await _httpClient.GetAsync(
                "api/sales/available-lots"
            );

        await EnsureSuccessAsync(response);


        return await response.Content
            .ReadFromJsonAsync<
                List<AvailableSaleLotResponse>>()
            ?? new();
    }


    public async Task<SaleDetailResponse> CreateSaleAsync(
        CreateSaleRequest request)
    {
        var response =
            await _httpClient.PostAsJsonAsync(
                "api/sales",
                request
            );

        await EnsureSuccessAsync(response);


        var result =
            await response.Content
                .ReadFromJsonAsync<SaleDetailResponse>();

        if (result == null)
        {
            throw new HttpRequestException(
                "La API no devolvió la venta creada."
            );
        }

        return result;
    }


    public async Task DeleteSaleAsync(
        int id,
        string password)
    {
        var request =
            new DeleteSaleRequest
            {
                Password = password
            };


        var response =
            await _httpClient.PostAsJsonAsync(
                $"api/sales/{id}/delete-with-password",
                request
            );


        await EnsureSuccessAsync(response);
    }


    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new HttpRequestException(
                "Solo una cuenta de administrador puede realizar esta acción."
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


            throw new HttpRequestException(content);
        }


        throw new HttpRequestException(
            $"BloomCast API respondió con código " +
            $"{(int)response.StatusCode}."
        );
    }
}