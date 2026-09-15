namespace Microsoft.OpenTelemetry.Agent365.S2S.Demo;

internal interface ITokenExchangeClient
{
    Task<TokenExchangeResult> AcquireBlueprintTokenAsync(
        SampleOptions options,
        CancellationToken cancellationToken);

    Task<TokenExchangeResult> AcquireObservabilityTokenAsync(
        SampleOptions options,
        string clientAssertion,
        CancellationToken cancellationToken);
}
