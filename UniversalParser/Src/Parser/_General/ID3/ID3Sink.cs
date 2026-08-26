using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.ID3
{
    internal readonly struct ID3Sink
    {
        private readonly List<(string K, string V)> _lines;
        private readonly string _prefix;
        public string Path => _prefix;
        public ID3Sink(
            List<(string K, string V)> lines,
            string? prefix = null)
        {
            _lines = lines ?? throw new ArgumentNullException(nameof(lines));
            _prefix = prefix ?? string.Empty;
        }

        public List<(string K, string V)> Lines => _lines;

        public ID3Sink Key(string name)
        {
            return Scope(name);
        }

        public ID3Sink Scope(string name)
        {
            string path = Join(_prefix, name);
            return new ID3Sink(_lines, path);
        }

        public void Text(string key, string? value)
        {
            Add(key, value ?? string.Empty);
        }

        public void Number(string key, int value)
        {
            Add(key, value.ToString());
        }

        public void Number(string key, long value)
        {
            Add(key, value.ToString());
        }

        public void Verbatim(string key, string? value)
        {
            Add(key, value ?? string.Empty);
        }

        public void Payload(
            string key,
            ReadOnlySpan<byte> payload,
            string type = "payload")
        {
            Add(key, ID3Format.Payload(type, payload.Length));
        }

        private void Add(string key, string value)
        {
            _lines.Add((Join(_prefix, key), value));
        }

        private static string Join(string left, string right)
        {
            if (string.IsNullOrEmpty(left))
                return right;

            if (string.IsNullOrEmpty(right))
                return left;

            return left + "." + right;
        }
    }
}