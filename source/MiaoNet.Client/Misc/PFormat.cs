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
    [GeneratedRegex(@"\((\d+)\)", RegexOptions.CultureInvariant)]
    private static partial Regex GetReplaceRegex();

    public static string Format(string format, params object?[] args) 
        => Format(CultureInfo.CurrentCulture, format, args);

    public static string Format(IFormatProvider? provider, string format, params object?[] args)
    {
        return GetReplaceRegex().Replace(format, m =>
        {
            int index = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            if (index < args.Length)
            {
                object? arg = args[index];
                return arg is IFormattable formattable 
                    ? formattable.ToString(null, provider) 
                    : arg?.ToString() ?? string.Empty;
            }
            else
            {
                return m.Value;
            }
        });
    }
}