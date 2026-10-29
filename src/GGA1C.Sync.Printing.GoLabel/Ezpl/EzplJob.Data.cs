// Подстановка данных объекта: счётчики ^C, переменные ^V, поля ^F, дата ^D/^T.
// Источник: Graphic.FillPrintingData/FillPrintingDataEZPL/getSerial (QLabelSDK/Graphic.cs:1517,
// :1952, :1236). Окна запроса GoLabel не переносятся: значения даёт PrintValues.
using System.Text.RegularExpressions;
using GGA1C.Sync.Printing.GoLabel.Ezpx;

namespace GGA1C.Sync.Printing.GoLabel.Ezpl;

public sealed partial class EzplJob
{
    // QLabel.DataRow — строка базы при печати из данных (шаг 6). Без базы — null.
    private IReadOnlyList<object?>? _dataRow;

    /// <summary>Graphic.FillPrintingData(bformat, ..., bcmd).</summary>
    /// <param name="measureOnly">
    /// Результат нужен GoLabel только для замера (выравнивание): дату ПК не подставляем, но
    /// побочные эффекты подстановки (выравнивание по формату переменной) сохраняются.
    /// </param>
    internal string FillPrintingData(EzpxGraphic g, bool bformat, bool bcmd = false, bool measureOnly = false)
    {
        if (g.FormatType != "None")
        {
            throw new NotSupportedException($"Форматирование данных ({g.FormatType}) не перенесено.");
        }
        string text = FillPrintingDataEZPL(g, bformat, bcmd, usePrinterClock: true, measureOnly);
        if (g.CharFilterRule != "None")
        {
            throw new NotSupportedException($"Фильтр символов ({g.CharFilterRule}) не перенесён.");
        }
        if (g.ReplaceInfoCount != 0)
        {
            throw new NotSupportedException("Замена текста (ReplaceInfoItems) не перенесена.");
        }
        text = g.CharTruncateRule.DoTruncate(text);
        if (g.GetAiFromDigitalLink)
        {
            throw new NotSupportedException("GS1 Digital Link не перенесён.");
        }
        return text;
    }

