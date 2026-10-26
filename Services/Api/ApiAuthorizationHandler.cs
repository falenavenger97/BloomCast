using System.Net.Http.Headers;

namespace BloomCast.Services.Api;

public class ApiAuthorizationHandler
    : DelegatingHandler
{
    private readonly IHttpContextAccessor
        _httpContextAccessor;


    public ApiAuthorizationHandler(
        IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor =
            httpContextAccessor;
    }


    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var context =
            _httpContextAccessor.HttpContext;


        var accessToken =
            context?.Session.GetString(
                "AccessToken"
            );


        Console.WriteLine(
            $"JWT PARA API: " +
            $"{!string.IsNullOrWhiteSpace(accessToken)} " +
            $"→ {request.RequestUri}"
        );


        if (!string.IsNullOrWhiteSpace(accessToken)
            &&
            request.Headers.Authorization == null)
        {
            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    accessToken
                );
        }


        return await base.SendAsync(
            request,
            cancellationToken
        );
    }
}