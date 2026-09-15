using Microsoft.Identity.Client;

namespace Microsoft.OpenTelemetry.Agent365.S2S.Demo;

internal sealed class MsalTokenExchangeClient : ITokenExchangeClient
{
    internal static readonly string[] TokenExchangeScopes =
        ["api://AzureADTokenExchange/.default"];

    private static readonly string[] ObservabilityScopes =
        ["api://9b975845-388f-4429-889e-eab1ef63949c/.default"];

    public async Task<TokenExchangeResult> AcquireBlueprintTokenAsync(
        SampleOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            var application = ConfidentialClientApplicationBuilder
                .Create(options.ClientId)
                .WithClientSecret(options.ClientSecret)
                .WithAuthority(BuildAuthority(options))
                .Build();

            var result = await application
                .AcquireTokenForClient(TokenExchangeScopes)
                .WithFmiPath(options.AgentId)
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);

            return new TokenExchangeResult(result.AccessToken, result.ExpiresOn);
        }
        catch (MsalException exception)
        {
            throw CreateExchangeFailure(
                "The blueprint app token exchange",
                exception);
        }
    }

    public async Task<TokenExchangeResult> AcquireObservabilityTokenAsync(
        SampleOptions options,
        string clientAssertion,
        CancellationToken cancellationToken)
    {
        try
        {
            var application = ConfidentialClientApplicationBuilder
                .Create(options.AgentId)
                .WithClientAssertion(
                    (Func<AssertionRequestOptions, Task<string>>)(_ => Task.FromResult(clientAssertion)))
                .WithAuthority(BuildAuthority(options))
                .Build();

            var result = await application
                .AcquireTokenForClient(ObservabilityScopes)
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);

            return new TokenExchangeResult(result.AccessToken, result.ExpiresOn);
        }
        catch (MsalException exception)
        {
            throw CreateExchangeFailure(
                "The Agent365 observability token exchange",
                exception);
        }
    }

    internal static InvalidOperationException CreateExchangeFailure(
        string stage,
        MsalException exception) =>
        new(
            $"{stage} failed ({exception.ErrorCode}).",
            new SanitizedMsalException(exception.ErrorCode));

    private static string BuildAuthority(SampleOptions options) =>
        $"{options.Authority.AbsoluteUri.TrimEnd('/')}/{options.TenantId}";

    /// <summary>
    /// Wraps an MSAL error code without the original exception's message or
    /// response body, which may contain sensitive request/response content
    /// that should never be surfaced through exception logging.
    /// </summary>
    private sealed class SanitizedMsalException(string errorCode)
        : Exception($"MSAL error code: {errorCode}");
}
