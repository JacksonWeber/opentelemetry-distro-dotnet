// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using FluentAssertions;
using Microsoft.Agents.A365.Observability.Hosting.Middleware;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;
using Microsoft.Agents.Builder;
using Microsoft.Agents.Core.Models;
using Moq;

using DiagnosticsActivity = System.Diagnostics.Activity;

namespace Microsoft.Agents.A365.Observability.Hosting.Tests.Middleware;

[TestClass]
public class OutputLoggingMiddlewareTests
{
    [TestMethod]
    public async Task OnTurnAsync_CallsNextDelegate()
    {
        // Arrange
        var middleware = new OutputLoggingMiddleware();
        var turnContext = CreateTurnContext();

        bool nextCalled = false;
        NextDelegate next = (ct) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        // Act
        await middleware.OnTurnAsync(turnContext, next);

        // Assert
        nextCalled.Should().BeTrue();
    }

    [TestMethod]
    public async Task OnTurnAsync_RegistersSendHandler_WhenRecipientHasDetails()
    {
        // Arrange
        var middleware = new OutputLoggingMiddleware();
        var mockTurnContext = new Mock<ITurnContext>();
        SetupTurnContext(mockTurnContext);

        NextDelegate next = (ct) => Task.CompletedTask;

        // Act
        await middleware.OnTurnAsync(mockTurnContext.Object, next);

        // Assert
        mockTurnContext.Verify(tc => tc.OnSendActivities(It.IsAny<SendActivitiesHandler>()), Times.Once);
    }

    [TestMethod]
    public async Task OnTurnAsync_PassesThrough_WhenRecipientIsNull()
    {
        // Arrange
        var middleware = new OutputLoggingMiddleware();
        var mockActivity = new Mock<IActivity>();
        mockActivity.Setup(a => a.Recipient).Returns((ChannelAccount)null!);
        mockActivity.Setup(a => a.Type).Returns("message");

        var mockTurnContext = new Mock<ITurnContext>();
        mockTurnContext.Setup(tc => tc.Activity).Returns(mockActivity.Object);

        bool nextCalled = false;
        NextDelegate next = (ct) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        // Act
        await middleware.OnTurnAsync(mockTurnContext.Object, next);

        // Assert
        nextCalled.Should().BeTrue();
        mockTurnContext.Verify(tc => tc.OnSendActivities(It.IsAny<SendActivitiesHandler>()), Times.Never);
    }

    [TestMethod]
    public async Task OnTurnAsync_RegistersHandler_WhenTenantIdIsMissing()
    {
        // Arrange
        var middleware = new OutputLoggingMiddleware();
        var mockActivity = new Mock<IActivity>();
        mockActivity.Setup(a => a.Type).Returns("message");
        mockActivity.Setup(a => a.Recipient).Returns(new ChannelAccount
        {
            Id = "agent-id",
            Name = "Agent",
            // No TenantId set - middleware no longer gates on TenantId
        });

        var mockTurnContext = new Mock<ITurnContext>();
        mockTurnContext.Setup(tc => tc.Activity).Returns(mockActivity.Object);

        bool nextCalled = false;
        NextDelegate next = (ct) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        // Act
        await middleware.OnTurnAsync(mockTurnContext.Object, next);

        // Assert
        nextCalled.Should().BeTrue();
        mockTurnContext.Verify(tc => tc.OnSendActivities(It.IsAny<SendActivitiesHandler>()), Times.Once);
    }

    [TestMethod]
    public async Task OnTurnAsync_UsesProductContextFromChannelData_WhenSubChannelIsNotSet()
    {
        // Arrange
        AppContext.SetSwitch(OpenTelemetryConstants.EnableOpenTelemetrySwitch, true);
        var middleware = new OutputLoggingMiddleware();
        var channelData = "{\"productContext\": \"copilot-m365\"}";
        SendActivitiesHandler? capturedHandler = null;

        var mockTurnContext = new Mock<ITurnContext>();
        SetupTurnContext(mockTurnContext, channelData: channelData);
        mockTurnContext.Setup(tc => tc.OnSendActivities(It.IsAny<SendActivitiesHandler>()))
            .Callback<SendActivitiesHandler>(handler => capturedHandler = handler)
            .Returns(mockTurnContext.Object);

        NextDelegate next = (ct) => Task.CompletedTask;

        // Act
        await middleware.OnTurnAsync(mockTurnContext.Object, next);

        // Assert – invoke captured handler and inspect the span
        capturedHandler.Should().NotBeNull();

        var outgoingActivity = new Mock<IActivity>();
        outgoingActivity.Setup(a => a.Type).Returns(ActivityTypes.Message);
        outgoingActivity.Setup(a => a.Text).Returns("Hello output");
        var activities = new List<IActivity> { outgoingActivity.Object };

        DiagnosticsActivity? recordedSpan = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == OpenTelemetryConstants.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = a => recordedSpan = a,
        };
        ActivitySource.AddActivityListener(listener);

