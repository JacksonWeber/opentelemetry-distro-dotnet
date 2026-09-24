// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;

namespace Microsoft.Agents.A365.Observability.Runtime.DTOs.Builders
{
    /// <summary>
    /// Builds an OpenTelemetry-compliant <see cref="SpanStatus"/> from an exception and, for error
    /// statuses, records the <c>error.type</c> attribute on the span attributes.
    /// </summary>
    /// <remarks>
    /// Centralizes the exception-to-status mapping so that the ETW DTO logging path stays consistent
    /// with the Activity-based scope path (<c>OpenTelemetryScope.RecordError(Exception)</c> in the
    /// <c>Microsoft.OpenTelemetry</c> distro). The distro type cannot be referenced with a
    /// <c>see cref</c> here because this contracts assembly must not depend on the distro.
    /// </remarks>
    public static class SpanStatusBuilder
    {
        private const string RequestFailedExceptionTypeName = "Azure.RequestFailedException";

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

            // Mirrors OpenTelemetryScope.RecordError: prefer the HTTP status from an
            // Azure.RequestFailedException (or any exception derived from it), otherwise fall back to
            // the exception's full type name.
            var errorType = TryGetRequestFailedStatus(error, out var requestStatus)
                ? requestStatus.ToString()
                : error.GetType().FullName ?? "error";

            if (attributes != null)
            {
                attributes[OpenTelemetryConstants.ErrorTypeKey] = errorType;
            }

            return new SpanStatus(SpanStatusCode.Error, error.Message);
        }

        private static bool TryGetRequestFailedStatus(Exception error, out int status)
        {
            status = 0;

            var requestFailedType = FindRequestFailedExceptionType(error.GetType());
            if (requestFailedType == null)
            {
                return false;
            }

            // Read the property off Azure.RequestFailedException itself, mirroring how the compile-time
            // `requestFailed.Status` access binds to the base declaration even for derived exceptions.
            var statusProperty = requestFailedType.GetProperty("Status");
            if (statusProperty?.PropertyType != typeof(int))
            {
                return false;
            }

            var value = (int?)statusProperty.GetValue(error);
            if (!value.HasValue || value.Value == 0)
            {
                return false;
            }

            status = value.Value;
            return true;
        }

        /// <summary>
        /// Walks the exception's type hierarchy so exceptions derived from
        /// <c>Azure.RequestFailedException</c> keep the HTTP-status behavior of the compile-time
        /// <c>error is RequestFailedException</c> check this reflection shim replaced.
        /// </summary>
        /// <returns>The <c>Azure.RequestFailedException</c> type in the hierarchy, or <c>null</c>.</returns>
        private static Type? FindRequestFailedExceptionType(Type? type)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                if (string.Equals(current.FullName, RequestFailedExceptionTypeName, StringComparison.Ordinal))
                {
                    return current;
                }
            }

            return null;
        }
    }
}
