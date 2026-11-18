using System.Collections.Frozen;
using UniversalParser.Core.Model;

namespace UniversalParser.Core.Formats.Asf;

/// <summary>
/// Known ASF Object, stream-type, and error-correction GUID names. Unknown GUIDs stay visible
/// as canonical GUID strings and are never assigned a guessed name.
/// </summary>
internal static class AsfGuids
{
    private static readonly FrozenDictionary<Guid, string> ObjectNames = new Dictionary<Guid, string>()
    {
        [AsfConstants.HeaderObject] = "HeaderObject",
        [AsfConstants.DataObject] = "DataObject",
        [AsfConstants.SimpleIndexObject] = "SimpleIndexObject",
        [AsfConstants.IndexObject] = "IndexObject",
        [AsfConstants.MediaObjectIndexObject] = "MediaObjectIndexObject",
        [AsfConstants.TimecodeIndexObject] = "TimecodeIndexObject",
        [AsfConstants.FilePropertiesObject] = "FilePropertiesObject",
        [AsfConstants.StreamPropertiesObject] = "StreamPropertiesObject",
        [AsfConstants.HeaderExtensionObject] = "HeaderExtensionObject",
        [AsfConstants.CodecListObject] = "CodecListObject",
        [AsfConstants.ScriptCommandObject] = "ScriptCommandObject",
        [AsfConstants.MarkerObject] = "MarkerObject",
        [AsfConstants.BitrateMutualExclusionObject] = "BitrateMutualExclusionObject",
        [AsfConstants.ErrorCorrectionObject] = "ErrorCorrectionObject",
        [AsfConstants.ContentDescriptionObject] = "ContentDescriptionObject",
        [AsfConstants.ExtendedContentDescriptionObject] = "ExtendedContentDescriptionObject",
        [AsfConstants.ContentBrandingObject] = "ContentBrandingObject",
        [AsfConstants.StreamBitratePropertiesObject] = "StreamBitratePropertiesObject",
        [AsfConstants.ContentEncryptionObject] = "ContentEncryptionObject",
        [AsfConstants.ExtendedContentEncryptionObject] = "ExtendedContentEncryptionObject",
        [AsfConstants.AdvancedContentEncryptionObject] = "AdvancedContentEncryptionObject",
        [AsfConstants.DigitalSignatureObject] = "DigitalSignatureObject",
        [AsfConstants.PaddingObject] = "PaddingObject",
        [AsfConstants.ExtendedStreamPropertiesObject] = "ExtendedStreamPropertiesObject",
        [AsfConstants.AdvancedMutualExclusionObject] = "AdvancedMutualExclusionObject",
        [AsfConstants.GroupMutualExclusionObject] = "GroupMutualExclusionObject",
        [AsfConstants.StreamPrioritizationObject] = "StreamPrioritizationObject",
        [AsfConstants.BandwidthSharingObject] = "BandwidthSharingObject",
        [AsfConstants.LanguageListObject] = "LanguageListObject",
        [AsfConstants.MetadataObject] = "MetadataObject",
        [AsfConstants.MetadataLibraryObject] = "MetadataLibraryObject",
        [AsfConstants.IndexParametersObject] = "IndexParametersObject",
        [AsfConstants.MediaObjectIndexParametersObject] = "MediaObjectIndexParametersObject",
        [AsfConstants.TimecodeIndexParametersObject] = "TimecodeIndexParametersObject",
        [AsfConstants.CompatibilityObject] = "CompatibilityObject",
        [AsfConstants.IndexPlaceholderObject] = "IndexPlaceholderObject",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<Guid, string> StreamTypeNames = new Dictionary<Guid, string>()
    {
        [AsfConstants.AudioMedia] = "ASF_Audio_Media",
        [AsfConstants.VideoMedia] = "ASF_Video_Media",
        [AsfConstants.CommandMedia] = "ASF_Command_Media",
        [AsfConstants.JfifMedia] = "ASF_JFIF_Media",
        [AsfConstants.DegradableJpegMedia] = "ASF_Degradable_JPEG_Media",
        [AsfConstants.FileTransferMedia] = "ASF_File_Transfer_Media",
        [AsfConstants.BinaryMedia] = "ASF_Binary_Media",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<Guid, string> ErrorCorrectionNames = new Dictionary<Guid, string>()
    {
        [AsfConstants.NoErrorCorrection] = "ASF_No_Error_Correction",
        [AsfConstants.AudioSpread] = "ASF_Audio_Spread",
    }.ToFrozenDictionary();

    public static bool TryGetObjectName(Guid guid, out string name) => ObjectNames.TryGetValue(guid, out name!);

    public static string GetObjectName(Guid guid) =>
        ObjectNames.TryGetValue(guid, out var name)
            ? name
            : "UnknownObject";


    public static string GetObjectTitle(Guid guid) =>
        ObjectNames.TryGetValue(guid, out var name)
            ? $"{name} '{RawIdentifier.FormatGuid(guid)}'"
            : $"UnknownObject '{RawIdentifier.FormatGuid(guid)}'";

    public static string GetObjectTreeName(Guid guid) =>
        ObjectNames.TryGetValue(guid, out var name)
            ? name
            : RawIdentifier.FormatGuid(guid);

    public static bool IsKnownObject(Guid guid) => ObjectNames.ContainsKey(guid);

    public static bool TryGetStreamTypeName(Guid guid, out string name) => StreamTypeNames.TryGetValue(guid, out name!);

    public static bool TryGetErrorCorrectionName(Guid guid, out string name) =>
        ErrorCorrectionNames.TryGetValue(guid, out name!);
}
