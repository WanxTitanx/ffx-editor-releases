using FFXProjectEditor.Core;
using FFXProjectEditor.Core.Writers;
using Xunit;

namespace FFXProjectEditor.Tests.Core
{
    public class WriterAdapterCatalogTests
    {
        [Fact]
        public void GetAll_ReturnsAtLeastTwoAdapters()
        {
            var catalog = new WriterAdapterCatalog();
            Assert.True(catalog.GetAll().Count >= 2);
        }

        [Fact]
        public void Get_MonsterStats_ReturnsAdapter()
        {
            var catalog = new WriterAdapterCatalog();
            var adapter = catalog.Get("monster-stats");
            Assert.NotNull(adapter);
            Assert.Equal("Monster Stat Sheet", adapter.DisplayName);
        }

        [Fact]
        public void Get_Treasure_ReturnsAdapter()
        {
            var catalog = new WriterAdapterCatalog();
            var adapter = catalog.Get("treasure-editor");
            Assert.NotNull(adapter);
            Assert.Equal("Treasure Table", adapter.DisplayName);
        }

        [Fact]
        public void Get_MonsterFile_ReturnsAdapter()
        {
            var catalog = new WriterAdapterCatalog();
            var adapter = catalog.Get("monster-file");
            Assert.NotNull(adapter);
            Assert.Equal("Monster File", adapter.DisplayName);
        }

        [Fact]
        public void Get_UnknownId_ReturnsNull()
        {
            var catalog = new WriterAdapterCatalog();
            Assert.Null(catalog.Get("nonexistent"));
        }

        [Fact]
        public void Register_DuplicateId_Overwrites()
        {
            var catalog = new WriterAdapterCatalog();
            var countBefore = catalog.GetAll().Count;

            // Re-register monster-stats (already registered in constructor)
            var adapter = catalog.Get("monster-stats")!;
            catalog.Register(adapter);

            Assert.Equal(countBefore, catalog.GetAll().Count);
        }
    }
}