    private string FillPrintingDataEZPL(EzpxGraphic g, bool bformat, bool bcmd, bool usePrinterClock, bool measureOnly)
    {
        var matches = new List<(char Label, int Id)>();
        if (g.Prompt != "None" && bformat)
        {
            throw new NotSupportedException($"Объект с окном ввода ({g.Prompt}) не перенесён: значение должен дать хаб.");
        }
        string linkData = GetLinkData(g);
        char[] a = linkData.ToCharArray();
        string text = "";
        bool flag = false;
        int len = linkData.Length;
        var serial = Label.SerialFormat;
        var variable = Label.VariableFormat;
        for (int i = 0; i < len; i++)
        {
            if (a[i] == '^' && !flag)
            {
                if (i != len - 1)
                {
                    if (i == len - 2 && a[i + 1] != 'T' && a[i + 1] != 'D' && a[i + 1] != '^')
                    {
                        text += a[i];
                        text += a[i + 1];
                        i++;
                        flag = false;
                        continue;
                    }
                    if (a[i + 1] is 'C' or 'V' or 'F' or 'T' or 'D' or '^')
                    {
                        flag = true;
                        continue;
                    }
                }
                goto Tail;
            }
            if ((a[i] == 'Y' || a[i] == 'M' || a[i] == 'H') && !flag)
            {
                if (i == len - 1 || i < 3)
                {
                    goto Tail;
                }
                if (a[i - 3] == '^' && (a[i - 2] == 'D' || a[i - 2] == 'T') && (a[i - 1] == '+' || a[i - 1] == '-') && a[i + 1] == 'V')
                {
                    text += a[i];
                    flag = true;
                    continue;
                }
            }
            if (flag)
            {
                switch (a[i])
                {
                    case '^':
                        text += bformat ? "^^" : "^";
                        flag = false;
                        continue;
                    case 'D':
                    case 'T':
                        if (!bformat || !usePrinterClock)
                        {
                            matches.Add((a[i], text.Length));
                        }
                        text += "^" + a[i];
                        flag = false;
                        continue;
                    case 'C':
                    {
                        if (i + 1 != len && char.IsDigit(a[i + 1]))
                        {
                            int num = a[i + 1] - '0';
                            if (i + 2 == len || !char.IsDigit(a[i + 2]) || EzpxLabel.MaxSerial <= 10)
                            {
                                if (!string.IsNullOrEmpty(serial[num]))
                                {
                                    if (!bformat || !bcmd)
                                    {
                                        text += GetSerial(g, serial[num]!.Split(',')[0], !string.IsNullOrEmpty(Label.SerialCustomSequence[num]));
                                        i++;
                                    }
                                    else
                                    {
                                        text += "^C";
                                    }
                                }
                                else
                                {
                                    text += "^C";
                                }
                                flag = false;
                                continue;
                            }
                            int num2 = num * 10 + a[i + 2] - '0';
                            if (num2 <= EzpxLabel.MaxSerial)
                            {
                                if (!string.IsNullOrEmpty(serial[num2]))
                                {
                                    if (!bformat || !bcmd)
                                    {
                                        text += GetSerial(g, serial[num2]!.Split(',')[0], !string.IsNullOrEmpty(Label.SerialCustomSequence[num2]));
                                        i += 2;
                                    }
                                    else
                                    {
                                        text += "^C";
                                    }
                                }
                                else
                                {
                                    text += "^C";
                                }
                                flag = false;
                                continue;
                            }
                        }
                        text += "^C";
                        flag = false;
                        continue;
                    }
                    case 'V':
                    {
                        if (i + 1 != len && char.IsDigit(a[i + 1]))
                        {
                            int num = a[i + 1] - '0';
                            if (i + 2 == len || !char.IsDigit(a[i + 2]))
                            {
                                if (!string.IsNullOrEmpty(variable[num]))
                                {
                                    string[] parts = variable[num]!.Split(',');
                                    if (!bformat)
                                    {
                                        text += "0".PadLeft(Convert.ToInt16(parts[0]), '0');
                                        i++;
                                    }
                                    else if (bcmd)
                                    {
                                        text += VariableTagText(a, i);
                                    }
                                    else
                                    {
                                        if (parts.Length < 4)
                                        {
                                            text += " ";
                                        }
                                        else
                                        {
                                            int totalWidth = Convert.ToInt16(parts[0]);
                                            string value = VariableValue(parts);
                                            CheckFloatFormat(num);
                                            if (value.Length >= totalWidth)
                                            {
                                                text += value;
                                            }
                                            else if (parts[2][1] == 'r')
                                            {
                                                text += value.PadLeft(totalWidth, ' ');
                                            }
                                            else
                                            {
                                                text += parts[2][1] != 'c' ? value : value.PadLeft(totalWidth - (totalWidth - value.Length) / 2, ' ');
                                            }
                                        }
                                        i++;
                                    }
                                    flag = false;
                                }
                                else
                                {
                                    text += VariableTagText(a, i);
                                    flag = false;
                                }
                                continue;
                            }
                            int num2 = num * 10 + a[i + 2] - '0';
                            if (num2 <= EzpxLabel.MaxVariable)
                            {
                                if (!string.IsNullOrEmpty(variable[num2]))
                                {
                                    string[] parts = variable[num2]!.Split(',');
                                    if (!bformat)
                                    {
                                        if (parts.Length == 1)
                                        {
                                            parts = (parts[0] + ",Prompt,jl8").Split(',');
                                        }
                                        else if (parts.Length == 2)
                                        {
                                            parts = (parts[0] + "," + parts[1] + ",jl8").Split(',');
                                        }
                                        SetAlignmentFromFormat(g, parts[2]);
                                        if (parts[1].StartsWith("V#OP", StringComparison.Ordinal))
                                        {
                                            text = g is not EzpxBarCode ? "##,##" : "000000000000";
                                        }
                                        else if (parts.Length < 4)
                                        {
                                            int totalWidth = Convert.ToInt16(parts[0]);
                                            bool dateOffset = i > 3 && a[i - 4] == '^' && (a[i - 3] == 'D' || a[i - 3] == 'T')
                                                && (a[i - 2] == '+' || a[i - 2] == '-') && (a[i - 1] == 'Y' || a[i - 1] == 'M' || a[i - 1] == 'H');
                                            text += dateOffset ? "0000.00" : "0".PadLeft(totalWidth, '0');
                                        }
                                        else
                                        {
                                            int totalWidth = Convert.ToInt16(parts[0]);
                                            string value = VariableValue(parts);
                                            if (Label.VariableOpFormat.Contains("V#SETZERO,V" + num2.ToString().PadLeft(2, '0')))
                                            {
                                                if (value.Trim() == "")
                                                {
                                                    value = value.Trim();
                                                }
                                                value = value.PadLeft(totalWidth, '0');
                                            }
                                            CheckFloatFormat(num2);
                                            text += value;
                                            if (value.Length < totalWidth)
                                            {
                                                SetAlignmentFromFormat(g, parts[2]);
                                            }
                                        }
                                        i += 2;
                                    }
                                    else if (bcmd)
                                    {
                                        text += VariableTagText(a, i);
                                    }
                                    else
                                    {
                                        if (parts.Length < 4)
                                        {
                                            text += " ";
                                        }
                                        else
                                        {
                                            int totalWidth = Convert.ToInt16(parts[0]);
                                            string value = VariableValue(parts);
                                            CheckFloatFormat(num2);
                                            text += value;
                                            if (value.Length < totalWidth)
                                            {
                                                SetAlignmentFromFormat(g, parts[2]);
                                            }
                                        }
                                        i += 2;
                                    }
                                    flag = false;
                                }
                                else
                                {
                                    text += VariableTagText(a, i);
                                    flag = false;
                                }
                                continue;
                            }
                        }
                        text += VariableTagText(a, i);
                        flag = false;
                        continue;
                    }
                    case 'F':
                    {
                        if (a.Length > i + 2 && a[i + 1] == 'P' && a[i + 2] == 'G')
                        {
                            throw new NotSupportedException("Номер страницы базы (^FPG) не перенесён.");
                        }
                        if (i + 1 != len && char.IsDigit(a[i + 1]))
                        {
                            int num = a[i + 1] - '0';
                            if (i + 2 == len || !char.IsDigit(a[i + 2]))
                            {
                                // GoLabel: исключение (нет строки базы) — continue без сброса флага.
                                if (_dataRow == null || num >= _dataRow.Count)
                                {
                                    continue;
                                }
                                text += _dataRow[num]?.ToString();
                                i++;
                                flag = false;
                                continue;
                            }
                            int num2 = num * 10 + a[i + 2] - '0';
                            if (num2 <= EzpxLabel.MaxField)
                            {
                                if (_dataRow != null)
                                {
                                    if (num2 >= _dataRow.Count)
                                    {
                                        continue;
                                    }
                                    if (_dataRow[num2] is DateTime date)
                                    {
                                        text += date.ToString("yyyy/MM/dd", System.Globalization.CultureInfo.InvariantCulture);
                                    }
                                    else
                                    {
                                        text += _dataRow[num2]?.ToString();
                                        if (g.ReplaceSpecialCharFromDB)
                                        {
                                            throw new NotSupportedException("Замена спецсимволов из базы не перенесена.");
                                        }
                                    }
                                }
                                i += 2;
                                flag = false;
                                continue;
                            }
                        }
                        text += "^F";
                        flag = false;
                        continue;
                    }
                }
            }
        Tail:
            if (!flag)
            {
                text += a[i];
            }
            else
            {
                flag = false;
            }
        }
        if (string.IsNullOrEmpty(text))
        {
            return " ";
        }
        if ((bformat && usePrinterClock) || matches.Count == 0 || measureOnly)
        {
            return text;
        }
        string dFormat = string.IsNullOrEmpty(g.DateFormat) ? Label.DateFormat ?? "" : g.DateFormat;
        return ConvDateTimeToStrEZPL(text, dFormat, matches);
    }

