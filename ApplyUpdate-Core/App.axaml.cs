// using ApplyUpdate_Avalonia.ViewModels;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Hi3Helper;
using Hi3Helper.Data;
using System.IO;
#if !DEBUG
using System;
#endif

namespace ApplyUpdate;

public partial class App : Application
{
    public static IClassicDesktopStyleApplicationLifetime CurrentWindow;

    public override void Initialize()
    {
        string localeName = GetCurrentLanguageFromCollapseConfig();
        Locale.InitializeLocale();
        Locale.LoadLocale(localeName);
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime window)
        {
            CurrentWindow = window;
            window.MainWindow = new MainWindow()
            /*
            {
                DataContext = new MainViewModel()
            }
            */
            ;
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

    public string GetCurrentLanguageFromCollapseConfig()
    {
        const string defaultLocale = "en-us";
        string configFile = Statics.AppConfigFile;

        string sectionName = "app";
        string keyName = "AppLanguage";
        if (File.Exists(configFile))
        {
            IniFile iniFile = new IniFile();
            iniFile.Load(configFile);

            if (!iniFile.ContainsSection(sectionName)) return defaultLocale;
            return iniFile[sectionName].ContainsKey(keyName) ? iniFile[sectionName][keyName].ToString() : defaultLocale;
        }

        return defaultLocale;
    }

    public static nint GetCurrentWindowHwnd()
        => CurrentWindow.MainWindow.TryGetPlatformHandle()?.Handle ?? nint.Zero;
}
