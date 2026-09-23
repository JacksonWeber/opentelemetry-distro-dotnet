// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.DTOs;
using Microsoft.Agents.A365.Observability.Runtime.Etw;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Microsoft.Agents.A365.Observability.Runtime.Tests.Etw
{
    [TestClass]
    public class EtwExportFormatterTests
    {
        [TestMethod]
        public void FormatLogData_WithErrorStatus_PreservesExistingJsonShape()
        {
            var data = new InvokeAgentData(
                new Dictionary<string, object?> { ["key"] = "value" },
                startTime: DateTimeOffset.FromUnixTimeSeconds(10),
                endTime: DateTimeOffset.FromUnixTimeSeconds(11),
                spanId: "span",
                parentSpanId: "parent",
                spanKind: "Server",
                traceId: "trace")
            {
                StatusCode = SpanStatusCode.Error,
                StatusMessage = "failed",
            };

            var json = new EtwExportFormatter().FormatLogData(data.ToDictionary());

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            root.GetProperty("Name").GetString().Should().Be("InvokeAgent");
            root.GetProperty("SpanId").GetString().Should().Be("span");
            root.GetProperty("ParentSpanId").GetString().Should().Be("parent");
            root.GetProperty("TraceId").GetString().Should().Be("trace");
            root.GetProperty("Kind").GetString().Should().Be("Server");
            root.GetProperty("Status").GetProperty("code").GetInt32().Should().Be(2);
            root.GetProperty("Status").GetProperty("message").GetString().Should().Be("failed");
        }

        [TestMethod]
        public void FormatLogData_WithoutStatusKey_DefaultsToUnsetStatus()
        {
            var data = new Dictionary<string, object?>
            {
                ["Name"] = "InvokeAgent",
                ["Attributes"] = new Dictionary<string, object?>(),
                ["SpanId"] = "span",
                ["ParentSpanId"] = "parent",
            };

            var json = new EtwExportFormatter().FormatLogData(data);

            using var document = JsonDocument.Parse(json);
            var status = document.RootElement.GetProperty("Status");
            status.GetProperty("code").GetInt32().Should().Be(0);
            status.GetProperty("message").GetString().Should().BeEmpty();
        }

        [TestMethod]
        public void FormatLogData_WithoutSpanKind_DefaultsToClient()
        {
            var data = new InvokeAgentData(
                new Dictionary<string, object?> { ["key"] = "value" },
                spanId: "span");

            var json = new EtwExportFormatter().FormatLogData(data.ToDictionary());

            using var document = JsonDocument.Parse(json);
            document.RootElement.GetProperty("Kind").GetString().Should().Be("Client");
        }

        [TestMethod]
        public void FormatLogData_WithTimestamps_UsesUnixNanoseconds()
        {
            var data = new InvokeAgentData(
                new Dictionary<string, object?> { ["key"] = "value" },
                startTime: DateTimeOffset.FromUnixTimeSeconds(10),
                endTime: DateTimeOffset.FromUnixTimeSeconds(11),
                spanId: "span");

            var json = new EtwExportFormatter().FormatLogData(data.ToDictionary());

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            root.GetProperty("StartTimeUnixNano").GetUInt64().Should().Be(10_000_000_000);
            root.GetProperty("EndTimeUnixNano").GetUInt64().Should().Be(11_000_000_000);
        }

        [TestMethod]
        public void FormatLogData_WithNullOptionalFields_OmitsNullProperties()
        {
            var data = new InvokeAgentData(
                new Dictionary<string, object?> { ["key"] = "value" },
                spanId: "span");

            var json = new EtwExportFormatter().FormatLogData(data.ToDictionary());

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            root.TryGetProperty("ParentSpanId", out _).Should().BeFalse();
            root.TryGetProperty("TraceId", out _).Should().BeFalse();
            root.GetProperty("SpanId").GetString().Should().Be("span");
        }
    }
}
