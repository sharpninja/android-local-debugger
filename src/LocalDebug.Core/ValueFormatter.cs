using System.Globalization;
using Mono.Debugger.Soft;

namespace LocalDebug.Core;

public static class ValueFormatter
{
    public static string Format(Value? value, int depth = 0) => value switch
    {
        null => "<unavailable>",
        PrimitiveValue primitive => FormatPrimitive(primitive.Value),
        StringMirror text => Quote(new string(text.GetChars(0, Math.Min(text.Length, 240))))
            + (text.Length > 240 ? "…" : ""),
        ArrayMirror array when depth < 1 => $"[{string.Join(", ", array.GetValues(0, Math.Min(array.Length, 8)).Select(v => Format(v, depth + 1)))}{(array.Length > 8 ? ", …" : "")}] (length {array.Length})",
        ArrayMirror array => $"{array.Type.FullName} (length {array.Length})",
        StructMirror structure when depth < 1 => $"{structure.Type.FullName} {{ {string.Join(", ", structure.Fields.Take(8).Select(v => Format(v, depth + 1)))} }}",
        StructMirror structure => structure.Type.FullName,
        ObjectMirror instance => $"{{{instance.Type.FullName}}}",
        _ => value.ToString() ?? "<unavailable>"
    };

    public static string FormatPrimitive(object? value) => value switch
    {
        null => "null",
        bool boolean => boolean ? "true" : "false",
        char character => $"'{Escape(character.ToString())}'",
        string text => Quote(text),
        IFormattable formatted => formatted.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "null"
    };

    private static string Quote(string text) => $"\"{Escape(text)}\"";
    private static string Escape(string text) => text.Replace("\\", "\\\\")
        .Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
}
