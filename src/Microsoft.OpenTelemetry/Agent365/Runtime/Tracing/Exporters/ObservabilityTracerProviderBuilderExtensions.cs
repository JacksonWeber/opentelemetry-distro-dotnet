// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using Microsoft.Agents.A365.Observability.Runtime.Common;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Processors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using global::OpenTelemetry;
using global::OpenTelemetry.Trace;
using System;
using System.Diagnostics;
using System.Net.Http;

namespace Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters
{
    /// <summary>
    /// Extension methods to add Agent365 Exporter to OpenTelemetry TracerProviderBuilder.
    /// </summary>
    public static class ObservabilityTracerProviderBuilderExtensions
    {

        /// <summary>
        /// Adds the Agent365 Exporter to the OpenTelemetry TracerProviderBuilder using deferred initialization.
        /// </summary>
        /// <param name="builder">The TracerProviderBuilder to configure.</param>
        /// <param name="exporterType">The Agent365 exporter type to use.</param>
        internal static TracerProviderBuilder AddAgent365Exporter(this TracerProviderBuilder builder, Agent365ExporterType exporterType = Agent365ExporterType.Agent365Exporter)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            var deferredBuilder = builder as IDeferredTracerProviderBuilder;
            if (deferredBuilder == null)
            {
                throw new InvalidOperationException("The provided TracerProviderBuilder does not implement IDeferredTracerProviderBuilder.");
            }

            return deferredBuilder.Configure((sp, builder) => ObservabilityTracerProviderBuilderExtensions.ConfigureInternal(sp, builder, exporterType));
        }

        /// <summary>
        /// Adds the Agent365 Exporter to the OpenTelemetry TracerProviderBuilder using the provided service collection.
        /// </summary>
        /// <param name="builder">The TracerProviderBuilder to configure.</param>
        /// <param name="serviceCollection">The service collection to use for dependency injection.</param>
        /// <param name="exporterType">The Agent365 exporter type to use.</param>
        internal static TracerProviderBuilder AddAgent365Exporter(this TracerProviderBuilder builder, IServiceCollection serviceCollection, Agent365ExporterType exporterType = Agent365ExporterType.Agent365Exporter)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (serviceCollection == null)
            {
                throw new ArgumentNullException(nameof(serviceCollection));
            }

