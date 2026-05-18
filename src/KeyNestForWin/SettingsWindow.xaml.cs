using System.Windows;
using KeyNestForWin.Services;

namespace KeyNestForWin;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        AppBranding.SetWindowIcon(this);
        MaxAccountsBox.Text = App.Settings.MaxAccountsPerSiteHost.ToString();
        DistinguishHostsByIpCheck.IsChecked = App.Settings.DistinguishHostsByIp;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        SettingsError.Visibility = Visibility.Collapsed;
        if (!int.TryParse(MaxAccountsBox.Text.Trim(), out var n))
        {
            SettingsError.Text = "请输入有效整数。";
            SettingsError.Visibility = Visibility.Visible;
            return;
        }

        n = AppSettingsStore.ClampMaxAccounts(n);
        App.Settings.Save(n, DistinguishHostsByIpCheck.IsChecked == true);

        if (App.Vault.IsUnlocked)
            await App.Vault.EnforceLimitsAndPersistAsync();

        DialogResult = true;
        Close();
    }

    private async void MergeDuplicates_Click(object sender, RoutedEventArgs e)
    {
        if (!App.Vault.IsUnlocked)
        {
            SettingsError.Text = "请先解锁保管库。";
            SettingsError.Visibility = Visibility.Visible;
            return;
        }
        SettingsError.Visibility = Visibility.Collapsed;
        var n = await App.Vault.MergeDuplicateHostUsernamesAsync();
        var msg = n > 0
            ? $"已合并删除 {n} 条重复条目。"
            : "当前没有可合并的重复条目。";
        System.Windows.MessageBox.Show(msg, "KeyNest", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void RotateRecoveryKey_Click(object sender, RoutedEventArgs e)
    {
        if (!App.Vault.IsUnlocked)
        {
            SettingsError.Text = "请先解锁保管库。";
            SettingsError.Visibility = Visibility.Visible;
            return;
        }
        SettingsError.Visibility = Visibility.Collapsed;
        if (System.Windows.MessageBox.Show(
                "确定要更换恢复密钥吗？\n\n更换成功后，旧恢复密钥将立即失效。请务必保存即将展示的新密钥。",
                "KeyNest",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        try
        {
            var phrase = await App.Vault.RotateRecoveryKeyAsync();
            var dlg = new RecoveryKeyWindow(phrase, isRecoveryKeyRotation: true) { Owner = this };
            dlg.ShowDialog();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"更换失败：{ex.Message}", "KeyNest", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
