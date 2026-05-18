using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using KeyNestForWin.Models;
using KeyNestForWin.Services;

namespace KeyNestForWin;

public partial class MainWindow : Window
{
    private enum VaultListFilter { All, Favorites, Recent, EmptyPassword, WeakPassword }
    private enum VaultListLayout { Flat, ByHost }

    private VaultListFilter _listFilter = VaultListFilter.All;
    private VaultListLayout _layoutMode = VaultListLayout.Flat;
    private Guid? _selectedId;
    private bool _detailPasswordRevealed;
    private CollectionViewSource? _itemsView;

    public MainWindow()
    {
        InitializeComponent();
        AppBranding.SetWindowIcon(this);
        InitFilterCombos();
        Loaded += (_, _) => RefreshUi();
        App.Vault.StateChanged += (_, _) => Dispatcher.Invoke(RefreshUi);
    }

    private static readonly string[] FilterLabels =
        ["全部", "收藏", "最近使用", "空密码", "弱密码"];

    private static readonly VaultListFilter[] FilterValues =
    [
        VaultListFilter.All,
        VaultListFilter.Favorites,
        VaultListFilter.Recent,
        VaultListFilter.EmptyPassword,
        VaultListFilter.WeakPassword
    ];

    private static readonly string[] LayoutLabels = ["列表", "按域名分组"];

    private static readonly VaultListLayout[] LayoutValues =
        [VaultListLayout.Flat, VaultListLayout.ByHost];

    private void InitFilterCombos()
    {
        FilterCombo.ItemsSource = FilterLabels;
        FilterCombo.SelectedIndex = 0;
        LayoutCombo.ItemsSource = LayoutLabels;
        LayoutCombo.SelectedIndex = 0;
    }

    public void RefreshUi()
    {
        var v = App.Vault;
        if (v.IsUnlocked)
        {
            LockPanel.Visibility = Visibility.Collapsed;
            MainPanel.Visibility = Visibility.Visible;
            App.Usage.Prune(v.Items.Select(x => x.Id).ToHashSet());
            RefreshItemList();
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

    private void RefreshItemList()
    {
        var v = App.Vault;
        var tokens = PasswordItemSearch.SplitSearchTokens(SearchBox?.Text ?? "");

        IEnumerable<PasswordItemDto> list = _listFilter switch
        {
            VaultListFilter.Favorites => v.Items.Where(x => x.IsFavorite),
            VaultListFilter.Recent => v.Items,
            VaultListFilter.EmptyPassword => v.Items.Where(x => string.IsNullOrEmpty(x.Password)),
            VaultListFilter.WeakPassword => v.Items.Where(x => PasswordStrength.IsWeak(x.Password)),
            _ => v.Items.AsEnumerable()
        };

        if (tokens.Count > 0)
            list = list.Where(x => PasswordItemSearch.MatchesSearchTokens(x, tokens));

        List<PasswordItemDto> sorted;
        if (_listFilter == VaultListFilter.Recent)
        {
            var order = App.Usage.SortIdsByRecentFirst(list.Select(x => x.Id).ToList());
            var map = list.ToDictionary(x => x.Id);
            sorted = order.Where(map.ContainsKey).Select(id => map[id]).ToList();
        }
        else
            sorted = OrderByVaultSequence(list);

        var rows = sorted.Select(ToListRow).ToList();
        _itemsView = new CollectionViewSource { Source = rows };
        if (_layoutMode == VaultListLayout.ByHost)
            _itemsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(VaultListRow.HostGroupTitle)));
        ItemsList.ItemsSource = _itemsView.View;

        FilterCaption.Text = _listFilter switch
        {
            VaultListFilter.Favorites => $"收藏 {rows.Count} 条",
            VaultListFilter.Recent => $"按最近打开/复制排序 · {rows.Count} 条",
            VaultListFilter.EmptyPassword => $"密码为空的条目 · {rows.Count} 条",
            VaultListFilter.WeakPassword => $"本地规则判定为弱密码 · {rows.Count} 条",
            _ => $"共 {rows.Count} 条"
        };

        EnsureSelectionValid(rows);
        UpdateDetailPanel();
    }

    private static List<PasswordItemDto> OrderByVaultSequence(IEnumerable<PasswordItemDto> items)
    {
        var index = new Dictionary<Guid, int>();
        for (var i = 0; i < App.Vault.Items.Count; i++)
            index[App.Vault.Items[i].Id] = i;
        return items.OrderBy(x => index.GetValueOrDefault(x.Id, int.MaxValue)).ToList();
    }

