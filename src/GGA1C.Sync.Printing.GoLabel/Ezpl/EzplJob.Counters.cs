// Счётчики и переменные. Источник: GlobalPrint.CheckSerialPrompt/CheckVariablePrompt
// (GlobalInfo/GlobalPrint.cs:782, :709), QLabel.UpdateSerialFormat/UpdateSerialFormatAll/
// UpdateVariableFormat/UpdateVariableFormatAll и помощники (QLabelSDK/QLabel.cs:4905–5643).
using System.Text.RegularExpressions;
using GGA1C.Sync.Printing.GoLabel.Ezpx;

namespace GGA1C.Sync.Printing.GoLabel.Ezpl;

public sealed partial class EzplJob
{
    /// <summary>
    /// CheckSerialPrompt: стартовые значения счётчиков. Значение из -S&lt;n&gt; (у нас —
    /// PrintValues.Serials) дополняется пробелами слева до длины формата. Счётчик с подсказкой
    /// без значения GoLabel спросил бы окном — здесь это ошибка.
    /// </summary>
    private void CheckSerialPrompt()
    {
        if (Label.VariableOpFormat.Count > 0)
        {
            throw new NotSupportedException("Операции над переменными (VariableOpFormat) не перенесены.");
        }
        for (int m = 0; m < EzpxLabel.MaxSerial; m++)
        {
            string? format = Label.SerialFormat[m];
            if (format == null)
            {
                continue;
            }
            string[] parts = format.Split(',');
            if ((parts.Length != 3 && parts.Length != 5) || parts[2].Trim() == "")
            {
                continue;
            }
            if (!_values.Serials.TryGetValue(m, out var value) && _values.SerialDefaultsFromTemplate)
            {
                value = parts[0];
            }
            if (value == null)
            {
                throw new InvalidOperationException(
                    $"Нет стартового значения счётчика C{m:00} («{parts[2]}»): GoLabel спросил бы его окном.");
            }
            if (value.Length < parts[0].Length)
            {
                value = value.PadLeft(parts[0].Length, ' ');
            }
            Label.SerialFormat[m] = value + "," + parts[1] + "," + parts[2];
        }
    }

    /// <summary>
    /// CheckVariablePrompt: значение переменной из -V&lt;nn&gt; (PrintValues.Variables) пишется в
    /// формат с режимом «2» (без запроса).
    /// </summary>
    private void CheckVariablePrompt()
    {
        for (int j = 0; j < EzpxLabel.MaxVariable; j++)
        {
            string? format = Label.VariableFormat[j];
            if (format == null)
            {
                continue;
            }
            string[] parts = format.Split(',');
            if (parts.Length <= 2)
            {
                continue;
            }
            string mode = "0";
            string result;
            if (_values.Variables.TryGetValue(j, out var value))
            {
                mode = "2";
                result = parts[0] + "," + parts[1] + "," + parts[2] + "," + value + "," + mode;
            }
            else if (parts.Length > 3)
            {
                result = parts[0];
                for (int l = 1; l < parts.Length - 1; l++)
                {
                    result = result + "," + parts[l];
                }
                result = result + "," + mode;
            }
            else
            {
                result = parts[0] + "," + parts[1] + "," + parts[2] + ",," + mode;
            }
            Label.VariableFormat[j] = result;
        }
    }

    /// <summary>QLabel.UpdateVariableFormatAll (EZPL, режим Legacy).</summary>
    private void UpdateVariableFormatAll()
    {
        for (int j = 0; j < EzpxLabel.MaxVariable; j++)
        {
            if (Label.VariableFormat[j] != null && j < EzpxLabel.MaxVariable - EzpxLabel.LinkedVariable)
            {
                Label.VariableFormat[j] = UpdateVariableFormat(Label.VariableFormat[j]!, j);
            }
        }
        // ParseVariableOpEZPL — пусто без VariableOpFormat (проверено в CheckSerialPrompt).
        for (int j = EzpxLabel.MaxVariable - EzpxLabel.LinkedVariable; j < EzpxLabel.MaxVariable; j++)
        {
            if (!string.IsNullOrWhiteSpace(Label.VariableFormat[j]))
            {
                throw new NotSupportedException("Связанные переменные (V90–V99, LinkedVar) не перенесены.");
            }
        }
    }

