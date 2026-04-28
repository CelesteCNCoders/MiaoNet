using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Celeste.Mod.MiaoNet;

// much simpler string.Format, but uses ()s
// since Celeste dialog has used {}...
// obviously this can be optimized
public static partial class PFormat
{
    [GeneratedRegex(@"\((\d+)\)")]
    private static partial Regex GetReplaceRegex();

    public static string Format(string format, params object?[] args)
    {
        return GetReplaceRegex().Replace(format, m =>
        {
            int index = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            return index < args.Length ? args[index]?.ToString() ?? string.Empty : m.Value;
        });
    }
}