    /// <summary>
    /// MyTimeDate.ConvDateTimeToStrEZPL (QLabelSDK/MyTimeDate.cs:241), bShowNowDateTime = true: дата
    /// ПК подставляется в данные (штрихкод растром). Часы — PrintValues.Clock, не DateTime.Now.
    /// Перенесены формы из шаблонов: «^D%y» (%m, %d) и «^D±Y/M/Hnnnn» без дробной части.
    /// </summary>
    private string ConvDateTimeToStrEZPL(string input, string dFormat, List<(char Label, int Id)> matches)
    {
        DateTime now = _values.Clock.GetLocalNow().DateTime;
        var en = System.Globalization.CultureInfo.CreateSpecificCulture("en-US");
        string text = input.Substring(0, matches[0].Id);
        for (int k = 0; k < matches.Count; k++)
        {
            int start = matches[k].Id;
            int remain = k != matches.Count - 1 ? matches[k + 1].Id - start : input.Length - start;
            if (matches[k].Label != 'D')
            {
                throw new NotSupportedException("Время ПК в данных (^T) не перенесено.");
            }
            string part = input.Substring(start, remain);
            if (remain >= 12 && Regex.IsMatch(part.Substring(0, 12), @"\b%[ymd][+-]\d{4}.\d{2}")
                || remain >= 10 && Regex.IsMatch(part.Substring(0, 10), @"\b[+-]\d{4}.\d{2}")
                || remain >= 10 && Regex.IsMatch(part.Substring(0, 10), @"\b%[ymd][+-][YM]\d{4}"))
            {
                throw new NotSupportedException($"Форма даты «{part}» не перенесена.");
            }
            if (remain >= 8 && Regex.IsMatch(part.Substring(0, 8), @"\b[+-][YMH]\d{4}"))
            {
                // Get_YMHxxxxyy_Date: «^D+Y0000[.yy]».
                string tail = input.Substring(start + 4);
                if (tail.Length > 7)
                {
                    tail = tail.Substring(0, 7);
                }
                string[] pieces = tail.Split('.');
                int digits = NumericLen(pieces[0], 4);
                int fraction = pieces.Length > 1 && pieces[0].Length <= 4 && digits == pieces[0].Length ? NumericLen(pieces[1], 2) : 0;
                if (fraction != 0)
                {
                    throw new NotSupportedException("Сдвиг даты с дробной частью не перенесён.");
                }
                int length = digits + 4;
                int amount = int.Parse(pieces[0].Substring(0, digits), System.Globalization.CultureInfo.InvariantCulture);
                DateTime run = part.Substring(2, 2) switch
                {
                    "+Y" => now.AddYears(now.Year + amount > 9999 ? (now.Year + amount) % 10000 - now.Year : amount),
                    "-Y" => now.AddYears(-(now.Year - amount < 0 ? -((10000 + now.Year - amount) % 10000 - now.Year) : amount)),
                    "+M" => now.AddMonths(amount),
                    "-M" => now.AddMonths(-amount),
                    "+H" => now.AddHours(amount),
                    _ => now.AddHours(-amount),
                };
                text += RunDateFormat(run, dFormat, en);
                text += input.Substring(start + length, remain - length);
            }
            else if (remain >= 4 && part.Substring(2, 2) is "%y" or "%m" or "%d")
            {
                // GoLabel форматирует текущей культурой: для yy/MM/dd цифры те же.
                text += now.ToString(DateFormatPart(part[3], dFormat), System.Globalization.CultureInfo.InvariantCulture);
                text += input.Substring(start + 4, remain - 4);
            }
            else
            {
                throw new NotSupportedException($"Форма даты «{part}» не перенесена.");
            }
        }
        return text;
    }

