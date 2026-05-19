// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Agents.A365.Observability.Runtime.Common;
using Microsoft.Agents.Builder;
using Microsoft.Agents.Builder.App.UserAuth;
using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;

namespace Agent365AgentFrameworkSampleAgent;

/// <summary>
/// Manual token service demonstrating ContextualTokenResolver usage.
/// Mirrors AgenticTokenCache behavior: stores per-turn credentials during the request,
/// then resolves tokens at export time via UserAuthorization.ExchangeTurnTokenAsync.
/// </summary>
public sealed class MyTokenService
{
    private sealed class Entry
    {
        public UserAuthorization UserAuthorization { get; }
        public ITurnContext TurnContext { get; }
        public string AuthHandlerName { get; }
        public string[] Scopes { get; }
        public string? Token { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }

        public Entry(UserAuthorization userAuth, ITurnContext turnContext, string authHandlerName, string[] scopes)
        {
            UserAuthorization = userAuth;
            TurnContext = turnContext;
            AuthHandlerName = authHandlerName;
            Scopes = scopes;
        }
    }

    private readonly ConcurrentDictionary<string, Entry> _map = new();

    /// <summary>
    /// Registers credentials during the turn so they can be used at export time.
    /// First registration per (agentId, tenantId) wins; subsequent calls are ignored.
    /// </summary>
    public void Register(string agentId, string tenantId, UserAuthorization userAuth, ITurnContext turnContext, string authHandlerName)
    {
        var scopes = EnvironmentUtils.GetObservabilityAuthenticationScope();
        _map.TryAdd($"{agentId}:{tenantId}", new Entry(userAuth, turnContext, authHandlerName, scopes));
    }

    /// <summary>
    /// Resolves an auth token for the given agent and tenant.
    /// Uses cached token if still valid (>5 min until expiry), otherwise exchanges via UserAuthorization.
    /// </summary>
    public async Task<string?> GetTokenAsync(string agentId, string tenantId, string? agenticUserId)
    {
        if (!_map.TryGetValue($"{agentId}:{tenantId}", out var entry))
            return null;

        try
        {
            // Return cached token if still valid (>5 min buffer before expiry).
            if (!string.IsNullOrEmpty(entry.Token) && entry.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(5))
            {
                return entry.Token;
            }

            // Exchange the turn token for an observability-scoped token.
            var token = await entry.UserAuthorization.ExchangeTurnTokenAsync(
                entry.TurnContext,
                entry.AuthHandlerName,
                exchangeConnection: null,
                exchangeScopes: entry.Scopes).ConfigureAwait(false);

            entry.Token = token;
            entry.ExpiresAt = GetTokenExpiration(token);

            return token;
        }
        catch
        {
            return null;
        }
    }

    private static DateTimeOffset? GetTokenExpiration(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);
        return jwtToken.Payload.Expiration == null
            ? null
            : new DateTimeOffset(jwtToken.ValidTo, TimeSpan.Zero);
    }
}
