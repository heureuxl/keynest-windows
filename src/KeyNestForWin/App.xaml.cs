using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using Forms = System.Windows.Forms;
using KeyNestForWin.Services;

namespace KeyNestForWin;

public partial class App : System.Windows.Application
{
    public static AppSettingsStore Settings { get; } = new();
    public static VaultService Vault { get; } = new(Settings);
    public static EntryUsageStore Usage { get; } = new();
    public static LocalBridgeServer Bridge { get; } = new(Vault);

    private Forms.NotifyIcon? _tray;
    private static Mutex? _singleInstanceMutex;

    // Windows API 用于激活已有实例窗口
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hWnd);

    private const int SW_RESTORE = 9;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 单实例检查：若已存在实例则激活并退出
        if (!TryAcquireMutex())
        {
            ActivateExistingInstance();
            Shutdown();
            return;
        }

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
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    /// <summary>尝试获取单实例互斥量，返回 true 表示首次启动。</summary>
    private static bool TryAcquireMutex()
    {
        const string mutexName = "Global\\KeyNestForWin_SingleInstance";
        bool createdNew;
        _singleInstanceMutex = new Mutex(true, mutexName, out createdNew);
        return createdNew;
    }

    /// <summary>激活已有实例的主窗口。</summary>
    private static void ActivateExistingInstance()
    {
        // 通过窗口标题查找已有实例
        var hWnd = FindWindow(null, "KeyNest");
        if (hWnd == nint.Zero)
        {
            // 尝试查找类名 WPF 窗口（HwndWrapper 类名不稳定，以标题为主）
            hWnd = FindWindow("HwndWrapper[DefaultDomain;", "KeyNest");
        }

        if (hWnd != nint.Zero)
        {
            if (IsIconic(hWnd))
                ShowWindow(hWnd, SW_RESTORE);
            SetForegroundWindow(hWnd);
        }
        else
        {
            // 若找不到窗口，至少提示用户已有实例在运行
            Forms.MessageBox.Show("KeyNest 已在运行，请通过系统托盘图标打开主窗口。", "KeyNest", Forms.MessageBoxButtons.OK, Forms.MessageBoxIcon.Information);
        }
    }
}
