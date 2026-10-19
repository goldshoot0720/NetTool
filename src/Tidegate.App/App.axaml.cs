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
            var board = new TrafficBoard(limits);
            _viewModel = new MainViewModel(
                board,
                new NettopTrafficSource(),
                new PsProcessCatalog(),
                new LsofConnectionLookup(),
                new MacLimitGateway(store.DirectoryPath),
                store,
                limits);
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
