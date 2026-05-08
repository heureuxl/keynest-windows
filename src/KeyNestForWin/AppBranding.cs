using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace KeyNestForWin;

/// <summary>从嵌入资源的 ICO 设置窗口图标（比 XAML 字符串 URI 更可靠）。</summary>
public static class AppBranding
{
    private const string IconPackUri = "pack://application:,,,/Assets/app.ico";

    public static void SetWindowIcon(Window window)
    {
        try
        {
            var info = System.Windows.Application.GetResourceStream(new Uri(IconPackUri, UriKind.Absolute));
            if (info?.Stream is not { } stream)
                return;
            using (stream)
            {
                window.Icon = BitmapFrame.Create(
                    stream,
                    BitmapCreateOptions.None,
                    BitmapCacheOption.OnLoad);
            }
        }
        catch
        {
            /* 缺失资源时保留系统默认 */
        }
    }

    /// <summary>供 WinForms NotifyIcon 使用，与窗口同源 ICO。</summary>
    public static System.Drawing.Icon? LoadTrayIcon()
    {
        try
        {
            var info = System.Windows.Application.GetResourceStream(new Uri(IconPackUri, UriKind.Absolute));
            if (info?.Stream is not { } stream)
                return null;
            using (stream)
            using (var ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                ms.Position = 0;
                using var tmp = new System.Drawing.Icon(ms);
                return (System.Drawing.Icon)tmp.Clone();
            }
        }
        catch
        {
            return null;
        }
    }
}