        await capturedHandler(
            mockTurnContext.Object,
            activities,
            () => Task.FromResult(Array.Empty<ResourceResponse>()));

        recordedSpan.Should().NotBeNull();
        recordedSpan!.TagObjects.Should().ContainKey(OpenTelemetryConstants.ChannelLinkKey)
            .WhoseValue.Should().Be("copilot-m365");
    }

    [TestMethod]
    public async Task OnTurnAsync_UsesSubChannel_WhenBothSubChannelAndProductContextArePresent()
    {
        // Arrange
        AppContext.SetSwitch(OpenTelemetryConstants.EnableOpenTelemetrySwitch, true);
        var middleware = new OutputLoggingMiddleware();
        var channelData = "{\"productContext\": \"copilot-m365\"}";
        SendActivitiesHandler? capturedHandler = null;

        var mockTurnContext = new Mock<ITurnContext>();
        SetupTurnContext(mockTurnContext, subChannel: "explicit-subchannel", channelData: channelData);
        mockTurnContext.Setup(tc => tc.OnSendActivities(It.IsAny<SendActivitiesHandler>()))
            .Callback<SendActivitiesHandler>(handler => capturedHandler = handler)
            .Returns(mockTurnContext.Object);

        NextDelegate next = (ct) => Task.CompletedTask;

        // Act
        await middleware.OnTurnAsync(mockTurnContext.Object, next);

        // Assert – invoke captured handler and inspect the span
        capturedHandler.Should().NotBeNull();

        var outgoingActivity = new Mock<IActivity>();
        outgoingActivity.Setup(a => a.Type).Returns(ActivityTypes.Message);
        outgoingActivity.Setup(a => a.Text).Returns("Hello output");
        var activities = new List<IActivity> { outgoingActivity.Object };

        DiagnosticsActivity? recordedSpan = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == OpenTelemetryConstants.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = a => recordedSpan = a,
        };
        ActivitySource.AddActivityListener(listener);

        await capturedHandler(
            mockTurnContext.Object,
            activities,
            () => Task.FromResult(Array.Empty<ResourceResponse>()));

        recordedSpan.Should().NotBeNull();
        recordedSpan!.TagObjects.Should().ContainKey(OpenTelemetryConstants.ChannelLinkKey)
            .WhoseValue.Should().Be("explicit-subchannel");
    }

    private static ITurnContext CreateTurnContext()
    {
        var mockTurnContext = new Mock<ITurnContext>();
        SetupTurnContext(mockTurnContext);
        return mockTurnContext.Object;
    }

    private static void SetupTurnContext(Mock<ITurnContext> mockTurnContext, string? subChannel = null, object? channelData = null)
    {
        var mockActivity = new Mock<IActivity>();
        mockActivity.Setup(a => a.Type).Returns("message");
        mockActivity.Setup(a => a.Text).Returns("Hello");
        mockActivity.Setup(a => a.From).Returns(new ChannelAccount
        {
            Id = "caller-id",
            Name = "Caller",
            AadObjectId = "caller-aad",
        });
        mockActivity.Setup(a => a.Recipient).Returns(new ChannelAccount
        {
            Id = "agent-id",
            Name = "Agent",
            TenantId = "badf1f56-284d-4dc5-ac59-0dd53900e743",
            Role = "agenticAppInstance",
        });
        mockActivity.Setup(a => a.Conversation).Returns(new ConversationAccount { Id = "conv-id" });
        mockActivity.Setup(a => a.ServiceUrl).Returns("https://example.com");

        var channelId = new ChannelId("test-channel");
        if (subChannel != null)
        {
            channelId.SubChannel = subChannel;
        }

        mockActivity.Setup(a => a.ChannelId).Returns(channelId);

        if (channelData != null)
        {
            mockActivity.Setup(a => a.ChannelData).Returns(channelData);
        }

        mockTurnContext.Setup(tc => tc.Activity).Returns(mockActivity.Object);
        mockTurnContext.Setup(tc => tc.StackState).Returns(new TurnContextStateCollection());
        mockTurnContext.Setup(tc => tc.OnSendActivities(It.IsAny<SendActivitiesHandler>()))
            .Returns(mockTurnContext.Object);
    }
}
