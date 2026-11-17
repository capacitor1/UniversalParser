namespace UniversalParser.Core.Model;

public static class DiagnosticCodes
{
    public const string TruncatedHeader = "TRUNCATED_HEADER";
    public const string TruncatedPayload = "TRUNCATED_PAYLOAD";
    public const string TruncatedChild = "TRUNCATED_CHILD";
    public const string InvalidLength = "INVALID_LENGTH";
    public const string InvalidVint = "INVALID_VINT";
    public const string InvalidFieldNumber = "INVALID_FIELD_NUMBER";
    public const string InvalidWireType = "INVALID_WIRE_TYPE";
    public const string InvalidTagType = "INVALID_TAG_TYPE";
    public const string UnknownSize = "UNKNOWN_SIZE";
    public const string SchemaMismatch = "SCHEMA_MISMATCH";
    public const string LengthOverflow = "LENGTH_OVERFLOW";
    public const string InvalidSignature = "INVALID_SIGNATURE";
    public const string InvalidEncoding = "INVALID_ENCODING";
    public const string LimitRecords = "LIMIT_RECORDS";
    public const string LimitString = "LIMIT_STRING";
    public const string LimitBlob = "LIMIT_BLOB";
    public const string UnsupportedVersion = "UNSUPPORTED_VERSION";
    public const string UnparsedPayload = "UNPARSED_PAYLOAD";
    public const string IgnoredFrameworkData = "IGNORED_FRAMEWORK_DATA";
    public const string PreviousTagSizeMismatch = "PREVIOUSTAGSIZE_MISMATCH";
    public const string CrcMismatch = "CRC_MISMATCH";
    public const string TrailingData = "TRAILING_DATA";
    public const string ProbeFailed = "PROBE_FAILED";
    public const string ParseFailed = "PARSE_FAILED";
}
