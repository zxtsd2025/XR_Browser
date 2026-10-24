using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using XRBrowser.Services;

namespace XRBrowser.Models;

public sealed class DownloadItem : INotifyPropertyChanged, IDisposable
{
    private readonly CoreWebView2DownloadOperation _operation;
    private long _lastBytes;
    private DateTime _lastSpeedSample = DateTime.UtcNow;
    private double _bytesPerSecond;
    private bool _disposed;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? Completed;

    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string FileName { get; }
    public string FullPath { get; }
    public string Url { get; }
    public string MimeType { get; }
    public DateTime StartedUtc { get; } = DateTime.UtcNow;

    [JsonIgnore]
    public CoreWebView2DownloadOperation Operation => _operation;

    public long BytesReceived => _operation.BytesReceived;
    public ulong? TotalBytes => _operation.TotalBytesToReceive;

    public double Progress
    {
        get
        {
            if (TotalBytes is null or 0) return IsCompleted ? 100 : 0;
            return Math.Clamp(BytesReceived * 100.0 / TotalBytes.Value, 0, 100);
        }
    }

    public bool IsIndeterminate => TotalBytes is null or 0 && IsActive;

    public bool IsActive => _operation.State == CoreWebView2DownloadState.InProgress;

    public bool IsCompleted => _operation.State == CoreWebView2DownloadState.Completed;

    public bool IsInterrupted => _operation.State == CoreWebView2DownloadState.Interrupted;

    public bool IsPaused => IsInterrupted &&
        (_operation.InterruptReason is CoreWebView2DownloadInterruptReason.UserCanceled
            or CoreWebView2DownloadInterruptReason.UserShutdown
            or CoreWebView2DownloadInterruptReason.DownloadProcessCrashed)
        && _operation.CanResume == false;

    public bool CanCancel => IsActive;
    public bool CanPause => IsActive;
    public bool CanResume => IsInterrupted && _operation.CanResume;
    public bool CanOpen => IsCompleted && File.Exists(FullPath);
    public bool CanShowInFolder => File.Exists(FullPath);

    public string ProgressText
    {
        get
        {
            if (IsCompleted) return FormatBytes(TotalBytes is > 0 ? (long)TotalBytes.Value : BytesReceived);
            if (TotalBytes is > 0) return $"{FormatBytes(BytesReceived)} / {FormatBytes((long)TotalBytes.Value)}";
            return FormatBytes(BytesReceived);
        }
    }

    public string SpeedText
    {
        get
        {
            if (!IsActive) return "";
            if (_bytesPerSecond <= 0) return "";
            return FormatBytes((long)_bytesPerSecond) + "/s";
        }
    }

    public string StateText => _operation.State switch
    {
        CoreWebView2DownloadState.Completed => "已完成",
        CoreWebView2DownloadState.InProgress => IsPaused ? "已暂停" : "正在下载",
        CoreWebView2DownloadState.Interrupted => _operation.InterruptReason == CoreWebView2DownloadInterruptReason.UserCanceled
            ? "已取消"
            : _operation.CanResume ? "已暂停" : "已中断",
        _ => "未知"
    };

    public string StateGlyph => IsCompleted ? "\uE73E" : IsActive ? "\uE896" : "\uE7BA";

    public DownloadItem(CoreWebView2DownloadOperation operation)
    {
        _operation = operation;
        FileName = Path.GetFileName(operation.ResultFilePath);
        if (string.IsNullOrWhiteSpace(FileName)) FileName = "下载文件";
        FullPath = operation.ResultFilePath;
        Url = operation.Uri;
        MimeType = operation.MimeType;
        _operation.BytesReceivedChanged += OnProgressChanged;
        _operation.StateChanged += OnStateChanged;
        _operation.EstimatedEndTimeChanged += OnProgressChanged;
    }

    private void OnProgressChanged(object? sender, object e)
    {
        var now = DateTime.UtcNow;
        var elapsed = (now - _lastSpeedSample).TotalSeconds;
        if (elapsed >= 0.5)
        {
            _bytesPerSecond = (_operation.BytesReceived - _lastBytes) / elapsed;
            _lastBytes = _operation.BytesReceived;
            _lastSpeedSample = now;
        }
        Dispatch(() =>
        {
            Raise(nameof(BytesReceived));
            Raise(nameof(TotalBytes));
            Raise(nameof(Progress));
            Raise(nameof(ProgressText));
            Raise(nameof(SpeedText));
            Raise(nameof(IsIndeterminate));
        });
    }

    private void OnStateChanged(object? sender, object e)
    {
        Dispatch(() =>
        {
            Raise(nameof(IsActive));
            Raise(nameof(IsCompleted));
            Raise(nameof(IsInterrupted));
            Raise(nameof(CanOpen));
            Raise(nameof(CanShowInFolder));
            Raise(nameof(CanCancel));
            Raise(nameof(CanPause));
            Raise(nameof(CanResume));
            Raise(nameof(StateText));
            Raise(nameof(StateGlyph));
            Raise(nameof(Progress));
            Raise(nameof(ProgressText));
            Raise(nameof(SpeedText));
            if (IsCompleted) Completed?.Invoke(this, EventArgs.Empty);
        });
    }

    private static void Dispatch(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }

    public void Pause() { try { if (IsActive) _operation.Pause(); } catch { } }
    public void Resume() { try { if (_operation.CanResume) _operation.Resume(); } catch { } }
    public void Cancel() { try { _operation.Cancel(); } catch { } }

    public void Open()
    {
        if (!CanOpen) return;
        try { Process.Start(new ProcessStartInfo(FullPath) { UseShellExecute = true }); }
        catch (Exception ex) { AppPaths.Log("Open download failed: " + ex.Message); }
    }

    public void ShowInFolder()
    {
        try
        {
            if (File.Exists(FullPath))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{FullPath}\"") { UseShellExecute = true });
            else if (Directory.Exists(Path.GetDirectoryName(FullPath)))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Path.GetDirectoryName(FullPath)}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { AppPaths.Log("Show download failed: " + ex.Message); }
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.0} {units[unit]}";
    }

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _operation.BytesReceivedChanged -= OnProgressChanged;
            _operation.StateChanged -= OnStateChanged;
            _operation.EstimatedEndTimeChanged -= OnProgressChanged;
        }
        catch { }
    }
}
