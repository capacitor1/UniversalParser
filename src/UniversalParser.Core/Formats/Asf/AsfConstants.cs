namespace UniversalParser.Core.Formats.Asf;

/// <summary>
/// Fixed sizes and official GUID values from the ASF Specification, Revision 01.20.05.
/// GUIDs are constructed from canonical strings; <see cref="Guid"/> uses the same mixed-endian
/// byte layout as the ASF Object ID field on disk.
/// </summary>
internal static class AsfConstants
{
    public const int ObjectHeaderLength = 24;
    public const int HeaderObjectFixedLength = 6;
    public const int HeaderExtensionFixedLength = 22;
    public const int DataObjectFixedLength = 26;
    public const int ExtendedStreamPropertiesFixedLength = 64;
    public const int BitmapInfoHeaderLength = 40;

    public const ushort HeaderReserved1 = 1;
    public const ushort HeaderReserved2 = 2;
    public const ushort HeaderExtensionReserved2 = 6;
    public const ushort DataObjectReserved = 0x0101;

    public static readonly Guid HeaderObject = new("75B22630-668E-11CF-A6D9-00AA0062CE6C");
    public static readonly Guid DataObject = new("75B22636-668E-11CF-A6D9-00AA0062CE6C");
    public static readonly Guid SimpleIndexObject = new("33000890-E5B1-11CF-89F4-00A0C90349CB");
    public static readonly Guid IndexObject = new("D6E229D3-35DA-11D1-9034-00A0C90349BE");
    public static readonly Guid MediaObjectIndexObject = new("FEB103F8-12AD-4C64-840F-2A1D2F7AD48C");
    public static readonly Guid TimecodeIndexObject = new("3CB73FD0-0C4A-4803-953D-EDF7B6228F0C");
    public static readonly Guid FilePropertiesObject = new("8CABDCA1-A947-11CF-8EE4-00C00C205365");
    public static readonly Guid StreamPropertiesObject = new("B7DC0791-A9B7-11CF-8EE6-00C00C205365");
    public static readonly Guid HeaderExtensionObject = new("5FBF03B5-A92E-11CF-8EE3-00C00C205365");
    public static readonly Guid CodecListObject = new("86D15240-311D-11D0-A3A4-00A0C90348F6");
    public static readonly Guid ScriptCommandObject = new("1EFB1A30-0B62-11D0-A39B-00A0C90348F6");
    public static readonly Guid MarkerObject = new("F487CD01-A951-11CF-8EE6-00C00C205365");
    public static readonly Guid BitrateMutualExclusionObject = new("D6E229DC-35DA-11D1-9034-00A0C90349BE");
    public static readonly Guid ErrorCorrectionObject = new("75B22635-668E-11CF-A6D9-00AA0062CE6C");
    public static readonly Guid ContentDescriptionObject = new("75B22633-668E-11CF-A6D9-00AA0062CE6C");
    public static readonly Guid ExtendedContentDescriptionObject = new("D2D0A440-E307-11D2-97F0-00A0C95EA850");
    public static readonly Guid ContentBrandingObject = new("2211B3FA-BD23-11D2-B4B7-00A0C955FC6E");
    public static readonly Guid StreamBitratePropertiesObject = new("7BF875CE-468D-11D1-8D82-006097C9A2B2");
    public static readonly Guid ContentEncryptionObject = new("2211B3FB-BD23-11D2-B4B7-00A0C955FC6E");
    public static readonly Guid ExtendedContentEncryptionObject = new("298AE614-2622-4C17-B935-DAE07EE9289C");
    public static readonly Guid AdvancedContentEncryptionObject = new("43058533-6981-49E6-9B74-AD12CB86D58C");
    public static readonly Guid DigitalSignatureObject = new("2211B3FC-BD23-11D2-B4B7-00A0C955FC6E");
    public static readonly Guid PaddingObject = new("1806D474-CADF-4509-A4BA-9AABCB96AAE8");
    public static readonly Guid ExtendedStreamPropertiesObject = new("14E6A5CB-C672-4332-8399-A96952065B5A");
    public static readonly Guid AdvancedMutualExclusionObject = new("A08649CF-4775-4670-8A16-6E35357566CD");
    public static readonly Guid GroupMutualExclusionObject = new("D1465A40-5A79-4338-B71B-E36B8FD6C249");
    public static readonly Guid StreamPrioritizationObject = new("D4FED15B-88D3-454F-81F0-ED5C45999E24");
    public static readonly Guid BandwidthSharingObject = new("A69609E6-517B-11D2-B6AF-00C04FD908E9");
    public static readonly Guid LanguageListObject = new("7C4346A9-EFE0-4BFC-B229-393EDE415C85");
    public static readonly Guid MetadataObject = new("C5F8CBEA-5BAF-4877-8467-AA8C44FA4CCA");
    public static readonly Guid MetadataLibraryObject = new("44231C94-9498-49D1-A141-1D134E457054");
    public static readonly Guid IndexParametersObject = new("D6E229DF-35DA-11D1-9034-00A0C90349BE");
    public static readonly Guid MediaObjectIndexParametersObject = new("6B203BAD-3F11-48E4-ACA8-D7613DE2CFA7");
    public static readonly Guid TimecodeIndexParametersObject = new("F55E496D-9797-4B5D-8C8B-604DFE9BFB24");
    public static readonly Guid CompatibilityObject = new("26F18B5D-4584-47EC-9F5F-0E651F0452C9");
    public static readonly Guid IndexPlaceholderObject = new("D9AADE20-7C17-4F9C-BC28-8555DD98E2A2");

    public static readonly Guid Reserved1 = new("ABD3D211-A9BA-11CF-8EE6-00C00C205365");
    public static readonly Guid Reserved2 = new("86D15241-311D-11D0-A3A4-00A0C90348F6");

    public static readonly Guid AudioMedia = new("F8699E40-5B4D-11CF-A8FD-00805F5C442B");
    public static readonly Guid VideoMedia = new("BC19EFC0-5B4D-11CF-A8FD-00805F5C442B");
    public static readonly Guid CommandMedia = new("59DACFC0-59E6-11D0-A3AC-00A0C90348F6");
    public static readonly Guid JfifMedia = new("B61BE100-5B4E-11CF-A8FD-00805F5C442B");
    public static readonly Guid DegradableJpegMedia = new("35907DE0-E415-11CF-A917-00805F5C442B");
    public static readonly Guid FileTransferMedia = new("91BD222C-F21C-497A-8B6D-5AA86BFC0185");
    public static readonly Guid BinaryMedia = new("3AFB65E2-47EF-40F2-AC2C-70A90D71D343");

    public static readonly Guid NoErrorCorrection = new("20FB5700-5B55-11CF-A8FD-00805F5C442B");
    public static readonly Guid AudioSpread = new("BFC3CD50-618F-11CF-8BB2-00AA00B4E220");
}
