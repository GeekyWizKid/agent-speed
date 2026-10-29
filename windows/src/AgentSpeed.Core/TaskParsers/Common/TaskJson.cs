using System.Text;
using System.Text.Json;

namespace AgentSpeed.Core.TaskParsers;

/// <summary>
/// JSON as TASK-PARSERS 1 reads it: RFC 8259 parsing (no NaN or Infinity; the last duplicate key wins, which is what
/// <see cref="JsonElement.TryGetProperty(string, out JsonElement)"/> returns), <i>the input as an object</i>, <i>the
/// leading JSON object</i> of a text, JSON integers, and strings read as scalar values. A value of an unexpected kind
/// (including <see cref="JsonValueKind.Undefined"/>, an absent value) reads as absent; nothing here throws.
/// </summary>
internal static class TaskJson
{
    /// <summary>Deeper documents fail to parse, as they do in the reference's parser (the depth is implementation-defined).</summary>
    public const int MaxDepth = 512;

    private static readonly JsonReaderOptions ReaderOptions = new() { MaxDepth = MaxDepth };

    /// <summary>JSON whitespace: space, tab, CR, LF.</summary>
    public static bool IsJsonWs(char c) => c is ' ' or '\t' or '\r' or '\n';

    /// <summary>The first character after JSON whitespace, or <c>'\0'</c> when there is none.</summary>
    public static char FirstNonWs(string text)
    {
        foreach (var c in text)
        {
            if (!IsJsonWs(c))
            {
                return c;
            }
        }

        return '\0';
    }

    /// <summary>A whole JSON document, or null when it does not parse.</summary>
    public static JsonElement? Parse(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        try
        {
            var reader = new Utf8JsonReader(bytes, ReaderOptions);
            var value = JsonElement.ParseValue(ref reader);
            return reader.Read() ? null : value;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The leading JSON object of a text: the first value after JSON whitespace, when it is an object.</summary>
    public static JsonElement? LeadingObject(string text)
    {
        if (FirstNonWs(text) != '{')
        {
            return null;
        }

        var bytes = Encoding.UTF8.GetBytes(text);
        try
        {
            var reader = new Utf8JsonReader(bytes, ReaderOptions);
            var value = JsonElement.ParseValue(ref reader);
            return value.ValueKind == JsonValueKind.Object ? value : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// <i>The input as an object</i>: an object as is; a string whose first character after JSON whitespace is
    /// <c>{</c> and that parses as a JSON object; otherwise null.
    /// </summary>
    public static JsonElement? AsObject(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            return value;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var text = String(value);
            if (text is not null && FirstNonWs(text) == '{' && Parse(text) is { ValueKind: JsonValueKind.Object } parsed)
            {
                return parsed;
            }
        }

        return null;
    }

    /// <summary>The member <paramref name="name"/> of an object, or <see cref="JsonValueKind.Undefined"/>.</summary>
    public static JsonElement Get(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var member) ? member : default;

    public static JsonElement Get(JsonElement? value, string name) => value is { } v ? Get(v, name) : default;

    public static bool Has(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out _);

    /// <summary>A string value read as scalar values, or null for any other kind.</summary>
    public static string? String(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        try
        {
            return value.GetString();
        }
        catch (InvalidOperationException)
        {
            return DecodeRawString(value.GetRawText());
        }
    }

    /// <summary>The string member <paramref name="name"/>, or null.</summary>
    public static string? GetString(JsonElement value, string name) => String(Get(value, name));

    public static string? GetString(JsonElement? value, string name) => String(Get(value, name));

    /// <summary>A JSON integer: a number without fraction or exponent in the signed 64-bit range.</summary>
    public static bool IsInt(JsonElement value, out long number)
    {
        number = 0;
        // TryGetInt64 refuses a fraction or an exponent, as the reference's integers do.
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out number);
    }

    public static long? Int(JsonElement value) => IsInt(value, out var number) ? number : null;

    /// <summary><c>== true</c>: the literal <c>true</c>.</summary>
    public static bool IsTrue(JsonElement value) => value.ValueKind == JsonValueKind.True;

    /// <summary>
    /// Decodes a JSON string literal whose escapes hold an unpaired surrogate, which <see cref="JsonElement.GetString"/>
    /// refuses: every unpaired surrogate reads as U+FFFD.
    /// </summary>
    private static string DecodeRawString(string raw)
    {
        var builder = new StringBuilder(raw.Length);
        var end = raw.Length - 1;
        for (var i = 1; i < end; i++)
        {
            var c = raw[i];
            if (c != '\\' || i + 1 >= end)
            {
                builder.Append(c);
                continue;
            }

            var e = raw[++i];
            switch (e)
            {
                case 'n': builder.Append('\n'); break;
                case 't': builder.Append('\t'); break;
                case 'r': builder.Append('\r'); break;
                case 'b': builder.Append('\b'); break;
                case 'f': builder.Append('\f'); break;
                case 'u' when i + 4 < end && int.TryParse(raw.AsSpan(i + 1, 4), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var unit):
                    builder.Append((char)unit);
                    i += 4;
                    break;
                default: builder.Append(e); break;
            }
        }

        return TaskText.Scalars(builder.ToString());
    }
}
