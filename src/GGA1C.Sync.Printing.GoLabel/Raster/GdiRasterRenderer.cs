// Объекты, которые GoLabel рисует сам (GDI+) и шлёт растром Q. Сами растры собирает
// RasterShapeBitmaps (вымещено из этого файла, П12.1) — их же использует превью редактора,
// чтобы превью и печать не расходились. Здесь — подстановка данных и отправка на порт.
using System.Drawing;
using System.Runtime.Versioning;
using GGA1C.Sync.Printing.GoLabel.Ezpl;
using GGA1C.Sync.Printing.GoLabel.Ezpx;

namespace GGA1C.Sync.Printing.GoLabel.Raster;

[SupportedOSPlatform("windows")]
public sealed class GdiRasterRenderer : IRasterRenderer
{
    public void Render(RasterRequest request, EzplWriter output)
    {
        switch (request.Shape.TypeName)
        {
            case "Image":
            {
                using var canvas = RasterShapeBitmaps.ImageBitmap((EzpxRasterShape)request.Shape);
                Raster1Bit.WriteQ(output, (int)Math.Round(request.OffsetX, 0) + request.Shape.XInPixel,
                    (int)Math.Round(request.OffsetY, 0) + request.Shape.YInPixel, canvas);
                break;
            }
            case "BarCode":
            {
                var b = (EzpxBarCode)request.Shape;
                string data = request.Job.FillPrintingData(b, bformat: false);
                using var bitmap = RasterShapeBitmaps.BarCodeBitmap(b, data);
                if (bitmap == null)
                {
                    break;
                }
                Raster1Bit.WriteQ(output, b.XInPixel + (int)request.OffsetX, b.YInPixel + (int)request.OffsetY, bitmap);
                break;
            }
            case "GdiWindowTextBox":
            {
                var shape = (EzpxRasterShape)request.Shape;
                string data = request.Job.FillPrintingData(shape, bformat: false);
                using var box = RasterShapeBitmaps.GdiTextBoxBitmap(shape, data, request.Job.Dpi);
                Raster1Bit.WriteQ(output, (int)Math.Round(request.OffsetX, 0) + shape.XInPixel, (int)Math.Round(request.OffsetY, 0) + shape.YInPixel, box);
                break;
            }
            case "WindowTextBox":
            {
                var shape = (EzpxRasterShape)request.Shape;
                request.Job.FillPrintingData(shape, bformat: true);
                string data = request.Job.FillPrintingData(shape, bformat: false);
                using var text = RasterShapeBitmaps.WindowTextBoxBitmap(shape, data, request.Job.Dpi);
                Raster1Bit.WriteQ(output, (int)Math.Round(request.OffsetX, 0) + shape.XInPixel, (int)Math.Round(request.OffsetY, 0) + shape.YInPixel, text);
                break;
            }
            case "WindowText":
            {
                var shape = (EzpxRasterShape)request.Shape;
                string data = request.Job.FillPrintingData(shape, bformat: false);
                var (bitmap, px, py) = RasterShapeBitmaps.WindowTextBitmap(shape, data, request.Job.Dpi);
                using (bitmap)
                {
                    Raster1Bit.WriteQ(output, (int)Math.Round(request.OffsetX, 0) + px, (int)Math.Round(request.OffsetY, 0) + py, bitmap);
                }
                break;
            }
            case "Ellipse":
            {
                using var canvas = RasterShapeBitmaps.EllipseBitmap((EzpxRasterShape)request.Shape);
                Raster1Bit.WriteQ(output, (int)(request.OffsetX + request.Shape.X), (int)(request.OffsetY + request.Shape.Y), canvas);
                break;
            }
            default:
                throw new NotSupportedException($"Объект {request.Shape.TypeName} растром ещё не перенесён.");
        }
    }
}
