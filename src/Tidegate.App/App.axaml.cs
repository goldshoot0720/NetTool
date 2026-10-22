using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Tidegate.App.ViewModels;
using Tidegate.App.Views;
using Tidegate.Core;
using Tidegate.Platform;

namespace Tidegate.App;

public partial class App : Application
{
    private MainViewModel? _viewModel;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var store = new JsonLimitStore();
            var limits = store.Load();
            var services = PlatformServices.Create(store.DirectoryPath);
            var board = new TrafficBoard(limits);
            _viewModel = new MainViewModel(
                board,
                services.Traffic,
                services.Catalog,
                services.Connections,
                services.Planner,
                services.Gateway,
                services.Elevation,
                services.Capabilities,
                store,
                limits,
                () => desktop.Shutdown());
            desktop.MainWindow = new MainWindow
            {
                DataContext = _viewModel,
            };
            desktop.ShutdownRequested += (_, _) => _viewModel.Stop();
            _viewModel.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
