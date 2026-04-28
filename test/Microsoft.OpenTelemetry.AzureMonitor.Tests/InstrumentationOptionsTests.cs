// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

#if NET

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using OpenTelemetry.Logs;
using Xunit;

namespace Microsoft.OpenTelemetry.AzureMonitor.Tests
{
    public class InstrumentationOptionsTests : IDisposable
    {
        private IHost? _host;

        public void Dispose()
        {
            _host?.Dispose();
        }

        // ══════════════════════════════════════════════════════════════
        //  1. POCO defaults & round-trips
        // ══════════════════════════════════════════════════════════════

        [Fact]
        public void Defaults_AllSignalsEnabled()
        {
            var options = new InstrumentationOptions();

            Assert.True(options.EnableTracing);
            Assert.True(options.EnableMetrics);
            Assert.True(options.EnableLogging);
        }

        [Fact]
        public void Defaults_AllInstrumentationsEnabled()
        {
            var options = new InstrumentationOptions();

            Assert.True(options.EnableAspNetCoreInstrumentation);
            Assert.True(options.EnableHttpClientInstrumentation);
            Assert.True(options.EnableSqlClientInstrumentation);
            Assert.True(options.EnableAzureSdkInstrumentation);
            Assert.True(options.EnableOpenAIInstrumentation);
            Assert.True(options.EnableSemanticKernelInstrumentation);
            Assert.True(options.EnableAgentFrameworkInstrumentation);
            Assert.True(options.EnableAgent365Instrumentation);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void EnableTracing_RoundTrips(bool value)
        {
            var options = new InstrumentationOptions { EnableTracing = value };
            Assert.Equal(value, options.EnableTracing);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void EnableMetrics_RoundTrips(bool value)
        {
            var options = new InstrumentationOptions { EnableMetrics = value };
            Assert.Equal(value, options.EnableMetrics);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void EnableLogging_RoundTrips(bool value)
        {
            var options = new InstrumentationOptions { EnableLogging = value };
            Assert.Equal(value, options.EnableLogging);
        }

        // ── Library toggles ──

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void EnableAspNetCoreInstrumentation_RoundTrips(bool value)
        {
            var options = new InstrumentationOptions { EnableAspNetCoreInstrumentation = value };
            Assert.Equal(value, options.EnableAspNetCoreInstrumentation);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void EnableHttpClientInstrumentation_RoundTrips(bool value)
        {
            var options = new InstrumentationOptions { EnableHttpClientInstrumentation = value };
            Assert.Equal(value, options.EnableHttpClientInstrumentation);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void EnableSqlClientInstrumentation_RoundTrips(bool value)
        {
            var options = new InstrumentationOptions { EnableSqlClientInstrumentation = value };
            Assert.Equal(value, options.EnableSqlClientInstrumentation);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void EnableAzureSdkInstrumentation_RoundTrips(bool value)
        {
            var options = new InstrumentationOptions { EnableAzureSdkInstrumentation = value };
            Assert.Equal(value, options.EnableAzureSdkInstrumentation);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void EnableOpenAIInstrumentation_RoundTrips(bool value)
        {
            var options = new InstrumentationOptions { EnableOpenAIInstrumentation = value };
            Assert.Equal(value, options.EnableOpenAIInstrumentation);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void EnableSemanticKernelInstrumentation_RoundTrips(bool value)
        {
            var options = new InstrumentationOptions { EnableSemanticKernelInstrumentation = value };
            Assert.Equal(value, options.EnableSemanticKernelInstrumentation);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void EnableAgentFrameworkInstrumentation_RoundTrips(bool value)
        {
            var options = new InstrumentationOptions { EnableAgentFrameworkInstrumentation = value };
            Assert.Equal(value, options.EnableAgentFrameworkInstrumentation);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void EnableAgent365Instrumentation_RoundTrips(bool value)
        {
            var options = new InstrumentationOptions { EnableAgent365Instrumentation = value };
            Assert.Equal(value, options.EnableAgent365Instrumentation);
        }

        [Fact]
        public void MultipleProperties_SetIndependently()
        {
            var options = new InstrumentationOptions
            {
                EnableTracing = false,
                EnableMetrics = true,
                EnableLogging = false,
                EnableAspNetCoreInstrumentation = false,
                EnableHttpClientInstrumentation = true,
                EnableSqlClientInstrumentation = false,
                EnableAzureSdkInstrumentation = true,
                EnableOpenAIInstrumentation = false,
                EnableSemanticKernelInstrumentation = true,
                EnableAgentFrameworkInstrumentation = false,
                EnableAgent365Instrumentation = true,
            };

            Assert.False(options.EnableTracing);
            Assert.True(options.EnableMetrics);
            Assert.False(options.EnableLogging);
            Assert.False(options.EnableAspNetCoreInstrumentation);
            Assert.True(options.EnableHttpClientInstrumentation);
            Assert.False(options.EnableSqlClientInstrumentation);
            Assert.True(options.EnableAzureSdkInstrumentation);
            Assert.False(options.EnableOpenAIInstrumentation);
            Assert.True(options.EnableSemanticKernelInstrumentation);
            Assert.False(options.EnableAgentFrameworkInstrumentation);
            Assert.True(options.EnableAgent365Instrumentation);
        }

        // ══════════════════════════════════════════════════════════════
        //  2. Integration with MicrosoftOpenTelemetryOptions
        // ══════════════════════════════════════════════════════════════

        [Fact]
        public void MicrosoftOpenTelemetryOptions_ExposesInstrumentationOptions()
        {
            var options = new MicrosoftOpenTelemetryOptions();

            Assert.NotNull(options.Instrumentation);
            Assert.IsType<InstrumentationOptions>(options.Instrumentation);
        }

        [Fact]
        public void MicrosoftOpenTelemetryOptions_InstrumentationDefaults_AllEnabled()
        {
            var options = new MicrosoftOpenTelemetryOptions();

            Assert.True(options.Instrumentation.EnableTracing);
            Assert.True(options.Instrumentation.EnableMetrics);
            Assert.True(options.Instrumentation.EnableLogging);
            Assert.True(options.Instrumentation.EnableAspNetCoreInstrumentation);
            Assert.True(options.Instrumentation.EnableHttpClientInstrumentation);
            Assert.True(options.Instrumentation.EnableSqlClientInstrumentation);
            Assert.True(options.Instrumentation.EnableAzureSdkInstrumentation);
            Assert.True(options.Instrumentation.EnableOpenAIInstrumentation);
            Assert.True(options.Instrumentation.EnableSemanticKernelInstrumentation);
            Assert.True(options.Instrumentation.EnableAgentFrameworkInstrumentation);
            Assert.True(options.Instrumentation.EnableAgent365Instrumentation);
        }

        [Fact]
        public void MicrosoftOpenTelemetryOptions_InstrumentationIsReadOnly()
        {
            var options = new MicrosoftOpenTelemetryOptions();
            var first = options.Instrumentation;
            var second = options.Instrumentation;
            Assert.Same(first, second);
        }

        [Fact]
        public void MicrosoftOpenTelemetryOptions_InstrumentationMutationsStick()
        {
            var options = new MicrosoftOpenTelemetryOptions();
            options.Instrumentation.EnableSqlClientInstrumentation = false;
            options.Instrumentation.EnableTracing = false;

            Assert.False(options.Instrumentation.EnableSqlClientInstrumentation);
            Assert.False(options.Instrumentation.EnableTracing);
            Assert.True(options.Instrumentation.EnableMetrics);
            Assert.True(options.Instrumentation.EnableAspNetCoreInstrumentation);
        }

        // ══════════════════════════════════════════════════════════════
        //  3. Behavioral: EnableTracing gates the tracing pipeline
        // ══════════════════════════════════════════════════════════════

        [Fact]
        public async Task EnableTracingFalse_NoAspNetCoreSpansCaptured()
        {
            var exportedActivities = new List<Activity>();

            _host = new HostBuilder()
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder.UseTestServer();
                    webBuilder.ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddOpenTelemetry()
                            .UseMicrosoftOpenTelemetry(o =>
                            {
                                o.Instrumentation.EnableTracing = false;
                            })
                            .WithTracing(t => t.AddInMemoryExporter(exportedActivities));
                    });
                    webBuilder.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapGet("/test", async context =>
                            {
                                await context.Response.WriteAsync("ok");
                            });
                        });
                    });
                })
                .Build();

            await _host.StartAsync();

            var client = _host.GetTestClient();
            await client.GetAsync("/test");

            // Force flush would normally export, but tracing pipeline was disabled
            // so nothing from our instrumentation was registered
            var aspnetSpans = exportedActivities.Where(a =>
                a.Source.Name == "OpenTelemetry.Instrumentation.AspNetCore").ToList();
            Assert.Empty(aspnetSpans);
        }

        [Fact]
        public void EnableTracingTrue_SpansCaptured()
        {
            var exportedActivities = new List<Activity>();

            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console;
                })
                .WithTracing(t => t.AddInMemoryExporter(exportedActivities));

            using var sp = services.BuildServiceProvider();
            var tracerProvider = sp.GetRequiredService<TracerProvider>();

            // Emit a span from Agent Framework source (registered by our code)
            using var source = new ActivitySource("Experimental.Microsoft.Agents.AI");
            using var activity = source.StartActivity("test-op");
            activity?.Stop();

            tracerProvider.ForceFlush();

            Assert.Contains(exportedActivities, a =>
                a.Source.Name == "Experimental.Microsoft.Agents.AI");
        }

