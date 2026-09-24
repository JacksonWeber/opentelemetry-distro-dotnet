// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Text.Json.Serialization;
using Microsoft.Agents.A365.Observability.Runtime.Tracing;

namespace Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Tools
{
    /// <summary>Represents the status of a tool call outcome.</summary>
    [JsonConverter(typeof(MessageUtils.SnakeCaseJsonStringEnumConverter))]
    public enum ToolCallOutcomeStatus
    {
        /// <summary>The tool call completed successfully.</summary>
        Success,

        /// <summary>The tool call failed.</summary>
        Failure,
    }

    /// <summary>Represents a policy decision for a tool call.</summary>
    [JsonConverter(typeof(MessageUtils.SnakeCaseJsonStringEnumConverter))]
    public enum ToolPolicyDecision
    {
        /// <summary>The policy allows the tool call.</summary>
        Allow,

        /// <summary>The policy denies the tool call.</summary>
        Deny,
    }

    /// <summary>Represents the structured result for an execute tool call.</summary>
    public sealed class ExecuteToolCallResult
    {
        /// <summary>Gets or sets the schema version.</summary>
        [JsonPropertyName("schema_version")]
        public string? SchemaVersion { get; set; } = "1.0";

        /// <summary>Gets or sets the tool call outcome.</summary>
        [JsonPropertyName("outcome")]
        public ToolCallResultOutcome? Outcome { get; set; }

        /// <summary>Gets or sets the tool call resources.</summary>
        [JsonPropertyName("resources")]
        public IList<ToolCallResultResource>? Resources { get; set; }

        /// <summary>Gets or sets the tool call data.</summary>
        [JsonPropertyName("data")]
        public IDictionary<string, object?>? Data { get; set; }

        /// <summary>Gets or sets the pagination information.</summary>
        [JsonPropertyName("pagination")]
        public ToolCallResultPagination? Pagination { get; set; }

        /// <summary>Gets or sets provider-specific properties not defined by the schema.</summary>
        [JsonExtensionData]
        public IDictionary<string, object?> AdditionalProperties { get; set; } =
            new Dictionary<string, object?>();
    }

    /// <summary>Represents a resource included in a tool call result.</summary>
    public sealed class ToolCallResultResource
    {
        /// <summary>Gets or sets the resource identifier.</summary>
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        /// <summary>Gets or sets the resource URI.</summary>
        [JsonPropertyName("uri")]
        public string? Uri { get; set; }

        /// <summary>Gets or sets the resource name.</summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        /// <summary>Gets or sets the resource type.</summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        /// <summary>Gets or sets the resource provider.</summary>
        [JsonPropertyName("provider")]
        public string? Provider { get; set; }

        /// <summary>Gets or sets the resource identifiers.</summary>
        [JsonPropertyName("identifiers")]
        public IList<ToolCallIdentifier>? Identifiers { get; set; }

        /// <summary>Gets or sets the resource container.</summary>
        [JsonPropertyName("container")]
        public ToolCallContainer? Container { get; set; }

        /// <summary>Gets or sets the tool call outcome.</summary>
        [JsonPropertyName("outcome")]
        public ToolCallResultOutcome? Outcome { get; set; }

        /// <summary>Gets or sets the sensitivity details.</summary>
        [JsonPropertyName("sensitivity")]
        public ToolCallResultSensitivity? Sensitivity { get; set; }

        /// <summary>Gets or sets the policy details.</summary>
        [JsonPropertyName("policy")]
        public ToolCallResultPolicy? Policy { get; set; }

        /// <summary>Gets or sets the security details.</summary>
        [JsonPropertyName("security")]
        public ToolCallResultSecurity? Security { get; set; }

        /// <summary>Gets or sets the resource data.</summary>
        [JsonPropertyName("data")]
        public IDictionary<string, object?>? Data { get; set; }

        /// <summary>Gets or sets provider-specific properties not defined by the schema.</summary>
        [JsonExtensionData]
        public IDictionary<string, object?> AdditionalProperties { get; set; } =
            new Dictionary<string, object?>();
    }

    /// <summary>Represents the outcome of a tool call.</summary>
    public sealed class ToolCallResultOutcome
    {
        /// <summary>Gets or sets the outcome status.</summary>
        [JsonPropertyName("status")]
        public ToolCallOutcomeStatus? Status { get; set; }

        /// <summary>Gets or sets the tool-specific code.</summary>
        [JsonPropertyName("code")]
        public string? Code { get; set; }

        /// <summary>Gets or sets the provider-specific code.</summary>
        [JsonPropertyName("provider_code")]
        public string? ProviderCode { get; set; }

        /// <summary>Gets or sets the outcome message.</summary>
        [JsonPropertyName("message")]
        public string? Message { get; set; }

        /// <summary>Gets or sets provider-specific properties not defined by the schema.</summary>
        [JsonExtensionData]
        public IDictionary<string, object?> AdditionalProperties { get; set; } =
            new Dictionary<string, object?>();
    }

    /// <summary>Represents sensitivity metadata for a tool call result.</summary>
    public sealed class ToolCallResultSensitivity
    {
        /// <summary>Gets or sets the sensitivity label identifier.</summary>
        [JsonPropertyName("label_id")]
        public string? LabelId { get; set; }

        /// <summary>Gets or sets provider-specific properties not defined by the schema.</summary>
        [JsonExtensionData]
        public IDictionary<string, object?> AdditionalProperties { get; set; } =
            new Dictionary<string, object?>();
    }

    /// <summary>Represents policy metadata for a tool call result.</summary>
    public sealed class ToolCallResultPolicy
    {
        /// <summary>Gets or sets the policy decision.</summary>
        [JsonPropertyName("decision")]
        public ToolPolicyDecision? Decision { get; set; }

        /// <summary>Gets or sets the policy identifier.</summary>
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        /// <summary>Gets or sets the policy name.</summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        /// <summary>Gets or sets provider-specific properties not defined by the schema.</summary>
        [JsonExtensionData]
        public IDictionary<string, object?> AdditionalProperties { get; set; } =
            new Dictionary<string, object?>();
    }

    /// <summary>Represents security metadata for a tool call result.</summary>
    public sealed class ToolCallResultSecurity
    {
        /// <summary>Gets or sets a value indicating whether xpia was detected.</summary>
        [JsonPropertyName("xpia_detected")]
        public bool? XpiaDetected { get; set; }

        /// <summary>Gets or sets provider-specific properties not defined by the schema.</summary>
        [JsonExtensionData]
        public IDictionary<string, object?> AdditionalProperties { get; set; } =
            new Dictionary<string, object?>();
    }

    /// <summary>Represents pagination metadata for a tool call result.</summary>
    public sealed class ToolCallResultPagination
    {
        /// <summary>Gets or sets a value indicating whether more results are available.</summary>
        [JsonPropertyName("has_more")]
        public bool? HasMore { get; set; }

        /// <summary>Gets or sets the cursor for the next page.</summary>
        [JsonPropertyName("next_cursor")]
        public string? NextCursor { get; set; }

        /// <summary>Gets or sets the total result count.</summary>
        [JsonPropertyName("total_count")]
        public long? TotalCount { get; set; }

        /// <summary>Gets or sets provider-specific properties not defined by the schema.</summary>
        [JsonExtensionData]
        public IDictionary<string, object?> AdditionalProperties { get; set; } =
            new Dictionary<string, object?>();
    }
}
