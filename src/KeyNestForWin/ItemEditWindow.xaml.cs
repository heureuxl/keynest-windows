using System.Windows;
using KeyNestForWin.Models;

namespace KeyNestForWin;

public partial class ItemEditWindow : Window
{
    private readonly PasswordItemDto? _existing;

    public PasswordItemDto? ResultItem { get; private set; }

    public ItemEditWindow(PasswordItemDto? existing)
    {
        InitializeComponent();
        _existing = existing;
        if (existing != null)
        {
            Title = "编辑条目";
            TitleBox.Text = existing.Title;
            UsernameBox.Text = existing.Username;
            PasswordBox.Password = existing.Password;
            UrlBox.Text = existing.Url;
            NotesBox.Text = existing.Notes;
        }
        else
            Title = "新建条目";
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var id = _existing?.Id ?? Guid.NewGuid();
        ResultItem = new PasswordItemDto
        {
            Id = id,
            Title = TitleBox.Text.Trim(),
            Username = UsernameBox.Text.Trim(),
            Password = PasswordBox.Password,
            Url = UrlBox.Text.Trim(),
            Notes = NotesBox.Text.Trim()
        };
        DialogResult = true;
        Close();
    }
}
