#pragma warning disable CS8604
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts
{
    /// <summary>
    /// Scope-level configuration for agent invocation tracing.
    /// </summary>
    public sealed class InvokeAgentScopeDetails : IEquatable<InvokeAgentScopeDetails>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="InvokeAgentScopeDetails"/> class
        /// with only an endpoint. Preserved for binary compatibility with consumers
        /// compiled against the original single-parameter constructor.
        /// </summary>
        /// <param name="endpoint">Optional endpoint URI of the agent to invoke.</param>
        public InvokeAgentScopeDetails(Uri? endpoint)
            : this(endpoint, null, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="InvokeAgentScopeDetails"/> class.
        /// </summary>
        /// <param name="endpoint">Optional endpoint URI of the agent to invoke.</param>
        /// <param name="requestParameters">Optional request-side GenAI parameters (model, sampling settings, data source, etc.).</param>
        /// <param name="responseParameters">Optional response-side GenAI parameters (finish reasons, token usage).</param>
        public InvokeAgentScopeDetails(
            Uri? endpoint = null,
            GenAiRequestParameters? requestParameters = null,
            GenAiResponseParameters? responseParameters = null)
        {
            Endpoint = endpoint;
            RequestParameters = requestParameters;
            ResponseParameters = responseParameters;
        }

        /// <summary>
        /// The endpoint URI for the AI agent.
        /// </summary>
        public Uri? Endpoint { get; }

        /// <summary>
        /// Optional request-side GenAI parameters (model, sampling settings, data source, output type, system instructions).
        /// </summary>
        public GenAiRequestParameters? RequestParameters { get; }

        /// <summary>
        /// Optional response-side GenAI parameters (finish reasons and token usage).
        /// </summary>
        public GenAiResponseParameters? ResponseParameters { get; }

        /// <inheritdoc/>
        public bool Equals(InvokeAgentScopeDetails? other)
        {
            if (other is null)
            {
                return false;
            }

            return EqualityComparer<Uri?>.Default.Equals(Endpoint, other.Endpoint) &&
                   EqualityComparer<GenAiRequestParameters?>.Default.Equals(RequestParameters, other.RequestParameters) &&
                   EqualityComparer<GenAiResponseParameters?>.Default.Equals(ResponseParameters, other.ResponseParameters);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return Equals(obj as InvokeAgentScopeDetails);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + EqualityComparer<Uri?>.Default.GetHashCode(Endpoint);
                hash = (hash * 31) + (RequestParameters != null ? RequestParameters.GetHashCode() : 0);
                hash = (hash * 31) + (ResponseParameters != null ? ResponseParameters.GetHashCode() : 0);
                return hash;
            }
        }
    }
}