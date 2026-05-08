using System.Windows;
using KeyNestForWin.Models;
using KeyNestForWin.Services;

namespace KeyNestForWin;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshUi();
        App.Vault.StateChanged += (_, _) => Dispatcher.Invoke(RefreshUi);
    }

    public void RefreshUi()
    {
        var v = App.Vault;
        if (v.IsUnlocked)
        {
            LockPanel.Visibility = Visibility.Collapsed;
            MainPanel.Visibility = Visibility.Visible;
            ItemsGrid.ItemsSource = v.Items;
            LockHint.Text = v.VaultExists ? "输入主密码以解锁保管库。" : "";
        }
        else
        {
            LockPanel.Visibility = Visibility.Visible;
            MainPanel.Visibility = Visibility.Collapsed;
            LockHint.Text = v.VaultExists
                ? "输入主密码以解锁保管库。"
                : "创建主密码以初始化本地加密保管库。";
        }

        if (v.IsUnlocked && BridgeToggle.IsChecked == true)
            App.Bridge.Start();
        else
            App.Bridge.Stop();
    }

    private async void Unlock_Click(object sender, RoutedEventArgs e)
    {
        LockError.Text = "";
        var pwd = MasterPasswordBox.Password;
        var result = await App.Vault.UnlockOrCreateAsync(pwd);
        MasterPasswordBox.Clear();
        if (result != VaultError.None)
        {
            LockError.Text = result switch
            {
                VaultError.WrongPassword => "主密码不正确。",
                VaultError.InvalidVault => "保管库文件无效或损坏。",
                _ => "无法解锁。"
            };
            return;
        }
        if (!string.IsNullOrEmpty(App.Vault.PendingRecoveryKeyToDisplay))
        {
            MessageBox.Show(
                "请立即保存恢复密钥（关闭本对话框后将不再完整展示）：\n\n" + App.Vault.PendingRecoveryKeyToDisplay,
                "恢复密钥",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            App.Vault.AcknowledgeRecoveryKeySaved();
        }
        RefreshUi();
    }

    private async void RecoveryUnlock_Click(object sender, RoutedEventArgs e)
    {
        LockError.Text = "";
        var phrase = RecoveryPhraseBox.Text.Trim();
        var newMaster = NewMasterAfterRecoveryBox.Password;
        if (newMaster.Length < 8)
        {
            LockError.Text = "新主密码至少 8 位。";
            return;
        }
        var result = await App.Vault.UnlockWithRecoveryAsync(phrase, newMaster);
        RecoveryPhraseBox.Clear();
        NewMasterAfterRecoveryBox.Clear();
        if (result != VaultError.None)
        {
            LockError.Text = result switch
            {
                VaultError.WrongRecoveryKey => "恢复密钥不正确或保管库格式不支持。",
                VaultError.InvalidVault => "保管库格式无效。",
                _ => "无法重置。"
            };
            return;
        }
        MessageBox.Show("已使用恢复密钥重置主密码并解锁。", "KeyNest", MessageBoxButton.OK, MessageBoxImage.Information);
        RefreshUi();
    }

    private void Lock_Click(object sender, RoutedEventArgs e)
    {
        App.Vault.Lock();
        App.Bridge.Stop();
        RefreshUi();
    }

    private void BridgeToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (App.Vault.IsUnlocked && BridgeToggle.IsChecked == true)
            App.Bridge.Start();
        else
            App.Bridge.Stop();
    }

    private void AddItem_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ItemEditWindow(null);
        if (dlg.ShowDialog() == true && dlg.ResultItem != null)
        {
            _ = App.Vault.UpsertItemAsync(dlg.ResultItem);
            RefreshUi();
        }
    }

    private void EditItem_Click(object sender, RoutedEventArgs e)
    {
        if (ItemsGrid.SelectedItem is not PasswordItemDto row) return;
        var dlg = new ItemEditWindow(row);
        if (dlg.ShowDialog() == true && dlg.ResultItem != null)
        {
            _ = App.Vault.UpsertItemAsync(dlg.ResultItem);
            RefreshUi();
        }
    }

    private async void DeleteItem_Click(object sender, RoutedEventArgs e)
    {
        if (ItemsGrid.SelectedItem is not PasswordItemDto row) return;
        if (MessageBox.Show("确定删除该条目？", "KeyNest", MessageBoxButton.YesNo, MessageBoxImage.Question) !=
            MessageBoxResult.Yes) return;
        await App.Vault.RemoveItemAsync(row.Id);
        RefreshUi();
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }
}
