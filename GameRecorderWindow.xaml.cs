
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace RiskGameRecorder;

public partial class GameRecorderWindow : Window
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public GameRecorderWindow(GameRecorderViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int value = App.IsDarkMode ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, Marshal.SizeOf(value));
        };
    }

    private void OpenRecordingsDirectory(object sender, RoutedEventArgs e)
    {
        var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GameReplays");
        Directory.CreateDirectory(dir);
        Process.Start("explorer.exe", dir);
    }


}
