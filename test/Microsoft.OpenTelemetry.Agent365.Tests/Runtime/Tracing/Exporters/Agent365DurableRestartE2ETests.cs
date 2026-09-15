// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.Common;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTelemetry;
using OpenTelemetry.Resources;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;

namespace Microsoft.Agents.A365.Observability.Tests.Tracing.Exporters;

/// <summary>
/// End-to-end <b>restart</b> durability test that drives the real store-and-forward stack across a
/// process-restart boundary using a single temp persistent directory. It exercises as much of the real
/// exporter/core/storage lifecycle as can be done deterministically — without starting the background
/// replay loop, so there is no wall-clock race:
/// <list type="number">
///   <item>A first <see cref="Agent365ExporterCore"/> (real <see cref="Agent365PersistentStorage"/> +
///   real <see cref="Agent365TransmissionGate"/>) formats and sends a live batch, receives a retryable
///   HTTP 503 on every attempt, and hands the <em>complete</em> chunk to durable storage before the
///   instance is disposed.</item>
///   <item>The persisted blob is asserted to survive the dispose and to contain <b>no bearer token</b>.</item>
///   <item>A second core over the <em>same directory</em> (a fresh gate, fresh instances) resolves a
///   <em>fresh</em> token, and a real <see cref="Agent365ReplayCoordinator.ReplayOnceAsync"/> sends the
///   chunk exactly once (200 OK) and deletes the blob from disk.</item>
/// </list>
/// This is the production-faithful proof that a retryable exhausted send transfers ownership to storage
/// and that a restarted process replays and removes the durable record with freshly resolved auth.
/// </summary>
[TestClass]
public sealed class Agent365DurableRestartE2ETests
{
    private const string FirstInstanceBearer = "SECRET-BEARER-FIRST-INSTANCE-DO-NOT-PERSIST";
    private const string SecondInstanceBearer = "FRESH-BEARER-SECOND-INSTANCE";

    private static string NewRoot() =>
        Path.Combine(Path.GetTempPath(), "Agent365DurableRestartE2E", Guid.NewGuid().ToString("N"));

    private static void SafeDelete(string root)
    {
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        catch
        {
            // Best-effort cleanup; a leaked temp folder must never fail a test.
        }
    }

    private static Agent365ExporterCore CreateCore(IAgent365PersistentStorage storage, Agent365TransmissionGate gate) =>
        new(
            new ExportFormatter(NullLogger<ExportFormatter>.Instance),
            NullLogger<Agent365ExporterCore>.Instance,
            () => DateTimeOffset.UtcNow,
            storage,
            gate);

    private static Agent365ExporterOptions CreateOptions() =>
        new()
        {
            DomainResolver = _ => "api.example.com",
            MaxPayloadBytes = 900_000,
        };

    /// <summary>
    /// Builds a single recorded GenAI <see cref="Activity"/> that <see cref="Agent365ExporterCore.PartitionByIdentity"/>
    /// accepts (known <c>gen_ai.operation.name</c> plus tenant and agent identity). A local
    /// <see cref="ActivitySource"/> and a literal source name in the listener predicate avoid the
    /// reentrant-static-constructor NRE seen with a self-referential static source field.
    /// </summary>
    private static Activity CreateGenAiActivity(
        string tenantId = "tenant-restart",
        string agentId = "agent-restart",
        string agenticUserId = "user-restart")
    {
        const string sourceName = "Agent365RestartE2ESource";
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == sourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = _ => { },
            ActivityStopped = _ => { },
        };
        ActivitySource.AddActivityListener(listener);

        using var source = new ActivitySource(sourceName);
        var activity = source.StartActivity("invoke_agent", ActivityKind.Client)
            ?? throw new InvalidOperationException("Failed to start activity.");

