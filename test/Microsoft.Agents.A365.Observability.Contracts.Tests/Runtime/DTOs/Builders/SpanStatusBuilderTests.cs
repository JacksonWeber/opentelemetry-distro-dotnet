// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.DTOs;
using Microsoft.Agents.A365.Observability.Runtime.DTOs.Builders;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;

namespace Microsoft.Agents.A365.Observability.Runtime.Tests.DTOs.Builders
{
    [TestClass]
    public class SpanStatusBuilderTests
    {
        [TestMethod]
        public void FromError_NullError_ReturnsUnset_AndDoesNotAddErrorType()
        {
            var attributes = new Dictionary<string, object?>();

            var status = SpanStatusBuilder.FromError(null, attributes);

            status.Code.Should().Be(SpanStatusCode.Unset);
            status.Message.Should().BeNull();
            attributes.Should().NotContainKey(OpenTelemetryConstants.ErrorTypeKey);
        }

        [TestMethod]
        public void FromError_NullError_RemovesPreExistingErrorType()
        {
            var attributes = new Dictionary<string, object?>
            {
                { OpenTelemetryConstants.ErrorTypeKey, "System.TimeoutException" },
                { "other", "kept" }
            };

            var status = SpanStatusBuilder.FromError(null, attributes);

            status.Code.Should().Be(SpanStatusCode.Unset);
            attributes.Should().NotContainKey(OpenTelemetryConstants.ErrorTypeKey);
            attributes.Should().ContainKey("other");
        }

        [TestMethod]
        public void FromError_WithException_ReturnsError_WithMessage()
        {
            var error = new InvalidOperationException("boom");

            var status = SpanStatusBuilder.FromError(error);

            status.Code.Should().Be(SpanStatusCode.Error);
            status.Message.Should().Be("boom");
        }

        [TestMethod]
        public void FromError_WithException_WritesErrorTypeAttribute_AsFullTypeName()
        {
            var attributes = new Dictionary<string, object?>();
            var error = new TimeoutException("timed out");

            SpanStatusBuilder.FromError(error, attributes);

            attributes.Should().ContainKey(OpenTelemetryConstants.ErrorTypeKey);
            attributes[OpenTelemetryConstants.ErrorTypeKey].Should().Be(typeof(TimeoutException).FullName);
        }

        [TestMethod]
        public void FromError_RequestFailedException_UsesHttpStatusAsErrorType()
        {
            var attributes = new Dictionary<string, object?>();
            var error = new Azure.RequestFailedException(404, "not found");

            var status = SpanStatusBuilder.FromError(error, attributes);

            status.Code.Should().Be(SpanStatusCode.Error);
            attributes[OpenTelemetryConstants.ErrorTypeKey].Should().Be("404");
        }

        [TestMethod]
        public void FromError_DerivedRequestFailedException_UsesHttpStatusAsErrorType()
        {
            var attributes = new Dictionary<string, object?>();
            var error = new Azure.DerivedRequestFailedException(429, "throttled");

            var status = SpanStatusBuilder.FromError(error, attributes);

            status.Code.Should().Be(SpanStatusCode.Error);
            status.Message.Should().Be("throttled");
            attributes[OpenTelemetryConstants.ErrorTypeKey].Should().Be("429");
        }

        [TestMethod]
        public void FromError_DeeplyDerivedRequestFailedException_UsesHttpStatusAsErrorType()
        {
            var attributes = new Dictionary<string, object?>();
            var error = new Azure.GrandchildRequestFailedException(503, "unavailable");

            SpanStatusBuilder.FromError(error, attributes);

            attributes[OpenTelemetryConstants.ErrorTypeKey].Should().Be("503");
        }

        [TestMethod]
        public void FromError_DerivedRequestFailedException_WithZeroStatus_FallsBackToTypeName()
        {
            var attributes = new Dictionary<string, object?>();
            var error = new Azure.DerivedRequestFailedException(0, "no status");

            SpanStatusBuilder.FromError(error, attributes);

            attributes[OpenTelemetryConstants.ErrorTypeKey]
                .Should().Be(typeof(Azure.DerivedRequestFailedException).FullName);
        }

        [TestMethod]
        public void FromError_DerivedRequestFailedException_HidingStatus_UsesBaseHttpStatus()
        {
            var attributes = new Dictionary<string, object?>();
            var error = new Azure.StatusHidingRequestFailedException(409, "conflict");

            SpanStatusBuilder.FromError(error, attributes);

            attributes[OpenTelemetryConstants.ErrorTypeKey].Should().Be("409");
        }

        [TestMethod]
        public void FromError_UnrelatedExceptionNamedLikeRequestFailed_FallsBackToTypeName()
        {
            var attributes = new Dictionary<string, object?>();
            var error = new NotAzure.RequestFailedException(404, "look-alike");

            SpanStatusBuilder.FromError(error, attributes);

            attributes[OpenTelemetryConstants.ErrorTypeKey]
                .Should().Be(typeof(NotAzure.RequestFailedException).FullName);
        }

        [TestMethod]
        public void FromError_NullAttributes_DoesNotThrow()
        {
            var act = () => SpanStatusBuilder.FromError(new Exception("x"), null);

            act.Should().NotThrow();
        }
    }
}

namespace Azure
{
    internal class RequestFailedException : Exception
    {
        public RequestFailedException(int status, string message)
            : base(message)
        {
            Status = status;
        }

        public int Status { get; }
    }

    /// <summary>
    /// Stands in for the SDK-specific exceptions that derive from <c>Azure.RequestFailedException</c>
    /// (for example the service-specific request-failed exceptions shipped by Azure client libraries).
    /// </summary>
    internal class DerivedRequestFailedException : RequestFailedException
    {
        public DerivedRequestFailedException(int status, string message)
            : base(status, message)
        {
        }
    }

    internal sealed class GrandchildRequestFailedException : DerivedRequestFailedException
    {
        public GrandchildRequestFailedException(int status, string message)
            : base(status, message)
        {
        }
    }

    /// <summary>
    /// Hides the base <c>Status</c> property with an incompatible type; the builder must still read the
    /// HTTP status from <c>Azure.RequestFailedException</c>, matching how a compile-time
    /// <c>((RequestFailedException)error).Status</c> access binds.
    /// </summary>
    internal sealed class StatusHidingRequestFailedException : RequestFailedException
    {
        public StatusHidingRequestFailedException(int status, string message)
            : base(status, message)
        {
        }

        public new string Status => "hidden";
    }
}

namespace NotAzure
{
    internal sealed class RequestFailedException : Exception
    {
        public RequestFailedException(int status, string message)
            : base(message)
        {
            Status = status;
        }

        public int Status { get; }
    }
}
