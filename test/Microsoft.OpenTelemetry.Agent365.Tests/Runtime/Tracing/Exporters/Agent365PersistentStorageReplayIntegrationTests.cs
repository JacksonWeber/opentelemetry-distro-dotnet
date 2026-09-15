// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.Common;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;

namespace Microsoft.Agents.A365.Observability.Tests.Tracing.Exporters;

/// <summary>
/// Production-faithful replay tests that drive the <b>real</b> <see cref="Agent365PersistentStorage"/>
/// (a live <c>FileBlobProvider</c> writing to a temp directory) through the real
/// <see cref="Agent365ReplayCoordinator"/> and <see cref="Agent365ExporterCore.ReplayRecordAsync"/>.
/// These guard the on-disk contract the in-memory doubles only approximate:
/// <list type="bullet">
///   <item><c>TryGetNext</c> is non-destructive and re-serves the same unleased blob; a leased blob is excluded.</item>
///   <item>A full drain deletes every delivered record from disk.</item>
///   <item>Retained records are leased so the pass advances past them within a single pass.</item>
///   <item>An un-leasable first blob does not spin the pass (the root-cause liveness defect). Its on-disk
///   reproduction relies on Windows open-file rename semantics and is Windows-gated; the cross-platform
///   proof lives in <c>Agent365ReplayCoordinatorTests.LeaseFailureDoesNotSpinWhenStorageReservesSameBlob</c>.</item>
/// </list>
/// </summary>
[TestClass]
public sealed class Agent365PersistentStorageReplayIntegrationTests
{
    private static string NewRoot() =>
        Path.Combine(Path.GetTempPath(), "Agent365ReplayIT", Guid.NewGuid().ToString("N"));

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

    private static Agent365DurableRecord Record(
        string tenantId = "tenant-1",
        string agentId = "agent-1",
        string? payload = null) =>
        new(tenantId, agentId, null, false, payload ?? "{\"resourceSpans\":[]}", DateTimeOffset.UtcNow);

    private static Agent365ReplayCoordinator CreateCoordinator(
        IAgent365PersistentStorage storage,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync,
        Func<string, string, Task<string?>>? tokenResolver = null,
        Agent365TransmissionGate? gate = null)
    {
        gate ??= new Agent365TransmissionGate();
        tokenResolver ??= (_, _) => Task.FromResult<string?>("fresh-token");
        var options = new Agent365ExporterOptions { DomainResolver = _ => "api.example.com" };
        var core = new Agent365ExporterCore(
            new ExportFormatter(NullLogger<ExportFormatter>.Instance),
            NullLogger<Agent365ExporterCore>.Instance,
            () => DateTimeOffset.UtcNow,
            storage,
            gate);

        return new Agent365ReplayCoordinator(
            storage,
            core.Gates,
            (record, ct) => core.ReplayRecordAsync(record, options, tokenResolver, sendAsync, ct),
            NullLogger.Instance);
    }

