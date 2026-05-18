using System.Windows;
using System.Windows.Controls;
using KeyNestForWin.Models;

namespace KeyNestForWin;

public partial class ItemEditWindow : Window
{
    private readonly PasswordItemDto? _existing;
    private readonly List<CustomFieldEditor> _fieldEditors = new();

    public PasswordItemDto? ResultItem { get; private set; }

    public ItemEditWindow(PasswordItemDto? existing)
    {
        InitializeComponent();
        AppBranding.SetWindowIcon(this);
        _existing = existing;
        if (existing != null)
        {
            Title = "编辑条目";
            TitleBox.Text = existing.Title;
            UsernameBox.Text = existing.Username;
            PasswordBox.Password = existing.Password;
            UrlBox.Text = existing.Url;
            NotesBox.Text = existing.Notes;
            FavoriteCheck.IsChecked = existing.IsFavorite;
            foreach (var f in existing.CustomFields)
                AddFieldEditor(f.Label, f.Value, f.Id);
        }
        else
            Title = "新建条目";
    }

    private void PasswordToggle_Click(object sender, RoutedEventArgs e)
    {
        if (PasswordPlain.Visibility == Visibility.Visible)
        {
            PasswordBox.Password = PasswordPlain.Text;
            PasswordPlain.Visibility = Visibility.Collapsed;
            PasswordBox.Visibility = Visibility.Visible;
            PasswordToggle.Content = "显示";
        }
        else
        {
            PasswordPlain.Text = PasswordBox.Password;
            PasswordPlain.Visibility = Visibility.Visible;
            PasswordBox.Visibility = Visibility.Collapsed;
            PasswordToggle.Content = "隐藏";
        }
    }

    private string ReadPassword() =>
        PasswordPlain.Visibility == Visibility.Visible ? PasswordPlain.Text : PasswordBox.Password;

    private void AddField_Click(object sender, RoutedEventArgs e) => AddFieldEditor("", "", null);

    private void AddFieldEditor(string label, string value, Guid? id)
    {
        CustomFieldEditor? editor = null;
        editor = new CustomFieldEditor(label, value, id ?? Guid.NewGuid(), () =>
        {
            if (editor == null) return;
            CustomFieldsPanel.Children.Remove(editor.Root);
            _fieldEditors.Remove(editor);
        });
        _fieldEditors.Add(editor);
        CustomFieldsPanel.Children.Add(editor.Root);
    }

    private void AppendFieldsIfAbsent(IEnumerable<(string Label, string Value)> pairs)
    {
        foreach (var (label, value) in pairs)
        {
            if (_fieldEditors.Any(e => e.Label.Trim().Equals(label, StringComparison.Ordinal)))
                continue;
            AddFieldEditor(label, value, null);
        }
    }

    private void TemplateBankCard_Click(object sender, RoutedEventArgs e) =>
        AppendFieldsIfAbsent([("卡号", ""), ("有效期", ""), ("CVV", ""), ("持卡人", "")]);

    private void TemplateApi_Click(object sender, RoutedEventArgs e) =>
        AppendFieldsIfAbsent([("Client ID", ""), ("Secret", ""), ("Endpoint", "")]);

    private void TemplateSecurityQA_Click(object sender, RoutedEventArgs e) =>
        AppendFieldsIfAbsent([
            ("问题 1", ""), ("答案 1", ""),
            ("问题 2", ""), ("答案 2", ""),
            ("问题 3", ""), ("答案 3", ""),
        ]);

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var fields = _fieldEditors
            .Select(ed => new CustomFieldDto
            {
                Id = ed.Id,
                Label = ed.Label.Trim(),
                Value = ed.Value
            })
            .Where(f => !string.IsNullOrEmpty(f.Label) || !string.IsNullOrEmpty(f.Value))
            .ToList();

        var id = _existing?.Id ?? Guid.NewGuid();
        ResultItem = new PasswordItemDto
        {
            Id = id,
            Title = TitleBox.Text.Trim(),
            Username = UsernameBox.Text.Trim(),
            Password = ReadPassword(),
            Url = UrlBox.Text.Trim(),
            Notes = NotesBox.Text.Trim(),
            CustomFields = fields,
            IsFavorite = FavoriteCheck.IsChecked == true
        };
        DialogResult = true;
        Close();
    }

    private sealed class CustomFieldEditor
    {
        public Guid Id { get; }
        public string Label => _labelBox.Text;
        public string Value => _valueBox.Text;
        public Grid Root { get; }

        private readonly System.Windows.Controls.TextBox _labelBox;
        private readonly System.Windows.Controls.TextBox _valueBox;

        public CustomFieldEditor(string label, string value, Guid id, Action onRemove)
        {
            Id = id;
            Root = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
            Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _labelBox = new System.Windows.Controls.TextBox { Text = label, VerticalContentAlignment = VerticalAlignment.Center };
            _valueBox = new System.Windows.Controls.TextBox { Text = value, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            var removeBtn = new System.Windows.Controls.Button
            {
                Content = "−",
                Width = 28,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid.SetColumn(_labelBox, 0);
            Grid.SetColumn(_valueBox, 1);
            Grid.SetColumn(removeBtn, 2);
            removeBtn.Click += (_, _) => onRemove();

            Root.Children.Add(_labelBox);
            Root.Children.Add(_valueBox);
            Root.Children.Add(removeBtn);
        }
    }
}
