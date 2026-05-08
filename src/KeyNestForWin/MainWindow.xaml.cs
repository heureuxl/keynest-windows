using System.Windows;
using System.Windows.Controls;
using KeyNestForWin.Models;
using KeyNestForWin.Services;

namespace KeyNestForWin;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AppBranding.SetWindowIcon(this);
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
            ItemsGrid.Items.Refresh();
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

    private string ReadMasterPassword() =>
        MasterPasswordPlain.Visibility == Visibility.Visible ? MasterPasswordPlain.Text : MasterPasswordBox.Password;

    private string ReadNewMasterAfterRecovery() =>
        NewMasterPlain.Visibility == Visibility.Visible ? NewMasterPlain.Text : NewMasterAfterRecoveryBox.Password;

    private void ClearMasterPasswordFields()
    {
        MasterPasswordBox.Clear();
        MasterPasswordPlain.Clear();
        SetPasswordFieldVisible(MasterPasswordBox, MasterPasswordPlain, MasterPasswordToggle, visible: false);
        NewMasterAfterRecoveryBox.Clear();
        NewMasterPlain.Clear();
        SetPasswordFieldVisible(NewMasterAfterRecoveryBox, NewMasterPlain, NewMasterToggle, visible: false);
    }

    private static void SetPasswordFieldVisible(PasswordBox pwdBox, System.Windows.Controls.TextBox plainBox, System.Windows.Controls.Button toggleBtn, bool visible)
    {
        if (visible)
        {
            plainBox.Text = pwdBox.Password;
            plainBox.Visibility = Visibility.Visible;
            pwdBox.Visibility = Visibility.Collapsed;
            toggleBtn.Content = "隐藏";
        }
        else
        {
            pwdBox.Password = plainBox.Text;
            pwdBox.Visibility = Visibility.Visible;
            plainBox.Visibility = Visibility.Collapsed;
            toggleBtn.Content = "显示";
        }
    }

    private void MasterPasswordToggle_Click(object sender, RoutedEventArgs e) =>
        SetPasswordFieldVisible(MasterPasswordBox, MasterPasswordPlain, MasterPasswordToggle,
            MasterPasswordPlain.Visibility != Visibility.Visible);

    private void NewMasterToggle_Click(object sender, RoutedEventArgs e) =>
        SetPasswordFieldVisible(NewMasterAfterRecoveryBox, NewMasterPlain, NewMasterToggle,
            NewMasterPlain.Visibility != Visibility.Visible);

    private async void Unlock_Click(object sender, RoutedEventArgs e)
    {
        LockError.Text = "";
        var pwd = ReadMasterPassword();
        var result = await App.Vault.UnlockOrCreateAsync(pwd);
        ClearMasterPasswordFields();
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
            var dlg = new RecoveryKeyWindow(App.Vault.PendingRecoveryKeyToDisplay)
            {
                Owner = this
            };
            dlg.ShowDialog();
            App.Vault.AcknowledgeRecoveryKeySaved();
        }
        RefreshUi();
    }

    private async void RecoveryUnlock_Click(object sender, RoutedEventArgs e)
    {
        LockError.Text = "";
        var phrase = RecoveryPhraseBox.Text.Trim();
        var newMaster = ReadNewMasterAfterRecovery();
        if (newMaster.Length < 8)
        {
            LockError.Text = "新主密码至少 8 位。";
            return;
        }
        var result = await App.Vault.UnlockWithRecoveryAsync(phrase, newMaster);
        RecoveryPhraseBox.Clear();
        NewMasterAfterRecoveryBox.Clear();
        NewMasterPlain.Clear();
        SetPasswordFieldVisible(NewMasterAfterRecoveryBox, NewMasterPlain, NewMasterToggle, visible: false);
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
        System.Windows.MessageBox.Show("已使用恢复密钥重置主密码并解锁。", "KeyNest", MessageBoxButton.OK, MessageBoxImage.Information);
        RefreshUi();
    }

    private async void RotateRecoveryKey_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.MessageBox.Show(
                "确定要更换恢复密钥吗？\n\n更换成功后，旧恢复密钥将立即失效，无法再用于找回主密码。\n请务必保存界面中展示的新密钥。",
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
        if (System.Windows.MessageBox.Show("确定删除该条目？", "KeyNest", MessageBoxButton.YesNo, MessageBoxImage.Question) !=
            MessageBoxResult.Yes) return;
        await App.Vault.RemoveItemAsync(row.Id);
        RefreshUi();
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => System.Windows.Application.Current.Shutdown();

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }
}
