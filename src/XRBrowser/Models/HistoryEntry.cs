using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace XRBrowser.Models;

public sealed class HistoryEntry : INotifyPropertyChanged
{
    private string _title = "";

    public string Url { get; set; } = "";
    public string Title
    {
        get => _title;
        set { if (_title != value) { _title = value; OnPropertyChanged(); } }
    }
    public DateTime LastVisitUtc { get; set; } = DateTime.UtcNow;
    public int VisitCount { get; set; } = 1;

    public string Host
    {
        get
        {
            try { return new Uri(Url).Host.Replace("www.", ""); }
            catch { return Url; }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
