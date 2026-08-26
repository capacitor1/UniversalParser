using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace UniversalParser.Src.Parser.FLV.Chunks
{
    /// <summary>
    /// AMF0 parser used by FLV ScriptDataTagData.
    ///
    /// Parsed values are flattened directly into ParseResult.DataLines:
    ///
    /// Name
    /// Value.duration
    /// Value.keyframes.times[0]
    /// Value.keyframes.filepositions[0]
    ///
    /// Object properties and array elements are never collapsed into a
    /// formatted string.
    /// </summary>
    internal sealed class AMF0Parser
    {
        private const byte NumberMarker = 0x00;
        private const byte BooleanMarker = 0x01;
        private const byte StringMarker = 0x02;
        private const byte ObjectMarker = 0x03;
        private const byte MovieClipMarker = 0x04;
        private const byte NullMarker = 0x05;
        private const byte UndefinedMarker = 0x06;
        private const byte ReferenceMarker = 0x07;
        private const byte EcmaArrayMarker = 0x08;
        private const byte ObjectEndMarker = 0x09;
        private const byte StrictArrayMarker = 0x0A;
        private const byte DateMarker = 0x0B;
        private const byte LongStringMarker = 0x0C;
        private const byte UnsupportedMarker = 0x0D;
        private const byte RecordSetMarker = 0x0E;
        private const byte XmlDocumentMarker = 0x0F;
        private const byte TypedObjectMarker = 0x10;
        private const byte AvmPlusObjectMarker = 0x11;

        private readonly FLVReader _reader;
        private readonly int _maxDepth;

        public AMF0Parser(
            FLVReader reader,
            int maxDepth)
        {
            ArgumentNullException.ThrowIfNull(reader);

            _reader = reader;
            _maxDepth = Math.Max(1, maxDepth);
        }

        public long Position => _reader.Position;

        /// <summary>
        /// Reads one AMF0 value and appends its complete flattened
        /// representation to <paramref name="dataLines"/>.
        /// </summary>
        public void ReadValue(
            string path,
            List<(string K, string V)> dataLines)
        {
            ArgumentException.ThrowIfNullOrEmpty(path);
            ArgumentNullException.ThrowIfNull(dataLines);

            ReadValue(path, dataLines, depth: 0);
        }

        private void ReadValue(
            string path,
            List<(string K, string V)> dataLines,
            int depth)
        {
            if (depth >= _maxDepth)
            {
                throw new InvalidDataException(
                    $"Maximum AMF0 recursion depth {_maxDepth} exceeded at '{path}'.");
            }

            byte marker = _reader.ReadUInt8();

            switch (marker)
            {
                case NumberMarker:
                    AddNumber(path, _reader.ReadDoubleBE(), dataLines);
                    return;

                case BooleanMarker:
                    AddBoolean(path, _reader.ReadUInt8(), dataLines);
                    return;

                case StringMarker:
                    dataLines.Add((
                        path,
                        _reader.ReadUtf8WithUInt16Length()));
                    return;

                case ObjectMarker:
                    ReadObject(path, dataLines, depth + 1);
                    return;

                case MovieClipMarker:
                    /*
                     * Reserved by the AMF0 specification. It has no defined
                     * payload, so retaining the marker meaning is complete.
                     */
                    dataLines.Add((path, "MovieClip"));
                    dataLines.Add(($"<{path}>", "Reserved AMF0 marker"));
                    return;

                case NullMarker:
                    dataLines.Add((path, "null"));
                    return;

                case UndefinedMarker:
                    dataLines.Add((path, "undefined"));
                    return;

                case ReferenceMarker:
                {
                    ushort referenceIndex = _reader.ReadUInt16BE();
                    dataLines.Add((path, referenceIndex.ToString(CultureInfo.InvariantCulture)));
                    dataLines.Add(($"<{path}>", "AMF0 reference index"));
                    return;
                }

                case EcmaArrayMarker:
                    ReadEcmaArray(path, dataLines, depth + 1);
                    return;

                case StrictArrayMarker:
                    ReadStrictArray(path, dataLines, depth + 1);
                    return;

                case DateMarker:
                    ReadDate(path, dataLines);
                    return;

                case LongStringMarker:
                    dataLines.Add((
                        path,
                        _reader.ReadUtf8WithUInt32Length()));
                    return;

                case UnsupportedMarker:
                    dataLines.Add((path, "unsupported"));
                    return;

                case RecordSetMarker:
                    /*
                     * RecordSet is reserved and has no body defined by AMF0.
                     */
                    dataLines.Add((path, "RecordSet"));
                    dataLines.Add(($"<{path}>", "Reserved AMF0 marker"));
                    return;

                case XmlDocumentMarker:
                    dataLines.Add((
                        path,
                        _reader.ReadUtf8WithUInt32Length()));
                    dataLines.Add(($"<{path}>", "XML document"));
                    return;

                case TypedObjectMarker:
                    ReadTypedObject(path, dataLines, depth + 1);
                    return;

                case AvmPlusObjectMarker:
                    throw new InvalidDataException(
                        $"AMF3 value encountered at '{path}'; AMF3 is not supported by this parser.");

                case ObjectEndMarker:
                    throw new InvalidDataException(
                        $"Unexpected AMF0 object-end marker at '{path}'.");

                default:
                    throw new InvalidDataException(
                        $"Unsupported AMF0 type marker 0x{marker:X2} at '{path}'.");
            }
        }

        private void ReadObject(
            string path,
            List<(string K, string V)> dataLines,
            int depth)
        {
            bool hasMembers = false;

            while (true)
            {
                ushort nameLength = _reader.ReadUInt16BE();

                if (nameLength == 0)
                {
                    byte endMarker = _reader.ReadUInt8();

                    if (endMarker != ObjectEndMarker)
                    {
                        throw new InvalidDataException(
                            $"Invalid AMF0 object-end marker 0x{endMarker:X2} at '{path}'.");
                    }

                    break;
                }

                string memberName = _reader.ReadUtf8(nameLength);
                string memberPath = AppendMemberPath(path, memberName);

                ReadValue(memberPath, dataLines, depth);
                hasMembers = true;
            }

            /*
             * A non-empty object is represented completely by its flattened
             * members. An empty object needs an explicit line or it would
             * disappear from the GUI.
             */
            if (!hasMembers)
                dataLines.Add((path, "{}"));
        }

        private void ReadEcmaArray(
            string path,
            List<(string K, string V)> dataLines,
            int depth)
        {
            uint declaredCount = _reader.ReadUInt32BE();

            /*
             * ECMAArrayLength is an original field in the serialized AMF0
             * structure. It is retained even though real-world files often
             * contain an inaccurate value and terminate by ObjectEnd.
             */
            dataLines.Add((
                $"{path}.ECMAArrayLength",
                declaredCount.ToString(CultureInfo.InvariantCulture)));

            uint actualCount = 0;

            while (true)
            {
                ushort nameLength = _reader.ReadUInt16BE();

                if (nameLength == 0)
                {
                    byte endMarker = _reader.ReadUInt8();

                    if (endMarker != ObjectEndMarker)
                    {
                        throw new InvalidDataException(
                            $"Invalid AMF0 ECMA array end marker 0x{endMarker:X2} at '{path}'.");
                    }

                    break;
                }

                string memberName = _reader.ReadUtf8(nameLength);
                string memberPath = AppendMemberPath(path, memberName);

                ReadValue(memberPath, dataLines, depth);
                actualCount++;
            }

            if (actualCount != declaredCount)
            {
                dataLines.Add((
                    $"<{path}.ECMAArrayLength>",
                    $"Declared {declaredCount}, parsed {actualCount}"));
            }
        }

        private void ReadStrictArray(
            string path,
            List<(string K, string V)> dataLines,
            int depth)
        {
            uint count = _reader.ReadUInt32BE();

            /*
             * StrictArrayLength is the serialized array length. Array values
             * are emitted individually and are not subject to a display cap.
             */
            dataLines.Add((
                $"{path}[{count.ToString(CultureInfo.InvariantCulture)}]",
                $"StrictArrayLength= {count.ToString(CultureInfo.InvariantCulture)}"));

            for (uint index = 0; index < count; index++)
            {
                //string elementPath =
                //    $"{path}[{index.ToString(CultureInfo.InvariantCulture)}]";

                ReadValue(string.Empty, dataLines, depth);
            }
        }

        private void ReadDate(
            string path,
            List<(string K, string V)> dataLines)
        {
            double milliseconds = _reader.ReadDoubleBE();
            short localTimeOffset = _reader.ReadInt16BE();

            /*
             * Date values contain two original fields. Preserve both, then
             * add a readable ISO-8601 representation.
             */
            dataLines.Add((
                $"{path}.DateTime",
                FormatNumber(milliseconds)));

            dataLines.Add((
                $"{path}.LocalTimeOffset",
                localTimeOffset.ToString(CultureInfo.InvariantCulture)));

            string readable;

            if (double.IsNaN(milliseconds)
                || double.IsInfinity(milliseconds)
                || milliseconds < long.MinValue
                || milliseconds > long.MaxValue)
            {
                readable = "Invalid date value";
            }
            else
            {
                try
                {
                    readable = DateTimeOffset
                        .FromUnixTimeMilliseconds((long)milliseconds)
                        .ToString("O", CultureInfo.InvariantCulture);
                }
                catch (ArgumentOutOfRangeException)
                {
                    readable = "Date value is outside the supported range";
                }
            }

            dataLines.Add(($"<{path}.DateTime>", readable));

            if (localTimeOffset != 0)
            {
                dataLines.Add((
                    $"<{path}.LocalTimeOffset>",
                    $"{localTimeOffset} minutes"));
            }
        }

        private void ReadTypedObject(
            string path,
            List<(string K, string V)> dataLines,
            int depth)
        {
            string className = _reader.ReadUtf8WithUInt16Length();

            dataLines.Add(($"{path}.ClassName", className));

            bool hasMembers = false;

            while (true)
            {
                ushort nameLength = _reader.ReadUInt16BE();

                if (nameLength == 0)
                {
                    byte endMarker = _reader.ReadUInt8();

                    if (endMarker != ObjectEndMarker)
                    {
                        throw new InvalidDataException(
                            $"Invalid AMF0 typed-object end marker 0x{endMarker:X2} at '{path}'.");
                    }

                    break;
                }

                string memberName = _reader.ReadUtf8(nameLength);
                string memberPath = AppendMemberPath(path, memberName);

                ReadValue(memberPath, dataLines, depth);
                hasMembers = true;
            }

            if (!hasMembers)
                dataLines.Add(($"<{path}>", "Typed object has no members"));
        }

        private static void AddNumber(
            string path,
            double value,
            List<(string K, string V)> dataLines)
        {
            dataLines.Add((path, FormatNumber(value)));

            /*
             * Only add a readable time conversion where the metadata name
             * gives the number a defined time-unit meaning. Arbitrary AMF
             * numbers must not be guessed or transformed.
             */
            string memberName = GetFinalPathComponent(path);

            if (memberName.Equals("duration", StringComparison.OrdinalIgnoreCase)
                && double.IsFinite(value)
                && value >= 0)
            {
                try
                {
                    TimeSpan duration = TimeSpan.FromSeconds(value);

                    string formatted = duration.TotalHours >= 1
                        ? duration.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture)
                        : duration.ToString(@"mm\:ss\.fff", CultureInfo.InvariantCulture);

                    dataLines.Add(($"<{path}>", formatted));
                }
                catch (OverflowException)
                {
                    dataLines.Add(($"<{path}>", "Duration is outside the supported range"));
                }
            }
        }

        private static void AddBoolean(
            string path,
            byte rawValue,
            List<(string K, string V)> dataLines)
        {
            dataLines.Add((
                path,
                rawValue.ToString(CultureInfo.InvariantCulture)));

            dataLines.Add((
                $"<{path}>",
                rawValue == 0 ? "False" : "True"));

            if (rawValue is not 0 and not 1)
            {
                dataLines.Add((
                    $"<{path}.Warning>",
                    $"Non-canonical Boolean value {rawValue}"));
            }
        }

        private static string AppendMemberPath(
            string parentPath,
            string memberName)
        {
            /*
             * Dot notation is used for normal property names. Bracket notation
             * keeps unusual names unambiguous and prevents dots or brackets
             * inside metadata keys from changing the displayed hierarchy.
             */
            if (IsSimpleMemberName(memberName))
                return $"{parentPath}.{memberName}";

            return $"{parentPath}[{QuotePathComponent(memberName)}]";
        }

        private static bool IsSimpleMemberName(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            char first = value[0];

            if (!(char.IsLetter(first) || first is '_' or '$'))
                return false;

            for (int i = 1; i < value.Length; i++)
            {
                char c = value[i];

                if (!(char.IsLetterOrDigit(c) || c is '_' or '$'))
                    return false;
            }

            return true;
        }

        private static string QuotePathComponent(string value)
        {
            return "'" + value
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("'", "\\'", StringComparison.Ordinal)
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal)
                .Replace("\t", "\\t", StringComparison.Ordinal)
                + "'";
        }

        private static string GetFinalPathComponent(string path)
        {
            int dot = path.LastIndexOf('.');
            int bracket = path.LastIndexOf('[');
            int separator = Math.Max(dot, bracket);

            if (separator < 0 || separator + 1 >= path.Length)
                return path;

            string result = path[(separator + 1)..];

            if (result.Length >= 2
                && result[0] == '\''
                && result[^1] == '\'')
            {
                result = result[1..^1];
            }

            return result;
        }

        private static string FormatNumber(double value)
        {
            if (double.IsNaN(value))
                return "NaN";

            if (double.IsPositiveInfinity(value))
                return "Infinity";

            if (double.IsNegativeInfinity(value))
                return "-Infinity";

            return value.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}