using GGA1C.Sync.Printing.GoLabel.Ezpx;

namespace GGA1C.Sync.Printing.GoLabel.Ezpl;

/// <summary>
/// Объекты, которые GoLabel рисует сам и шлёт картинкой Q (Image, Ellipse, *TextBox, штрихкод с
/// подписью шрифтом Windows). Сборка задания вызывает рендерер в том месте, где GoLabel шлёт растр.
/// </summary>
public interface IRasterRenderer
{
    void Render(RasterRequest request, EzplWriter output);
}

/// <summary>Объект, смещение наклейки на странице и данные задания для подстановки.</summary>
public sealed record RasterRequest(EzpxShape Shape, float OffsetX, float OffsetY, EzplJob Job);

/// <summary>Растры ещё не перенесены — честный отказ вместо неверных байтов.</summary>
public sealed class UnsupportedRasterRenderer : IRasterRenderer
{
    public static readonly UnsupportedRasterRenderer Instance = new();

    public void Render(RasterRequest request, EzplWriter output) =>
        throw new NotSupportedException($"Объект {request.Shape.TypeName} печатается растром — ещё не перенесено.");
}
