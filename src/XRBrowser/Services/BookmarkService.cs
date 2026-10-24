using System.Collections.ObjectModel;
using XRBrowser.Models;

namespace XRBrowser.Services;

public sealed class BookmarkService
{
    private sealed class BookmarkFile
    {
        public List<Bookmark> Items { get; set; } = new();
    }

    private readonly JsonStore<BookmarkFile> _store = new(AppPaths.BookmarksFile);
    public ObservableCollection<Bookmark> Items { get; } = new();

    public event EventHandler? Changed;

    public BookmarkService()
    {
        foreach (var bookmark in _store.Load().Items.OrderBy(b => b.AddedUtc))
            Items.Add(bookmark);
    }

    public bool Contains(string? url)
        => url is not null && Items.Any(b => string.Equals(b.Url, url, StringComparison.OrdinalIgnoreCase));

    public Bookmark? Find(string? url)
        => url is null ? null : Items.FirstOrDefault(b => string.Equals(b.Url, url, StringComparison.OrdinalIgnoreCase));

    public Bookmark Add(string title, string url)
    {
        var existing = Find(url);
        if (existing is not null) return existing;

        if (string.IsNullOrWhiteSpace(title))
        {
            try { title = new Uri(url).Host; } catch { title = url; }
        }

        var bookmark = new Bookmark
        {
            Title = title.Trim(),
            Url = url,
            AddedUtc = DateTime.UtcNow
        };
        Items.Add(bookmark);
        SaveAndNotify();
        return bookmark;
    }

    public bool Remove(string? url)
    {
        var item = Find(url);
        if (item is null) return false;
        Items.Remove(item);
        SaveAndNotify();
        return true;
    }

    public void Remove(Bookmark bookmark)
    {
        if (Items.Remove(bookmark)) SaveAndNotify();
    }

    public void Update(Bookmark bookmark, string title, string url)
    {
        bookmark.Title = string.IsNullOrWhiteSpace(title) ? bookmark.Title : title.Trim();
        bookmark.Url = url;
        SaveAndNotify();
    }

    public void SaveAndNotify()
    {
        _store.Save(new BookmarkFile { Items = Items.ToList() });
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
