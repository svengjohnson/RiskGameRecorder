
using System;
using System.Windows;
using Microsoft.Win32;

namespace RiskGameRecorder;

public partial class App : Application
{
    public static bool IsDarkMode { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        IsDarkMode = DetectDarkMode();
        LoadTheme(IsDarkMode);
        base.OnStartup(e);

        var viewModel = new MainViewModel();
        var window    = new GameRecorderWindow(viewModel.RecorderInfo);
        MainWindow    = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
        Environment.Exit(e.ApplicationExitCode);
    }

    static bool DetectDarkMode()
    {
        try
        {
            var value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1);
            return value is int i && i == 0;
        }
        catch
        {
            return false;
        }
    }

    static void LoadTheme(bool dark)
    {
        var uri = new Uri(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative);
        var dict = new ResourceDictionary { Source = uri };
        Current.Resources.MergedDictionaries.Clear();
        Current.Resources.MergedDictionaries.Add(dict);
    }
}
