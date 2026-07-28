using ECommons.DalamudServices;
using Lumina.Excel.Sheets;
using System.Text;
using System.Text.RegularExpressions;

namespace BOCCHI;

public static class LogMessageHelper
{
    public static string GetLogMessagePattern(uint id)
    {
        var macroString = Svc.Data.GetExcelSheet<LogMessage>().GetRow(id).Text.ToMacroString();
        return BuildPattern(macroString);
    }

    public static string BuildPattern(string macroString)
    {
        var matches = Regex.Matches(macroString, @"<num\((\w+)\)>");
        var pattern = new StringBuilder("^");
        var offset = 0;

        foreach (Match match in matches)
        {
            pattern.Append(Regex.Escape(macroString[offset..match.Index]));
            pattern.Append($"(?<{match.Groups[1].Value}>\\d+)");
            offset = match.Index + match.Length;
        }

        pattern.Append(Regex.Escape(macroString[offset..]));
        pattern.Append('$');

        return pattern.ToString();
    }
}
