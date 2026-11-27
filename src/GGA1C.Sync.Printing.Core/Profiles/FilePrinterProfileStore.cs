using System.Text.Json;

namespace GGA1C.Sync.Printing.Profiles;

/// <summary>The profile files are server data, separate from EZPX files and from the browser.</summary>
public sealed class FilePrinterProfileStore(string folder)
{
    private readonly string _folder = Path.GetFullPath(folder);

    internal async Task<PrinterProfileData> ReadAsync(Guid? tenantId, string printerId)
    {
        var path = ProfilePath(tenantId, printerId);
        try
        {
            await using var file = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<PrinterProfileData>(file).ConfigureAwait(false)
                ?? throw new InvalidDataException("Не удалось прочитать профиль принтера.");
        }
        catch (FileNotFoundException) { return PrinterProfileData.Empty(); }
        catch (DirectoryNotFoundException) { return PrinterProfileData.Empty(); }
    }

    internal async Task WriteAsync(Guid? tenantId, string printerId, PrinterProfileData data)
    {
        var path = ProfilePath(tenantId, printerId);
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

    // Both components are trusted identifiers (tenant Guid and an ID looked up in the catalog).
    private string ProfilePath(Guid? tenantId, string printerId) =>
        Path.Combine(_folder, tenantId?.ToString("N") ?? "host", printerId + ".json");
}