        activity.SetTag(OpenTelemetryConstants.GenAiOperationNameKey, OpenTelemetryConstants.InvokeAgentOperationName);
        activity.SetTag(OpenTelemetryConstants.TenantIdKey, tenantId);
        activity.SetTag(OpenTelemetryConstants.GenAiAgentIdKey, agentId);
        activity.SetTag(OpenTelemetryConstants.AgentAUIDKey, agenticUserId);
        activity.Stop();
        return activity;
    }

    [TestMethod]
    public async Task RestartReplaysPersistedChunkWithFreshTokenAndDeletesBlob()
    {
        var root = NewRoot();
        try
        {
            var resource = ResourceBuilder.CreateEmpty().Build();
            var options = CreateOptions();
            var activity = CreateGenAiActivity();

            // ---- First instance: live send gets a retryable 503 on every attempt, persists the chunk. ----
            var firstSendAttempts = 0;
            string directoryPath;
            using (var firstStorage = Agent365PersistentStorage.Create(root))
            {
                directoryPath = firstStorage.DirectoryPath;
                var firstGate = new Agent365TransmissionGate();
                var firstCore = CreateCore(firstStorage, firstGate);
                var groups = firstCore.PartitionByIdentity(new[] { activity });
                groups.Should().ContainSingle("the GenAI activity forms exactly one identity group to export");

                var firstResult = await firstCore.ExportBatchCoreAsync(
                    groups,
                    resource,
                    options,
                    tokenResolver: (_, _) => Task.FromResult<string?>(FirstInstanceBearer),
                    sendAsync: _ =>
                    {
                        firstSendAttempts++;
                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
                    },
                    CancellationToken.None);

                firstSendAttempts.Should().Be(1, "the one-attempt live send is tried once and returns a retryable 503");
                firstResult.Should().Be(
                    ExportResult.Success,
                    "a retryable failure hands the telemetry to durable storage, so the batch is reported handled, not dropped");

                Directory.EnumerateFiles(directoryPath, "*.blob").Should().ContainSingle(
                    "the complete chunk was persisted to disk on the retryable 503");
            }

            // ---- The persisted record must outlive the first instance and never carry a bearer token. ----
            var persistedBlobs = Directory.EnumerateFiles(directoryPath, "*.blob").ToArray();
            persistedBlobs.Should().ContainSingle("the persisted chunk survives the first instance's dispose");
            var persistedText = Encoding.UTF8.GetString(File.ReadAllBytes(persistedBlobs[0]));
            persistedText.Should().NotContain(
                FirstInstanceBearer,
                "a durable record must never contain the bearer token used for the live send attempt");
            persistedText.Should().NotContain(
                "Bearer",
                "no Authorization header material is persisted with the durable record");

            // ---- Second instance over the SAME directory: fresh token, one replay send, blob deleted. ----
            var replayTokenResolutions = 0;
            var replaySendAttempts = 0;
            var replayAuthorization = string.Empty;
            using (var secondStorage = Agent365PersistentStorage.Create(root))
            {
                secondStorage.DirectoryPath.Should().Be(
                    directoryPath, "the second instance resolves the same on-disk store as the first");

                var secondGate = new Agent365TransmissionGate();
                var secondCore = CreateCore(secondStorage, secondGate);

                var coordinator = new Agent365ReplayCoordinator(
                    secondStorage,
                    secondCore.Gates,
                    replayAsync: (record, ct) => secondCore.ReplayRecordAsync(
                        record,
                        options,
                        tokenResolver: (_, _) =>
                        {
                            replayTokenResolutions++;
                            return Task.FromResult<string?>(SecondInstanceBearer);
                        },
                        sendAsync: (request, _) =>
                        {
                            replaySendAttempts++;
                            replayAuthorization = request.Headers.Authorization?.ToString() ?? string.Empty;
                            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
                        },
                        ct),
                    NullLogger.Instance);

                await coordinator.ReplayOnceAsync(CancellationToken.None);

                replayTokenResolutions.Should().Be(1, "the restarted instance resolves a fresh token for the replay");
                replaySendAttempts.Should().Be(1, "the persisted chunk is replayed with exactly one send attempt");
                replayAuthorization.Should().Be(
                    $"Bearer {SecondInstanceBearer}",
                    "replay authenticates with the freshly resolved token, never a persisted one");

                secondStorage.TryGetNext(out _).Should().BeFalse("the delivered record was deleted from the store");
            }

            // ---- Nothing remains on disk once the restarted instance delivered the chunk. ----
            Directory.EnumerateFiles(directoryPath, "*.blob").Should().BeEmpty(
                "a delivered replayed record is removed from disk");
            Directory.EnumerateFiles(directoryPath, "*.lock").Should().BeEmpty(
                "no leased record remains after a successful drain");
        }
        finally
        {
            SafeDelete(root);
        }
    }
}
