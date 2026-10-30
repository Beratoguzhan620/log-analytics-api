using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LogAnalytics.Core.Events;

/// <summary>
/// Renders a Serilog message template (<c>Order {OrderId} paid in {Elapsed:0.0} ms</c>) with the event's
/// properties, the way Serilog would show it. Formats and alignment are ignored; strings are quoted the way
/// Serilog quotes them unless the hole says <c>:l</c>.
/// </summary>
public static class MessageTemplate
{
    public static string Render(string template, JsonObject properties)
    {
        var builder = new StringBuilder(template.Length + 32);
        var i = 0;
        while (i < template.Length)
        {
            var c = template[i];
            if (c == '{' && i + 1 < template.Length && template[i + 1] == '{')
            {
                builder.Append('{');
                i += 2;
                continue;
            }

            if (c == '}' && i + 1 < template.Length && template[i + 1] == '}')
            {
                builder.Append('}');
                i += 2;
                continue;
            }

            if (c != '{')
            {
                builder.Append(c);
                i++;
                continue;
            }

            var close = template.IndexOf('}', i + 1);
            if (close < 0)
            {
                builder.Append(template, i, template.Length - i);
                break;
            }

            var hole = template[(i + 1)..close];
            var name = hole.TrimStart('@', '$');
            var cut = name.IndexOfAny([':', ',']);
            var format = cut >= 0 && name[cut] == ':' ? name[(cut + 1)..] : null;
            if (cut >= 0)
            {
                name = name[..cut];
            }

            if (properties.TryGetPropertyValue(name, out var value))
            {
                builder.Append(Format(value, literal: format == "l"));
            }
            else
            {
                builder.Append('{').Append(hole).Append('}');
            }

            i = close + 1;
        }

        return builder.ToString();
    }

    private static string Format(JsonNode? value, bool literal) => value switch
    {
        null => "null",
        JsonValue v when v.GetValueKind() == JsonValueKind.String => literal ? v.GetValue<string>() : $"\"{v.GetValue<string>()}\"",
        JsonValue v => v.ToJsonString(),
        _ => value.ToJsonString(),
    };
}
