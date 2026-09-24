// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Text.Json.Serialization;
using Microsoft.Agents.A365.Observability.Runtime.Tracing;

namespace Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Tools
{
    /// <summary>
    /// Represents the action taken by an execute tool call.
    /// </summary>
    [JsonConverter(typeof(MessageUtils.SnakeCaseJsonStringEnumConverter))]
    public enum ToolCallAction
    {
        /// <summary>Creates a resource.</summary>
        Create,

        /// <summary>Reads a resource.</summary>
        Read,

        /// <summary>Updates a resource.</summary>
        Update,

        /// <summary>Deletes a resource.</summary>
        Delete,
    }

    /// <summary>
    /// Represents the structured arguments for an execute tool call.
    /// </summary>
    public sealed class ExecuteToolCallArguments
    {
        /// <summary>Gets or sets the schema version.</summary>
        [JsonPropertyName("schema_version")]
        public string? SchemaVersion { get; set; } = "1.0";

        /// <summary>Gets or sets the tool call resources.</summary>
        [JsonPropertyName("resources")]
        public IList<ToolCallResource>? Resources { get; set; }

        /// <summary>Gets or sets the action.</summary>
        [JsonPropertyName("action")]
        public ToolCallAction? Action { get; set; }

        /// <summary>Gets or sets the tool parameters.</summary>
        [JsonPropertyName("parameters")]
        public IDictionary<string, object?>? Parameters { get; set; }

        /// <summary>Gets or sets provider-specific properties not defined by the schema.</summary>
        [JsonExtensionData]
        public IDictionary<string, object?> AdditionalProperties { get; set; } =
            new Dictionary<string, object?>();
    }

    /// <summary>
    /// Represents a resource referenced by an execute tool call.
    /// </summary>
    public sealed class ToolCallResource
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

        /// <summary>Gets or sets provider-specific properties not defined by the schema.</summary>
        [JsonExtensionData]
        public IDictionary<string, object?> AdditionalProperties { get; set; } =
            new Dictionary<string, object?>();
    }

    /// <summary>
    /// Represents a resource identifier.
    /// </summary>
    public sealed class ToolCallIdentifier
    {
        /// <summary>Gets or sets the identifier type.</summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        /// <summary>Gets or sets the identifier value.</summary>
        [JsonPropertyName("value")]
        public string? Value { get; set; }

        /// <summary>Gets or sets provider-specific properties not defined by the schema.</summary>
        [JsonExtensionData]
        public IDictionary<string, object?> AdditionalProperties { get; set; } =
            new Dictionary<string, object?>();
    }

    /// <summary>
    /// Represents the resource container for a tool call.
    /// </summary>
    public sealed class ToolCallContainer
    {
        /// <summary>Gets or sets the container identifier.</summary>
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        /// <summary>Gets or sets the container URI.</summary>
        [JsonPropertyName("uri")]
        public string? Uri { get; set; }

        /// <summary>Gets or sets the container type.</summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        /// <summary>Gets or sets provider-specific properties not defined by the schema.</summary>
        [JsonExtensionData]
        public IDictionary<string, object?> AdditionalProperties { get; set; } =
            new Dictionary<string, object?>();
    }
}
