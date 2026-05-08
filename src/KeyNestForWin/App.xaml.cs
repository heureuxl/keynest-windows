using System.Windows;
using Forms = System.Windows.Forms;
using KeyNestForWin.Services;

namespace KeyNestForWin;

public partial class App : System.Windows.Application
{
    public static VaultService Vault { get; } = new();
    public static LocalBridgeServer Bridge { get; } = new(Vault);

    private Forms.NotifyIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var tray = new Forms.NotifyIcon
        {
            Visible = true,
            Text = "KeyNest",
            Icon = AppBranding.LoadTrayIcon() ?? System.Drawing.SystemIcons.Application
        };
        tray.ContextMenuStrip = new Forms.ContextMenuStrip();
        tray.ContextMenuStrip.Items.Add("打开主窗口", null, (_, _) => ShowMainWindow());
        tray.ContextMenuStrip.Items.Add("锁定保管库", null, (_, _) =>
        {
            Vault.Lock();
            Current.Dispatcher.Invoke(() =>
            {
                foreach (Window w in Current.Windows)
                    if (w is MainWindow mw) mw.RefreshUi();
            });
        });
        tray.ContextMenuStrip.Items.Add("退出 KeyNest", null, (_, _) => Shutdown());
        tray.DoubleClick += (_, _) => ShowMainWindow();
        _tray = tray;

        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    private void ShowMainWindow()
    {
        if (MainWindow != null)
        {
            if (MainWindow is MainWindow mw)
                mw.RefreshUi();
            MainWindow.Show();
            MainWindow.WindowState = WindowState.Normal;
            MainWindow.Activate();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Bridge.Stop();
        _tray?.Dispose();
        base.OnExit(e);
    }
}
