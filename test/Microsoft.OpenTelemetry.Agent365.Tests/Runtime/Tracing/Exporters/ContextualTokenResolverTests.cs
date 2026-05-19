// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Agents.A365.Observability.Runtime.Common;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;
using OpenTelemetry;
using OpenTelemetry.Resources;
using System.Diagnostics;
using System.Reflection;

namespace Microsoft.Agents.A365.Observability.Tests.Tracing.Exporters;

[TestClass]
public sealed class ContextualTokenResolverTests
{
    #region Helpers

    private static Activity CreateActivity(
        string? tenantId = null,
        string? agentId = null,
        string? agenticUserId = null,
        string? operationName = "invoke_agent")
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Agent365Sdk.CtxResolver",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = _ => { },
            ActivityStopped = _ => { }
        };
        ActivitySource.AddActivityListener(listener);

        var source = new ActivitySource("Agent365Sdk.CtxResolver");
        var activity = source.StartActivity("test-span", ActivityKind.Client)
            ?? throw new InvalidOperationException("Failed to start activity.");

        if (operationName != null)
            activity.SetTag(OpenTelemetryConstants.GenAiOperationNameKey, operationName);
        if (tenantId != null)
            activity.SetTag(OpenTelemetryConstants.TenantIdKey, tenantId);
        if (agentId != null)
            activity.SetTag(OpenTelemetryConstants.GenAiAgentIdKey, agentId);
        if (agenticUserId != null)
            activity.SetTag(OpenTelemetryConstants.AgentAUIDKey, agenticUserId);

        activity.Stop();
        return activity;
    }

    private static Batch<Activity> CreateBatch(params Activity[] activities)
    {
        var batchType = typeof(Batch<Activity>);
        var ctor = batchType
            .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault();

        if (ctor == null)
            Assert.Inconclusive("Could not locate internal Batch<Activity> constructor.");

        var circularBufferType = batchType.Assembly.GetType("OpenTelemetry.Internal.CircularBuffer`1")!
            .MakeGenericType(typeof(Activity));
        var buffer = Activator.CreateInstance(circularBufferType, activities.Length)
            ?? throw new InvalidOperationException("Could not create CircularBuffer<Activity>.");

        var addMethod = circularBufferType.GetMethod("Add");
        foreach (var act in activities)
            addMethod!.Invoke(buffer, new object[] { act });

        return (Batch<Activity>)ctor.Invoke(new object[] { buffer, activities.Length });
    }

    private static readonly Agent365ExporterCore Core = new(
        new ExportFormatter(NullLogger<ExportFormatter>.Instance),
        NullLogger<Agent365ExporterCore>.Instance);

    private static Agent365Exporter CreateExporter(Agent365ExporterOptions options)
    {
        var resource = ResourceBuilder.CreateEmpty()
            .AddService("unit-test-service", serviceVersion: "1.0.0")
            .Build();

        return new Agent365Exporter(Core, NullLogger<Agent365Exporter>.Instance, options, resource);
    }

    #endregion

    // ───────────────────── TokenResolverContext tests ─────────────────────

    [TestMethod]
    public void TokenResolverContext_Constructor_SetsKeyFields()
    {
        var identity = new AgentIdentity("agent-1");
        var ctx = new TokenResolverContext(identity, "tenant-1");

        ctx.Identity.AgentId.Should().Be("agent-1");
        ctx.TenantId.Should().Be("tenant-1");
        ctx.Identity.Should().BeSameAs(identity);
        ctx.Identity.AgenticUserId.Should().BeNull();
    }

    [TestMethod]
    public void TokenResolverContext_WithIdentity_ExposesAgenticUserId()
    {
        var identity = new AgentIdentity("agent-1", "user-42");
        var ctx = new TokenResolverContext(identity, "tenant-1");

        ctx.Identity.AgenticUserId.Should().Be("user-42");
        ctx.Identity.AgentId.Should().Be("agent-1");
    }

    [TestMethod]
    public void TokenResolverContext_WithoutAgenticUserId_ReturnsNull()
    {
        var identity = new AgentIdentity("agent-1");
        var ctx = new TokenResolverContext(identity, "tenant-1");

        ctx.Identity.AgenticUserId.Should().BeNull();
    }

    [TestMethod]
    public void AgentIdentity_Constructor_SetsProperties()
    {
        var id = new AgentIdentity("agent-1", "user-1");

        id.AgentId.Should().Be("agent-1");
        id.AgenticUserId.Should().Be("user-1");
    }

    [TestMethod]
    public void AgentIdentity_NullAgenticUserId_IsAllowed()
    {
        var id = new AgentIdentity("agent-1");

        id.AgentId.Should().Be("agent-1");
        id.AgenticUserId.Should().BeNull();
    }

    // ──────────── Constructor validation: either resolver accepted ────────────

    [TestMethod]
    public void Constructor_ContextualTokenResolverOnly_DoesNotThrow()
    {
        var options = new Agent365ExporterOptions
        {
            ContextualTokenResolver = _ => Task.FromResult<string?>("token")
        };

        Action act = () => CreateExporter(options);

        act.Should().NotThrow();
    }

    [TestMethod]
    public void Constructor_TokenResolverOnly_DoesNotThrow()
    {
        var options = new Agent365ExporterOptions
        {
            TokenResolver = (_, _) => Task.FromResult<string?>("token")
        };

        Action act = () => CreateExporter(options);

        act.Should().NotThrow();
    }

    [TestMethod]
    public void Constructor_BothResolvers_DoesNotThrow()
    {
        var options = new Agent365ExporterOptions
        {
            TokenResolver = (_, _) => Task.FromResult<string?>("token"),
            ContextualTokenResolver = _ => Task.FromResult<string?>("ctx-token")
        };

        Action act = () => CreateExporter(options);

        act.Should().NotThrow();
    }

    [TestMethod]
    public void Constructor_NeitherResolver_Throws()
    {
        var options = new Agent365ExporterOptions
        {
            TokenResolver = null,
            ContextualTokenResolver = null
        };

        Action act = () => CreateExporter(options);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("TokenResolver");
    }

    // ──────── ContextualTokenResolver precedence during export ────────

    [TestMethod]
    public void Export_ContextualResolverSet_ReceivesContext()
    {
        TokenResolverContext? captured = null;
        var options = new Agent365ExporterOptions
        {
            ContextualTokenResolver = ctx =>
            {
                captured = ctx;
                return Task.FromResult<string?>("ctx-token");
            }
        };

        var exporter = CreateExporter(options);
        using var act = CreateActivity("tenant-1", "agent-1", agenticUserId: "user-99");
        var batch = CreateBatch(act);
        exporter.Export(in batch);

        captured.Should().NotBeNull();
        captured!.Identity.AgentId.Should().Be("agent-1");
        captured.TenantId.Should().Be("tenant-1");
        captured.Identity.AgenticUserId.Should().Be("user-99");
    }

    [TestMethod]
    public void Export_ContextualResolverSet_WithoutAgenticUserId_ContextHasNullUserId()
    {
        TokenResolverContext? captured = null;
        var options = new Agent365ExporterOptions
        {
            ContextualTokenResolver = ctx =>
            {
                captured = ctx;
                return Task.FromResult<string?>("ctx-token");
            }
        };

        var exporter = CreateExporter(options);
        using var act = CreateActivity("tenant-1", "agent-1", agenticUserId: null);
        var batch = CreateBatch(act);
        exporter.Export(in batch);

        captured.Should().NotBeNull();
        captured!.Identity.AgenticUserId.Should().BeNull();
    }

    [TestMethod]
    public void Export_BothResolversSet_ContextualTakesPrecedence()
    {
        bool vanillaCalled = false;
        TokenResolverContext? captured = null;

        var options = new Agent365ExporterOptions
        {
            TokenResolver = (_, _) =>
            {
                vanillaCalled = true;
                return Task.FromResult<string?>("vanilla-token");
            },
            ContextualTokenResolver = ctx =>
            {
                captured = ctx;
                return Task.FromResult<string?>("ctx-token");
            }
        };

        var exporter = CreateExporter(options);
        using var act = CreateActivity("tenant-1", "agent-1", agenticUserId: "user-1");
        var batch = CreateBatch(act);
        exporter.Export(in batch);

        vanillaCalled.Should().BeFalse("ContextualTokenResolver should take precedence");
        captured.Should().NotBeNull();
        captured!.Identity.AgentId.Should().Be("agent-1");
    }

    [TestMethod]
    public void Export_OnlyVanillaResolverSet_VanillaCalled()
    {
        bool vanillaCalled = false;
        var options = new Agent365ExporterOptions
        {
            TokenResolver = (agentId, tenantId) =>
            {
                vanillaCalled = true;
                return Task.FromResult<string?>("vanilla-token");
            }
        };

        var exporter = CreateExporter(options);
        using var act = CreateActivity("tenant-1", "agent-1");
        var batch = CreateBatch(act);
        exporter.Export(in batch);

        vanillaCalled.Should().BeTrue("vanilla TokenResolver should be called when ContextualTokenResolver is null");
    }

    [TestMethod]
    public void Export_NoIdentityActivities_ContextualResolverNotCalled()
    {
        bool called = false;
        var options = new Agent365ExporterOptions
        {
            ContextualTokenResolver = _ =>
            {
                called = true;
                return Task.FromResult<string?>("token");
            }
        };

        var exporter = CreateExporter(options);
        using var act = CreateActivity(); // no tenant/agent
        var batch = CreateBatch(act);
        var result = exporter.Export(in batch);

        result.Should().Be(ExportResult.Success);
        called.Should().BeFalse("resolver should not be called for activities without identity");
    }

    // ───────── Agent365ExporterOptions property tests ─────────

    [TestMethod]
    public void Agent365ExporterOptions_ContextualTokenResolver_DefaultsToNull()
    {
        var options = new Agent365ExporterOptions();
        options.ContextualTokenResolver.Should().BeNull();
    }

    [TestMethod]
    public void Agent365ExporterOptions_ContextualTokenResolver_CanBeSet()
    {
        AsyncContextualTokenResolver resolver = _ => Task.FromResult<string?>("token");
        var options = new Agent365ExporterOptions
        {
            ContextualTokenResolver = resolver
        };

        options.ContextualTokenResolver.Should().BeSameAs(resolver);
    }
}
