using Avalonia;
using System;
using Tidegate.Platform.Windows;

namespace Tidegate.App;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Windows 版要讀每個應用程式的流量，靠的是只有系統管理員能開啟的 TCP 延伸統計。
        // 沒提權就先把自己用 UAC 重新開一份；使用者取消的話才退回唯讀畫面。
        if (OperatingSystem.IsWindows())
        {
            var elevation = new WindowsElevation();
            if (!elevation.IsElevated && elevation.TryStartElevated())
                return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
