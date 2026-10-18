using System;
using Volo.Abp.Domain.Entities;

namespace GGA1C.Sync.Labels;

/// <summary>
/// Строка буфера этикеток в базе хаба.
///
/// Раньше буфер был отдельным файлом SQLite на каждого человека, потому что предполагалось,
/// что GoLabel II читает его через ODBC. Выяснилось, что **ODBC в GoLabel II нет вовсе**
/// (в его сборках строка ODBC не встречается ни разу), а есть CSV, Excel, Access и
/// подключение по учётным данным к MySQL, SQL Server и Oracle — причём к MySQL своим
/// драйвером, который лежит прямо в папке программы. Поэтому буфер переехал в MySQL: та же
/// база, что у хаба, никаких файлов и никаких драйверов на машине с принтером.
///
/// Разделение по людям теперь держит сама СУБД: у каждого свой логин, а представление
/// <c>my_labels</c> отдаёт только строки с его <see cref="Slug"/>. Прежний довод «файл на
/// человека, потому что в SQLite нет пользователей» с переходом на MySQL потерял силу.
/// </summary>
public class LabelRow : Entity<long>
{
    /// <summary>
    /// Имя учётки (оно же логин в MySQL). По нему представление отбирает строки человека,
    /// поэтому значение должно совпадать с именем пользователя базы.
    /// </summary>
    public string Slug { get; set; } = "";

    /// <summary>Ключ строки: по нему идёт upsert и удаление. Уникален внутри учётки.</summary>
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