    /// <summary>
    /// QLabel.UpdateVariableFormat: режим «0»/«1» — окно ввода (здесь ошибка), «2» — без запроса.
    /// </summary>
    private static string? UpdateVariableFormat(string f, int index)
    {
        string[] parts = f.Split(',');
        if (parts.Length < 2)
        {
            return null;
        }
        string width = parts[0];
        string prompt = parts[1];
        string align;
        string value;
        string mode;
        switch (parts.Length)
        {
            case 2:
                align = "jl";
                value = " ";
                mode = "0";
                break;
            case 3:
                align = parts[2];
                value = " ";
                mode = "0";
                break;
            case 4:
                align = parts[2];
                value = parts[3];
                mode = "0";
                break;
            case 5:
                align = parts[2];
                value = parts[3];
                mode = parts[4];
                break;
            default:
                align = parts[2];
                value = parts[3];
                for (int i = 4; i < parts.Length - 1; i++)
                {
                    value = value + "," + parts[i];
                }
                mode = parts[^1];
                break;
        }
        if (mode == "0" || mode == "1")
        {
            throw new InvalidOperationException(
                $"Нет значения переменной V{index:00} («{prompt}»): GoLabel спросил бы его окном.");
        }
        return width + "," + prompt + "," + align + "," + value + "," + mode;
    }

    /// <summary>QLabel.UpdateSerialFormatAll.</summary>
    private void UpdateSerialFormatAll(int n)
    {
        for (int i = 0; i < EzpxLabel.MaxSerial; i++)
        {
            if (Label.SerialFormat[i] != null)
            {
                Label.SerialFormat[i] = UpdateSerialFormat(Label.SerialFormat[i]!, n, Label.SerialLeadingCode[i],
                    Label.SerialCustomSequence[i], Label.SerialSpecialCarry[i]);
            }
        }
    }

    /// <summary>QLabel.UpdateSerialFormat: шаг счётчика «значение,±шаг,подсказка[,мин,макс]».</summary>
    internal static string? UpdateSerialFormat(string f, int n, int leadingCode, string? customSequence, bool specialCarry)
    {
        string[] parts = f.Split(',');
        if (parts.Length < 2)
        {
            return null;
        }
        char c = parts[0][0];
        string value = parts[0];
        int radix = 0;
        if (string.IsNullOrEmpty(customSequence))
        {
            if (c >= '0' && c <= '9' || c == ' ')
            {
                radix = 10;
            }
            else if (c is 'A' or 'a')
            {
                value = parts[0].Substring(1);
                radix = 16;
            }
            else if (c is 'C' or 'c')
            {
                value = parts[0].Substring(1);
                radix = 36;
            }
            else
            {
                return null;
            }
        }
        bool leadingSpace = leadingCode == 0;
        char sign = parts[1][0];
        string step = parts[1].Substring(1);
        string prompt = parts.Length >= 3 ? parts[2] : "prompt";
        string Tail() => parts.Length > 4
            ? "," + parts[1] + "," + prompt + "," + parts[3] + "," + parts[4]
            : "," + parts[1] + "," + prompt;

        if (string.IsNullOrEmpty(customSequence))
        {
            bool negative = false;
            if (n < 0)
            {
                negative = true;
                n = Math.Abs(n);
            }
            string delta = ConvertInt2HD(ConvertHD2Int(step, radix) * n, radix);
            value = sign == '+'
                ? (!negative ? HexDecimalAdd(value, delta, radix) : HexDecimalSub(value, delta, radix))
                : (!negative ? HexDecimalSub(value, delta, radix) : HexDecimalAdd(value, delta, radix));
            int length = value.Length;
            if (parts.Length > 4)
            {
                int current = ConvertHD2Int(value, radix);
                int min = ConvertHD2Int(parts[3].Trim(), radix);
                int max = ConvertHD2Int(parts[4].Trim(), radix);
                if (current > max || current < min)
                {
                    value = ConvertInt2HD(sign != '+' ? max : min, radix);
                }
            }
            value = !leadingSpace ? value.PadLeft(length, '0') : value.PadLeft(length, ' ');
            if (leadingSpace)
            {
                value = LeadingSpace(value);
            }
            return radix == 10 ? value + Tail() : c + value + Tail();
        }
        throw new NotSupportedException("Счётчик со своей последовательностью символов не перенесён.");
    }

