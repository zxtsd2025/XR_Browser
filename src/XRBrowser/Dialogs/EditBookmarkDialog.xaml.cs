using System.Windows;
using System.Windows.Input;
using XRBrowser.Models;

namespace XRBrowser.Dialogs;

public partial class EditBookmarkDialog : Window
{
    public Bookmark? Bookmark { get; private set; }

    public EditBookmarkDialog(Bookmark bookmark)
    {
        InitializeComponent();
        Bookmark = bookmark;
        TitleBox.Text = bookmark.Title;
        UrlBox.Text = bookmark.Url;
        Loaded += (_, _) =>
        {
            TitleBox.Focus();
            TitleBox.SelectAll();
        };
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var title = TitleBox.Text.Trim();
        var url = UrlBox.Text.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            ErrorText.Text = "请输入有效的 http:// 或 https:// 网址。";
            ErrorText.Visibility = Visibility.Visible;
            UrlBox.Focus();
            UrlBox.SelectAll();
            return;
        }
        if (string.IsNullOrWhiteSpace(title)) title = uri.Host;
        if (Bookmark is not null)
        {
            Bookmark.Title = title;
            Bookmark.Url = url;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            Save_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }
}
