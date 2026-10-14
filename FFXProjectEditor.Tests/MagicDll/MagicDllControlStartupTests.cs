using System;
using FFXProjectEditor.Modules.MagicDllEditor;
using Xunit;

namespace FFXProjectEditor.Tests.MagicDll
{
    /// <summary>
    /// Smoke de startup do Control: instancia o MagicDllEditor_Control (DataContext =
    /// ViewModel) sem host Avalonia — prova que o módulo carrega sem exceção de
    /// construção (o registro no ModuleCatalogPolicy não quebra o boot do editor).
    /// </summary>
    public class MagicDllControlStartupTests
    {
        [Fact]
        public void Control_Instantiates_WithViewModelDataContext()
        {
            var control = new MagicDllEditor_Control();
            Assert.NotNull(control);
            Assert.NotNull(control.DataContext);
            Assert.IsType<MagicDllEditor_ViewModel>(control.DataContext);
        }

        [Fact]
        public void ViewModel_Defaults_AreSafe()
        {
            var vm = new MagicDllEditor_ViewModel();
            Assert.False(vm.HasDocument);
            Assert.False(vm.IsDirty);
            Assert.False(vm.CanAddField);
            Assert.False(vm.HasBackup);
            Assert.Empty(vm.RootNodes);
            Assert.NotNull(vm.Log);
            Assert.Equal(4, vm.GrowWidth); // default da UI
        }
    }
}