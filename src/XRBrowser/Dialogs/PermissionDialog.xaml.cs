using System.Windows;
using System.Windows.Input;

namespace XRBrowser.Dialogs;

public partial class PermissionDialog : Window
{
    public bool Allowed { get; private set; }

    public PermissionDialog(string site, string title, string description, string glyph)
    {
        InitializeComponent();
        SiteName.Text = site;
        PermissionTitle.Text = title;
        PermissionDescription.Text = description;
        PermissionGlyph.Text = glyph;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Allow_Click(object sender, RoutedEventArgs e)
    {
        Allowed = true;
        DialogResult = true;
    }

    private void Block_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
    }
}
