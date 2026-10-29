using System.Buffers;
using System.Globalization;
using System.Text.Json;

namespace AgentSpeed.Core.TaskParsers;

/// <summary>
/// A literal value of a code-mode script (TASK-PARSERS 4.4 <i>Literals</i>), written out as JSON the way the reference's
/// value would be: integers of any size, doubles as <c>float(raw)</c>, objects in first-seen key order with the last
/// value of a repeated key.
/// </summary>
internal abstract class JsLiteral
{
    /// <summary>Containers nested deeper than this are not literals (the reference exhausts its recursion instead).</summary>
    public const int MaxDepth = TaskJson.MaxDepth;

    public static readonly JsLiteral True = new Constant(JsonValueKind.True);
    public static readonly JsLiteral False = new Constant(JsonValueKind.False);
    public static readonly JsLiteral Null = new Constant(JsonValueKind.Null);

    private JsonElement? element;

    /// <summary>0 for a scalar; 1 + the deepest member for an array or an object.</summary>
    public virtual int Depth => 0;

    public static JsLiteral String(string value) => new Text(value);

    /// <summary>A valued number token (<c>JS_NUMBER</c>): an integer without <c>.</c> or exponent, else a double.</summary>
    public static JsLiteral Number(string raw, bool negative)
    {
        if (raw.AsSpan().IndexOfAny('.', 'e', 'E') < 0)
        {
            var digits = raw.TrimStart('0');
            return new Integer(digits.Length == 0 ? "0" : negative ? "-" + digits : digits);
        }

        var value = double.Parse(raw, NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture);
        return new Real(negative ? -value : value);
    }

    /// <summary>The value as JSON; a literal bound to a name and passed to several calls is written once.</summary>
    public JsonElement ToElement() => element ??= Build();

    private JsonElement Build()
    {
        var buffer = new ArrayBufferWriter<byte>(64);
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { MaxDepth = MaxDepth + 2 }))
        {
            Write(writer);
        }

        var reader = new Utf8JsonReader(buffer.WrittenSpan, new JsonReaderOptions { MaxDepth = MaxDepth + 2 });
        return JsonElement.ParseValue(ref reader);
    }

    public abstract void Write(Utf8JsonWriter writer);

    private sealed class Constant(JsonValueKind kind) : JsLiteral
    {
        public override void Write(Utf8JsonWriter writer)
        {
            switch (kind)
            {
                case JsonValueKind.True:
                    writer.WriteBooleanValue(true);
                    break;
                case JsonValueKind.False:
                    writer.WriteBooleanValue(false);
                    break;
                default:
                    writer.WriteNullValue();
                    break;
            }
        }
    }

    private sealed class Text(string value) : JsLiteral
    {
        public override void Write(Utf8JsonWriter writer) => writer.WriteStringValue(value);
    }

    /// <summary>Decimal digits without leading zeros, with a sign when negative; written raw so no size is lost.</summary>
    private sealed class Integer(string digits) : JsLiteral
    {
        public override void Write(Utf8JsonWriter writer) => writer.WriteRawValue(digits);
    }

    /// <summary>
    /// A double, written with a fraction or an exponent so that it never reads back as a JSON integer (Python's
    /// <c>7.0</c> is not <c>is_int</c>). Beyond the range it is infinite, as <c>float(raw)</c> is; JSON has no
    /// infinity, so it is written as a number every reader takes for one.
    /// </summary>
    private sealed class Real(double value) : JsLiteral
    {
        public override void Write(Utf8JsonWriter writer)
        {
            if (!double.IsFinite(value))
            {
                writer.WriteRawValue(value > 0 ? "1e999" : "-1e999");
                return;
            }

            var text = value.ToString("R", CultureInfo.InvariantCulture);
            writer.WriteRawValue(text.AsSpan().IndexOfAny('.', 'E', 'e') < 0 ? text + ".0" : text);
        }
    }
}

/// <summary>An array literal.</summary>
internal sealed class JsArrayLiteral(List<JsLiteral> items) : JsLiteral
{
    public override int Depth { get; } = 1 + items.Aggregate(0, (depth, item) => Math.Max(depth, item.Depth));

    public override void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartArray();
        foreach (var item in items)
        {
            item.Write(writer);
        }

        writer.WriteEndArray();
    }
}

/// <summary>An object literal: members in first-seen key order, a repeated key keeping its last value.</summary>
internal sealed class JsObjectLiteral : JsLiteral
{
    private readonly List<KeyValuePair<string, JsLiteral>> members = [];
    private readonly Dictionary<string, int> index = new(StringComparer.Ordinal);
    private int depth;

    /// <summary>Read once the object is complete.</summary>
    public override int Depth => depth > 0 ? depth : depth = 1 + members.Aggregate(0, (d, member) => Math.Max(d, member.Value.Depth));

    public void Set(string key, JsLiteral value)
    {
        if (index.TryGetValue(key, out var at))
        {
            members[at] = new KeyValuePair<string, JsLiteral>(key, value);
        }
        else
        {
            index[key] = members.Count;
            members.Add(new KeyValuePair<string, JsLiteral>(key, value));
        }
    }

    public override void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        foreach (var (key, value) in members)
        {
            writer.WritePropertyName(key);
            value.Write(writer);
        }

        writer.WriteEndObject();
    }
}
