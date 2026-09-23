// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
#pragma warning disable CS8604

using System;
using System.Collections.Generic;

namespace Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts
{
    /// <summary>
    /// Reusable container for request-side GenAI parameters defined by the
    /// OpenTelemetry GenAI semantic conventions. Can be supplied to any GenAI
    /// span (e.g. invoke_agent, chat) that accepts request parameters.
    /// </summary>
    public sealed class GenAiRequestParameters : IEquatable<GenAiRequestParameters>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="GenAiRequestParameters"/> class.
        /// </summary>
        /// <param name="model">The name of the GenAI model the request is being made to (e.g. "gpt-4").</param>
        /// <param name="seed">Requests with the same seed value are more likely to return the same result.</param>
        /// <param name="choiceCount">The target number of candidate completions to return.</param>
        /// <param name="frequencyPenalty">The frequency penalty setting for the request.</param>
        /// <param name="maxTokens">The maximum number of tokens the model generates for the request.</param>
        /// <param name="presencePenalty">The presence penalty setting for the request.</param>
        /// <param name="stopSequences">List of sequences that the model will use to stop generating further tokens.</param>
        /// <param name="temperature">The temperature setting for the request.</param>
        /// <param name="topP">The top_p sampling setting for the request.</param>
        /// <param name="dataSourceId">The data source identifier used for grounding/retrieval (e.g. a knowledge base or index id).</param>
        /// <param name="outputType">The content type requested by the client (e.g. "text", "json", "image").</param>
        /// <param name="systemInstructions">The system/developer instructions provided to the model.</param>
        public GenAiRequestParameters(
            string? model = null,
            int? seed = null,
            int? choiceCount = null,
            double? frequencyPenalty = null,
            int? maxTokens = null,
            double? presencePenalty = null,
            string[]? stopSequences = null,
            double? temperature = null,
            double? topP = null,
            string? dataSourceId = null,
            string? outputType = null,
            string? systemInstructions = null)
        {
            Model = model;
            Seed = seed;
            ChoiceCount = choiceCount;
            FrequencyPenalty = frequencyPenalty;
            MaxTokens = maxTokens;
            PresencePenalty = presencePenalty;
            StopSequences = stopSequences;
            Temperature = temperature;
            TopP = topP;
            DataSourceId = dataSourceId;
            OutputType = outputType;
            SystemInstructions = systemInstructions;
        }

        /// <summary>
        /// Gets the name of the GenAI model the request is being made to.
        /// </summary>
        public string? Model { get; }

        /// <summary>
        /// Gets the request seed used to improve result reproducibility.
        /// </summary>
        public int? Seed { get; }

        /// <summary>
        /// Gets the target number of candidate completions to return.
        /// </summary>
        public int? ChoiceCount { get; }

        /// <summary>
        /// Gets the frequency penalty setting for the request.
        /// </summary>
        public double? FrequencyPenalty { get; }

        /// <summary>
        /// Gets the maximum number of tokens the model generates for the request.
        /// </summary>
        public int? MaxTokens { get; }

        /// <summary>
        /// Gets the presence penalty setting for the request.
        /// </summary>
        public double? PresencePenalty { get; }

        /// <summary>
        /// Gets the list of sequences that the model will use to stop generating further tokens.
        /// </summary>
        public string[]? StopSequences { get; }

        /// <summary>
        /// Gets the temperature setting for the request.
        /// </summary>
        public double? Temperature { get; }

        /// <summary>
        /// Gets the top_p sampling setting for the request.
        /// </summary>
        public double? TopP { get; }

        /// <summary>
        /// Gets the data source identifier used for grounding/retrieval.
        /// </summary>
        public string? DataSourceId { get; }

        /// <summary>
        /// Gets the content type requested by the client.
        /// </summary>
        public string? OutputType { get; }

        /// <summary>
        /// Gets the system/developer instructions provided to the model.
        /// </summary>
        public string? SystemInstructions { get; }

        /// <inheritdoc/>
        public bool Equals(GenAiRequestParameters? other)
        {
            if (other is null)
            {
                return false;
            }

            return string.Equals(Model, other.Model, StringComparison.Ordinal) &&
                   Seed == other.Seed &&
                   ChoiceCount == other.ChoiceCount &&
                   Nullable.Equals(FrequencyPenalty, other.FrequencyPenalty) &&
                   MaxTokens == other.MaxTokens &&
                   Nullable.Equals(PresencePenalty, other.PresencePenalty) &&
                   EqualityComparer<string[]?>.Default.Equals(StopSequences, other.StopSequences) &&
                   Nullable.Equals(Temperature, other.Temperature) &&
                   Nullable.Equals(TopP, other.TopP) &&
                   string.Equals(DataSourceId, other.DataSourceId, StringComparison.Ordinal) &&
                   string.Equals(OutputType, other.OutputType, StringComparison.Ordinal) &&
                   string.Equals(SystemInstructions, other.SystemInstructions, StringComparison.Ordinal);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return Equals(obj as GenAiRequestParameters);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + (Model != null ? StringComparer.Ordinal.GetHashCode(Model) : 0);
                hash = (hash * 31) + (Seed?.GetHashCode() ?? 0);
                hash = (hash * 31) + (ChoiceCount?.GetHashCode() ?? 0);
                hash = (hash * 31) + (FrequencyPenalty?.GetHashCode() ?? 0);
                hash = (hash * 31) + (MaxTokens?.GetHashCode() ?? 0);
                hash = (hash * 31) + (PresencePenalty?.GetHashCode() ?? 0);
                hash = (hash * 31) + EqualityComparer<string[]?>.Default.GetHashCode(StopSequences);
                hash = (hash * 31) + (Temperature?.GetHashCode() ?? 0);
                hash = (hash * 31) + (TopP?.GetHashCode() ?? 0);
                hash = (hash * 31) + (DataSourceId != null ? StringComparer.Ordinal.GetHashCode(DataSourceId) : 0);
                hash = (hash * 31) + (OutputType != null ? StringComparer.Ordinal.GetHashCode(OutputType) : 0);
                hash = (hash * 31) + (SystemInstructions != null ? StringComparer.Ordinal.GetHashCode(SystemInstructions) : 0);
                return hash;
            }
        }
    }
}
