using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;

namespace Crucible.Common.Authentication.ServiceAccounts;

public static class CrucibleHttpClientBuilderExtensions
{
    private static IHttpClientBuilder AddCrucibleAsyncRetryPolicy(this IHttpClientBuilder builder, int maxRetries)
    {
        return builder
            .AddPolicyHandler
            (
                HttpPolicyExtensions
                    .HandleTransientHttpError()
                    .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.NotFound)
                    .WaitAndRetryAsync(maxRetries, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)))
            );
    }

    public static IHttpClientBuilder ConfigureCrucibleServiceAccountClient(this IHttpClientBuilder builder, Uri baseAddress, int maxRetries = 10, int httpClientTimeout = 300)
    {
        return builder
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = baseAddress;
                client.Timeout = TimeSpan.FromSeconds(httpClientTimeout);
            })
            .AddHttpMessageHandler<CrucibleServiceAccountBearerTokenHandler>()
            .AddCrucibleAsyncRetryPolicy(maxRetries);
    }
}
