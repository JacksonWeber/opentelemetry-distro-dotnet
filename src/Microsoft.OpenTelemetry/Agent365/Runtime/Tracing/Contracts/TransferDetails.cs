#pragma warning disable RS0026 // Multiple overloads with optional parameters — by design for ergonomic agent vs explicit typed transfer construction
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts
{
    /// <summary>
    /// Describes an explicit transfer performed by an execute-tool operation.
    /// </summary>
    public sealed class TransferDetails : IEquatable<TransferDetails>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TransferDetails"/> class.
        /// </summary>
        /// <param name="mode">How control passes to the target.</param>
        /// <param name="targetAgentDetails">Optional target agent identity. Only <see cref="AgentDetails.AgentId"/>, <see cref="AgentDetails.AgentName"/>, <see cref="AgentDetails.AgentBlueprintId"/>, <see cref="AgentDetails.AgentPlatformId"/>, and <see cref="AgentDetails.AgentVersion"/> are emitted for this transfer model. Other <see cref="AgentDetails"/> properties are not emitted.</param>
        /// <remarks>The target type defaults to <see cref="TransferTargetType.Agent"/>, including when <paramref name="targetAgentDetails"/> is omitted.</remarks>
        public TransferDetails(
            TransferMode mode,
            AgentDetails? targetAgentDetails = null)
            : this(mode, TransferTargetType.Agent, targetAgentDetails)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TransferDetails"/> class.
        /// </summary>
        /// <param name="mode">How control passes to the target.</param>
        /// <param name="targetType">What kind of target receives control.</param>
        /// <param name="targetAgentDetails">Optional target agent identity. Only <see cref="AgentDetails.AgentId"/>, <see cref="AgentDetails.AgentName"/>, <see cref="AgentDetails.AgentBlueprintId"/>, <see cref="AgentDetails.AgentPlatformId"/>, and <see cref="AgentDetails.AgentVersion"/> are emitted for this transfer model. Other <see cref="AgentDetails"/> properties are not emitted.</param>
        public TransferDetails(
            TransferMode mode,
            TransferTargetType targetType,
            AgentDetails? targetAgentDetails = null)
        {
            if (!Enum.IsDefined(typeof(TransferMode), mode))
            {
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "The transfer mode must be a defined enum value.");
            }

            if (!Enum.IsDefined(typeof(TransferTargetType), targetType))
            {
                throw new ArgumentOutOfRangeException(nameof(targetType), targetType, "The transfer target type must be a defined enum value.");
            }

            if (targetAgentDetails != null && targetType != TransferTargetType.Agent)
            {
                throw new ArgumentException("Target agent details can only be supplied when the transfer target type is Agent.", nameof(targetAgentDetails));
            }

            Mode = mode;
            TargetType = targetType;
            TargetAgentDetails = targetAgentDetails;
        }

        /// <summary>
        /// Gets how control passes to the target.
        /// </summary>
        public TransferMode Mode { get; }

        /// <summary>
        /// Gets what kind of target receives control.
        /// </summary>
        public TransferTargetType TargetType { get; }

        /// <summary>
        /// Gets the optional target agent identity whose supported fields are emitted for transfers.
        /// </summary>
        public AgentDetails? TargetAgentDetails { get; }

        internal string ModeValue => Mode switch
        {
            TransferMode.ReturnToCaller => "return_to_caller",
            TransferMode.PassControl => "pass_control",
            _ => throw new ArgumentOutOfRangeException(nameof(Mode)),
        };

        internal string TargetTypeValue => TargetType switch
        {
            TransferTargetType.Agent => "agent",
            TransferTargetType.Human => "human",
            TransferTargetType.Workflow => "workflow",
            _ => throw new ArgumentOutOfRangeException(nameof(TargetType)),
        };

        /// <inheritdoc/>
        public bool Equals(TransferDetails? other) =>
            other is not null &&
            Mode == other.Mode &&
            TargetType == other.TargetType &&
            EqualityComparer<AgentDetails?>.Default.Equals(TargetAgentDetails, other.TargetAgentDetails);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => Equals(obj as TransferDetails);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = (hash * 31) + Mode.GetHashCode();
                hash = (hash * 31) + TargetType.GetHashCode();
                hash = (hash * 31) + (TargetAgentDetails?.GetHashCode() ?? 0);
                return hash;
            }
        }
    }

    /// <summary>
    /// Describes how control passes during a transfer.
    /// </summary>
    public enum TransferMode
    {
        /// <summary>
        /// The source agent waits for the target and resumes afterward.
        /// </summary>
        ReturnToCaller,

        /// <summary>
        /// The target assumes control of the remaining work.
        /// </summary>
        PassControl,
    }

    /// <summary>
    /// Describes the kind of target receiving an explicit transfer.
    /// </summary>
    public enum TransferTargetType
    {
        /// <summary>
        /// The transfer target is another agent.
        /// </summary>
        Agent,

        /// <summary>
        /// The transfer target is a human.
        /// </summary>
        Human,

        /// <summary>
        /// The transfer target is a workflow.
        /// </summary>
        Workflow,
    }
}
