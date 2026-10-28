namespace GGA1C.Sync.Printing;

/// <summary>
/// Собирает задание печати целиком: шаблон наклейки и значения → байты, которые уходят в
/// принтер. Транспорт (TCP 9100 и т. п.) — отдельно, драйвер в порт не пишет.
/// </summary>
public interface IPrinterDriver
{
    byte[] Build(Stream template, PrintValues values);
}
