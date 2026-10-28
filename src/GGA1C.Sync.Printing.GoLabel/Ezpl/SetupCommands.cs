// Шапка задания — Setup.GetCommandEZPL (QLabelSDK/Setup.cs:322).
using GGA1C.Sync.Printing.GoLabel.Ezpx;

namespace GGA1C.Sync.Printing.GoLabel.Ezpl;

internal static class SetupCommands
{
    /// <param name="number">Setup.Number на время шапки (PrintJob.Print ставит 1).</param>
    /// <param name="copy">Setup.Copy на время шапки (Print(records) ставит 1).</param>
    public static List<string> GetCommandEZPL(EzpxSetup s, int number, int copy)
    {
        // GoLabel печатает float текущей культурой и меняет её разделитель на точку — то же,
        // что инвариантная культура.
        string length = Fmt.F(s.LabelLength);
        string gap = Fmt.F(s.GapLength);
        string feed = Fmt.F(s.FeedLength);
        string blackMark = Fmt.F(s.BlackMark);
        string position = Fmt.F(s.Position);
        string width = Fmt.F(s.LabelWidth);

        var commands = new List<string>();
        string prefix = s.UnitType == Unit.Dot ? "^QD" : "^Q";
        string? item;
        switch (s.LabelType)
        {
            case 0:
                item = prefix + length + "," + gap;
                break;
            case 1:
                item = prefix + length + ",0," + feed;
                break;
            case 2:
                item = prefix + length + "," + blackMark + "," + position + s.ZSign;
                break;
            case 3:
                item = null;
                break;
            default:
                return commands;
        }
        // При LabelType = 3 GoLabel добавляет null — SendStringToPrinter шлёт пустую строку + \r\n.
        commands.Add(item ?? "");
        commands.Add("^W" + width);
        commands.Add("^H" + s.Darkness);
        commands.Add(s.InfinityPrint ? "^PI" : "^P" + number);
        commands.Add("^S" + s.Speed);
        commands.Add(s.PrintMode == 0 ? "^AT" : "^AD");
        commands.Add("^C" + copy);
        commands.Add("^R" + s.LeftMargin);
        commands.Add(s.TopMargin >= 0 ? "~Q+" + s.TopMargin : "~Q" + s.TopMargin);
        commands.Add("^O" + s.Stripper);
        commands.Add(s.BatchCut ? "^Db" : "^D" + s.LabelsPerCut);
        commands.Add("^E" + Fmt.D(Math.Round(s.Stop, 1, MidpointRounding.AwayFromZero)));
        commands.Add("~R" + s.Rotate180);
        return commands;
    }
}
