using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace XRBrowser.Models;

public sealed class ExtensionItem : INotifyPropertyChanged
{
    private bool _isEnabled;

    public string Id { get; init; } = "";
    public string Name { get; init; } = "";

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StateText));
        }
    }

    public string StateText => IsEnabled ? "已启用" : "已停用";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
