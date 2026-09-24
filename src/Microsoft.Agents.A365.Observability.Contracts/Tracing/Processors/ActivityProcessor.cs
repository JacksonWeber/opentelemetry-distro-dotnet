// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
namespace Microsoft.Agents.A365.Observability.Runtime.Tracing.Processors
{
    using Microsoft.Agents.A365.Observability.Runtime.Common;
    using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;
    using global::OpenTelemetry;
    using System;
    using System.Diagnostics;

    /// <summary>
    /// Processes activity telemetry data by adding contextual baggage information.
    /// </summary>
    public sealed class ActivityProcessor : BaseProcessor<Activity>
    {
        private static readonly string[] AttributeKeys = new[]
        {
            OpenTelemetryConstants.GenAiAgentIdKey,
            OpenTelemetryConstants.GenAiAgentNameKey,
            OpenTelemetryConstants.GenAiAgentDescriptionKey,
            OpenTelemetryConstants.GenAiAgentVersionKey,
            OpenTelemetryConstants.AgentEmailKey,
            OpenTelemetryConstants.AgentBlueprintIdKey,
            OpenTelemetryConstants.AgentAUIDKey,
            OpenTelemetryConstants.AgentPlatformIdKey,
            OpenTelemetryConstants.TenantIdKey,
            OpenTelemetryConstants.GenAiConversationIdKey,
            OpenTelemetryConstants.GenAiConversationItemLinkKey,
            OpenTelemetryConstants.GenAiInputMessagesKey,
            OpenTelemetryConstants.GenAiOutputMessagesKey,
            OpenTelemetryConstants.GenAiToolCallResultKey,
            OpenTelemetryConstants.GenAiToolNameKey,
            OpenTelemetryConstants.GenAiToolCallIdKey,
            OpenTelemetryConstants.GenAiToolDescriptionKey,
            OpenTelemetryConstants.GenAiToolArgumentsKey,
            OpenTelemetryConstants.GenAiToolTypeKey,
            OpenTelemetryConstants.GenAiProviderNameKey,
            OpenTelemetryConstants.SessionIdKey,
            OpenTelemetryConstants.SessionDescriptionKey,
            OpenTelemetryConstants.ChannelNameKey,
            OpenTelemetryConstants.ChannelLinkKey,
            OpenTelemetryConstants.UserIdKey,
            OpenTelemetryConstants.UserNameKey,
            OpenTelemetryConstants.UserEmailKey,
            OpenTelemetryConstants.CallerClientIpKey,
            OpenTelemetryConstants.ServiceNameKey,
        };

        private static readonly string[] InvokeAgentAttributeKeys = new[]
        {
            OpenTelemetryConstants.ServerAddressKey,
            OpenTelemetryConstants.ServerPortKey,
        };

        /// <summary>
        /// Called when an activity starts, adds tags for attributes listed in AttributeKeys,
        /// plus any custom baggage keys (set via <c>BaggageBuilder.CustomAttribute</c>) that are
        /// recorded in the <c>_internal.custom_keys</c> baggage entry. Any span with an
        /// allowlisted <c>gen_ai.operation.name</c> tag is processed; all other activities pass
        /// through unmodified. Tags already set directly on the span take precedence over baggage.
        /// </summary>
        /// <param name="activity">The activity that is starting.</param>
        public override void OnStart(Activity activity)
        {
            var operationName = activity.GetTagItem(OpenTelemetryConstants.GenAiOperationNameKey) as string;
            if (string.IsNullOrEmpty(operationName) ||
                !OpenTelemetryConstants.GenAiOperationNames.Contains(operationName!))
            {
                base.OnStart(activity);
                return;
            }

            // Set telemetry SDK attributes
            activity.CoalesceTag(OpenTelemetryConstants.TelemetrySdkNameKey, OpenTelemetryConstants.TelemetrySdkNameValue);
            activity.CoalesceTag(OpenTelemetryConstants.TelemetrySdkLanguageKey, OpenTelemetryConstants.TelemetrySdkLanguageValue);
            activity.CoalesceTag(OpenTelemetryConstants.TelemetrySdkVersionKey, OpenTelemetryConstants.TelemetrySdkVersionValue);

            foreach (var key in AttributeKeys)
            {
                activity.CoalesceTag(key, Baggage.Current.GetBaggage(key));
            }

            if (activity.OperationName == OpenTelemetryConstants.InvokeAgentOperationName ||
                (activity.DisplayName != null && activity.DisplayName.StartsWith(OpenTelemetryConstants.InvokeAgentOperationName)))
            {
                foreach (var key in InvokeAgentAttributeKeys)
                {
                    activity.CoalesceTag(key, Baggage.Current.GetBaggage(key));
                }
            }

            // Coalesce any user-defined custom baggage keys onto every GenAI span.
            var customKeys = Baggage.Current.GetBaggage(OpenTelemetryConstants.CustomBaggageKeysKey);
            if (!string.IsNullOrEmpty(customKeys))
            {
                foreach (var key in customKeys!.Split(','))
                {
                    var trimmedKey = key.Trim();
                    if (trimmedKey.Length > 0 &&
                        !string.Equals(trimmedKey, OpenTelemetryConstants.CustomBaggageKeysKey, StringComparison.Ordinal))
                    {
                        activity.CoalesceTag(trimmedKey, Baggage.Current.GetBaggage(trimmedKey));
                    }
                }
            }

            base.OnStart(activity);
        }
    }
}
