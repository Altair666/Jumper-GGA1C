using GGA1C.Sync.Printing.GoLabel.Ezpl;
using GGA1C.Sync.Printing.GoLabel.Ezpx;

namespace GGA1C.Sync.Printing.GoLabel;

/// <summary>Шаблон GoLabel (.ezpx) → задание EZPL, байт в байт как у GoLabel.</summary>
public sealed class GoLabelDriver(IRasterRenderer? raster = null) : IPrinterDriver
{
    public byte[] Build(Stream template, PrintValues values) =>
        new EzplJob(EzpxReader.Load(template), values, raster).Build();
}
