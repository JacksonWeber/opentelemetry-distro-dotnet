using System.Collections.Concurrent;

namespace Microsoft.OpenTelemetry.Agent365.S2S.Demo;

internal sealed class S2STokenProvider
{
    private static readonly TimeSpan RefreshBuffer = TimeSpan.FromMinutes(1);

    private readonly SampleOptions _options;
    private readonly ITokenExchangeClient _client;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

    internal S2STokenProvider(
        SampleOptions options,
        ITokenExchangeClient client,
        TimeProvider timeProvider)
    {
        _options = options;
        _client = client;
        _timeProvider = timeProvider;
    }

    internal async Task<string?> ResolveAsync(
        string agentId,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(tenantId, _options.TenantId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The exporter requested tenant '{tenantId}', which does not match configured tenant '{_options.TenantId}'.");
        }

        var cacheEntry = _cache.GetOrAdd(
            $"{tenantId}:{agentId}",
            static _ => new CacheEntry());

        var cachedToken = cacheEntry.Token;
        if (IsUsable(cachedToken))
        {
            return cachedToken!.AccessToken;
        }

        await cacheEntry.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cachedToken = cacheEntry.Token;
            if (IsUsable(cachedToken))
            {
                return cachedToken!.AccessToken;
            }

            var blueprintToken = await _client
                .AcquireBlueprintTokenAsync(_options, cancellationToken)
                .ConfigureAwait(false);
            var observabilityToken = await _client
                .AcquireObservabilityTokenAsync(
                    _options,
                    blueprintToken.AccessToken,
                    cancellationToken)
                .ConfigureAwait(false);

            cacheEntry.Token = observabilityToken;
            return observabilityToken.AccessToken;
        }
        finally
        {
            cacheEntry.Gate.Release();
        }
    }

    private bool IsUsable(TokenExchangeResult? token) =>
        token is not null
        && token.ExpiresOn > _timeProvider.GetUtcNow().Add(RefreshBuffer);

    private sealed class CacheEntry
    {
        internal SemaphoreSlim Gate { get; } = new(1, 1);

        internal TokenExchangeResult? Token { get; set; }
    }
}