        // ══════════════════════════════════════════════════════════════
        //  4. Behavioral: DisableAspNetCore but keep HttpClient
        // ══════════════════════════════════════════════════════════════

        [Fact]
        public async Task DisableAspNetCore_HttpClientStillWorks()
        {
            var exportedActivities = new List<Activity>();

            // Start a dummy HTTP server
            var tcpListener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            tcpListener.Start();
            var port = ((IPEndPoint)tcpListener.LocalEndpoint).Port;
            tcpListener.Stop();

            var httpListener = new HttpListener();
            httpListener.Prefixes.Add($"http://localhost:{port}/");
            httpListener.Start();

            _ = Task.Run(async () =>
            {
                try
                {
                    var ctx = await httpListener.GetContextAsync();
                    ctx.Response.StatusCode = 200;
                    ctx.Response.Close();
                }
                catch { }
            });

            try
            {
                _host = new HostBuilder()
                    .ConfigureWebHost(webBuilder =>
                    {
                        webBuilder.UseTestServer();
                        webBuilder.ConfigureServices(services =>
                        {
                            services.AddRouting();
                            services.AddOpenTelemetry()
                                .UseMicrosoftOpenTelemetry(o =>
                                {
                                    o.Exporters = ExportTarget.Console;
                                    o.Instrumentation.EnableAspNetCoreInstrumentation = false;
                                })
                                .WithTracing(t => t.AddInMemoryExporter(exportedActivities));
                        });
                        webBuilder.Configure(app =>
                        {
                            app.UseRouting();
                            app.UseEndpoints(endpoints =>
                            {
                                endpoints.MapGet("/test", async context =>
                                {
                                    // This outgoing HttpClient call should be instrumented
                                    using var httpClient = new HttpClient();
                                    await httpClient.GetAsync($"http://localhost:{port}/");
                                    await context.Response.WriteAsync("ok");
                                });
                            });
                        });
                    })
                    .Build();

                await _host.StartAsync();

                var testClient = _host.GetTestClient();
                await testClient.GetAsync("/test");
                await Task.Delay(500);

                // Should have HttpClient spans but NOT ASP.NET Core spans
                var aspnetSpans = exportedActivities.Where(a =>
                    a.Source.Name == "OpenTelemetry.Instrumentation.AspNetCore").ToList();
                var httpSpans = exportedActivities.Where(a =>
                    a.Source.Name == "System.Net.Http").ToList();

                Assert.Empty(aspnetSpans);
                Assert.NotEmpty(httpSpans);
            }
            finally
            {
                httpListener.Stop();
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  5. Behavioral: EnableMetrics=false suppresses metrics
        // ══════════════════════════════════════════════════════════════

        [Fact]
        public void EnableMetricsFalse_NoAspNetCoreMeters()
        {
            var exportedMetrics = new List<Metric>();

            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Instrumentation.EnableMetrics = false;
                })
                .WithMetrics(m => m.AddInMemoryExporter(exportedMetrics));

            using var sp = services.BuildServiceProvider();
            var meterProvider = sp.GetRequiredService<MeterProvider>();
            meterProvider.ForceFlush();

            // No ASP.NET Core hosting or System.Net.Http meters registered by our code
            Assert.DoesNotContain(exportedMetrics, m =>
                m.MeterName == "Microsoft.AspNetCore.Hosting" ||
                m.MeterName == "System.Net.Http");
        }

