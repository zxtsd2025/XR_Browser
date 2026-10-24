using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;

namespace XRBrowser.Services;

public sealed class CrxInstallResult
{
    public string ExtensionFolder { get; init; } = "";
    public string Name { get; init; } = "";
    public string Version { get; init; } = "";
    public int ManifestVersion { get; init; }
}

/// <summary>
/// Extracts a Chromium CRX package into a normal unpacked-extension folder.
/// WebView2's AddBrowserExtensionAsync only accepts unpacked folders, not CRX files.
/// </summary>
public static class CrxInstaller
{
    private const int MaxCrxBytes = 200 * 1024 * 1024;
    private const long MaxUncompressedBytes = 400L * 1024 * 1024;
    private const int MaxEntries = 50_000;

    public static async Task<CrxInstallResult> ExtractAsync(string crxPath, string extensionsRoot, CancellationToken cancellationToken = default)
    {
        var fileInfo = new FileInfo(crxPath);
        if (!fileInfo.Exists) throw new FileNotFoundException("找不到 CRX 文件。", crxPath);
        if (fileInfo.Length <= 16) throw new InvalidDataException("CRX 文件不完整。");
        if (fileInfo.Length > MaxCrxBytes) throw new InvalidDataException("CRX 文件过大（上限 200 MB）。");

        long zipOffset;
        await using (var headerStream = new FileStream(crxPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var magic = new byte[4];
            if (await ReadExactAsync(headerStream, magic, cancellationToken).ConfigureAwait(false) != 4 ||
                magic[0] != (byte)'C' || magic[1] != (byte)'r' ||
                magic[2] != (byte)'2' || magic[3] != (byte)'4')
                throw new InvalidDataException("不是有效的 CRX 文件（缺少 Cr24 标识）。");

            var versionBytes = new byte[4];
            if (await ReadExactAsync(headerStream, versionBytes, cancellationToken).ConfigureAwait(false) != 4)
                throw new InvalidDataException("无法读取 CRX 版本。");
            var version = BinaryPrimitives.ReadUInt32LittleEndian(versionBytes);

            if (version == 3)
            {
                var sizeBytes = new byte[4];
                if (await ReadExactAsync(headerStream, sizeBytes, cancellationToken).ConfigureAwait(false) != 4)
                    throw new InvalidDataException("无法读取 CRX3 头长度。");
                var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(sizeBytes);
                if (headerSize == 0 || headerSize > 2 * 1024 * 1024)
                    throw new InvalidDataException("CRX3 头长度不合法。");
                zipOffset = 12L + headerSize;
            }
            else if (version == 2)
            {
                var lengths = new byte[8];
                if (await ReadExactAsync(headerStream, lengths, cancellationToken).ConfigureAwait(false) != 8)
                    throw new InvalidDataException("无法读取 CRX2 头。");
                var publicKeyLength = BinaryPrimitives.ReadUInt32LittleEndian(lengths.AsSpan(0, 4));
                var signatureLength = BinaryPrimitives.ReadUInt32LittleEndian(lengths.AsSpan(4, 4));
                var headerLength = 16L + publicKeyLength + signatureLength;
                if (publicKeyLength > 2 * 1024 * 1024 || signatureLength > 2 * 1024 * 1024)
                    throw new InvalidDataException("CRX2 头长度不合法。");
                zipOffset = headerLength;
            }
            else
            {
                throw new InvalidDataException($"不支持的 CRX 版本：{version}。仅支持 CRX2 / CRX3。");
            }
        }

        if (zipOffset >= fileInfo.Length)
            throw new InvalidDataException("CRX 文件不包含扩展内容。");

        var target = Path.Combine(extensionsRoot, "crx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(target);
        var rootWithSeparator = target.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        try
        {
            await using var packageStream = new FileStream(crxPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            packageStream.Seek(zipOffset, SeekOrigin.Begin);
            // ZipArchive expects offsets relative to the start of its stream; normalize the
            // CRX payload into a memory stream before opening the archive.
            using var zipBytes = new MemoryStream((int)Math.Min(packageStream.Length - zipOffset, 16 * 1024 * 1024));
            await packageStream.CopyToAsync(zipBytes, cancellationToken).ConfigureAwait(false);
            zipBytes.Position = 0;
            using var archive = new ZipArchive(zipBytes, ZipArchiveMode.Read, leaveOpen: false);

            if (archive.Entries.Count > MaxEntries)
                throw new InvalidDataException("CRX 内文件数量过多。");

            long totalBytes = 0;
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var normalized = entry.FullName.Replace('\\', '/').TrimStart('/');
                if (normalized.Length == 0) continue;
                if (normalized.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase)) continue;
                // Chrome's CRX signature metadata is a reserved folder and must not be exposed to WebView2.
                if (normalized.StartsWith("_metadata/", StringComparison.OrdinalIgnoreCase)) continue;
                if (normalized.EndsWith('/')) continue;

                var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length == 0 ||
                    segments.Any(s => s is "." or ".." || s.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
                    throw new InvalidDataException("CRX 包含非法文件路径。");

                totalBytes += entry.Length;
                if (totalBytes > MaxUncompressedBytes)
                    throw new InvalidDataException("CRX 解压后体积过大。");

                var destination = Path.GetFullPath(Path.Combine(target, normalized.Replace('/', Path.DirectorySeparatorChar)));
                if (!destination.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("CRX 包含越界文件路径。");

                var directory = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                await using var source = entry.Open();
                await using var targetFile = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await source.CopyToAsync(targetFile, cancellationToken).ConfigureAwait(false);
            }

            var manifestPath = Path.Combine(target, "manifest.json");
            if (!File.Exists(manifestPath))
                throw new InvalidDataException("CRX 根目录没有 manifest.json。");

            var manifest = await ParseManifestAsync(manifestPath, cancellationToken).ConfigureAwait(false);
            return new CrxInstallResult
            {
                ExtensionFolder = target,
                Name = manifest.Name,
                Version = manifest.Version,
                ManifestVersion = manifest.ManifestVersion
            };
        }
        catch
        {
            TryDelete(target);
            throw;
        }
    }

    private static async Task<(string Name, string Version, int ManifestVersion)> ParseManifestAsync(
        string manifestPath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(manifestPath);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("manifest.json 格式不正确。");

        var manifestVersion = root.TryGetProperty("manifest_version", out var mv) && mv.TryGetInt32(out var parsedMv)
            ? parsedMv
            : 0;
        if (manifestVersion is < 2 or > 3)
            throw new InvalidDataException("只支持 Manifest V2 / V3 扩展。");

        var name = root.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
            ? nameElement.GetString() ?? ""
            : "";
        var version = root.TryGetProperty("version", out var versionElement) && versionElement.ValueKind == JsonValueKind.String
            ? versionElement.GetString() ?? ""
            : "";

        if (string.IsNullOrWhiteSpace(name)) name = "未命名扩展";
        if (string.IsNullOrWhiteSpace(version)) version = "0";
        return (name, version, manifestVersion);
    }

    public static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex)
        {
            AppPaths.Log("Failed to remove CRX folder: " + ex.Message);
        }
    }

    private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
        }
        return total;
    }
}
