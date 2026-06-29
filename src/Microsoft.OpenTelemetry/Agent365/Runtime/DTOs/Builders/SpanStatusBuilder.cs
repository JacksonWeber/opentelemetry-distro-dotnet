// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using Azure;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;

namespace Microsoft.Agents.A365.Observability.Runtime.DTOs.Builders
{
    /// <summary>
    /// Builds an OpenTelemetry-compliant <see cref="SpanStatus"/> from an exception and, for error
    /// statuses, records the <c>error.type</c> attribute on the span attributes.
    /// </summary>
    /// <remarks>
    /// Centralizes the exception-to-status mapping so that the ETW DTO logging path stays consistent
    /// with the Activity-based scope path (<see cref="OpenTelemetryScope.RecordError(Exception)"/>).
    /// </remarks>
    public static class SpanStatusBuilder
    {
        /// <summary>
        /// Creates a <see cref="SpanStatus"/> from an optional exception.
        /// </summary>
        /// <param name="error">
        /// The exception to record. When <c>null</c>, an <see cref="SpanStatusCode.Unset"/> status is
        /// returned and any pre-existing <c>error.type</c> attribute is removed so it is never emitted
        /// without an accompanying error status.
        /// </param>
        /// <param name="attributes">
        /// Optional span attributes dictionary. When an error is supplied and this is non-<c>null</c>, the
        /// <c>error.type</c> attribute is written following OTel semantic conventions.
        /// </param>
        /// <returns>
        /// An <see cref="SpanStatusCode.Error"/> status (with the exception message) when <paramref name="error"/>
        /// is non-<c>null</c>; otherwise an <see cref="SpanStatusCode.Unset"/> status.
        /// </returns>
        public static SpanStatus FromError(Exception? error, IDictionary<string, object?>? attributes = null)
        {
            if (error == null)
            {
                // Ensure error.type is never emitted without an accompanying error status, even if a
                // caller passed it through extraAttributes (it is not part of the reserved-key filter).
                attributes?.Remove(OpenTelemetryConstants.ErrorTypeKey);
                return new SpanStatus(SpanStatusCode.Unset);
            }

            // Mirrors OpenTelemetryScope.RecordError: prefer the HTTP status from a RequestFailedException,
            // otherwise fall back to the exception's full type name.
            var errorType = error is RequestFailedException requestFailed && requestFailed.Status != 0
                ? requestFailed.Status.ToString()
                : error.GetType().FullName ?? "error";

            if (attributes != null)
            {
                attributes[OpenTelemetryConstants.ErrorTypeKey] = errorType;
            }

            return new SpanStatus(SpanStatusCode.Error, error.Message);
        }
    }
}
