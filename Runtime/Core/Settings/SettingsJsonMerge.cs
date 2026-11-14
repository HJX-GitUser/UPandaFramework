using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace UPandaGF
{
    /// <summary>
    /// 设置 JSON 的合并工具：保存时把已知字段覆盖到原始 JSON 上，保留未来版本/其它模块写入的未知字段。
    /// <para>只做对象递归合并；数组按“已知值整体替换”。解析失败时退回 known JSON，绝不阻断保存。</para>
    /// <para>不依赖 Newtonsoft/System.Text.Json，适配 Unity 2021 的 JsonUtility 环境。</para>
    /// </summary>
    public static class SettingsJsonMerge
    {
        public static string Merge(string originalJson, string knownJson)
        {
            if (string.IsNullOrWhiteSpace(originalJson)) return knownJson;
            if (string.IsNullOrWhiteSpace(knownJson)) return originalJson;
            try
            {
                JsonNode original = new JsonParser(originalJson).Parse();
                JsonNode known = new JsonParser(knownJson).Parse();
                if (original == null || known == null) return knownJson;
                JsonNode merged = MergeNode(original, known);
                return JsonWriter.Write(merged);
            }
            catch (Exception)
            {
                return knownJson;
            }
        }

        private static JsonNode MergeNode(JsonNode original, JsonNode known)
        {
            if (original.Kind == JsonKind.Array && known.Kind == JsonKind.Array)
                return MergeArray(original, known);
            if (original.Kind != JsonKind.Object || known.Kind != JsonKind.Object) return known;
            foreach (KeyValuePair<string, JsonNode> pair in known.Properties)
            {
                JsonNode existing;
                original.Properties[pair.Key] = original.Properties.TryGetValue(pair.Key, out existing)
                    ? MergeNode(existing, pair.Value)
                    : pair.Value;
            }
            return original;
        }

        /// <summary>
        /// 对象数组按 key/id/name 合并：保留仍然存在的元素的未知字段；已知数组删除的元素不会复活，
        /// 新元素直接追加。没有稳定 key 的数组按 known 整体替换。
        /// </summary>
        private static JsonNode MergeArray(JsonNode original, JsonNode known)
        {
            string keyField = FindKeyField(known);
            if (string.IsNullOrEmpty(keyField)) return known;

            var originalByKey = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
            for (int i = 0; i < original.Items.Count; i++)
            {
                JsonNode item = original.Items[i];
                string key;
                if (TryGetKey(item, keyField, out key) && !originalByKey.ContainsKey(key))
                    originalByKey[key] = item;
            }

            JsonNode result = JsonNode.Array();
            for (int i = 0; i < known.Items.Count; i++)
            {
                JsonNode item = known.Items[i];
                string key;
                JsonNode existing;
                if (TryGetKey(item, keyField, out key) && originalByKey.TryGetValue(key, out existing))
                    result.Items.Add(MergeNode(existing, item));
                else
                    result.Items.Add(item);
            }
            return result;
        }

        private static string FindKeyField(JsonNode array)
        {
            for (int i = 0; i < array.Items.Count; i++)
            {
                JsonNode item = array.Items[i];
                if (item.Kind != JsonKind.Object) continue;
                if (item.Properties.ContainsKey("key")) return "key";
                if (item.Properties.ContainsKey("id")) return "id";
                if (item.Properties.ContainsKey("name")) return "name";
            }
            return null;
        }

        private static bool TryGetKey(JsonNode item, string keyField, out string key)
        {
            key = null;
            if (item == null || item.Kind != JsonKind.Object) return false;
            JsonNode node;
            if (!item.Properties.TryGetValue(keyField, out node) || node.Kind != JsonKind.String) return false;
            key = node.Value;
            return !string.IsNullOrEmpty(key);
        }

        private enum JsonKind { Object, Array, String, Number, Bool, Null }

        private sealed class JsonNode
        {
            public JsonKind Kind;
            public readonly Dictionary<string, JsonNode> Properties = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
            public readonly List<JsonNode> Items = new List<JsonNode>();
            public string Value;
            public bool BoolValue;

            public static JsonNode Object() { return new JsonNode { Kind = JsonKind.Object }; }
            public static JsonNode Array() { return new JsonNode { Kind = JsonKind.Array }; }
            public static JsonNode String(string value) { return new JsonNode { Kind = JsonKind.String, Value = value }; }
            public static JsonNode Number(string value) { return new JsonNode { Kind = JsonKind.Number, Value = value }; }
            public static JsonNode Boolean(bool value) { return new JsonNode { Kind = JsonKind.Bool, BoolValue = value }; }
            public static JsonNode Null() { return new JsonNode { Kind = JsonKind.Null }; }
        }

        private sealed class JsonParser
        {
            private readonly string text;
            private int index;

            public JsonParser(string text) { this.text = text ?? string.Empty; }

            public JsonNode Parse()
            {
                SkipWhitespace();
                JsonNode value = ParseValue();
                SkipWhitespace();
                return value;
            }

            private JsonNode ParseValue()
            {
                SkipWhitespace();
                if (index >= text.Length) throw new FormatException("JSON 提前结束");
                char c = text[index];
                if (c == '{') return ParseObject();
                if (c == '[') return ParseArray();
                if (c == '"') return JsonNode.String(ParseString());
                if (c == 't') { Expect("true"); return JsonNode.Boolean(true); }
                if (c == 'f') { Expect("false"); return JsonNode.Boolean(false); }
                if (c == 'n') { Expect("null"); return JsonNode.Null(); }
                return JsonNode.Number(ParseNumber());
            }

            private JsonNode ParseObject()
            {
                JsonNode node = JsonNode.Object();
                index++; // {
                SkipWhitespace();
                if (Peek() == '}') { index++; return node; }
                while (true)
                {
                    SkipWhitespace();
                    string key = ParseString();
                    SkipWhitespace();
                    if (Peek() != ':') throw new FormatException("JSON 对象缺少冒号");
                    index++;
                    node.Properties[key] = ParseValue();
                    SkipWhitespace();
                    char next = Peek();
                    if (next == ',') { index++; continue; }
                    if (next == '}') { index++; return node; }
                    throw new FormatException("JSON 对象缺少逗号或右括号");
                }
            }

            private JsonNode ParseArray()
            {
                JsonNode node = JsonNode.Array();
                index++; // [
                SkipWhitespace();
                if (Peek() == ']') { index++; return node; }
                while (true)
                {
                    node.Items.Add(ParseValue());
                    SkipWhitespace();
                    char next = Peek();
                    if (next == ',') { index++; continue; }
                    if (next == ']') { index++; return node; }
                    throw new FormatException("JSON 数组缺少逗号或右括号");
                }
            }

            private string ParseString()
            {
                if (Peek() != '"') throw new FormatException("JSON 字符串缺少引号");
                index++;
                var builder = new StringBuilder();
                while (index < text.Length)
                {
                    char c = text[index++];
                    if (c == '"') return builder.ToString();
                    if (c != '\\') { builder.Append(c); continue; }
                    if (index >= text.Length) throw new FormatException("JSON 转义不完整");
                    char escape = text[index++];
                    switch (escape)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u':
                            if (index + 4 > text.Length) throw new FormatException("JSON Unicode 转义不完整");
                            string hex = text.Substring(index, 4);
                            index += 4;
                            builder.Append((char)int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            break;
                        default: throw new FormatException("未知 JSON 转义：" + escape);
                    }
                }
                throw new FormatException("JSON 字符串未闭合");
            }

            private string ParseNumber()
            {
                int start = index;
                while (index < text.Length)
                {
                    char c = text[index];
                    if (char.IsDigit(c) || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E') index++;
                    else break;
                }
                if (index == start) throw new FormatException("JSON 数字无效");
                return text.Substring(start, index - start);
            }

            private void Expect(string literal)
            {
                if (index + literal.Length > text.Length
                    || string.Compare(text, index, literal, 0, literal.Length, StringComparison.Ordinal) != 0)
                    throw new FormatException("JSON 字面量无效");
                index += literal.Length;
            }

            private void SkipWhitespace()
            {
                while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
            }

            private char Peek()
            {
                if (index >= text.Length) throw new FormatException("JSON 提前结束");
                return text[index];
            }
        }

        private static class JsonWriter
        {
            public static string Write(JsonNode node)
            {
                var builder = new StringBuilder();
                WriteNode(builder, node);
                return builder.ToString();
            }

            private static void WriteNode(StringBuilder builder, JsonNode node)
            {
                if (node == null) { builder.Append("null"); return; }
                switch (node.Kind)
                {
                    case JsonKind.Object:
                        builder.Append('{');
                        bool first = true;
                        foreach (KeyValuePair<string, JsonNode> pair in node.Properties)
                        {
                            if (!first) builder.Append(',');
                            first = false;
                            WriteString(builder, pair.Key);
                            builder.Append(':');
                            WriteNode(builder, pair.Value);
                        }
                        builder.Append('}');
                        break;
                    case JsonKind.Array:
                        builder.Append('[');
                        for (int i = 0; i < node.Items.Count; i++)
                        {
                            if (i > 0) builder.Append(',');
                            WriteNode(builder, node.Items[i]);
                        }
                        builder.Append(']');
                        break;
                    case JsonKind.String:
                        WriteString(builder, node.Value);
                        break;
                    case JsonKind.Number:
                        builder.Append(node.Value);
                        break;
                    case JsonKind.Bool:
                        builder.Append(node.BoolValue ? "true" : "false");
                        break;
                    default:
                        builder.Append("null");
                        break;
                }
            }

            private static void WriteString(StringBuilder builder, string value)
            {
                builder.Append('"');
                if (value != null)
                {
                    for (int i = 0; i < value.Length; i++)
                    {
                        char c = value[i];
                        switch (c)
                        {
                            case '"': builder.Append("\\\""); break;
                            case '\\': builder.Append("\\\\"); break;
                            case '\b': builder.Append("\\b"); break;
                            case '\f': builder.Append("\\f"); break;
                            case '\n': builder.Append("\\n"); break;
                            case '\r': builder.Append("\\r"); break;
                            case '\t': builder.Append("\\t"); break;
                            default:
                                if (c < 0x20) builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                                else builder.Append(c);
                                break;
                        }
                    }
                }
                builder.Append('"');
            }
        }
    }
}
