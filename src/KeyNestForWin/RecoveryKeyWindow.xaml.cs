using System.Windows;

namespace KeyNestForWin;

public partial class RecoveryKeyWindow : Window
{
    public RecoveryKeyWindow(string recoveryKeyPhrase, bool isRecoveryKeyRotation = false)
    {
        InitializeComponent();
        AppBranding.SetWindowIcon(this);
        KeyText.Text = recoveryKeyPhrase ?? "";
        if (isRecoveryKeyRotation)
            Title = "新的恢复密钥";
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(KeyText.Text);
            CopyBtn.Content = "已复制";
        }
        catch
        {
            CopyBtn.Content = "复制失败";
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
