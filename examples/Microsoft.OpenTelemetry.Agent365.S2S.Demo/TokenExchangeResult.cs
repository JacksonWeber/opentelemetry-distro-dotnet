namespace Microsoft.OpenTelemetry.Agent365.S2S.Demo;

internal sealed record TokenExchangeResult(
    string AccessToken,
    DateTimeOffset ExpiresOn);