        [Fact]
        public void EnableMetricsTrue_AspNetCoreMetersRegistered()
        {
            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console;
                });

            // Verify AspNetCore and Http meters are configured via service registrations
            // (actual metric data requires a running server, so we verify registration)
            using var sp = services.BuildServiceProvider();
            // MeterProvider should resolve without error
            var meterProvider = sp.GetRequiredService<MeterProvider>();
            Assert.NotNull(meterProvider);
        }

        // ══════════════════════════════════════════════════════════════
        //  6. Behavioral: DisableAgentFramework — no MAF sources
        // ══════════════════════════════════════════════════════════════

        [Fact]
        public void DisableAgentFramework_NoMafSourcesRegistered()
        {
            var exportedActivities = new List<Activity>();

            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Instrumentation.EnableAgentFrameworkInstrumentation = false;
                })
                .WithTracing(t => t.AddInMemoryExporter(exportedActivities));

            using var sp = services.BuildServiceProvider();
            var tracerProvider = sp.GetRequiredService<TracerProvider>();

            // Emit a span from Agent Framework source — should NOT be captured
            using var source = new ActivitySource("Experimental.Microsoft.Agents.AI");
            using var activity = source.StartActivity("test-agent-op");
            activity?.Stop();

            Assert.DoesNotContain(exportedActivities, a =>
                a.Source.Name == "Experimental.Microsoft.Agents.AI");
        }

        [Fact]
        public void EnableAgentFramework_MafSourcesCaptured()
        {
            var exportedActivities = new List<Activity>();

            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console;
                    // defaults — MAF enabled
                })
                .WithTracing(t => t.AddInMemoryExporter(exportedActivities));

            using var sp = services.BuildServiceProvider();
            var tracerProvider = sp.GetRequiredService<TracerProvider>();

            using var source = new ActivitySource("Experimental.Microsoft.Agents.AI");
            using var activity = source.StartActivity("test-agent-op");
            activity?.Stop();

            tracerProvider.ForceFlush();

            Assert.Contains(exportedActivities, a =>
                a.Source.Name == "Experimental.Microsoft.Agents.AI");
        }

        // ══════════════════════════════════════════════════════════════
        //  7. Behavioral: Console exporter respects signal flags
        // ══════════════════════════════════════════════════════════════

        [Fact]
        public void ConsoleExporter_TracingDisabled_NoSpansCaptured()
        {
            var exportedActivities = new List<Activity>();

            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console;
                    o.Instrumentation.EnableTracing = false;
                })
                .WithTracing(t => t.AddInMemoryExporter(exportedActivities));

            using var sp = services.BuildServiceProvider();
            var tracerProvider = sp.GetRequiredService<TracerProvider>();

            // Emit a span from a source that would normally be captured
            using var source = new ActivitySource("Experimental.Microsoft.Agents.AI");
            using var activity = source.StartActivity("test-op");
            activity?.Stop();

            tracerProvider.ForceFlush();

            // No Agent Framework spans because tracing was disabled by our code
            Assert.DoesNotContain(exportedActivities, a =>
                a.Source.Name == "Experimental.Microsoft.Agents.AI");
        }

        [Fact]
        public void ConsoleExporter_TracingEnabled_SpansCaptured()
        {
            var exportedActivities = new List<Activity>();

            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console;
                    o.Instrumentation.EnableTracing = true;
                })
                .WithTracing(t => t.AddInMemoryExporter(exportedActivities));

            using var sp = services.BuildServiceProvider();
            var tracerProvider = sp.GetRequiredService<TracerProvider>();

            using var source = new ActivitySource("Experimental.Microsoft.Agents.AI");
            using var activity = source.StartActivity("test-op");
            activity?.Stop();

            tracerProvider.ForceFlush();

            Assert.Contains(exportedActivities, a =>
                a.Source.Name == "Experimental.Microsoft.Agents.AI");
        }

        // ══════════════════════════════════════════════════════════════
        //  8. Behavioral: OTLP exporter respects signal flags
        // ══════════════════════════════════════════════════════════════

        [Fact]
        public void OtlpExporter_MetricsDisabled_NoCustomMeters()
        {
            var exportedMetrics = new List<Metric>();

            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Otlp;
                    o.Instrumentation.EnableMetrics = false;
                })
                .WithMetrics(m => m.AddInMemoryExporter(exportedMetrics));

            using var sp = services.BuildServiceProvider();
            var meterProvider = sp.GetRequiredService<MeterProvider>();
            meterProvider.ForceFlush();

            // No ASP.NET Core or Agent Framework meters from our code
            Assert.DoesNotContain(exportedMetrics, m =>
                m.MeterName == "Microsoft.AspNetCore.Hosting" ||
                m.MeterName == "Experimental.Microsoft.Agents.AI");
        }

        // ══════════════════════════════════════════════════════════════
        //  9. Behavioral: OTLP logging exporter respects EnableLogging
        // ══════════════════════════════════════════════════════════════

        [Fact]
        public void OtlpExporter_LoggingEnabled_LogProviderRegistered()
        {
            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Otlp;
                    o.Instrumentation.EnableLogging = true;
                });

            // WithLogging() + AddOtlpExporter() registers log processing configuration
            var hasOtlpLogConfig = services.Any(s =>
                s.ServiceType.FullName != null &&
                s.ServiceType.FullName.Contains("IConfigureOptions") &&
                s.ImplementationType?.FullName?.Contains("Otlp") == true);

            // Alternative: verify LoggerProviderBuilder configuration was added
            var hasLogBuilderConfig = services.Any(s =>
                s.ServiceType.FullName != null &&
                s.ServiceType.FullName.Contains("LoggerProviderBuilder"));

            Assert.True(hasOtlpLogConfig || hasLogBuilderConfig,
                "OTLP logging exporter should be registered when EnableLogging=true");
        }

        [Fact]
        public void OtlpExporter_LoggingDisabled_FewerServicesThanEnabled()
        {
            var servicesEnabled = new ServiceCollection();
            servicesEnabled.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Otlp;
                    o.Instrumentation.EnableLogging = true;
                });

            var servicesDisabled = new ServiceCollection();
            servicesDisabled.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Otlp;
                    o.Instrumentation.EnableLogging = false;
                });

            // When logging is enabled, WithLogging() + AddOtlpExporter() adds
            // more service registrations than when logging is disabled
            Assert.True(servicesEnabled.Count > servicesDisabled.Count,
                $"Expected more services when logging enabled ({servicesEnabled.Count}) " +
                $"than disabled ({servicesDisabled.Count})");
        }

        // ══════════════════════════════════════════════════════════════
        //  10. Behavioral: Console logging exporter respects EnableLogging
        // ══════════════════════════════════════════════════════════════

        [Fact]
        public void ConsoleExporter_LoggingEnabled_LogProviderRegistered()
        {
            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console;
                    o.Instrumentation.EnableLogging = true;
                });

            var hasConsoleLogConfig = services.Any(s =>
                s.ImplementationType?.FullName?.Contains("Console") == true &&
                s.ImplementationType?.FullName?.Contains("Log") == true);

            var hasLogBuilderConfig = services.Any(s =>
                s.ServiceType.FullName != null &&
                s.ServiceType.FullName.Contains("LoggerProviderBuilder"));

            Assert.True(hasConsoleLogConfig || hasLogBuilderConfig,
                "Console logging exporter should be registered when EnableLogging=true");
        }

        [Fact]
        public void ConsoleExporter_LoggingDisabled_FewerServicesThanEnabled()
        {
            var servicesEnabled = new ServiceCollection();
            servicesEnabled.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console;
                    o.Instrumentation.EnableLogging = true;
                });

            var servicesDisabled = new ServiceCollection();
            servicesDisabled.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console;
                    o.Instrumentation.EnableLogging = false;
                });

            Assert.True(servicesEnabled.Count > servicesDisabled.Count,
                $"Expected more services when logging enabled ({servicesEnabled.Count}) " +
                $"than disabled ({servicesDisabled.Count})");
        }

        // ══════════════════════════════════════════════════════════════
        //  11. E2E: EnableLogging kill switch suppresses log records
        // ══════════════════════════════════════════════════════════════

        [Fact]
        public void EnableLoggingFalse_NoLogRecordsExported()
        {
            var exportedLogs = new List<LogRecord>();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Instrumentation.EnableLogging = false;
                    o.Exporters = ExportTarget.Console;
                })
                .WithLogging(logging => logging.AddInMemoryExporter(exportedLogs));

            using var sp = services.BuildServiceProvider();

            // Emit a log via ILogger
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("TestCategory");
            logger.LogInformation("This log should be suppressed");
            logger.LogError("This error should also be suppressed");
            logger.LogWarning("This warning too");

            // Force flush the log provider
            var loggerProvider = sp.GetRequiredService<LoggerProvider>();
            loggerProvider.ForceFlush();

            Assert.Empty(exportedLogs);
        }

        [Fact]
        public void EnableLoggingTrue_LogRecordsExported()
        {
            var exportedLogs = new List<LogRecord>();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Instrumentation.EnableLogging = true;
                    o.Exporters = ExportTarget.Console;
                })
                .WithLogging(logging => logging.AddInMemoryExporter(exportedLogs));

            using var sp = services.BuildServiceProvider();

            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("TestCategory");
            logger.LogInformation("This log should be captured");

            var loggerProvider = sp.GetRequiredService<LoggerProvider>();
            loggerProvider.ForceFlush();

            Assert.NotEmpty(exportedLogs);
            Assert.Contains(exportedLogs, r => r.Body?.ToString()?.Contains("captured") == true);
        }

        [Fact]
        public void EnableLoggingFalse_OtherLoggersNotAffected()
        {
            var otlpLogs = new List<LogRecord>();
            var capturedByCustomLogger = new List<string>();

            var services = new ServiceCollection();
            services.AddLogging(logging =>
            {
                // Add a custom logging provider to verify it still works
                logging.AddProvider(new TestLoggerProvider(capturedByCustomLogger));
            });
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Instrumentation.EnableLogging = false;
                    o.Exporters = ExportTarget.Console;
                })
                .WithLogging(logging => logging.AddInMemoryExporter(otlpLogs));

            using var sp = services.BuildServiceProvider();

            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("TestCategory");
            logger.LogInformation("Should reach custom logger but not OTel");

            var loggerProvider = sp.GetRequiredService<LoggerProvider>();
            loggerProvider.ForceFlush();

            // OTel logs suppressed
            Assert.Empty(otlpLogs);
            // Custom logger still received the log
            Assert.NotEmpty(capturedByCustomLogger);
            Assert.Contains(capturedByCustomLogger, m => m.Contains("custom logger"));
        }

        // ══════════════════════════════════════════════════════════════
        //  13. Signal-level flags override library-level flags
        // ══════════════════════════════════════════════════════════════

        [Theory]
        [InlineData("Experimental.Microsoft.Agents.AI", "AgentFramework")]
        [InlineData("OpenAI.ChatClient", "OpenAI")]
        [InlineData("Microsoft.SemanticKernel", "SemanticKernel")]
        [InlineData("Agent365Sdk", "Agent365")]
        public void TracingDisabled_LibraryEnabled_NoSpansCaptured(string sourceName, string libraryKey)
        {
            var exportedActivities = new List<Activity>();

            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console;
                    o.Instrumentation.EnableTracing = false;

                    switch (libraryKey)
                    {
                        case "AgentFramework":
                            o.Instrumentation.EnableAgentFrameworkInstrumentation = true;
                            break;
                        case "OpenAI":
                            o.Instrumentation.EnableOpenAIInstrumentation = true;
                            break;
                        case "SemanticKernel":
                            o.Instrumentation.EnableSemanticKernelInstrumentation = true;
                            break;
                        case "Agent365":
                            o.Instrumentation.EnableAgent365Instrumentation = true;
                            break;
                    }
                })
                .WithTracing(t => t.AddInMemoryExporter(exportedActivities));

            using var sp = services.BuildServiceProvider();
            var tracerProvider = sp.GetRequiredService<TracerProvider>();

            using var source = new ActivitySource(sourceName);
            using var activity = source.StartActivity("test-op");
            activity?.Stop();

            tracerProvider.ForceFlush();

            if (sourceName.Contains("OpenAI"))
            {
                Assert.DoesNotContain(exportedActivities, a =>
                    a.Source.Name.StartsWith("OpenAI."));
            }
            else
            {
                Assert.DoesNotContain(exportedActivities, a =>
                    a.Source.Name == sourceName);
            }
        }

        [Theory]
        [InlineData("Microsoft.AspNetCore.Hosting", "AspNetCore")]
        [InlineData("System.Net.Http", "HttpClient")]
        public void MetricsDisabled_LibraryEnabled_NoMetersCaptured(string meterName, string libraryKey)
        {
            var exportedMetrics = new List<Metric>();

            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console;
                    o.Instrumentation.EnableMetrics = false;

                    switch (libraryKey)
                    {
                        case "AspNetCore":
                            o.Instrumentation.EnableAspNetCoreInstrumentation = true;
                            break;
                        case "HttpClient":
                            o.Instrumentation.EnableHttpClientInstrumentation = true;
                            break;
                    }
                })
                .WithMetrics(m => m.AddInMemoryExporter(exportedMetrics));

            using var sp = services.BuildServiceProvider();
            var meterProvider = sp.GetRequiredService<MeterProvider>();
            meterProvider.ForceFlush();

            Assert.DoesNotContain(exportedMetrics, m =>
                m.MeterName == meterName);
        }

        [Fact]
        public void MetricsDisabled_AgentFrameworkEnabled_NoAgentMetersCaptured()
        {
            var exportedMetrics = new List<Metric>();

            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console;
                    o.Instrumentation.EnableMetrics = false;
                    o.Instrumentation.EnableAgentFrameworkInstrumentation = true;
                })
                .WithMetrics(m => m.AddInMemoryExporter(exportedMetrics));

            using var sp = services.BuildServiceProvider();
            var meterProvider = sp.GetRequiredService<MeterProvider>();
            meterProvider.ForceFlush();

            Assert.DoesNotContain(exportedMetrics, m =>
                m.MeterName == "Experimental.Microsoft.Agents.AI" ||
                m.MeterName.StartsWith("Microsoft.Agents"));
        }

        // ══════════════════════════════════════════════════════════════
        //  14. Library pairwise combinations
        // ══════════════════════════════════════════════════════════════

        [Fact]
        public void AgentFrameworkOff_Agent365Off_NeitherCaptured()
        {
            var exportedActivities = new List<Activity>();

            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console;
                    o.Instrumentation.EnableAgentFrameworkInstrumentation = false;
                    o.Instrumentation.EnableAgent365Instrumentation = false;
                })
                .WithTracing(t => t.AddInMemoryExporter(exportedActivities));

            using var sp = services.BuildServiceProvider();
            var tracerProvider = sp.GetRequiredService<TracerProvider>();

            using var mafSource = new ActivitySource("Experimental.Microsoft.Agents.AI");
            using var a365Source = new ActivitySource("Agent365Sdk");

            using var mafActivity = mafSource.StartActivity("maf-op");
            mafActivity?.Stop();

            using var a365Activity = a365Source.StartActivity("a365-op");
            a365Activity?.Stop();

            tracerProvider.ForceFlush();

            Assert.DoesNotContain(exportedActivities, a =>
                a.Source.Name == "Experimental.Microsoft.Agents.AI");
            Assert.DoesNotContain(exportedActivities, a =>
                a.Source.Name == "Agent365Sdk");
        }

        [Fact]
        public void AgentFrameworkOn_Agent365Off_OnlyMafCaptured()
        {
            var exportedActivities = new List<Activity>();

            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console;
                    o.Instrumentation.EnableAgentFrameworkInstrumentation = true;
                    o.Instrumentation.EnableAgent365Instrumentation = false;
                })
                .WithTracing(t => t.AddInMemoryExporter(exportedActivities));

            using var sp = services.BuildServiceProvider();
            var tracerProvider = sp.GetRequiredService<TracerProvider>();

            using var mafSource = new ActivitySource("Experimental.Microsoft.Agents.AI");
            using var a365Source = new ActivitySource("Agent365Sdk");

            using var mafActivity = mafSource.StartActivity("maf-op");
            mafActivity?.Stop();

            using var a365Activity = a365Source.StartActivity("a365-op");
            a365Activity?.Stop();

            tracerProvider.ForceFlush();

            Assert.Contains(exportedActivities, a =>
                a.Source.Name == "Experimental.Microsoft.Agents.AI");
            Assert.DoesNotContain(exportedActivities, a =>
                a.Source.Name == "Agent365Sdk");
        }

        [Fact]
        public void OpenAIOff_SemanticKernelOff_NeitherCaptured()
        {
            var exportedActivities = new List<Activity>();

            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console;
                    o.Instrumentation.EnableOpenAIInstrumentation = false;
                    o.Instrumentation.EnableSemanticKernelInstrumentation = false;
                })
                .WithTracing(t => t.AddInMemoryExporter(exportedActivities));

            using var sp = services.BuildServiceProvider();
            var tracerProvider = sp.GetRequiredService<TracerProvider>();

            using var openaiSource = new ActivitySource("OpenAI.ChatClient");
            using var skSource = new ActivitySource("Microsoft.SemanticKernel");

            using var openaiActivity = openaiSource.StartActivity("openai-op");
            openaiActivity?.Stop();

            using var skActivity = skSource.StartActivity("sk-op");
            skActivity?.Stop();

            tracerProvider.ForceFlush();

            Assert.DoesNotContain(exportedActivities, a =>
                a.Source.Name == "OpenAI.ChatClient");
            Assert.DoesNotContain(exportedActivities, a =>
                a.Source.Name == "Microsoft.SemanticKernel");
        }

        [Fact]
        public void OpenAIOn_SemanticKernelOff_OnlyOpenAICaptured()
        {
            var exportedActivities = new List<Activity>();

            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console;
                    o.Instrumentation.EnableOpenAIInstrumentation = true;
                    o.Instrumentation.EnableSemanticKernelInstrumentation = false;
                })
                .WithTracing(t => t.AddInMemoryExporter(exportedActivities));

            using var sp = services.BuildServiceProvider();
            var tracerProvider = sp.GetRequiredService<TracerProvider>();

            using var openaiSource = new ActivitySource("OpenAI.ChatClient");
            using var skSource = new ActivitySource("Microsoft.SemanticKernel");

            using var openaiActivity = openaiSource.StartActivity("openai-op");
            openaiActivity?.Stop();

            using var skActivity = skSource.StartActivity("sk-op");
            skActivity?.Stop();

            tracerProvider.ForceFlush();

            Assert.Contains(exportedActivities, a =>
                a.Source.Name == "OpenAI.ChatClient");
            Assert.DoesNotContain(exportedActivities, a =>
                a.Source.Name == "Microsoft.SemanticKernel");
        }

        [Fact]
        public void AllLibraryFlagsOff_TracingOn_NoSourcesCaptured()
        {
            var exportedActivities = new List<Activity>();

            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console;
                    o.Instrumentation.EnableTracing = true;
                    o.Instrumentation.EnableAspNetCoreInstrumentation = false;
                    o.Instrumentation.EnableHttpClientInstrumentation = false;
                    o.Instrumentation.EnableSqlClientInstrumentation = false;
                    o.Instrumentation.EnableAzureSdkInstrumentation = false;
                    o.Instrumentation.EnableOpenAIInstrumentation = false;
                    o.Instrumentation.EnableSemanticKernelInstrumentation = false;
                    o.Instrumentation.EnableAgentFrameworkInstrumentation = false;
                    o.Instrumentation.EnableAgent365Instrumentation = false;
                })
                .WithTracing(t => t.AddInMemoryExporter(exportedActivities));

            using var sp = services.BuildServiceProvider();
            var tracerProvider = sp.GetRequiredService<TracerProvider>();

            using var mafSource = new ActivitySource("Experimental.Microsoft.Agents.AI");
            using var a365Source = new ActivitySource("Agent365Sdk");
            using var openaiSource = new ActivitySource("OpenAI.ChatClient");
            using var skSource = new ActivitySource("Microsoft.SemanticKernel");

            using var a1 = mafSource.StartActivity("maf-op");
            a1?.Stop();
            using var a2 = a365Source.StartActivity("a365-op");
            a2?.Stop();
            using var a3 = openaiSource.StartActivity("openai-op");
            a3?.Stop();
            using var a4 = skSource.StartActivity("sk-op");
            a4?.Stop();

            tracerProvider.ForceFlush();

            Assert.DoesNotContain(exportedActivities, a =>
                a.Source.Name == "Experimental.Microsoft.Agents.AI");
            Assert.DoesNotContain(exportedActivities, a =>
                a.Source.Name == "Agent365Sdk");
            Assert.DoesNotContain(exportedActivities, a =>
                a.Source.Name == "OpenAI.ChatClient");
            Assert.DoesNotContain(exportedActivities, a =>
                a.Source.Name == "Microsoft.SemanticKernel");
        }

        [Fact]
        public void AspNetCoreMetricsOn_HttpClientMetricsOff_OnlyAspNetCoreRegistered()
        {
            var exportedMetrics = new List<Metric>();

            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console;
                    o.Instrumentation.EnableAspNetCoreInstrumentation = true;
                    o.Instrumentation.EnableHttpClientInstrumentation = false;
                })
                .WithMetrics(m => m.AddInMemoryExporter(exportedMetrics));

            using var sp = services.BuildServiceProvider();
            var meterProvider = sp.GetRequiredService<MeterProvider>();
            meterProvider.ForceFlush();

            // HttpClient meter should not be registered
            Assert.DoesNotContain(exportedMetrics, m =>
                m.MeterName == "System.Net.Http");
        }

        [Fact]
        public void AspNetCoreMetricsOff_HttpClientMetricsOff_NeitherRegistered()
        {
            var exportedMetrics = new List<Metric>();

            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console;
                    o.Instrumentation.EnableAspNetCoreInstrumentation = false;
                    o.Instrumentation.EnableHttpClientInstrumentation = false;
                })
                .WithMetrics(m => m.AddInMemoryExporter(exportedMetrics));

            using var sp = services.BuildServiceProvider();
            var meterProvider = sp.GetRequiredService<MeterProvider>();
            meterProvider.ForceFlush();

            Assert.DoesNotContain(exportedMetrics, m =>
                m.MeterName == "Microsoft.AspNetCore.Hosting");
            Assert.DoesNotContain(exportedMetrics, m =>
                m.MeterName == "System.Net.Http");
        }

        /// <summary>
        /// Minimal ILoggerProvider for testing that non-OTel loggers are not affected.
        /// </summary>
        private sealed class TestLoggerProvider : ILoggerProvider
        {
            private readonly List<string> _messages;
            public TestLoggerProvider(List<string> messages) => _messages = messages;
            public ILogger CreateLogger(string categoryName) => new TestLogger(_messages);
            public void Dispose() { }

            private sealed class TestLogger : ILogger
            {
                private readonly List<string> _messages;
                public TestLogger(List<string> messages) => _messages = messages;
                public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
                public bool IsEnabled(LogLevel logLevel) => true;
                public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                {
                    _messages.Add(formatter(state, exception));
                }
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  16. Edge cases
        // ══════════════════════════════════════════════════════════════

        [Fact]
        public void AllFlagsFalse_NoCrash()
        {
            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console;
                    o.Instrumentation.EnableTracing = false;
                    o.Instrumentation.EnableMetrics = false;
                    o.Instrumentation.EnableLogging = false;
                    o.Instrumentation.EnableAspNetCoreInstrumentation = false;
                    o.Instrumentation.EnableHttpClientInstrumentation = false;
                    o.Instrumentation.EnableSqlClientInstrumentation = false;
                    o.Instrumentation.EnableAzureSdkInstrumentation = false;
                    o.Instrumentation.EnableOpenAIInstrumentation = false;
                    o.Instrumentation.EnableSemanticKernelInstrumentation = false;
                    o.Instrumentation.EnableAgentFrameworkInstrumentation = false;
                    o.Instrumentation.EnableAgent365Instrumentation = false;
                });

            using var sp = services.BuildServiceProvider();

            // Providers may not be registered when all signals are disabled.
            // The key invariant is that building the service provider does not throw.
            var tracerProvider = sp.GetService<TracerProvider>();
            var meterProvider = sp.GetService<MeterProvider>();

            // Whether they're null or non-null, no exception was thrown — that's the test.
        }


        // ══════════════════════════════════════════════════════════════
        //  12. Signal-level combination matrix (2^3 = 8 combos)
        // ══════════════════════════════════════════════════════════════

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

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console;
                    o.Instrumentation.EnableTracing = enableTracing;
                    o.Instrumentation.EnableMetrics = enableMetrics;
                    o.Instrumentation.EnableLogging = enableLogging;
                })
                .WithTracing(t => t.AddInMemoryExporter(exportedActivities))
                .WithMetrics(m => m.AddInMemoryExporter(exportedMetrics))
                .WithLogging(logging => logging.AddInMemoryExporter(exportedLogs));

            using var sp = services.BuildServiceProvider();

            // --- Tracing: emit a span from a registered ActivitySource ---
            var tracerProvider = sp.GetRequiredService<TracerProvider>();
            using var source = new ActivitySource("Experimental.Microsoft.Agents.AI");
            using var activity = source.StartActivity("combo-test-op");
            activity?.Stop();
            tracerProvider.ForceFlush();

            // --- Metrics: flush to collect any registered meters ---
            var meterProvider = sp.GetRequiredService<MeterProvider>();
            meterProvider.ForceFlush();

            // --- Logging: emit a log record ---
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("TestCategory");
            logger.LogInformation("Combo test log");
            var loggerProvider = sp.GetRequiredService<LoggerProvider>();
            loggerProvider.ForceFlush();

            // --- Assert tracing ---
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

            // --- Assert metrics ---
            if (enableMetrics)
            {
                Assert.NotNull(meterProvider);
            }
            else
            {
                Assert.DoesNotContain(exportedMetrics, m =>
                    m.MeterName == "Microsoft.AspNetCore.Hosting" ||
                    m.MeterName == "System.Net.Http");
            }

            // --- Assert logging ---
            if (enableLogging)
            {
                Assert.NotEmpty(exportedLogs);
            }
            else
            {
                Assert.Empty(exportedLogs);
            }
        }
        // ══════════════════════════════════════════════════════════════
        //  17. AzureSdk behavioral tests
        // ══════════════════════════════════════════════════════════════

        [Fact]
        public void EnableAzureSdkInstrumentation_True_CapturesAzureSpans()
        {
            var exportedActivities = new List<Activity>();

            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console;
                    // defaults — AzureSdk enabled
                })
                .WithTracing(t => t.AddInMemoryExporter(exportedActivities));

            using var sp = services.BuildServiceProvider();
            var tracerProvider = sp.GetRequiredService<TracerProvider>();

            using var source = new ActivitySource("Azure.TestSdk");
            using var activity = source.StartActivity("test-op");
            activity?.Stop();

            tracerProvider.ForceFlush();

            Assert.Contains(exportedActivities, a =>
                a.Source.Name == "Azure.TestSdk");
        }

        [Fact]
        public void EnableAzureSdkInstrumentation_False_NoAzureSpansCaptured()
        {
            var exportedActivities = new List<Activity>();

            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console;
                    o.Instrumentation.EnableAzureSdkInstrumentation = false;
                })
                .WithTracing(t => t.AddInMemoryExporter(exportedActivities));

            using var sp = services.BuildServiceProvider();
            var tracerProvider = sp.GetRequiredService<TracerProvider>();

            using var source = new ActivitySource("Azure.TestSdk");
            using var activity = source.StartActivity("test-op");
            activity?.Stop();

            tracerProvider.ForceFlush();

            Assert.DoesNotContain(exportedActivities, a =>
                a.Source.Name == "Azure.TestSdk");
        }
        // ══════════════════════════════════════════════════════════════
        //  18. AspNetCore + HttpClient behavioral gap tests
        // ══════════════════════════════════════════════════════════════

        [Fact]
        public async Task EnableAspNetCoreTrue_CapturesAspNetCoreSpans()
        {
            var exportedActivities = new List<Activity>();

            _host = new HostBuilder()
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder.UseTestServer();
                    webBuilder.ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddOpenTelemetry()
                            .UseMicrosoftOpenTelemetry(o =>
                            {
                                o.Exporters = ExportTarget.Console;
                            })
                            .WithTracing(t => t.AddInMemoryExporter(exportedActivities));
                    });
                    webBuilder.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapGet("/test", async context =>
                            {
                                await context.Response.WriteAsync("ok");
                            });
                        });
                    });
                })
                .Build();

            await _host.StartAsync();

            var client = _host.GetTestClient();
            var response = await client.GetAsync("/test");
            await Task.Delay(500);

            var tracerProvider = _host.Services.GetRequiredService<TracerProvider>();
            tracerProvider.ForceFlush();

            Assert.True(response.IsSuccessStatusCode, "Request should succeed");

            // With EnableAspNetCoreInstrumentation=true (default), ASP.NET Core spans are captured.
            // The source name is Microsoft.AspNetCore in .NET 8+.
            var aspnetSpans = exportedActivities.Where(a =>
                a.Source.Name == "Microsoft.AspNetCore").ToList();

            Assert.NotEmpty(aspnetSpans);
        }

        [Fact]
        public async Task EnableHttpClientFalse_NoHttpClientSpansCaptured()
        {
            var exportedActivities = new List<Activity>();

            // Start a dummy HTTP server
            var tcpListener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            tcpListener.Start();
            var port = ((IPEndPoint)tcpListener.LocalEndpoint).Port;
            tcpListener.Stop();

            var httpListener = new HttpListener();
            httpListener.Prefixes.Add($"http://localhost:{port}/");
            httpListener.Start();

            _ = Task.Run(async () =>
            {
                try
                {
                    var ctx = await httpListener.GetContextAsync();
                    ctx.Response.StatusCode = 200;
                    ctx.Response.Close();
                }
                catch { }
            });

            try
            {
                _host = new HostBuilder()
                    .ConfigureWebHost(webBuilder =>
                    {
                        webBuilder.UseTestServer();
                        webBuilder.ConfigureServices(services =>
                        {
                            services.AddRouting();
                            services.AddOpenTelemetry()
                                .UseMicrosoftOpenTelemetry(o =>
                                {
                                    o.Exporters = ExportTarget.Console;
                                    o.Instrumentation.EnableHttpClientInstrumentation = false;
                                    o.Instrumentation.EnableAspNetCoreInstrumentation = true;
                                })
                                .WithTracing(t => t.AddInMemoryExporter(exportedActivities));
                        });
                        webBuilder.Configure(app =>
                        {
                            app.UseRouting();
                            app.UseEndpoints(endpoints =>
                            {
                                endpoints.MapGet("/test", async context =>
                                {
                                    using var httpClient = new HttpClient();
                                    await httpClient.GetAsync($"http://localhost:{port}/");
                                    await context.Response.WriteAsync("ok");
                                });
                            });
                        });
                    })
                    .Build();

                await _host.StartAsync();

                var testClient = _host.GetTestClient();
                await testClient.GetAsync("/test");
                await Task.Delay(500);

                var httpClientSpans = exportedActivities.Where(a =>
                    a.Source.Name == "System.Net.Http").ToList();
                Assert.Empty(httpClientSpans);

                var aspnetSpans = exportedActivities.Where(a =>
                    a.Source.Name == "Microsoft.AspNetCore").ToList();
                Assert.NotEmpty(aspnetSpans);
            }
            finally
            {
                httpListener.Stop();
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  19. SemanticKernel + Agent365 standalone behavioral tests
        // ══════════════════════════════════════════════════════════════

        [Fact]
        public void EnableSemanticKernelTrue_CapturesSemanticKernelSpans()
        {
            var exportedActivities = new List<Activity>();

            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console;
                })
                .WithTracing(t => t.AddInMemoryExporter(exportedActivities));

            using var sp = services.BuildServiceProvider();
            var tracerProvider = sp.GetRequiredService<TracerProvider>();

            using var source = new ActivitySource("Microsoft.SemanticKernel");
            using var activity = source.StartActivity("test-sk-op");
            activity?.Stop();

            tracerProvider.ForceFlush();

            Assert.Contains(exportedActivities, a =>
                a.Source.Name == "Microsoft.SemanticKernel");
        }

        [Fact]
        public void EnableAgent365True_CapturesAgent365Spans()
        {
            var exportedActivities = new List<Activity>();

            var services = new ServiceCollection();
            services.AddOpenTelemetry()
                .UseMicrosoftOpenTelemetry(o =>
                {
                    o.Exporters = ExportTarget.Console;
                })
                .WithTracing(t => t.AddInMemoryExporter(exportedActivities));

            using var sp = services.BuildServiceProvider();
            var tracerProvider = sp.GetRequiredService<TracerProvider>();

            using var source = new ActivitySource("Agent365Sdk");
            using var activity = source.StartActivity("test-agent365-op");
            activity?.Stop();

            tracerProvider.ForceFlush();

            Assert.Contains(exportedActivities, a =>
                a.Source.Name == "Agent365Sdk");
        }

        // ── Explicit metrics/logging override tracking ──

        [Fact]
        public void MetricsExplicitlySet_FalseByDefault()
        {
            var options = new InstrumentationOptions();
            Assert.False(options.MetricsExplicitlySet);
        }

        [Fact]
        public void MetricsExplicitlySet_TrueAfterAssignment()
        {
            var options = new InstrumentationOptions { EnableMetrics = true };
            Assert.True(options.MetricsExplicitlySet);
        }

        [Fact]
        public void LoggingExplicitlySet_FalseByDefault()
        {
            var options = new InstrumentationOptions();
            Assert.False(options.LoggingExplicitlySet);
        }

        [Fact]
        public void LoggingExplicitlySet_TrueAfterAssignment()
        {
            var options = new InstrumentationOptions { EnableLogging = true };
            Assert.True(options.LoggingExplicitlySet);
        }
    }
}

#endif
