using BloomCast.Contracts.Lots;

namespace BloomCast.Services.Api;

public interface ILotsApiService
{
    Task<List<LotResponse>> GetLotsAsync(
        string? search = null,
        string? estado = null);

    Task<List<OrchidResponse>> GetOrchidsAsync();

    Task<LotResponse> CreateLotAsync(
        CreateLotRequest request);
}