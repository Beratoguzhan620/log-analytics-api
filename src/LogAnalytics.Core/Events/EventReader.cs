using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LogAnalytics.Core.Events;

/// <summary>
/// Reads the two accepted formats. Each event is read on its own, so one bad line costs that line, not the batch.
/// <list type="bullet">
/// <item>Serilog's compact JSON (CLEF), one object per line: <c>@t</c>, <c>@m</c> or <c>@mt</c>, <c>@l</c>,
/// <c>@x</c>, <c>@tr</c>, <c>@sp</c>, and everything else as properties.</item>
/// <item>A JSON array of <c>{ timestamp, level, message, messageTemplate, exception, service, traceId, spanId, properties }</c>.</item>
/// </list>
/// </summary>
public static class EventReader
{
    public static (List<(int Index, IncomingEvent Event)> Events, List<RejectedEvent> Rejected) ReadClef(string body)
    {
        var events = new List<(int, IncomingEvent)>();
        var rejected = new List<RejectedEvent>();
        var index = 0;
        foreach (var raw in body.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            try
            {
                if (JsonNode.Parse(line) is not JsonObject json)
                {
                    rejected.Add(new RejectedEvent(index, "A line is one JSON object."));
                }
                else if (FromClef(json, out var reason) is { } parsed)
                {
                    events.Add((index, parsed));
                }
                else
                {
                    rejected.Add(new RejectedEvent(index, reason!));
                }
            }
            catch (JsonException)
            {
                rejected.Add(new RejectedEvent(index, "The line is not valid JSON."));
            }

            index++;
        }

        return (events, rejected);
    }

    public static (List<(int Index, IncomingEvent Event)> Events, List<RejectedEvent> Rejected) ReadJsonArray(string body)
    {
        var events = new List<(int, IncomingEvent)>();
        var rejected = new List<RejectedEvent>();
        JsonArray array;
        try
        {
            array = JsonNode.Parse(body) as JsonArray ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "The body is a JSON array of events.");
        }

        for (var index = 0; index < array.Count; index++)
        {
            if (array[index] is JsonObject json && FromJson(json, out var reason) is { } parsed)
            {
                events.Add((index, parsed));
            }
            else
            {
                rejected.Add(new RejectedEvent(index, array[index] is JsonObject ? reason! : "An event is a JSON object."));
            }
        }

        return (events, rejected);
    }

    private static IncomingEvent? FromClef(JsonObject json, out string? reason)
    {
        reason = null;
        if (!TryTimestamp(json["@t"], out var timestamp))
        {
            reason = "@t (the time) is missing or not an ISO 8601 date.";
            return null;
        }

        var level = Levels.Parse(Text(json["@l"]));
        if (level is null)
        {
            reason = "@l is not a known level.";
            return null;
        }

        var properties = new JsonObject();
        foreach (var (key, value) in json)
        {
            if (key.StartsWith("@@", StringComparison.Ordinal))
            {
                properties[key[1..]] = value?.DeepClone();
            }
            else if (!key.StartsWith('@'))
            {
                properties[key] = value?.DeepClone();
            }
        }

        var template = Text(json["@mt"]);
        var message = Text(json["@m"]) ?? (template is null ? null : MessageTemplate.Render(template, properties));
        if (message is null)
        {
            reason = "Either @m (the message) or @mt (its template) is needed.";
            return null;
        }

        return new IncomingEvent(timestamp, level.Value, message, template, Text(json["@x"]),
            ServiceOf(properties), Text(json["@tr"]), Text(json["@sp"]), properties);
    }

    private static IncomingEvent? FromJson(JsonObject json, out string? reason)
    {
        reason = null;
        if (!TryTimestamp(json["timestamp"], out var timestamp))
        {
            reason = "timestamp is missing or not an ISO 8601 date.";
            return null;
        }

        var level = Levels.Parse(Text(json["level"]));
        if (level is null)
        {
            reason = "level is not a known level.";
            return null;
        }

        var properties = json["properties"] switch
        {
            JsonObject o => (JsonObject)o.DeepClone(),
            null => new JsonObject(),
            _ => null,
        };
        if (properties is null)
        {
            reason = "properties is a JSON object.";
            return null;
        }

        var template = Text(json["messageTemplate"]);
        var message = Text(json["message"]) ?? (template is null ? null : MessageTemplate.Render(template, properties));
        if (message is null)
        {
            reason = "Either message or messageTemplate is needed.";
            return null;
        }

        return new IncomingEvent(timestamp, level.Value, message, template, Text(json["exception"]),
            Text(json["service"]) ?? ServiceOf(properties), Text(json["traceId"]), Text(json["spanId"]), properties);
    }

    /// <summary>Which application said it: the <c>Service</c> property, else <c>Application</c>, as Serilog setups usually name it.</summary>
    private static string? ServiceOf(JsonObject properties) =>
        Text(properties["Service"]) ?? Text(properties["Application"]);

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;

    private static bool TryTimestamp(JsonNode? node, out DateTimeOffset timestamp)
    {
        timestamp = default;
        return Text(node) is { } text
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out timestamp);
    }
}
