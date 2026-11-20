using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Hotfix;

public static class Extensions
{
    public static readonly JsonSerializerOptions RelaxedJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    extension(string)
    {
        public static string operator /(string left, string right)
        {
            bool leftEndsWithSlash = left.EndsWith('/'),
                rightStartsWithSlash = right.StartsWith('/');

            if (leftEndsWithSlash ^ rightStartsWithSlash) return left + right;
            if (leftEndsWithSlash && rightStartsWithSlash) return left[..^1] + right;
            // false, false
            return left + '/' + right;
        }
    }

    extension(string json)
    {
        public JsonNode? TryParseJson()
        {
            try
            {
                return JsonNode.Parse(json);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    extension(JsonNode? node)
    {
        public string? GetString(string name) =>
            (node as JsonObject)?[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

        public string GetString(string name, string fallback) => node.GetString(name) ?? fallback;

        public int? GetInt(string name)
        {
            if ((node as JsonObject)?[name] is not JsonValue value)
                return null;
            if (value.TryGetValue<int>(out var number))
                return number;
            // 숫자를 문자열로 보내는 경우
            return value.TryGetValue<string>(out var text) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out number) ? number : null;
        }

        public int GetInt(string name, int fallback) => node.GetInt(name) ?? fallback;

        public bool GetBool(string name) =>
            (node as JsonObject)?[name] is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;

        public JsonArray GetArray(string name) => (node as JsonObject)?[name] as JsonArray ?? [];
    }

    extension<T>(IEnumerable<T> source)
    {
        public JsonArray ToJsonArray(Func<T, JsonNode?> selector) => new(source.Select(selector).ToArray());
    }

}