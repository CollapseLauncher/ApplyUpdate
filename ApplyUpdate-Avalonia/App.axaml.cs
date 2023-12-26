// using ApplyUpdate_Avalonia.ViewModels;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
#if !DEBUG
using System;
#endif

namespace ApplyUpdate;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime window)
        {
            PInvoke.m_window = window;
            window.MainWindow = new MainWindow();
            window.Exit += (a, b) =>
            {
#if !DEBUG
                PInvoke.ShowWindow(PInvoke.m_consoleWindow, 5);
                Console.WriteLine("Quit from ApplyUpdate and restored the console window.");
#endif
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
