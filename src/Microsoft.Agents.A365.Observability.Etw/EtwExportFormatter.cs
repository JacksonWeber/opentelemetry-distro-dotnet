// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Agents.A365.Observability.Runtime.DTOs;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Agents.A365.Observability.Runtime.Etw
{
    /// <summary>
    /// Formats ETW log payload dictionaries into the existing JSON envelope.
    /// </summary>
    public sealed class EtwExportFormatter
    {
        /// <summary>
        /// Formats operation data into the ETW JSON payload shape.
        /// </summary>
        /// <param name="data">The operation data to serialize.</param>
        /// <returns>The serialized ETW JSON payload.</returns>
        public string FormatLogData(IDictionary<string, object?> data)
        {
            var payload = new
            {
                Name = data["Name"],
                Attributes = data["Attributes"],
                StartTimeUnixNano = data.TryGetValue("StartTime", out var startTimeObj) && startTimeObj != null
                    ? ToUnixNanos(((DateTimeOffset)startTimeObj).UtcDateTime)
                    : 0,
                EndTimeUnixNano = data.TryGetValue("EndTime", out var endTimeObj) && endTimeObj != null
                    ? ToUnixNanos(((DateTimeOffset)endTimeObj).UtcDateTime)
                    : 0,
                SpanId = data["SpanId"],
                ParentSpanId = data["ParentSpanId"],
                TraceId = data.TryGetValue("TraceId", out var traceIdObj) ? traceIdObj : null,
                Kind = data.TryGetValue("SpanKind", out var spanKindObj) && spanKindObj != null
                    ? spanKindObj
                    : SpanKindConstants.Client,
                Status = data.TryGetValue("Status", out var statusObj) && statusObj != null
                    ? statusObj
                    : new Dictionary<string, object> { ["code"] = 0, ["message"] = string.Empty },
            };

            return JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                PropertyNamingPolicy = null,
                WriteIndented = false,
            });
        }

        private static ulong ToUnixNanos(DateTime utc)
        {
            var value = utc.Kind == DateTimeKind.Utc ? utc : utc.ToUniversalTime();
            var unixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return (ulong)((value - unixEpoch).Ticks * 100);
        }
    }
}
