// WHY: Legacy Aurora wrappers need bounded family cleanup without a second route registry.
// MAINT: This capability stores leases only. Never enroll by path, string ID or HTTP identity.
using System;
using System.Collections.Generic;
using FFXProjectEditor.Modules.Common.ViewerHub;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.AuroraChamber;

internal sealed class AuroraMemoryPreviewSession : IDisposable
{
    private const int MaxLeaseReferences = 32;
    private readonly object _gate = new();
    private readonly HashSet<StudioWebServer.ExactMemoryLease> _leases = new();
    private bool _disposed;

    internal AuroraBattleOverlayResult Stage(
        string battlePath, int encounterId, string selectedDirectory, StudioWebServer? server)
    {
        lock (_gate)
        {
            _leases.RemoveWhere(lease => !lease.IsActive);
            if (_disposed || _leases.Count >= MaxLeaseReferences)
                return new(false, Strings.U_Au_MemoryPreviewUnavailable);

            // Serialize admission/read/copy/handoff against this family's Clear/Dispose.
            AuroraBattleOverlayResult result = Aurora3DLauncher.StageNativeBattleCore(
                battlePath, encounterId, selectedDirectory, server);
            if (!result.Success || result.MemoryLease == null)
                return result;
            try
            {
                _leases.Add(result.MemoryLease);
                return result with { MemorySession = this };
            }
            catch
            {
                result.MemoryLease.Dispose();
                throw;
            }
        }
    }

    internal bool Owns(StudioWebServer.ExactMemoryLease lease)
    {
        lock (_gate) return !_disposed && _leases.Contains(lease);
    }

    internal void Forget(StudioWebServer.ExactMemoryLease lease)
    {
        lock (_gate) _leases.Remove(lease);
    }

    internal int Clear(out string detail)
    {
        lock (_gate)
        {
            var releasedPaths = new List<string>();
            foreach (var lease in _leases)
            {
                if (lease.IsActive) releasedPaths.Add(lease.RequestPath);
                lease.Dispose();
            }
            _leases.Clear();
            detail = string.Join(", ", releasedPaths);
            return releasedPaths.Count;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            Clear(out _);
        }
    }
}
