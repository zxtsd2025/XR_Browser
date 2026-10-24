using System.Text.Json;
using XRBrowser.Models;

namespace XRBrowser.Services;

public sealed class HistoryService : IDisposable
{
    private sealed class HistoryFile
    {
        public List<HistoryEntry> Items { get; set; } = new();
    }

    private const int MaxEntries = 5000;
    private readonly JsonStore<HistoryFile> _store = new(AppPaths.HistoryFile);
    private readonly List<HistoryEntry> _items;
    private readonly object _gate = new();
    private Timer? _saveTimer;
    private bool _dirty;

    public event EventHandler? Changed;

    public HistoryService()
    {
        _items = _store.Load().Items.OrderByDescending(h => h.LastVisitUtc).ToList();
    }

    public IReadOnlyList<HistoryEntry> Items
    {
        get { lock (_gate) return _items.ToList(); }
    }

    public void AddVisit(string url, string title)
    {
        if (string.IsNullOrWhiteSpace(url) || UrlHelper.IsInternalUrl(url)) return;
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return;

        lock (_gate)
        {
            var entry = _items.FirstOrDefault(h => string.Equals(h.Url, url, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                entry = new HistoryEntry { Url = url, Title = title, LastVisitUtc = DateTime.UtcNow };
                _items.Insert(0, entry);
            }
            else
            {
                _items.Remove(entry);
                entry.LastVisitUtc = DateTime.UtcNow;
                entry.VisitCount++;
                if (!string.IsNullOrWhiteSpace(title)) entry.Title = title;
                _items.Insert(0, entry);
            }

            if (_items.Count > MaxEntries)
                _items.RemoveRange(MaxEntries, _items.Count - MaxEntries);
            _dirty = true;
        }
        ScheduleSave();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateTitle(string url, string title)
    {
        if (string.IsNullOrWhiteSpace(title) || UrlHelper.IsInternalUrl(url)) return;
        lock (_gate)
        {
            var entry = _items.FirstOrDefault(h => string.Equals(h.Url, url, StringComparison.OrdinalIgnoreCase));
            if (entry is null || entry.Title == title) return;
            entry.Title = title;
            _dirty = true;
        }
        ScheduleSave();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Delete(string url)
    {
        lock (_gate)
        {
            var removed = _items.RemoveAll(h => string.Equals(h.Url, url, StringComparison.OrdinalIgnoreCase));
            if (removed == 0) return;
            _dirty = true;
        }
        SaveNow();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        lock (_gate)
        {
            _items.Clear();
            _dirty = true;
        }
        SaveNow();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public List<HistoryEntry> Search(string? query, int limit = 200)
    {
        lock (_gate)
        {
            IEnumerable<HistoryEntry> q = _items;
            if (!string.IsNullOrWhiteSpace(query))
            {
                var needle = query.Trim();
                q = q.Where(h =>
                    h.Title.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                    h.Url.Contains(needle, StringComparison.OrdinalIgnoreCase));
            }
            return q.Take(limit).ToList();
        }
    }

    public List<HistoryEntry> TopSites(int count)
    {
        lock (_gate)
        {
            return _items
                .GroupBy(h => h.Host, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(x => x.LastVisitUtc).First())
                .OrderByDescending(h => h.VisitCount)
                .ThenByDescending(h => h.LastVisitUtc)
                .Take(count)
                .ToList();
        }
    }

    private void ScheduleSave()
    {
        _saveTimer ??= new Timer(_ => SaveNow(), null, Timeout.Infinite, Timeout.Infinite);
        _saveTimer.Change(1500, Timeout.Infinite);
    }

    public void SaveNow()
    {
        HistoryFile file;
        lock (_gate)
        {
            if (!_dirty) return;
            _dirty = false;
            file = new HistoryFile { Items = _items.ToList() };
        }
        _store.Save(file);
    }

    public string ToJson(IEnumerable<HistoryEntry> entries)
        => JsonSerializer.Serialize(entries.Select(h => new
        {
            h.Url,
            h.Title,
            h.Host,
            lastVisit = h.LastVisitUtc,
            h.VisitCount
        }), JsonStore<HistoryFile>.Options);

    public void Dispose()
    {
        _saveTimer?.Dispose();
        SaveNow();
    }
}
