// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
#pragma warning disable CS8604

using System;
using System.Collections.Generic;

namespace Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts
{
    /// <summary>
    /// Reusable container for response-side GenAI parameters defined by the
    /// OpenTelemetry GenAI semantic conventions (finish reasons and token usage).
    /// Can be supplied to any GenAI span (e.g. invoke_agent, chat) that records a response.
    /// </summary>
    public sealed class GenAiResponseParameters : IEquatable<GenAiResponseParameters>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="GenAiResponseParameters"/> class.
        /// </summary>
        /// <param name="finishReasons">Array of reasons the model stopped generating tokens.</param>
        /// <param name="inputTokens">The number of tokens used in the GenAI input (prompt).</param>
        /// <param name="outputTokens">The number of tokens used in the GenAI response (completion).</param>
        /// <param name="cacheCreationInputTokens">The number of input tokens written to a provider-managed cache.</param>
        /// <param name="cacheReadInputTokens">The number of input tokens served from a provider-managed cache.</param>
        public GenAiResponseParameters(
            string[]? finishReasons = null,
            int? inputTokens = null,
            int? outputTokens = null,
            int? cacheCreationInputTokens = null,
            int? cacheReadInputTokens = null)
        {
            FinishReasons = finishReasons;
            InputTokens = inputTokens;
            OutputTokens = outputTokens;
            CacheCreationInputTokens = cacheCreationInputTokens;
            CacheReadInputTokens = cacheReadInputTokens;
        }

        /// <summary>
        /// Gets the array of reasons the model stopped generating tokens.
        /// </summary>
        public string[]? FinishReasons { get; }

        /// <summary>
        /// Gets the number of tokens used in the GenAI input (prompt).
        /// </summary>
        public int? InputTokens { get; }

        /// <summary>
        /// Gets the number of tokens used in the GenAI response (completion).
        /// </summary>
        public int? OutputTokens { get; }

        /// <summary>
        /// Gets the number of input tokens written to a provider-managed cache.
        /// </summary>
        public int? CacheCreationInputTokens { get; }

        /// <summary>
        /// Gets the number of input tokens served from a provider-managed cache.
        /// </summary>
        public int? CacheReadInputTokens { get; }

        /// <inheritdoc/>
        public bool Equals(GenAiResponseParameters? other)
        {
            if (other is null)
            {
                return false;
            }

            return EqualityComparer<string[]?>.Default.Equals(FinishReasons, other.FinishReasons) &&
                   InputTokens == other.InputTokens &&
                   OutputTokens == other.OutputTokens &&
                   CacheCreationInputTokens == other.CacheCreationInputTokens &&
                   CacheReadInputTokens == other.CacheReadInputTokens;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return Equals(obj as GenAiResponseParameters);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + EqualityComparer<string[]?>.Default.GetHashCode(FinishReasons);
                hash = (hash * 31) + (InputTokens?.GetHashCode() ?? 0);
                hash = (hash * 31) + (OutputTokens?.GetHashCode() ?? 0);
                hash = (hash * 31) + (CacheCreationInputTokens?.GetHashCode() ?? 0);
                hash = (hash * 31) + (CacheReadInputTokens?.GetHashCode() ?? 0);
                return hash;
            }
        }
    }
}
