using BloomCast.Contracts.Dashboard;

namespace BloomCast.Services.Api;

public interface IDashboardApiService
{
    Task<DashboardResponse> GetDashboardAsync();
}