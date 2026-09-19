using System.IO;
using System.Windows;
using CaptureDesk.Core;
using CaptureDesk.Native;
using WpfApplication = System.Windows.Application;

namespace CaptureDesk.App;

public partial class App : WpfApplication
{
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private System.Drawing.Icon? _brandIcon;
    private bool _diagnostic;
    public static bool IsExiting { get; private set; }
    public static AppSettings Settings { get; private set; } = new();
    public static IConfigStore ConfigStore { get; } = new JsonConfigStore();
    public static NativeCaptureService CaptureService { get; } = new();
    public static MemoryHistoryStore History { get; private set; } = new();
    public static MainWindow MainWindowInstance { get; private set; } = null!;

    public static void ApplySettings(AppSettings settings)
    {
        var startupChanged = settings.StartWithWindows != Settings.StartWithWindows;
        if (startupChanged) ConfigureStartup(settings.StartWithWindows);
        try { ConfigStore.Save(settings); }
        catch { if (startupChanged) ConfigureStartup(Settings.StartWithWindows); throw; }
        Settings = settings;
        History.SetLimit(settings.HistoryLimit);
        Theme.Apply(settings.ThemeMode == "深色");
    }
    private static void ConfigureStartup(bool enabled)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled)
        {
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("无法找到应用程序路径。");
            key.SetValue("CaptureDesk", "\"" + executable + "\" --background");
        }
        else key.DeleteValue("CaptureDesk", false);
    }
    public static void ExitApplication() { IsExiting = true; Current.Shutdown(); }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _diagnostic = e.Args.Any(arg => arg.StartsWith("--snapshot", StringComparison.Ordinal) || arg == "--verify-ui");
        Settings = _diagnostic ? new AppSettings() : ConfigStore.Load();
        Theme.Apply(Settings.ThemeMode == "深色");
        History = new MemoryHistoryStore(Settings.HistoryLimit);
        MainWindowInstance = new MainWindow(!_diagnostic);
        if (e.Args.Contains("--verify-ui"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var index = Array.IndexOf(e.Args, "--verify-ui");
                try { UiVerification.Run(e.Args[index + 1]); Shutdown(0); }
                catch (Exception ex) { File.WriteAllText(e.Args[index + 1], ex.ToString()); Shutdown(1); }
            }));
            return;
        }
        var settingsIndex = Array.IndexOf(e.Args, "--snapshot-settings");
        var snapshotIndex = Array.IndexOf(e.Args, "--snapshot");
        var captureIndex = Array.IndexOf(e.Args, "--snapshot-capture");
        if (settingsIndex >= 0 || snapshotIndex >= 0 || captureIndex >= 0)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var index = Math.Max(settingsIndex, Math.Max(snapshotIndex, captureIndex));
            Window window = MainWindowInstance;
            if (e.Args.Contains("--dark")) Theme.Apply(true);
            if (settingsIndex >= 0)
            {
                var settings = new SettingsWindow();
                if (e.Args.Length > index + 2 && int.TryParse(e.Args[index + 2], out var page)) settings.SelectPage(page);
                if (e.Args.Contains("--dark")) Theme.Apply(true);
                if (e.Args.Contains("--compact")) { settings.Width = 880; settings.Height = 580; }
                window = settings;
            }
            if (captureIndex >= 0) window = UiVerification.CreateOverlayPreview();
            window.ContentRendered += (_, _) =>
            {
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    UiSnapshot.Save(window, e.Args[index + 1]);
                    Shutdown();
                }));
            };
            window.Show();
            return;
        }
        MainWindowInstance.Show();
        using (var iconStream = GetResourceStream(new Uri("pack://application:,,,/CaptureDesk.App;component/Assets/Brand/CaptureDesk.ico")).Stream)
        using (var sourceIcon = new System.Drawing.Icon(iconStream, 32, 32))
            _brandIcon = (System.Drawing.Icon)sourceIcon.Clone();
        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = "CaptureDesk", Icon = _brandIcon, Visible = true,
            ContextMenuStrip = new System.Windows.Forms.ContextMenuStrip()
        };
        _trayIcon.ContextMenuStrip.Items.Add("截图", null, (_, _) => Dispatcher.Invoke(MainWindowInstance.StartCaptureFromTray));
        _trayIcon.ContextMenuStrip.Items.Add("历史", null, (_, _) => Dispatcher.Invoke(MainWindowInstance.OpenHistory));
        _trayIcon.ContextMenuStrip.Items.Add("设置", null, (_, _) => Dispatcher.Invoke(MainWindowInstance.OpenSettings));
        _trayIcon.ContextMenuStrip.Items.Add("显示主窗口", null, (_, _) => Dispatcher.Invoke(() => { MainWindowInstance.Show(); MainWindowInstance.Activate(); }));
        _trayIcon.ContextMenuStrip.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        _trayIcon.ContextMenuStrip.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(ExitApplication));
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(() => { MainWindowInstance.Show(); MainWindowInstance.Activate(); });
        if (e.Args.Contains("--background")) MainWindowInstance.Hide();
        if (e.Args.Contains("--settings")) Dispatcher.BeginInvoke(new Action(MainWindowInstance.OpenSettings));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (!_diagnostic)
        {
            try { ConfigStore.Save(Settings); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        if (_trayIcon is not null) { _trayIcon.Visible = false; _trayIcon.Dispose(); }
        _brandIcon?.Dispose();
        base.OnExit(e);
    }
}
