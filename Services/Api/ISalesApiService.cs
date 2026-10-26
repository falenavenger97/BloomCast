using BloomCast.Contracts.Sales;

namespace BloomCast.Services.Api;

public interface ISalesApiService
{
    Task<List<SaleResponse>> GetSalesAsync(
        string? search = null,
        DateTime? fecha = null);

    Task<SaleDetailResponse?> GetSaleByIdAsync(int id);

    Task<List<SaleUserResponse>> GetUsersAsync();

    Task<List<AvailableSaleLotResponse>> GetAvailableLotsAsync();

    Task<SaleDetailResponse> CreateSaleAsync(
        CreateSaleRequest request);

    Task DeleteSaleAsync(int id, string password);
}