using Avalonia.Controls;
using Avalonia.Interactivity;
using System;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor.Modules.AuroraFieldExplorer
{
    public partial class AuroraFieldExplorer_Control : UserControl, IRestorableModule
    {
        readonly AuroraFieldExplorer_DataModel dataModel;

        /// <summary>User clicked open arena — parent opens Aurora Chamber at the selected map key.</summary>
        public event Action<string>? RequestOpenAuroraChamberForMap;

        public AuroraFieldExplorer_Control()
        {
            dataModel = new AuroraFieldExplorer_DataModel();
            DataContext = dataModel;
            dataModel.ViewerNavigateRequested += OnViewerNavigateRequested;
            InitializeComponent();
        }

        void OnViewerNavigateRequested(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return;
            // Non-Windows: no WebView2 embed — the same loopback URL opens in the system browser.
            if (!Modules.Common.ViewerShell.ExternalBrowserLauncher.IsEmbeddedViewerSupported)
            {
                Modules.Common.ViewerShell.ExternalBrowserLauncher.Open(url);
                return;
            }
            ViewerHost.Navigate(url);
        }

        private void Button_Refresh(object? sender, RoutedEventArgs e) => dataModel.RefreshCatalog();

        private async void Button_PublishScout(object? sender, RoutedEventArgs e) =>
            await dataModel.PublishScoutAsync();

        private async void Button_RefreshWalkMarkers(object? sender, RoutedEventArgs e) =>
            await dataModel.RefreshWalkMarkersAsync();

        private async void Button_PublishAndOpen(object? sender, RoutedEventArgs e) =>
            await dataModel.PublishAndOpenSelectedAsync();

        private async void Button_ValidateVisual(object? sender, RoutedEventArgs e) =>
            await dataModel.ValidateVisualAsync();

        private async void Button_Render(object? sender, RoutedEventArgs e) =>
            await dataModel.RenderSelectedAsync(forceReexport: false);

        private async void Button_Reexport(object? sender, RoutedEventArgs e) =>
            await dataModel.RenderSelectedAsync(forceReexport: true);

        private void Button_OpenInBrowser(object? sender, RoutedEventArgs e) => dataModel.OpenInBrowser();

        private void Button_OutputFolder(object? sender, RoutedEventArgs e) => dataModel.OpenOutputFolder();

        private void Button_OpenArena(object? sender, RoutedEventArgs e)
        {
            string? mapKey = dataModel.GetSelectedMapKeyForArena();
            if (!string.IsNullOrWhiteSpace(mapKey))
                RequestOpenAuroraChamberForMap?.Invoke(mapKey);
        }
    }
}
