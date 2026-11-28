using System.Text.Json;

namespace GGA1C.Sync.Printing.Profiles;

/// <summary>The profile files are server data, separate from EZPX files and from the browser.</summary>
public sealed class FilePrinterProfileStore(string folder)
{
    private readonly string _folder = Path.GetFullPath(folder);

    internal async Task<PrinterProfileData> ReadAsync(Guid? tenantId, string printerId)
    {
        var data = await ReadAsync(ProfilePath(tenantId, printerId), PrinterProfileData.Empty).ConfigureAwait(false);
        if (data.SchemaVersion is < 0 or > PrinterProfileData.CurrentSchemaVersion)
            throw new InvalidDataException("Версия профиля принтера не поддерживается.");
        // Version 0 is the previously deployed format (without a version or Connection).
        return data;
    }

    internal Task WriteAsync(Guid? tenantId, string printerId, PrinterProfileData data) =>
        WriteAsync(ProfilePath(tenantId, printerId), data);

    internal Task<PrinterApplicationOptions> ReadOptionsAsync(Guid? tenantId) =>
        ReadAsync(ProfilePath(tenantId, "application"), PrinterApplicationOptions.Defaults);

    internal Task WriteOptionsAsync(Guid? tenantId, PrinterApplicationOptions options) =>
        WriteAsync(ProfilePath(tenantId, "application"), options);

    private static async Task<T> ReadAsync<T>(string path, Func<T> empty) where T : class
    {
        try
        {
            await using var file = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(file).ConfigureAwait(false)
                ?? throw new InvalidDataException("Не удалось прочитать настройки печати.");
        }
        catch (FileNotFoundException) { return empty(); }
        catch (DirectoryNotFoundException) { return empty(); }
    }

    private static async Task WriteAsync<T>(string path, T data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await JsonSerializer.SerializeAsync(file, data).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    // The tenant and the file name are trusted identifiers, not user-supplied paths.
    private string ProfilePath(Guid? tenantId, string name) =>
        Path.Combine(_folder, tenantId?.ToString("N") ?? "host", name + ".json");
}
