using System;

namespace GGA1C.Sync.Labels;

/// <summary>
/// Строка буфера этикеток.
///
/// Буфер намеренно живёт в ОТДЕЛЬНОМ файле SQLite, а не в основной базе приложения:
/// GoLabel2 читает его снаружи (через ODBC или через выгрузку в CSV), поэтому рядом
/// не должно оказаться ничего секретного — ни паролей Altium и 1С, ни ключей Grist.
///
/// Поля date / product_code / mode_code — то, что реально печатается на этикетке ГКВ-3
/// и что присылает виджет Grist. Остальные оставлены под другие сценарии и приходят пустыми.
/// </summary>
public class LabelRecord
{
    public int Id { get; set; }

    /// <summary>Ключ строки для upsert и удаления. Если виджет его не прислал, собирается
    /// из product_code и mode_code (см. GristLabelsController).</summary>
    public string ExternalId { get; set; } = "";

    public string Date { get; set; } = "";
    public string ProductCode { get; set; } = "";
    public string ModeCode { get; set; } = "";

    public string Article { get; set; } = "";
    public string Name { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string Barcode { get; set; } = "";
    public string Qty { get; set; } = "";
    public string Extra1 { get; set; } = "";
    public string Extra2 { get; set; } = "";
    public string Extra3 { get; set; } = "";

    public DateTime UpdatedAt { get; set; }
}
