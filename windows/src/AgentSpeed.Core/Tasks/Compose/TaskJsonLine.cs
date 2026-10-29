using System.Buffers;
using System.Runtime.InteropServices;
using System.Text.Json;
using AgentSpeed.Core.TaskParsers;

namespace AgentSpeed.Core.Tasks;

/// <summary>
/// One log line parsed as a JSON document for the composer, over a pooled copy of its bytes that goes back to the pool
/// with the document. Elements must not outlive it (clone what is kept).
/// </summary>
internal readonly struct TaskJsonLine : IDisposable
{
    private static readonly JsonDocumentOptions Options = new() { MaxDepth = TaskJson.MaxDepth };

    private readonly byte[]? _buffer;
    private readonly JsonDocument? _document;

    private TaskJsonLine(byte[] buffer, JsonDocument document)
    {
        _buffer = buffer;
        _document = document;
    }

    public JsonElement Root => _document?.RootElement ?? default;

    public bool IsObject => _document is { RootElement.ValueKind: JsonValueKind.Object };

    /// <summary>The line as a document; <see cref="IsObject"/> is false when it does not parse as an object.</summary>
    public static TaskJsonLine Parse(ReadOnlySpan<byte> raw)
    {
        if (raw.StartsWith("﻿"u8))
        {
            raw = raw[3..];
        }

        var buffer = ArrayPool<byte>.Shared.Rent(Math.Max(1, raw.Length));
        raw.CopyTo(buffer);
        try
        {
            return new TaskJsonLine(buffer, JsonDocument.Parse(buffer.AsMemory(0, raw.Length), Options));
        }
        catch (JsonException)
        {
            ArrayPool<byte>.Shared.Return(buffer);
            return default;
        }
    }

    public void Dispose()
    {
        if (_document is not null)
        {
            _document.Dispose();
            ArrayPool<byte>.Shared.Return(_buffer!);
        }
    }
}

/// <summary>The input digest of METRICS 6.10 over a parsed value, and small JSON helpers of the composer.</summary>
internal static class TaskInputs
{
    /// <summary>The digest of an absent or null input: SHA-256 of the empty text.</summary>
    public static string Empty { get; } = TaskText.Sha16(ReadOnlySpan<byte>.Empty);

    /// <summary>
    /// A string input hashes as its text; an object or array (or any other value) as its JSON text exactly as the line
    /// stores it; an absent or null input as empty text.
    /// </summary>
    public static string Digest(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Undefined or JsonValueKind.Null:
                return Empty;
            case JsonValueKind.String:
                return TaskText.Sha16(TaskJson.String(value) ?? string.Empty);
            default:
                return TaskText.Sha16(JsonMarshal.GetRawUtf8Value(value));
        }
    }

    /// <summary>A JSON string value holding <paramref name="text"/> (a code-mode item read as a call's result).</summary>
    public static JsonElement StringElement(string text)
    {
        var buffer = new ArrayBufferWriter<byte>(text.Length + 16);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStringValue(text);
        }

        var reader = new Utf8JsonReader(buffer.WrittenSpan);
        return JsonElement.ParseValue(ref reader);
    }

    /// <summary>The member of an object, or <see cref="JsonValueKind.Undefined"/>.</summary>
    public static JsonElement Get(JsonElement value, string name) => TaskJson.Get(value, name);

    public static string? GetString(JsonElement value, string name) => TaskJson.GetString(value, name);

    /// <summary>The array items of a member, or none.</summary>
    public static IEnumerable<JsonElement> Blocks(JsonElement value, string name)
    {
        var member = Get(value, name);
        return member.ValueKind == JsonValueKind.Array ? member.EnumerateArray() : [];
    }

    public static bool IsType(JsonElement block, string type) =>
        block.ValueKind == JsonValueKind.Object && GetString(block, "type") == type;
}