    [TestMethod]
    public void TryGetNextReServesUnleasedBlobAndExcludesLeasedBlob()
    {
        var root = NewRoot();
        try
        {
            using var storage = Agent365PersistentStorage.Create(root);
            storage.TryStore(Record()).Should().BeTrue();

            storage.TryGetNext(out var first).Should().BeTrue("a stored blob is served");
            storage.TryGetNext(out var second).Should().BeTrue(
                "TryGetNext is non-destructive: the same unleased blob is re-served on the next call");
            first.Should().NotBeNull();
            second.Should().NotBeNull();

            first!.TryLease(TimeSpan.FromMinutes(2)).Should().BeTrue("the served blob can be leased");
            storage.TryGetNext(out var afterLease).Should().BeFalse(
                "a leased blob is excluded from subsequent gets, so the only record is no longer served");
            afterLease.Should().BeNull();
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [TestMethod]
    public async Task CoordinatorDrainsRealStorageDeletingDeliveredRecords()
    {
        var root = NewRoot();
        try
        {
            using var storage = Agent365PersistentStorage.Create(root);
            for (var i = 0; i < 3; i++)
                storage.TryStore(Record(agentId: $"agent-{i}")).Should().BeTrue();

            var coordinator = CreateCoordinator(
                storage,
                sendAsync: (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

            await coordinator.ReplayOnceAsync(CancellationToken.None);

            storage.TryGetNext(out _).Should().BeFalse("every delivered record was deleted from real storage");
            Directory.EnumerateFiles(storage.DirectoryPath, "*.blob").Should().BeEmpty(
                "no unleased blobs remain after a full drain");
            Directory.EnumerateFiles(storage.DirectoryPath, "*.lock").Should().BeEmpty(
                "no leased blobs remain after a full drain");
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [TestMethod]
    public async Task UnresolvedTokenRetainsAndLeasesRealRecordsWithinAPass()
    {
        var root = NewRoot();
        try
        {
            using var storage = Agent365PersistentStorage.Create(root);
            storage.TryStore(Record(agentId: "a")).Should().BeTrue();
            storage.TryStore(Record(agentId: "b")).Should().BeTrue();

            var sends = 0;
            var coordinator = CreateCoordinator(
                storage,
                sendAsync: (_, _) =>
                {
                    sends++;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
                },
                tokenResolver: (_, _) => Task.FromResult<string?>(null));

            await coordinator.ReplayOnceAsync(CancellationToken.None);

            sends.Should().Be(0, "no send happens without a resolved token");
            storage.TryGetNext(out _).Should().BeFalse(
                "both retained records are leased for the pass, so neither is re-served");
            Directory.EnumerateFiles(storage.DirectoryPath, "*.lock").Should().HaveCount(2,
                "each retained record was leased, proving the pass advanced past the first via lease exclusion");
            Directory.EnumerateFiles(storage.DirectoryPath, "*.blob").Should().BeEmpty(
                "no unleased blob remains within the pass window");
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [TestMethod]
    public async Task UnleasableFirstBlobDoesNotSpinTheRealPass()
    {
        // This reproduces an un-leasable first blob by holding the .blob file open so the provider's
        // lease (a File.Move rename) fails. That mechanism is Windows-specific: Windows denies renaming
        // an open file, whereas POSIX (Linux/macOS) permits renaming an open file, so the lease would
        // succeed there and the on-disk scenario could not be reproduced. This test therefore gates only
        // its OS-specific reproduction mechanism to Windows; the platform-independent proof that a failed
        // lease stops (rather than spins) the pass lives in
        // Agent365ReplayCoordinatorTests.LeaseFailureDoesNotSpinWhenStorageReservesSameBlob, which drives
        // the same coordinator logic against a re-serving storage double on every OS the CI matrix runs.
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Inconclusive(
                "The un-leasable-blob reproduction relies on Windows open-file rename semantics. The " +
                "cross-platform behavior is covered by " +
                "Agent365ReplayCoordinatorTests.LeaseFailureDoesNotSpinWhenStorageReservesSameBlob.");
            return;
        }

        var root = NewRoot();
        try
        {
            using var storage = Agent365PersistentStorage.Create(root);
            storage.TryStore(Record()).Should().BeTrue();

            var blobFile = Directory.EnumerateFiles(storage.DirectoryPath, "*.blob").Single();

            // Hold the blob open so the provider's lease (a File.Move rename) fails, reproducing an
            // un-leasable first blob. The real provider keeps re-serving this same .blob, so a pass
            // that "continue"d would re-fetch and re-lease it up to maxRecordsPerPass times.
            using (new FileStream(blobFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var counting = new CountingStorage(storage);
                var coordinator = CreateCoordinator(
                    counting,
                    sendAsync: (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

                await coordinator.ReplayOnceAsync(CancellationToken.None);

                counting.GetNextCalls.Should().Be(1,
                    "a failed lease stops the pass; the same un-leasable blob is not re-fetched up to ten times");
            }

            Directory.EnumerateFiles(storage.DirectoryPath, "*.blob").Should().ContainSingle(
                "the un-leasable record is retained, not lost");
        }
        finally
        {
            SafeDelete(root);
        }
    }
}

/// <summary>
/// Decorates a real <see cref="IAgent365PersistentStorage"/> and counts <see cref="TryGetNext"/> calls
/// so a test can prove the coordinator does not spin over a re-served, un-leasable blob. The wrapped
/// store owns its own lifetime (disposed by the test), so this decorator's <see cref="Dispose"/> is a no-op.
/// </summary>
internal sealed class CountingStorage : IAgent365PersistentStorage
{
    private readonly IAgent365PersistentStorage _inner;

    public CountingStorage(IAgent365PersistentStorage inner) => _inner = inner;

    public int GetNextCalls { get; private set; }

    public bool TryStore(Agent365DurableRecord record) => _inner.TryStore(record);

    public bool TryGetNext([NotNullWhen(true)] out IAgent365StoredRecord? record)
    {
        GetNextCalls++;
        return _inner.TryGetNext(out record);
    }

    public void Dispose()
    {
    }
}
