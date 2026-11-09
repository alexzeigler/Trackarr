using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Trackarr.Integrations.Media.Arr;

/// <summary>Registers the HttpClients that <see cref="ArrClientFactory" /> hands to the Radarr and Sonarr clients.</summary>
public static class ArrHttpClients
{
    /// <summary>
    ///     Adds <see cref="ArrClientFactory.HttpClientName" /> and <see cref="ArrClientFactory.InsecureHttpClientName" />.
    ///     Both retry a GET that failed for a reason that may pass: no connection, 408, 429 or a 5xx. They retry it
    ///     twice, about 1 and then 2 seconds later. A 4xx (a wrong API key, a deleted movie) isn't retried, and neither is
    ///     anything but a GET: repeating a DELETE, a profile change or a search command could repeat the action.
    ///     The retries share the server's Request Timeout (<see cref="HttpClient.Timeout" /> covers the whole call), so
    ///     a server that's slow rather than down still times out when it did before.
    /// </summary>
    /// <param name="primaryHandler">Makes the socket handler. The flag is true for the insecure client.</param>
    public static IServiceCollection AddArrHttpClients(this IServiceCollection services,
        Func<IServiceProvider, bool, HttpMessageHandler> primaryHandler)
    {
        return services.AddArrHttpClients(primaryHandler, TimeSpan.FromSeconds(1));
    }

    /// <param name="retryDelay">Before the first retry; the second waits about twice as long. Zero in tests.</param>
    internal static IServiceCollection AddArrHttpClients(this IServiceCollection services,
        Func<IServiceProvider, bool, HttpMessageHandler> primaryHandler, TimeSpan retryDelay)
    {
        foreach (var (name, insecure) in new[]
                 {
                     (ArrClientFactory.HttpClientName, false), (ArrClientFactory.InsecureHttpClientName, true)
                 })
        {
            services.AddHttpClient(name)
                .ConfigurePrimaryHttpMessageHandler(sp => primaryHandler(sp, insecure))
                .AddResilienceHandler("retry", pipeline => pipeline.AddRetry(RetryOptions(retryDelay)));
        }

        return services;
    }

    private static HttpRetryStrategyOptions RetryOptions(TimeSpan delay)
    {
        var options = new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 2,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            Delay = delay,
            MaxDelay = TimeSpan.FromSeconds(5),
            // Radarr and Sonarr don't rate-limit. A proxy's Retry-After could ask for longer than the Request Timeout,
            // and failing with its 429 says more than timing out.
            ShouldRetryAfterHeader = false
        };

        // POST, PUT, PATCH, DELETE and CONNECT.
        options.DisableForUnsafeHttpMethods();
        return options;
    }
}