    private void RowGrip_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Controls.RowReorderGrip grip)
        {
            grip.ReorderTo -= OnRowReorder;
            grip.ReorderTo += OnRowReorder;
            grip.IsEnabled = _layoutMode == VaultListLayout.Flat;
        }
    }

    private async void OnRowReorder(object? sender, Controls.RowReorderEventArgs e)
    {
        if (_layoutMode != VaultListLayout.Flat)
        {
            System.Windows.MessageBox.Show("请切换到「列表」布局后再拖动调整顺序。", "KeyNest",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var targetRow = GetRowAt(ItemsList, Mouse.GetPosition(ItemsList));
        if (targetRow == null || targetRow.Id == e.SourceId) return;
        await App.Vault.MoveItemBeforeAsync(e.SourceId, targetRow.Id);
        RefreshUi();
    }

    private static VaultListRow? GetRowAt(System.Windows.Controls.ListView list, System.Windows.Point position)
    {
        var element = list.InputHitTest(position) as DependencyObject;
        while (element != null)
        {
            if (element is System.Windows.Controls.ListViewItem lvi)
                return lvi.Content as VaultListRow;
            element = System.Windows.Media.VisualTreeHelper.GetParent(element);
        }
        return null;
    }

    private static VaultListRow ToListRow(PasswordItemDto item)
    {
        var distinguish = App.Settings.DistinguishHostsByIp;
        var identityKey = SiteIdentityService.GetIdentityKey(item.Url, item.SiteEndpoint, distinguish)
                          ?? "__none__";
        var site = SiteIdentityService.FormatSiteDisplay(item.Url, item.SiteEndpoint, distinguish);
        return new VaultListRow
        {
            Id = item.Id,
            Title = string.IsNullOrEmpty(item.Title) ? "未命名" : item.Title,
            Username = item.Username,
            SiteHost = site,
            HostGroupKey = identityKey,
            HostGroupTitle = SiteIdentityService.FormatGroupTitle(identityKey == "__none__" ? null : identityKey),
            IsFavorite = item.IsFavorite,
            Source = item
        };
    }

    private void EnsureSelectionValid(IReadOnlyList<VaultListRow> rows)
    {
        var ids = rows.Select(r => r.Id).ToHashSet();
        if (_selectedId is { } sid && ids.Contains(sid))
        {
            SelectRowById(sid);
            return;
        }
        _selectedId = rows.FirstOrDefault()?.Id;
        if (_selectedId != null)
            SelectRowById(_selectedId.Value);
    }

    private void SelectRowById(Guid id)
    {
        if (ItemsList.ItemsSource is not ICollectionView view) return;
        foreach (var obj in view)
        {
            if (obj is VaultListRow row && row.Id == id)
            {
                ItemsList.SelectedItem = row;
                return;
            }
        }
    }

    private PasswordItemDto? GetSelectedItem()
    {
        if (ItemsList.SelectedItem is VaultListRow row)
            return App.Vault.Items.FirstOrDefault(x => x.Id == row.Id);
        if (_selectedId is { } id)
            return App.Vault.Items.FirstOrDefault(x => x.Id == id);
        return null;
    }

    private void UpdateDetailPanel()
    {
        var item = GetSelectedItem();
        if (item == null)
        {
            DetailEmptyHint.Visibility = Visibility.Visible;
            DetailContent.Visibility = Visibility.Collapsed;
            return;
        }

        DetailEmptyHint.Visibility = Visibility.Collapsed;
        DetailContent.Visibility = Visibility.Visible;
        _detailPasswordRevealed = false;

        DetailTitle.Text = string.IsNullOrEmpty(item.Title) ? "未命名" : item.Title;
        if (string.IsNullOrEmpty(item.Url))
            DetailUrl.Text = "—";
        else if (App.Settings.DistinguishHostsByIp && !string.IsNullOrWhiteSpace(item.SiteEndpoint))
            DetailUrl.Text = $"{item.Url}\n环境 IP：{item.SiteEndpoint.Trim()}";
        else
            DetailUrl.Text = item.Url;
        DetailUsername.Text = string.IsNullOrEmpty(item.Username) ? "—" : item.Username;
        DetailNotes.Text = string.IsNullOrEmpty(item.Notes) ? "—" : item.Notes;
        DetailWeakHint.Visibility = PasswordStrength.IsWeak(item.Password) ? Visibility.Visible : Visibility.Collapsed;
        RefreshDetailPasswordDisplay(item);
        BuildCustomFieldsPanel(item);
        App.Usage.RecordAccess(item.Id);
    }

    private void RefreshDetailPasswordDisplay(PasswordItemDto item)
    {
        if (string.IsNullOrEmpty(item.Password))
        {
            DetailPassword.Text = "—";
            DetailRevealBtn.IsEnabled = false;
        }
        else
        {
            DetailRevealBtn.IsEnabled = true;
            DetailRevealBtn.Content = _detailPasswordRevealed ? "隐藏" : "显示";
            if (_detailPasswordRevealed)
                DetailPassword.Text = item.Password;
            else
            {
                var n = item.Password.Length;
                var cap = 32;
                DetailPassword.Text = new string('•', Math.Min(n, cap)) + (n > cap ? "…" : "");
            }
        }
    }

    private void BuildCustomFieldsPanel(PasswordItemDto item)
    {
        DetailCustomFieldsPanel.Children.Clear();
        if (item.CustomFields.Count == 0) return;

        DetailCustomFieldsPanel.Children.Add(new TextBlock
        {
            Text = "自定义字段",
            Style = (Style)FindResource("TextCaptionStyle"),
            Margin = new Thickness(0, 0, 0, 4)
        });

        foreach (var field in item.CustomFields)
        {
            var label = string.IsNullOrEmpty(field.Label) ? "（未命名）" : field.Label;
            var val = string.IsNullOrEmpty(field.Value) ? "—" : field.Value;

            var wrap = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            wrap.Children.Add(new TextBlock
            {
                Text = label,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 2)
            });

            var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            row.Children.Add(new TextBlock
            {
                Text = val,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 220
            });
            if (!string.IsNullOrEmpty(field.Value))
            {
                var copyBtn = new System.Windows.Controls.Button
                {
                    Content = "复制",
                    Style = (Style)FindResource("GhostButtonStyle"),
                    Margin = new Thickness(8, 0, 0, 0),
                    Tag = field.Value
                };
                copyBtn.Click += (_, _) => CopyToClipboard((string)copyBtn.Tag);
                row.Children.Add(copyBtn);
            }
            wrap.Children.Add(row);
            DetailCustomFieldsPanel.Children.Add(wrap);
        }
    }

    private void SearchOrFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (!App.Vault.IsUnlocked) return;
        var fi = FilterCombo.SelectedIndex;
        if (fi >= 0 && fi < FilterValues.Length)
            _listFilter = FilterValues[fi];
        var li = LayoutCombo.SelectedIndex;
        if (li >= 0 && li < LayoutValues.Length)
            _layoutMode = LayoutValues[li];
        RefreshItemList();
    }

    private void ItemsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ItemsList.SelectedItem is VaultListRow row)
            _selectedId = row.Id;
        UpdateDetailPanel();
    }

    private void ItemsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject dep && FindAncestor<System.Windows.Controls.Button>(dep) != null)
            return;
        CopyPasswordForSelection();
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match) return match;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void ItemsList_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CopyPasswordForSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            DeleteItem_Click(sender, e);
            e.Handled = true;
        }
    }

    private void CopyPasswordForSelection()
    {
        var item = GetSelectedItem();
        if (item == null || string.IsNullOrEmpty(item.Password)) return;
        CopyToClipboard(item.Password);
        App.Usage.RecordAccess(item.Id);
    }

    private static void CopyToClipboard(string text)
    {
        try { System.Windows.Clipboard.SetText(text); } catch { /* ignore */ }
    }

    private void DetailReveal_Click(object sender, RoutedEventArgs e)
    {
        _detailPasswordRevealed = !_detailPasswordRevealed;
        var item = GetSelectedItem();
        if (item != null) RefreshDetailPasswordDisplay(item);
    }

    private void DetailCopyPassword_Click(object sender, RoutedEventArgs e) => CopyPasswordForSelection();

    private async void RowFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn || btn.Tag is not Guid id) return;
        e.Handled = true;
        await App.Vault.ToggleFavoriteAsync(id);
        RefreshUi();
    }

    private void RowEdit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn || btn.Tag is not Guid id) return;
        e.Handled = true;
        var item = App.Vault.Items.FirstOrDefault(x => x.Id == id);
        if (item == null) return;
        _selectedId = id;
        var dlg = new ItemEditWindow(item) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.ResultItem != null)
        {
            _ = App.Vault.UpsertItemAsync(dlg.ResultItem);
            RefreshUi();
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SettingsWindow { Owner = this };
        if (dlg.ShowDialog() == true)
            RefreshUi();
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
            var dlg = new RecoveryKeyWindow(App.Vault.PendingRecoveryKeyToDisplay) { Owner = this };
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

    private void Lock_Click(object sender, RoutedEventArgs e)
    {
        App.Vault.Lock();
        App.Bridge.Stop();
        _selectedId = null;
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
        var dlg = new ItemEditWindow(null) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.ResultItem != null)
        {
            _ = App.Vault.UpsertItemAsync(dlg.ResultItem);
            RefreshUi();
        }
    }

    private void EditItem_Click(object sender, RoutedEventArgs e)
    {
        var row = GetSelectedItem();
        if (row == null) return;
        var dlg = new ItemEditWindow(row) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.ResultItem != null)
        {
            _ = App.Vault.UpsertItemAsync(dlg.ResultItem);
            RefreshUi();
        }
    }

    private async void DeleteItem_Click(object sender, RoutedEventArgs e)
    {
        var row = GetSelectedItem();
        if (row == null) return;
        if (System.Windows.MessageBox.Show("确定删除该条目？", "KeyNest", MessageBoxButton.YesNo, MessageBoxImage.Question) !=
            MessageBoxResult.Yes) return;
        await App.Vault.RemoveItemAsync(row.Id);
        App.Usage.Remove(row.Id);
        if (_selectedId == row.Id) _selectedId = null;
        RefreshUi();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }

}