    private static int ConvertHD2Int(string str, int radix)
    {
        int num = 0;
        foreach (char c in str)
        {
            num = c >= '0' && c <= '9' ? num * radix + c - 48
                : c >= 'A' && c <= 'Z' ? num * radix + c - 65 + 10
                : c >= 'a' && c <= 'z' ? num * radix + c - 97 + 10
                : 0;
        }
        return num;
    }

    private static string ConvertInt2HD(int q, int radix)
    {
        string text = "";
        int rest = q;
        for (int i = 0; i < 20; i++)
        {
            int digit = rest % radix;
            if (rest >= radix)
            {
                rest /= radix;
                text += Digit(digit);
                continue;
            }
            text += Digit(rest);
            return Reverse(text);
        }
        return Reverse(text);
    }

    private static string HexDecimalAdd(string d1, string s1, int radix)
    {
        int carry = 0;
        string text = "";
        int[] a = ReverseHD2Int(d1);
        int[] b = ReverseHD2Int(s1);
        for (int i = 0; i < a.Length; i++)
        {
            int sum = a[i] + (i < b.Length ? b[i] : 0) + carry;
            if (sum >= radix)
            {
                carry = 1;
                sum %= radix;
            }
            else
            {
                carry = 0;
            }
            text += Digit(sum);
        }
        return Reverse(text);
    }

    private static string HexDecimalSub(string d1, string s1, int radix)
    {
        int borrow = 0;
        string text = "";
        int[] a = ReverseHD2Int(d1);
        int[] b = ReverseHD2Int(s1);
        for (int i = 0; i < a.Length; i++)
        {
            int diff = a[i] - (i < b.Length ? b[i] : 0) - borrow;
            if (diff < 0)
            {
                borrow = 1;
                diff += radix;
            }
            else
            {
                borrow = 0;
            }
            text += Digit(diff);
        }
        return Reverse(text);
    }

    private static char Digit(int d) => d > 9 ? (char)(d - 10 + 65) : (char)(d + 48);

    private static int[] ReverseHD2Int(string str)
    {
        var result = new int[str.Length];
        for (int i = 0; i < str.Length; i++)
        {
            char c = str[str.Length - 1 - i];
            result[i] = c >= '0' && c <= '9' ? c - 48
                : c >= 'A' && c <= 'Z' ? c - 65 + 10
                : c >= 'a' && c <= 'z' ? c - 97 + 10
                : 0;
        }
        return result;
    }

    private static string Reverse(string s)
    {
        var chars = s.ToCharArray();
        Array.Reverse(chars);
        return new string(chars);
    }

    /// <summary>QLabel.LeadingSpace: ведущие нули → пробелы.</summary>
    private static string LeadingSpace(string ser)
    {
        if (Regex.IsMatch(ser, "^[0-9]+$") && decimal.Parse(ser, System.Globalization.CultureInfo.InvariantCulture) == 0m)
        {
            return "0".PadLeft(ser.Length, ' ');
        }
        int i = 0;
        string text = "";
        for (; ser[i] == '0'; i++)
        {
            text += " ";
        }
        return text + ser.Substring(i);
    }
}
