using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.Core
{
    /// <summary>
    /// Registry of all available writer adapters.
    /// The UI layer queries this to discover what can be edited.
    /// </summary>
    public sealed class WriterAdapterCatalog
    {
        private readonly Dictionary<string, IWriterAdapter> _adapters = new(StringComparer.OrdinalIgnoreCase);

        public WriterAdapterCatalog()
        {
            // Register built-in adapters
            Register(new Writers.MonsterStatSheetAdapter());
            Register(new Writers.TreasureAdapter());
            Register(new Writers.MonsterFileAdapter());
            Register(new Writers.AbilityCommandAdapter());
            Register(new Writers.AtelPhaseRotationAdapter());
            Register(new Writers.BytePatchAdapter()); // perfil 1: byte-splice genérico (ByteReplace)
        }

        public void Register(IWriterAdapter adapter)
        {
            _adapters[adapter.CapabilityId] = adapter;
        }

        public IWriterAdapter? Get(string capabilityId)
        {
            return _adapters.TryGetValue(capabilityId, out var adapter) ? adapter : null;
        }

        public IReadOnlyList<IWriterAdapter> GetAll() => _adapters.Values.ToList();

        public IReadOnlyList<IWriterAdapter> GetByDomain(string domain)
        {
            return _adapters.Values
                .Where(a => a.CapabilityId.Contains(domain, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }
}
