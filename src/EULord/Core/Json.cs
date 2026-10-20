// Minimal, order-preserving JSON reader/writer.
//
// Why not a library: EULord ships as a single dependency-free exe compiled by the
// in-box csc.exe, so it cannot reference Newtonsoft. Why not JavaScriptSerializer:
// it loses member order and rewrites numbers. Rewriting a Windows system file
// demands an exact round trip, so unknown keys, key order and number spelling are
// all preserved verbatim here.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace EULord.Core
{
    internal enum JsonKind { Object, Array, String, Number, Boolean, Null }

    internal abstract class JsonValue
    {
        public abstract JsonKind Kind { get; }

        public static JsonValue Parse(string text)
        {
            var p = new JsonParser(text);
            JsonValue v = p.ParseValue();
            p.SkipWhitespace();
            if (!p.AtEnd) throw new JsonException("Trailing content at offset " + p.Offset);
            return v;
        }

        public string ToJson() { var sb = new StringBuilder(); Write(sb, 0); return sb.ToString(); }

        internal abstract void Write(StringBuilder sb, int indent);

        internal static void Indent(StringBuilder sb, int level) { sb.Append(' ', level * 2); }
    }

    internal sealed class JsonException : Exception
    {
        public JsonException(string message) : base(message) { }
    }

    /// <summary>A JSON object that remembers the order its keys were written in.</summary>
    internal sealed class JsonObject : JsonValue
    {
        private readonly List<string> _keys = new List<string>();
        private readonly Dictionary<string, JsonValue> _values =
            new Dictionary<string, JsonValue>(StringComparer.Ordinal);

        public override JsonKind Kind { get { return JsonKind.Object; } }
        public IList<string> Keys { get { return _keys; } }
        public int Count { get { return _keys.Count; } }

        public bool Has(string key) { return _values.ContainsKey(key); }

        public JsonValue this[string key]
        {
            get { JsonValue v; return _values.TryGetValue(key, out v) ? v : null; }
            set
            {
                if (!_values.ContainsKey(key)) _keys.Add(key);
                _values[key] = value;
            }
        }

        public void Remove(string key)
        {
            if (_values.Remove(key)) _keys.Remove(key);
        }

        public string GetString(string key)
        {
            var s = this[key] as JsonString;
            return s == null ? null : s.Value;
        }

        public JsonObject GetObject(string key) { return this[key] as JsonObject; }
        public JsonArray GetArray(string key) { return this[key] as JsonArray; }

        public bool GetBool(string key, bool fallback)
        {
            var b = this[key] as JsonBool;
            return b == null ? fallback : b.Value;
        }

        public int GetInt(string key, int fallback)
        {
            var n = this[key] as JsonNumber;
            int v;
            if (n != null && int.TryParse(n.Raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v;
            return fallback;
        }

        internal override void Write(StringBuilder sb, int indent)
        {
            if (_keys.Count == 0) { sb.Append("{}"); return; }
            sb.Append("{\n");
            for (int i = 0; i < _keys.Count; i++)
            {
                Indent(sb, indent + 1);
                JsonString.WriteQuoted(sb, _keys[i]);
                sb.Append(": ");
                _values[_keys[i]].Write(sb, indent + 1);
                if (i < _keys.Count - 1) sb.Append(',');
                sb.Append('\n');
            }
            Indent(sb, indent);
            sb.Append('}');
        }
    }

    internal sealed class JsonArray : JsonValue
    {
        private readonly List<JsonValue> _items = new List<JsonValue>();

        public override JsonKind Kind { get { return JsonKind.Array; } }
        public IList<JsonValue> Items { get { return _items; } }
        public int Count { get { return _items.Count; } }
        public JsonValue this[int i] { get { return _items[i]; } }

        public void Add(JsonValue v) { _items.Add(v); }

        public List<string> ToStringList()
        {
            var list = new List<string>(_items.Count);
            foreach (JsonValue v in _items)
            {
                var s = v as JsonString;
                if (s != null) list.Add(s.Value);
            }
            return list;
        }

        public static JsonArray Of(IEnumerable<string> values)
        {
            var a = new JsonArray();
            foreach (string s in values) a.Add(new JsonString(s));
            return a;
        }

        private bool AllStrings()
        {
            foreach (JsonValue v in _items) if (!(v is JsonString)) return false;
            return _items.Count > 0;
        }

        internal override void Write(StringBuilder sb, int indent)
        {
            if (_items.Count == 0) { sb.Append("[]"); return; }

            // Mirror the system file's house style: arrays of plain strings stay on
            // one line, everything else breaks.
            if (AllStrings())
            {
                sb.Append('[');
                for (int i = 0; i < _items.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    _items[i].Write(sb, indent);
                }
                sb.Append(']');
                return;
            }

            sb.Append("[\n");
            for (int i = 0; i < _items.Count; i++)
            {
                Indent(sb, indent + 1);
                _items[i].Write(sb, indent + 1);
                if (i < _items.Count - 1) sb.Append(',');
                sb.Append('\n');
            }
            Indent(sb, indent);
            sb.Append(']');
        }
    }

    internal sealed class JsonString : JsonValue
    {
        public readonly string Value;
        public JsonString(string value) { Value = value ?? string.Empty; }
        public override JsonKind Kind { get { return JsonKind.String; } }
        internal override void Write(StringBuilder sb, int indent) { WriteQuoted(sb, Value); }

        internal static void WriteQuoted(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }

    /// <summary>Numbers keep their original spelling so a round trip never reformats them.</summary>
    internal sealed class JsonNumber : JsonValue
    {
        public readonly string Raw;
        public JsonNumber(string raw) { Raw = raw; }
        public JsonNumber(int value) { Raw = value.ToString(CultureInfo.InvariantCulture); }
        public override JsonKind Kind { get { return JsonKind.Number; } }
        internal override void Write(StringBuilder sb, int indent) { sb.Append(Raw); }
    }

    internal sealed class JsonBool : JsonValue
    {
        public readonly bool Value;
        public JsonBool(bool value) { Value = value; }
        public override JsonKind Kind { get { return JsonKind.Boolean; } }
        internal override void Write(StringBuilder sb, int indent) { sb.Append(Value ? "true" : "false"); }
    }

    internal sealed class JsonNull : JsonValue
    {
        public static readonly JsonNull Instance = new JsonNull();
        private JsonNull() { }
        public override JsonKind Kind { get { return JsonKind.Null; } }
        internal override void Write(StringBuilder sb, int indent) { sb.Append("null"); }
    }

    internal sealed class JsonParser
    {
        private readonly string _s;
        private int _i;

        public JsonParser(string text) { _s = text ?? string.Empty; _i = 0; }

        public int Offset { get { return _i; } }
        public bool AtEnd { get { return _i >= _s.Length; } }

        public void SkipWhitespace()
        {
            while (_i < _s.Length)
            {
                char c = _s[_i];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r') _i++;
                else if (c == '﻿') _i++;   // tolerate a BOM on input; never write one
                else break;
            }
        }

        public JsonValue ParseValue()
        {
            SkipWhitespace();
            if (AtEnd) throw new JsonException("Unexpected end of input");
            char c = _s[_i];
            switch (c)
            {
                case '{': return ParseObject();
                case '[': return ParseArray();
                case '"': return new JsonString(ParseString());
                case 't': Expect("true"); return new JsonBool(true);
                case 'f': Expect("false"); return new JsonBool(false);
                case 'n': Expect("null"); return JsonNull.Instance;
                default: return ParseNumber();
            }
        }

        private void Expect(string literal)
        {
            if (_i + literal.Length > _s.Length || string.CompareOrdinal(_s, _i, literal, 0, literal.Length) != 0)
                throw new JsonException("Expected '" + literal + "' at offset " + _i);
            _i += literal.Length;
        }

        private JsonObject ParseObject()
        {
            var o = new JsonObject();
            _i++;                               // '{'
            SkipWhitespace();
            if (!AtEnd && _s[_i] == '}') { _i++; return o; }
            while (true)
            {
                SkipWhitespace();
                if (AtEnd || _s[_i] != '"') throw new JsonException("Expected a key at offset " + _i);
                string key = ParseString();
                SkipWhitespace();
                if (AtEnd || _s[_i] != ':') throw new JsonException("Expected ':' at offset " + _i);
                _i++;
                o[key] = ParseValue();
                SkipWhitespace();
                if (AtEnd) throw new JsonException("Unterminated object");
                if (_s[_i] == ',') { _i++; continue; }
                if (_s[_i] == '}') { _i++; return o; }
                throw new JsonException("Expected ',' or '}' at offset " + _i);
            }
        }

        private JsonArray ParseArray()
        {
            var a = new JsonArray();
            _i++;                               // '['
            SkipWhitespace();
            if (!AtEnd && _s[_i] == ']') { _i++; return a; }
            while (true)
            {
                a.Add(ParseValue());
                SkipWhitespace();
                if (AtEnd) throw new JsonException("Unterminated array");
                if (_s[_i] == ',') { _i++; continue; }
                if (_s[_i] == ']') { _i++; return a; }
                throw new JsonException("Expected ',' or ']' at offset " + _i);
            }
        }

        private string ParseString()
        {
            _i++;                               // opening quote
            var sb = new StringBuilder();
            while (true)
            {
                if (AtEnd) throw new JsonException("Unterminated string");
                char c = _s[_i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (AtEnd) throw new JsonException("Unterminated escape");
                char e = _s[_i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (_i + 4 > _s.Length) throw new JsonException("Truncated \\u escape");
                        sb.Append((char)int.Parse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        _i += 4;
                        break;
                    default: throw new JsonException("Unknown escape '\\" + e + "' at offset " + (_i - 1));
                }
            }
        }

        private JsonNumber ParseNumber()
        {
            int start = _i;
            if (!AtEnd && (_s[_i] == '-' || _s[_i] == '+')) _i++;
            while (!AtEnd)
            {
                char c = _s[_i];
                if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '-' || c == '+') _i++;
                else break;
            }
            if (_i == start) throw new JsonException("Expected a value at offset " + start);
            string raw = _s.Substring(start, _i - start);
            double probe;
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out probe))
                throw new JsonException("Malformed number '" + raw + "' at offset " + start);
            return new JsonNumber(raw);
        }
    }
}
