// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Globalization;
using Azure;
using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.DTOs;
using Microsoft.Agents.A365.Observability.Runtime.DTOs.Builders;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;

namespace Microsoft.OpenTelemetry.Agent365.Tests.Runtime.DTOs.Builders
{
    /// <summary>
    /// Guards the reflection-based <c>Azure.RequestFailedException</c> detection in
    /// <see cref="SpanStatusBuilder"/> against the real Azure.Core type. The contracts assembly cannot
    /// reference Azure.Core, so the contracts-side tests use a local stand-in shape; these tests prove
    /// the reflection shim keeps parity with <c>OpenTelemetryScope.RecordError</c>, whose compile-time
    /// <c>is RequestFailedException</c> pattern also matches derived exceptions.
    /// </summary>
    [TestClass]
    public class SpanStatusBuilderAzureParityTests
    {
        /// <summary>
        /// Representative of the service-specific exceptions Azure client libraries derive from
        /// <see cref="RequestFailedException"/>; the base type is public and unsealed, so a derived
        /// exception is constructible here without taking a dependency on an extra service SDK.
        /// </summary>
        private sealed class ServiceRequestFailedException : RequestFailedException
        {
            public ServiceRequestFailedException(int status, string message)
                : base(status, message)
            {
            }
        }

        [TestMethod]
        public void FromError_RealRequestFailedException_UsesHttpStatusAsErrorType()
        {
            var attributes = new Dictionary<string, object?>();
            var error = new RequestFailedException(404, "not found");

            var status = SpanStatusBuilder.FromError(error, attributes);

            status.Code.Should().Be(SpanStatusCode.Error);
            status.Message.Should().Be(error.Message);
            attributes[OpenTelemetryConstants.ErrorTypeKey].Should().Be("404");
        }

        [TestMethod]
        public void FromError_DerivedRealRequestFailedException_UsesHttpStatusAsErrorType()
        {
            var attributes = new Dictionary<string, object?>();
            var error = new ServiceRequestFailedException(429, "throttled");

            var status = SpanStatusBuilder.FromError(error, attributes);

            status.Code.Should().Be(SpanStatusCode.Error);
            attributes[OpenTelemetryConstants.ErrorTypeKey].Should().Be("429");
        }

        [TestMethod]
        public void FromError_DerivedRealRequestFailedException_MatchesActivityScopeParity()
        {
            var attributes = new Dictionary<string, object?>();
            var error = new ServiceRequestFailedException(500, "server error");

            SpanStatusBuilder.FromError(error, attributes);

            var scopeErrorType = error is RequestFailedException requestFailed && requestFailed.Status != 0
                ? requestFailed.Status.ToString(CultureInfo.InvariantCulture)
                : error.GetType().FullName ?? "error";

            attributes[OpenTelemetryConstants.ErrorTypeKey].Should().Be(scopeErrorType);
        }

        [TestMethod]
        public void FromError_DerivedRealRequestFailedException_WithZeroStatus_FallsBackToTypeName()
        {
            var attributes = new Dictionary<string, object?>();
            var error = new ServiceRequestFailedException(0, "no status");

            SpanStatusBuilder.FromError(error, attributes);

            attributes[OpenTelemetryConstants.ErrorTypeKey]
                .Should().Be(typeof(ServiceRequestFailedException).FullName);
        }

        [TestMethod]
        public void FromError_NonAzureException_FallsBackToTypeName()
        {
            var attributes = new Dictionary<string, object?>();
            var error = new TimeoutException("timed out");

            SpanStatusBuilder.FromError(error, attributes);

            attributes[OpenTelemetryConstants.ErrorTypeKey].Should().Be(typeof(TimeoutException).FullName);
        }
    }
}
