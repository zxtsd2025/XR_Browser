using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace XRBrowser.Models;

public sealed class Bookmark : INotifyPropertyChanged
{
    private string _title = "";
    private string _url = "";
    private string? _faviconUrl;
    private ImageSource? _favicon;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title
    {
        get => _title;
        set { if (_title != value) { _title = value; OnPropertyChanged(); } }
    }
    public string Url
    {
        get => _url;
        set
        {
            if (_url == value) return;
            _url = value;
            Favicon = null;
            OnPropertyChanged();
        }
    }
    public string? FaviconUrl
    {
        get => _faviconUrl;
        set { if (_faviconUrl != value) { _faviconUrl = value; OnPropertyChanged(); } }
    }
    public DateTime AddedUtc { get; set; } = DateTime.UtcNow;
    public bool ShowInBar { get; set; } = true;

    /// <summary>
    /// In-memory favicon for the bookmark bar. The bytes are cached on disk by
    /// <see cref="Services.FaviconService"/> and reloaded after a restart.
    /// </summary>
    [JsonIgnore]
    public ImageSource? Favicon
    {
        get => _favicon;
        set
        {
            if (ReferenceEquals(_favicon, value)) return;
            _favicon = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasFavicon));
        }
    }

    [JsonIgnore]
    public bool HasFavicon => _favicon is not null;

    [JsonIgnore]
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
