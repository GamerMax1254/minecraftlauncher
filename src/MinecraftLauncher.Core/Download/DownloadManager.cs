using MinecraftLauncher.Core.Logging;

namespace MinecraftLauncher.Core.Download;

public class DownloadProgress
{
    public string FileName { get; set; } = "";
    public long BytesDownloaded { get; set; }
    public long TotalBytes { get; set; }
    public double Percentage => TotalBytes > 0
        ? (double)BytesDownloaded / TotalBytes * 100 : 0;
}

public class DownloadManager
{
    private readonly HttpClient _http;

    public DownloadManager(HttpClient http)
    {
        _http = http;
    }

    public async Task DownloadFileAsync(
        string url,
        string destPath,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

        using var response = await _http.GetAsync(url,
            HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        var fileName = Path.GetFileName(destPath);

        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(destPath,
            FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        var buffer = new byte[8192];
        long totalRead = 0;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            totalRead += bytesRead;

            progress?.Report(new DownloadProgress
            {
                FileName = fileName,
                BytesDownloaded = totalRead,
                TotalBytes = totalBytes
            });
        }
    }

    public async Task DownloadMultipleAsync(IEnumerable<(string Url, string Path)> files, IProgress<(int Done, int Total, string File)>? progress = null,
    int maxParallel = 4, CancellationToken ct = default)
    {
        var fileList = files.ToList();
        int done = 0;
        int total = fileList.Count;
        var errors = new List<string>();

        using var semaphore = new SemaphoreSlim(maxParallel);

        var tasks = fileList.Select(async file =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                if (!File.Exists(file.Path))
                {
                    try
                    {
                        await DownloadFileAsync(file.Url, file.Path, ct: ct);
                    }
                    catch (Exception ex)
                    {
                        lock (errors)
                            errors.Add($"{Path.GetFileName(file.Path)}: {ex.Message}");
                        // логгируем но не бросаем — попробуем скачать остальные
                        Logger.Warn($"Failed to download {file.Url}: {ex.Message}");
                    }
                }
                var current = Interlocked.Increment(ref done);
                progress?.Report((current, total, Path.GetFileName(file.Path)));
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }
}