    private static int NumericLen(string data, int max)
    {
        int n = 0;
        for (int i = 0; i < data.Length; i++)
        {
            if (char.IsAsciiDigit(data[i]))
            {
                n = i + 1;
                if (n >= max)
                {
                    break;
                }
            }
        }
        return n;
    }

    /// <summary>MyTimeDate.GetDateFormat: часть формата даты («y2-me-dd») для %y, %m, %d.</summary>
    private static string DateFormatPart(char ymd, string format)
    {
        if (ymd == 'y')
        {
            string f = format.Substring(0, 2);
            return f.Contains("y2") ? f.Replace("y2", "yy") : f.Replace("y4", "yyyy");
        }
        if (ymd == 'm')
        {
            return format.Substring(3, 2).Replace("mn", "MM").Replace("me", "MM").Replace("m1", "MMM").Replace("m2", "MMMM");
        }
        return format.Substring(6, 2);
    }

    /// <summary>MyTimeDate.GetRunDateFormat без недель, юлианского дня и дня недели.</summary>
    private static string RunDateFormat(DateTime run, string format, System.Globalization.CultureInfo en)
    {
        if (format.Contains("wy") || format.Contains("jj"))
        {
            throw new NotSupportedException($"Формат даты «{format}» (неделя, юлианский день) не перенесён.");
        }
        string f = format.Contains("y2") ? format.Replace("y2", "yy") : format.Replace("y4", "yyyy");
        f = f.Replace("mn", "MM").Replace("me", "MMM").Replace("m1", "MMM").Replace("m2", "MMMM");
        return f.Contains("yy") || f.Contains("MM") || f.Contains("dd") ? run.ToString(f, en) : f;
    }

