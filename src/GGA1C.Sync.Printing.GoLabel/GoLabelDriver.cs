using GGA1C.Sync.Printing.GoLabel.Ezpl;
using GGA1C.Sync.Printing.GoLabel.Ezpx;
using GGA1C.Sync.Printing.GoLabel.Raster;

namespace GGA1C.Sync.Printing.GoLabel;

/// <summary>Шаблон GoLabel (.ezpx) → задание EZPL, байт в байт как у GoLabel.</summary>
public sealed class GoLabelDriver(IRasterRenderer? raster = null) : IPrinterDriver
{
    private readonly IRasterRenderer _raster = raster
        ?? (OperatingSystem.IsWindows() ? new GdiRasterRenderer() : UnsupportedRasterRenderer.Instance);

    public byte[] Build(Stream template, PrintValues values) =>
        new EzplJob(EzpxReader.Load(template), values, _raster).Build();
}
