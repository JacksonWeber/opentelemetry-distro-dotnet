// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

#if NET

using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.OpenTelemetry.AzureMonitor.Tests
{
    // ══════════════════════════════════════════════════════════════
    //  1. SDK Creation & Lifecycle
    //     Verifies OpenTelemetrySdk.Create() produces working providers
    // ══════════════════════════════════════════════════════════════

    [Collection("EnvironmentVariableTests")]
    public class NonHostedSdkCreationTests
    {
        private const string TestConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000";

        [Fact]
        public void Create_WithConsoleExporter_ProvidersExist()
        {
            using var sdk = OpenTelemetrySdk.Create(otel =>
            {
                otel.UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console;
                });
            });

            Assert.NotNull(sdk.TracerProvider);
            Assert.NotNull(sdk.MeterProvider);
            Assert.NotNull(sdk.GetLoggerFactory());
        }

        [Fact]
        public void Create_WithAzureMonitor_ProvidersExist()
        {
            using var sdk = OpenTelemetrySdk.Create(otel =>
            {
                otel.UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.AzureMonitor;
                    o.AzureMonitor.ConnectionString = TestConnectionString;
                    o.AzureMonitor.DisableOfflineStorage = true;
                    o.AzureMonitor.EnableLiveMetrics = false;
                });
            });

            Assert.NotNull(sdk.TracerProvider);
            Assert.NotNull(sdk.MeterProvider);
            Assert.NotNull(sdk.GetLoggerFactory());
        }

        [Fact]
        public void Create_Dispose_IsClean()
        {
            var sdk = OpenTelemetrySdk.Create(otel =>
            {
                otel.UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console;
                });
            });

            sdk.Dispose();
        }

        [Fact]
        public void Create_ForceFlush_AllProviders()
        {
            using var sdk = OpenTelemetrySdk.Create(otel =>
            {
                otel.UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console;
                });
            });

            sdk.TracerProvider?.ForceFlush();
            sdk.MeterProvider?.ForceFlush();
            sdk.LoggerProvider?.ForceFlush();
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  1b. Non-Hosted Exporter Behavior (behavioral mirrors of
    //      NonHostedExporterTests which inspect DI internals)
    //     Verifies the Azure Monitor non-hosted fix actually works
    //     end-to-end: traces, metrics, and logs are captured when
    //     using OpenTelemetrySdk.Create() with Azure Monitor.
    // ══════════════════════════════════════════════════════════════

    [Collection("EnvironmentVariableTests")]
    public class NonHostedExporterBehaviorTests
    {
        private const string TestConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000";

        [Fact]
        public void NonHosted_TracesCaptured_WithAzureMonitorConfigured()
        {
            // Mirrors NonHostedExporterTests.AzureMonitor_ExporterHostedService_RemovedFromDI
            // Instead of checking DI internals, verify traces actually flow
            // through the pipeline when there is no IHostedService to start.
            var exportedActivities = new List<Activity>();

            using var sdk = OpenTelemetrySdk.Create(otel =>
            {
                otel.UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.AzureMonitor;
                    o.AzureMonitor.ConnectionString = TestConnectionString;
                    o.AzureMonitor.DisableOfflineStorage = true;
                    o.AzureMonitor.EnableLiveMetrics = false;
                })
                .WithTracing(t => t.AddInMemoryExporter(exportedActivities));
            });

            using var source = new ActivitySource("Experimental.Microsoft.Agents.AI");
            using var activity = source.StartActivity("non-hosted-trace-test");
            activity?.Stop();

            sdk.TracerProvider?.ForceFlush();

            Assert.Contains(exportedActivities, a =>
                a.Source.Name == "Experimental.Microsoft.Agents.AI");
        }

        [Fact]
        public void NonHosted_MetricsCaptured_WithAzureMonitorConfigured()
        {
            // Mirrors NonHostedExporterTests.AzureMonitor_MeterProviderCallback_Registered
            // Instead of checking the callback is registered, verify metrics flow.
            var exportedMetrics = new List<Metric>();

            using var sdk = OpenTelemetrySdk.Create(otel =>
            {
                otel.UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.AzureMonitor;
                    o.AzureMonitor.ConnectionString = TestConnectionString;
                    o.AzureMonitor.DisableOfflineStorage = true;
                    o.AzureMonitor.EnableLiveMetrics = false;
                })
                .WithMetrics(m =>
                {
                    m.AddMeter("Demo.NonHostedExporter");
                    m.AddInMemoryExporter(exportedMetrics);
                });
            });

            using var meter = new Meter("Demo.NonHostedExporter");
            var counter = meter.CreateCounter<long>("demo.exporter.requests");
            counter.Add(1);

            sdk.MeterProvider?.ForceFlush();

            Assert.Contains(exportedMetrics, m =>
                m.MeterName == "Demo.NonHostedExporter" && m.Name == "demo.exporter.requests");
        }

        [Fact]
        public void NonHosted_LogsCaptured_WithAzureMonitorConfigured()
        {
            // Verifies logs flow through the non-hosted pipeline with Azure Monitor configured.
            var exportedLogs = new List<LogRecord>();

            using var sdk = OpenTelemetrySdk.Create(otel =>
            {
                otel.UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.AzureMonitor;
                    o.AzureMonitor.ConnectionString = TestConnectionString;
                    o.AzureMonitor.DisableOfflineStorage = true;
                    o.AzureMonitor.EnableLiveMetrics = false;
                })
                .WithLogging(logging => logging.AddInMemoryExporter(exportedLogs));
            });

            var logger = sdk.GetLoggerFactory().CreateLogger("Demo.NonHostedExporter");
            logger.LogInformation("Non-hosted Azure Monitor log test");

            sdk.LoggerProvider?.ForceFlush();

            Assert.NotEmpty(exportedLogs);
        }

        [Fact]
        public void NoAzureMonitor_ConsoleOnly_StillWorks()
        {
            // Mirrors NonHostedExporterTests.NoAzureMonitor_HostedServices_Untouched
            // Verifies non-hosted SDK works correctly when Azure Monitor is NOT configured.
            const string envVar = "APPLICATIONINSIGHTS_CONNECTION_STRING";
            var original = Environment.GetEnvironmentVariable(envVar);
            try
            {
                Environment.SetEnvironmentVariable(envVar, null);

                var exportedActivities = new List<Activity>();

                using var sdk = OpenTelemetrySdk.Create(otel =>
                {
                    otel.UseMicrosoftOpenTelemetry(o =>
                    {
                        o.Exporters = ExportTarget.Console;
                    })
                    .WithTracing(t => t.AddInMemoryExporter(exportedActivities));
                });

                using var source = new ActivitySource("Experimental.Microsoft.Agents.AI");
                using var activity = source.StartActivity("console-only-test");
                activity?.Stop();

                sdk.TracerProvider?.ForceFlush();

                Assert.Contains(exportedActivities, a =>
                    a.Source.Name == "Experimental.Microsoft.Agents.AI");
            }
            finally
            {
                Environment.SetEnvironmentVariable(envVar, original);
            }
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  2. Signal Enable/Disable via OpenTelemetrySdk.Create()
    //     Verifies traces, metrics, logs can be toggled on/off
    // ══════════════════════════════════════════════════════════════

    public class NonHostedInstrumentationTests
    {
        [Fact]
        public void LoggingEnabled_LogRecordsExported()
        {
            var exportedLogs = new List<LogRecord>();

            using var sdk = OpenTelemetrySdk.Create(otel =>
            {
                otel.UseMicrosoftOpenTelemetry(o =>
                {
                    o.Instrumentation.EnableLogging = true;
                    o.Exporters = ExportTarget.Console;
                })
                .WithLogging(logging => logging.AddInMemoryExporter(exportedLogs));
            });

            var logger = sdk.GetLoggerFactory().CreateLogger("TestCategory");
            logger.LogInformation("This log should be captured");

            sdk.LoggerProvider?.ForceFlush();

            Assert.NotEmpty(exportedLogs);
            Assert.Contains(exportedLogs, r => r.Body?.ToString()?.Contains("captured") == true);
        }

        [Fact]
        public void LoggingDisabled_NoLogRecords()
        {
            var exportedLogs = new List<LogRecord>();

            using var sdk = OpenTelemetrySdk.Create(otel =>
            {
                otel.UseMicrosoftOpenTelemetry(o =>
                {
                    o.Instrumentation.EnableLogging = false;
                    o.Exporters = ExportTarget.Console;
                })
                .WithLogging(logging => logging.AddInMemoryExporter(exportedLogs));
            });

            var logger = sdk.GetLoggerFactory().CreateLogger("TestCategory");
            logger.LogInformation("This log should be suppressed");
            logger.LogError("This error should also be suppressed");

            sdk.LoggerProvider?.ForceFlush();

            Assert.Empty(exportedLogs);
        }

        [Theory]
        [InlineData(true,  true,  true)]
        [InlineData(true,  true,  false)]
        [InlineData(true,  false, true)]
        [InlineData(true,  false, false)]
        [InlineData(false, true,  true)]
        [InlineData(false, true,  false)]
        [InlineData(false, false, true)]
        [InlineData(false, false, false)]
        public void SignalCombinationMatrix(bool enableTracing, bool enableMetrics, bool enableLogging)
        {
            var exportedActivities = new List<Activity>();
            var exportedMetrics = new List<Metric>();
            var exportedLogs = new List<LogRecord>();

            using var sdk = OpenTelemetrySdk.Create(otel =>
            {
                otel.UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console;
                    o.Instrumentation.EnableTracing = enableTracing;
                    o.Instrumentation.EnableMetrics = enableMetrics;
                    o.Instrumentation.EnableLogging = enableLogging;
                })
                // In-memory exporters are added unconditionally so we can verify
                // that disabled signals produce NO telemetry (not just that providers
                // are absent). This matches the DI-based SignalCombinationMatrix pattern
                // in InstrumentationOptionsTests.cs.
                .WithTracing(t => t.AddInMemoryExporter(exportedActivities))
                .WithMetrics(m => m.AddInMemoryExporter(exportedMetrics))
                .WithLogging(logging => logging.AddInMemoryExporter(exportedLogs));
            });

            // Tracing: emit a span from a registered ActivitySource
            using var source = new ActivitySource("Experimental.Microsoft.Agents.AI");
            using var activity = source.StartActivity("combo-test-op");
            activity?.Stop();
            sdk.TracerProvider?.ForceFlush();

            // Metrics: flush to collect any registered meters
            sdk.MeterProvider?.ForceFlush();

            // Logging: emit a log record
            var logger = sdk.GetLoggerFactory().CreateLogger("TestCategory");
            logger.LogInformation("Combo test log");
            sdk.LoggerProvider?.ForceFlush();

            // Assert tracing
            if (enableTracing)
            {
                Assert.Contains(exportedActivities, a =>
                    a.Source.Name == "Experimental.Microsoft.Agents.AI");
            }
            else
            {
                Assert.DoesNotContain(exportedActivities, a =>
                    a.Source.Name == "Experimental.Microsoft.Agents.AI");
            }

            // Assert metrics
            if (enableMetrics)
            {
                Assert.NotNull(sdk.MeterProvider);
            }
            else
            {
                Assert.DoesNotContain(exportedMetrics, m =>
                    m.MeterName == "Microsoft.AspNetCore.Hosting" ||
                    m.MeterName == "System.Net.Http");
            }

            // Assert logging
            if (enableLogging)
            {
                Assert.NotEmpty(exportedLogs);
            }
            else
            {
                Assert.Empty(exportedLogs);
            }
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  3. End-to-End Telemetry Capture
    //     Verifies traces, metrics, logs flow through
    //     OpenTelemetrySdk.Create() with in-memory exporters
    // ══════════════════════════════════════════════════════════════

    public class NonHostedE2ETests
    {
        private readonly ITestOutputHelper _output;

        public NonHostedE2ETests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Theory]
        [InlineData(200)]
        [InlineData(500)]
        public async Task HttpClient_SpansCaptured(int expectedStatusCode)
        {
            var tcpListener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            tcpListener.Start();
            var port = ((IPEndPoint)tcpListener.LocalEndpoint).Port;
            tcpListener.Stop();

            var listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{port}/");
            listener.Start();

            try
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var ctx = await listener.GetContextAsync();
                        ctx.Response.StatusCode = expectedStatusCode;
                        ctx.Response.Close();
                    }
                    catch { }
                });

                var exportedActivities = new List<Activity>();

                using var sdk = OpenTelemetrySdk.Create(otel =>
                {
                    otel.UseMicrosoftOpenTelemetry(o =>
                    {
                        o.Exporters = ExportTarget.Console;
                    })
                    .WithTracing(t => t.AddInMemoryExporter(exportedActivities));
                });

                using var client = new HttpClient();
                try
                {
                    await client.GetAsync($"http://localhost:{port}/test");
                }
                catch { }

                sdk.TracerProvider?.ForceFlush();

                var clientActivities = exportedActivities.Where(a => a.Kind == ActivityKind.Client).ToList();

                _output.WriteLine($"Exported {exportedActivities.Count} activities, {clientActivities.Count} client");
                foreach (var a in exportedActivities)
                {
                    _output.WriteLine($"  {a.Source.Name} {a.Kind} {a.DisplayName} {a.Status}");
                }

                Assert.NotEmpty(clientActivities);
                var activity = clientActivities.First();
                Assert.NotEqual(default, activity.TraceId);
            }
            finally
            {
                listener.Stop();
                listener.Close();
            }
        }

        [Fact]
        public async Task HttpClient_ErrorsCaptured()
        {
            var exportedActivities = new List<Activity>();

            using var sdk = OpenTelemetrySdk.Create(otel =>
            {
                otel.UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console;
                })
                .WithTracing(t => t.AddInMemoryExporter(exportedActivities));
            });

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            try
            {
                await client.GetAsync("http://fakehostthatdoesnotexist.invalid/test");
            }
            catch { }

            sdk.TracerProvider?.ForceFlush();

            var clientActivities = exportedActivities.Where(a => a.Kind == ActivityKind.Client).ToList();

            _output.WriteLine($"Exported {exportedActivities.Count} activities");
            foreach (var a in exportedActivities)
            {
                _output.WriteLine($"  {a.Source.Name} {a.Kind} {a.DisplayName} status={a.Status}");
            }

            Assert.NotEmpty(clientActivities);
            var activity = clientActivities.First();
            Assert.Equal(ActivityStatusCode.Error, activity.Status);
        }

        [Fact]
        public void CustomActivitySource_SpansCaptured()
        {
            var exportedActivities = new List<Activity>();

            using var sdk = OpenTelemetrySdk.Create(otel =>
            {
                otel.UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console;
                })
                .WithTracing(t =>
                {
                    t.AddSource("Demo.NonHosted");
                    t.AddInMemoryExporter(exportedActivities);
                });
            });

            using var activitySource = new ActivitySource("Demo.NonHosted");
            using (var parent = activitySource.StartActivity("ProcessRequest", ActivityKind.Server))
            {
                parent?.SetTag("request.id", 1);

                using (var child = activitySource.StartActivity("DatabaseQuery", ActivityKind.Client))
                {
                    child?.SetTag("db.system", "sqlite");
                }
            }

            sdk.TracerProvider?.ForceFlush();

            _output.WriteLine($"Exported {exportedActivities.Count} activities");
            foreach (var a in exportedActivities)
            {
                _output.WriteLine($"  {a.Source.Name} {a.Kind} {a.DisplayName}");
            }

            Assert.Contains(exportedActivities, a =>
                a.Source.Name == "Demo.NonHosted" && a.DisplayName == "ProcessRequest");
            Assert.Contains(exportedActivities, a =>
                a.Source.Name == "Demo.NonHosted" && a.DisplayName == "DatabaseQuery");
        }

        [Fact]
        public void CustomMeter_MetricsCaptured()
        {
            var exportedMetrics = new List<Metric>();

            using var sdk = OpenTelemetrySdk.Create(otel =>
            {
                otel.UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console;
                })
                .WithMetrics(m =>
                {
                    m.AddMeter("Demo.NonHosted");
                    m.AddInMemoryExporter(exportedMetrics);
                });
            });

            using var meter = new Meter("Demo.NonHosted");
            var counter = meter.CreateCounter<long>("demo.requests", description: "Number of demo requests");
            counter.Add(1);
            counter.Add(2);

            sdk.MeterProvider?.ForceFlush();

            _output.WriteLine($"Exported {exportedMetrics.Count} metrics");
            foreach (var m in exportedMetrics)
            {
                _output.WriteLine($"  {m.MeterName}/{m.Name}");
            }

            Assert.Contains(exportedMetrics, m =>
                m.MeterName == "Demo.NonHosted" && m.Name == "demo.requests");
        }

        [Fact]
        public void LoggerFactory_LogsCaptured()
        {
            var exportedLogs = new List<LogRecord>();

            using var sdk = OpenTelemetrySdk.Create(otel =>
            {
                otel.UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console;
                })
                .WithLogging(logging => logging.AddInMemoryExporter(exportedLogs));
            });

            var logger = sdk.GetLoggerFactory().CreateLogger("Demo.NonHosted");
            logger.LogInformation("Processing request {RequestId}", 42);
            logger.LogError("Simulated error on request {RequestId}", 2);

            sdk.LoggerProvider?.ForceFlush();

            _output.WriteLine($"Exported {exportedLogs.Count} log records");

            Assert.NotEmpty(exportedLogs);
            Assert.True(exportedLogs.Count >= 2, $"Expected at least 2 log records, got {exportedLogs.Count}");
        }

        [Fact]
        public async Task SqlClient_SpansCaptured()
        {
            var exportedActivities = new List<Activity>();
            using var fakeSqlDiagnosticSource = new DiagnosticListener("SqlClientDiagnosticListener");

            using var sdk = OpenTelemetrySdk.Create(otel =>
            {
                otel.UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console;
                })
                .WithTracing(t => t.AddInMemoryExporter(exportedActivities));
            });

            using var sqlConnection = new SqlConnection("Data Source=(localdb)\\MSSQLLocalDB;Database=master");
            using var sqlCommand = sqlConnection.CreateCommand();
            sqlCommand.CommandText = "SP_GetOrders";
            sqlCommand.CommandType = CommandType.StoredProcedure;

            var operationId = Guid.NewGuid();

            fakeSqlDiagnosticSource.Write(
                name: "Microsoft.Data.SqlClient.WriteCommandBefore",
                value: new { OperationId = operationId, Command = sqlCommand, Timestamp = 1000000L });

            fakeSqlDiagnosticSource.Write(
                name: "Microsoft.Data.SqlClient.WriteCommandAfter",
                value: new { OperationId = operationId, Command = sqlCommand, Timestamp = 2000000L });

            sdk.TracerProvider?.ForceFlush();

            _output.WriteLine($"Exported {exportedActivities.Count} activities");
            foreach (var a in exportedActivities)
            {
                _output.WriteLine($"  {a.Source.Name} {a.Kind} {a.DisplayName} tags=[{string.Join(", ", a.Tags.Select(t => $"{t.Key}={t.Value}"))}]");
            }

            var sqlActivities = exportedActivities.Where(a =>
                a.DisplayName.Contains("SP_GetOrders") ||
                a.Tags.Any(t => t.Key == "db.system")).ToList();

            Assert.NotEmpty(sqlActivities);
            var activity = sqlActivities.First();
            Assert.Contains(activity.Tags, t => t.Key == "db.system.name" || t.Key == "db.system");
        }
    }
}
#endif
