using Avalonia;
using System;

namespace CS2_Director_Tool.App;

sealed class Program
{
    // 初始化代码。在调用 AppMain 之前，不要使用任何 Avalonia、第三方 API 或依赖
    // SynchronizationContext 的代码：此时一切都尚未初始化，可能会引发问题。
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia 配置，请勿移除；可视化设计器也会使用此方法。
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}