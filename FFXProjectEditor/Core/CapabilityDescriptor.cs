using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.Core
{
    /// <summary>
    /// How the capability operates at runtime.
    /// </summary>
    public enum CapabilityMode
    {
        ReadOnly = 0,
        OfflineWriter = 1,
        RuntimeTool = 2,
        Lab = 3,
        Research = 4
    }

    /// <summary>
    /// How much trust we have in the underlying data or RE.
    /// </summary>
    public enum EvidenceLevel
    {
        Production = 0,
        Partial = 1,
        NeedsTesting = 2,
        Blocked = 3,
        Research = 4
    }

    /// <summary>
    /// Game platform this capability targets.
    /// </summary>
    public enum Platform
    {
        PS2,
        PS3,
        PC,
        All
    }

    /// <summary>
    /// Granular operation types for capability scoping.
    /// </summary>
    public enum AllowedOperation
    {
        Read,
        Edit,
        Clone,
        Export,
        Patch
    }

    /// <summary>
    /// Declares a single editor capability: what it is, what it needs,
    /// what it can do, and how safe it is.
    /// </summary>
    public sealed record CapabilityDescriptor
    {
        public required string Id { get; init; }
        public required string Domain { get; init; }
        public required string Title { get; init; }
        public required string Description { get; init; }
        public required CapabilityMode Mode { get; init; }
        public required EvidenceLevel Evidence { get; init; }
        public required IReadOnlyList<Platform> Platforms { get; init; }
        public required IReadOnlyList<string> RequiredDependencies { get; init; }
        public required IReadOnlyList<string> OptionalDependencies { get; init; }
        public required IReadOnlyList<string> Risks { get; init; }
        public required IReadOnlyList<AllowedOperation> AllowedOperations { get; init; }
        public required IReadOnlyList<AllowedOperation> ProhibitedOperations { get; init; }
        public required IReadOnlyList<string> Preconditions { get; init; }
        public required IReadOnlyList<string> DocumentationLinks { get; init; }
        public required string OwnerAgent { get; init; }
    }

    /// <summary>
    /// A capability bound to a specific workspace context.
    /// Computed by merging the static CapabilityDescriptor
    /// with the live workspace inspection results.
    /// </summary>
    public sealed record CapabilityAvailability
    {
        public required CapabilityDescriptor Descriptor { get; init; }
        public required bool IsAvailable { get; init; }
        public required IReadOnlyList<string> UnavailabilityReasons { get; init; }
        public required bool UserEnabled { get; init; }
    }

    /// <summary>
    /// Output of a workspace scan. Produced once when the user
    /// opens or switches a workspace.
    /// </summary>
    public sealed record WorkspaceInspectionResult
    {
        /// <summary>Extracted-game workspace (the <c>master</c> folder) — the editing base, independent of the install.</summary>
        public required string SourcePath { get; init; }
        /// <summary>FFX HD install root (the folder containing <c>FFX.exe</c>) — deploy paths derive from here.</summary>
        public string? GamePath { get; init; }
        public string? OutputPath { get; init; }
        public required string DetectedVersion { get; init; }
        public required Platform DetectedPlatform { get; init; }
        public required IReadOnlyList<CapabilityAvailability> Capabilities { get; init; }
        public required IReadOnlyList<string> MissingDependencies { get; init; }
        public required IReadOnlyList<string> Warnings { get; init; }
        public required DateTimeOffset ScanTimestamp { get; init; }
    }
}