            return ObservabilityTracerProviderBuilderExtensions.ConfigureInternal(
                serviceProvider: serviceCollection.BuildServiceProvider(),
                builder: builder,
                exporterType: exporterType);
        }

        private static TracerProviderBuilder ConfigureInternal(IServiceProvider serviceProvider, TracerProviderBuilder builder, Agent365ExporterType exporterType)
        {
            // Ensure required services are registered
            var exporterOptions = serviceProvider.GetRequiredService<Agent365ExporterOptions>();
            var httpClient = serviceProvider.GetService<HttpClient>();

            // Resolve ILoggerFactory from DI to ensure loggers have proper lifetime; fall back to NullLogger when unavailable.
            var loggerFactory = serviceProvider.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance;
            var logger = serviceProvider.GetService<ILogger<Agent365Exporter>>() ?? loggerFactory.CreateLogger<Agent365Exporter>();
            var coreLogger = serviceProvider.GetService<ILogger<Agent365ExporterCore>>() ?? loggerFactory.CreateLogger<Agent365ExporterCore>();
            var formatterLogger = serviceProvider.GetService<ILogger<ExportFormatter>>() ?? loggerFactory.CreateLogger<ExportFormatter>();

            // Create ExportFormatter
            var exportFormatter = new ExportFormatter(formatterLogger);

            // Create the shared durable store honoring the options — a no-op store when offline storage
            // is disabled or its initialization fails — and hand it, together with the core, to the
            // durable-delivery builder below. The builder injects the store into the core so the live
            // persist path and the background replay drain operate on one queue and shared transmission
            // coordination state, wires each exporter with wireDurableDelivery: true (so it builds and starts its
            // replay coordinator from that shared store, or skips it when the store is disabled), and
            // disposes the eagerly created store if exporter/processor construction throws.
            var storage = Agent365DurableDelivery.CreateStorage(exporterOptions, coreLogger);

            var processor = BuildDurableProcessor(
                exporterType,
                storage,
                exportFormatter,
                coreLogger,
                exporterOptions,
                logger,
                httpClient);

            builder.AddProcessor(processor);
            return builder;
        }

        /// <summary>
        /// Builds the Agent365 export processor with durable delivery wired to the supplied eagerly
        /// created <paramref name="storage"/>. On success the exporter takes ownership of the store (and
        /// disposes it with the provider). If exporter or processor construction throws, the store — and
        /// any already-built exporter — is disposed before the exception propagates, so a partially built
        /// pipeline never leaks the on-disk store's maintenance timer.
        /// </summary>
        internal static BaseProcessor<Activity> BuildDurableProcessor(
            Agent365ExporterType exporterType,
            IAgent365PersistentStorage storage,
            ExportFormatter exportFormatter,
            ILogger<Agent365ExporterCore> coreLogger,
            Agent365ExporterOptions exporterOptions,
            ILogger<Agent365Exporter> logger,
            HttpClient? httpClient)
        {
            // Ownership of the eagerly created store transfers to the exporter the moment its constructor
            // succeeds (the exporter then disposes the store with the provider). Until then this method
            // owns it: if exporter or processor construction throws, dispose the store here so a partially
            // built pipeline never leaks the on-disk store's maintenance timer.
            IAgent365PersistentStorage? owningStorage = storage;
            var exporterCore = new Agent365ExporterCore(exportFormatter, coreLogger, utcNow: null, storage: storage, gate: null);

            try
            {
                switch (exporterType)
                {
                    case Agent365ExporterType.Agent365ExporterAsync:
                        var asyncExporter = new Agent365ExporterAsync(core: exporterCore, logger: logger, options: exporterOptions, resource: null, httpClient: httpClient, replayCoordinator: null, wireDurableDelivery: true);
                        owningStorage = null; // the exporter now owns and will dispose the store
                        try
                        {
                            var asyncBatchProcessor = new BatchActivityExportProcessorAsync(
                                asyncExporter,
                                maxQueueSize: exporterOptions.MaxQueueSize,
                                scheduledDelayMilliseconds: exporterOptions.ScheduledDelayMilliseconds,
                                maxExportBatchSize: exporterOptions.MaxExportBatchSize);
                            return new GenAiActivityFilterProcessor(asyncBatchProcessor);
                        }
                        catch
                        {
                            asyncExporter.Dispose();
                            throw;
                        }

                    case Agent365ExporterType.Agent365Exporter:
                        var syncExporter = new Agent365Exporter(core: exporterCore, logger: logger, options: exporterOptions, resource: null, httpClient: httpClient, replayCoordinator: null, wireDurableDelivery: true);
                        owningStorage = null; // the exporter now owns and will dispose the store
                        try
                        {
                            var batchProcessor = new BatchActivityExportProcessor(
                                syncExporter,
                                maxQueueSize: exporterOptions.MaxQueueSize,
                                scheduledDelayMilliseconds: exporterOptions.ScheduledDelayMilliseconds,
                                exporterTimeoutMilliseconds: exporterOptions.ExporterTimeoutMilliseconds,
                                maxExportBatchSize: exporterOptions.MaxExportBatchSize);
                            return new GenAiActivityFilterProcessor(batchProcessor);
                        }
                        catch
                        {
                            syncExporter.Dispose();
                            throw;
                        }

                    default:
                        throw new ArgumentOutOfRangeException(nameof(exporterType), exporterType, "Unknown Agent365ExporterType specified.");
                }
            }
            catch
            {
                // Reached when exporter construction threw (ownership never transferred) or for an unknown
                // exporter type. When processor construction threw, the inner catch already disposed the
                // exporter (which disposed the store) and nulled ownership, so this is a no-op there.
                owningStorage?.Dispose();
                throw;
            }
        }
    }
}