    private static string VariableTagText(char[] a, int i) =>
        i <= 0 || a[i] != 'V' || (a[i - 1] != 'Y' && a[i - 1] != 'M' && a[i - 1] != 'H') ? "^V" : "V";

    /// <summary>Значение из формата переменной «ширина,подсказка,выравнивание,значение[,...],режим».</summary>
    private static string VariableValue(string[] parts)
    {
        if (parts.Length <= 5)
        {
            return parts[3];
        }
        string value = parts[3];
        for (int k = 4; k < parts.Length - 1; k++)
        {
            value = value + "," + parts[k];
        }
        return value;
    }

    // QLabel.FLOATFORMATx заполняет ParseVariableOpEZPL — только при VariableOpFormat (не перенесено).
    private void CheckFloatFormat(int index)
    {
        if (Label.VariableOpFormat.Count > 0)
        {
            throw new NotSupportedException("Операции над переменными (VariableOpFormat) не перенесены.");
        }
    }

    /// <summary>Побочный эффект подстановки переменной: Alignment объекта по формату («jl», «jr», «jc»).</summary>
    private static void SetAlignmentFromFormat(EzpxGraphic g, string format)
    {
        if (g.TypeName == "CurvedText")
        {
            return;
        }
        if (g.Direction > Orientation.Angle270)
        {
            g.Alignment = TextAlignment.Left;
        }
        else if (format[1] == 'r')
        {
            g.Alignment = TextAlignment.Right;
        }
        else if (format[1] == 'c')
        {
            g.Alignment = TextAlignment.Center;
        }
        else if (format[1] == 'l')
        {
            g.Alignment = TextAlignment.Left;
        }
    }

    /// <summary>Graphic.GetLinkData.</summary>
    private static string GetLinkData(EzpxGraphic g)
    {
        if (g.ItemInfoCount > 0 && g.LinkMode == "DisplayData")
        {
            throw new NotSupportedException("Связь с отображаемыми данными (LinkMode = DisplayData) не перенесена.");
        }
        // Ссылки DataSourceFromObject отвергнуты при чтении, остальное GetLinkData не меняет.
        return g.Data;
    }

    /// <summary>Graphic.getSerial: значение счётчика без префикса системы счисления.</summary>
    private static string GetSerial(EzpxGraphic g, string sub, bool isCustomSeq)
    {
        char[] trim = ['0', ' '];
        char c = sub[0];
        bool prefixed = !(c >= '0' && c <= '9') && c != ' ' && !isCustomSeq && (c is 'A' or 'a' or 'C' or 'c');
        string value = prefixed ? sub.Substring(1) : sub;
        return g.RemovePreZeroAndEmpty ? value.TrimStart(trim) : value;
    }
}
