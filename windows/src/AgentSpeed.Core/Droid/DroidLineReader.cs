using System.Text.Json;
using AgentSpeed.Core.Parsing;

namespace AgentSpeed.Core.Droid;

/// <summary>
/// Pure, thread-safe extraction of <see cref="DroidLine"/> (METRICS 4.16). Duplicate keys resolve like Python's
/// <c>json.loads</c>: the last occurrence wins.
/// </summary>
internal static class DroidLineReader
{
    public static DroidLine Read(ReadOnlySpan<byte> line, long offset, int length)
    {
        var result = Read(line);
        result.Offset = offset;
        result.Length = length;
        return result;
    }

    public static DroidLine Read(ReadOnlySpan<byte> line)
    {
        if (JsonScan.IsBlank(line))
        {
            return new DroidLine { Status = LineStatus.Blank };
        }

        // Like json.loads on the line's bytes: invalid UTF-8 anywhere makes it malformed; a leading BOM is accepted.
        if (!System.Text.Unicode.Utf8.IsValid(line))
        {
            return Malformed();
        }

        if (line.StartsWith("﻿"u8))
        {
            line = line[3..];
        }

        var result = default(DroidLine);
        var type = DroidLineKind.Other;
        var hasMessage = false;
        try
        {
            var reader = new Utf8JsonReader(line, JsonScan.Options);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return Malformed();
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("type"u8))
                {
                    reader.Read();
                    type = reader.TokenType != JsonTokenType.String ? DroidLineKind.Other
                        : reader.ValueTextEquals("message"u8) ? DroidLineKind.Message
                        : reader.ValueTextEquals("session_start"u8) ? DroidLineKind.SessionStart
                        : reader.ValueTextEquals("compaction_state"u8) ? DroidLineKind.Compaction
                        : reader.ValueTextEquals("agent_turn_outcome"u8) ? DroidLineKind.TurnOutcome
                        : DroidLineKind.Other;
                    JsonScan.SkipValue(ref reader);
                }
                else if (reader.ValueTextEquals("id"u8))
                {
                    reader.Read();
                    result.Id = JsonScan.ReadString(ref reader);
                }
                else if (reader.ValueTextEquals("timestamp"u8))
                {
                    reader.Read();
                    result.TimestampMs = JsonScan.TryReadTimestamp(ref reader, out var ms) ? ms : null;
                }
                else if (reader.ValueTextEquals("message"u8))
                {
                    reader.Read();
                    hasMessage = reader.TokenType == JsonTokenType.StartObject;
                    ClearMessage(ref result);
                    if (hasMessage)
                    {
                        ReadMessage(ref reader, ref result);
                    }
                    else
                    {
                        JsonScan.SkipValue(ref reader);
                    }
                }
                else if (reader.ValueTextEquals("cwd"u8))
                {
                    reader.Read();
                    result.Cwd = JsonScan.ReadToken(ref reader);
                }
                else if (reader.ValueTextEquals("parent"u8))
                {
                    reader.Read();
                    result.Parent = SessionId(JsonScan.ReadToken(ref reader));
                }
                else if (reader.ValueTextEquals("callingSessionId"u8))
                {
                    reader.Read();
                    result.CallingSessionId = SessionId(JsonScan.ReadToken(ref reader));
                }
                else
                {
                    reader.Read();
                    JsonScan.SkipValue(ref reader);
                }
            }

            if (reader.TokenType != JsonTokenType.EndObject || reader.Read())
            {
                return Malformed();
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            return Malformed();
        }

        result.Kind = type == DroidLineKind.Message && !hasMessage ? DroidLineKind.Other : type;
        return result;
    }

    private static DroidLine Malformed() => new() { Status = LineStatus.Malformed };

    private static string? SessionId(string? value) => value is not null && DroidPaths.IsSessionId(value) ? value : null;

    private static void ClearMessage(ref DroidLine result)
    {
        result.Role = DroidRole.Other;
        result.UserOnly = false;
        result.ModelId = null;
        result.Effort = null;
        result.HasToolUse = false;
    }

    private static void ReadMessage(ref Utf8JsonReader reader, ref DroidLine result)
    {
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("role"u8))
            {
                reader.Read();
                result.Role = reader.TokenType != JsonTokenType.String ? DroidRole.Other
                    : reader.ValueTextEquals("user"u8) ? DroidRole.User
                    : reader.ValueTextEquals("assistant"u8) ? DroidRole.Assistant
                    : DroidRole.Other;
                JsonScan.SkipValue(ref reader);
            }
            else if (reader.ValueTextEquals("visibility"u8))
            {
                reader.Read();
                result.UserOnly = reader.TokenType == JsonTokenType.String && reader.ValueTextEquals("user_only"u8);
                JsonScan.SkipValue(ref reader);
            }
            else if (reader.ValueTextEquals("modelId"u8))
            {
                reader.Read();
                result.ModelId = JsonScan.ReadToken(ref reader)?.Trim() is { Length: > 0 } model ? model : null;
            }
            else if (reader.ValueTextEquals("reasoningEffort"u8))
            {
                reader.Read();
                result.Effort = JsonScan.ReadToken(ref reader);
            }
            else if (reader.ValueTextEquals("content"u8))
            {
                reader.Read();
                result.HasToolUse = reader.TokenType == JsonTokenType.StartArray && HasToolUse(ref reader);
                JsonScan.SkipValue(ref reader);
            }
            else
            {
                reader.Read();
                JsonScan.SkipValue(ref reader);
            }
        }
    }

    // Leaves the reader on the array's EndArray.
    private static bool HasToolUse(ref Utf8JsonReader reader)
    {
        var found = false;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                JsonScan.SkipValue(ref reader);
                continue;
            }

            var toolUse = false;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var isType = reader.ValueTextEquals("type"u8);
                reader.Read();
                if (isType)
                {
                    toolUse = reader.TokenType == JsonTokenType.String && reader.ValueTextEquals("tool_use"u8);
                }

                JsonScan.SkipValue(ref reader);
            }

            found |= toolUse;
        }

        return found;
    }
}
