using System.Collections.ObjectModel;
using Microsoft.Web.WebView2.Core;
using XRBrowser.Models;

namespace XRBrowser.Services;

public sealed class DownloadService
{
    public ObservableCollection<DownloadItem> Items { get; } = new();
    public event EventHandler? Changed;
    public event EventHandler<DownloadItem>? DownloadStarted;
    public event EventHandler<DownloadItem>? DownloadCompleted;

    public DownloadItem Attach(CoreWebView2DownloadOperation operation)
    {
        var item = new DownloadItem(operation);
        Items.Insert(0, item);
        while (Items.Count > 100)
        {
            var old = Items[^1];
            Items.RemoveAt(Items.Count - 1);
            old.Dispose();
        }
        item.Completed += (_, _) =>
        {
            DownloadCompleted?.Invoke(this, item);
            Changed?.Invoke(this, EventArgs.Empty);
        };
        item.PropertyChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        DownloadStarted?.Invoke(this, item);
        Changed?.Invoke(this, EventArgs.Empty);
        return item;
    }

    public void Remove(DownloadItem item)
    {
        Items.Remove(item);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ClearFinished()
    {
        foreach (var item in Items.Where(i => i.IsCompleted || i.IsInterrupted).ToList())
        {
            Items.Remove(item);
            item.Dispose();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public int ActiveCount => Items.Count(i => i.IsActive);

    public static string BuildUniquePath(string folder, string suggestedName)
    {
        Directory.CreateDirectory(folder);
        var name = Path.GetFileName(suggestedName);
        if (string.IsNullOrWhiteSpace(name)) name = "download";
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');

        var candidate = Path.Combine(folder, name);
        if (!File.Exists(candidate)) return candidate;

        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        for (var i = 1; i < 1000; i++)
        {
            candidate = Path.Combine(folder, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
        return Path.Combine(folder, $"{stem}-{DateTime.Now:HHmmss}{ext}");
    }
}
