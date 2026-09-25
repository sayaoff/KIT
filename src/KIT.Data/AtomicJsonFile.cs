using System.Text.Json;

namespace KIT.Data;

internal static class AtomicJsonFile
{
    public static async Task WriteAsync<T>(string path, T value, JsonSerializerOptions options,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("The data path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryFile = path + ".tmp";
        await using (var stream = new FileStream(temporaryFile, FileMode.Create, FileAccess.Write,
                         FileShare.None, 4096, FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(stream, value, options, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        File.Move(temporaryFile, path, overwrite: true);
    }
}
