using System.Text.Encodings.Web;
using System.Text.Json;

namespace XRBrowser.Services;

public sealed class JsonStore<T> where T : class, new()
{
    private readonly string _path;
    private readonly object _gate = new();
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true
    };

    public JsonStore(string path) => _path = path;

    public T Load()
    {
        try
        {
            if (!File.Exists(_path)) return new T();
            var json = File.ReadAllText(_path);
            if (string.IsNullOrWhiteSpace(json)) return new T();
            return JsonSerializer.Deserialize<T>(json, Options) ?? new T();
        }
        catch (Exception ex)
        {
            AppPaths.Log($"Failed to load {_path}: {ex}");
            try
            {
                if (File.Exists(_path))
                    File.Move(_path, _path + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss"), true);
            }
            catch { }
            return new T();
        }
    }

    public void Save(T value)
    {
        lock (_gate)
        {
            try
            {
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var tmp = _path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
                if (File.Exists(_path)) File.Replace(tmp, _path, null);
                else File.Move(tmp, _path);
            }
            catch (Exception ex)
            {
                AppPaths.Log($"Failed to save {_path}: {ex}");
            }
        }
    }
}
