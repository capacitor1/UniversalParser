/*
 * Tag / type tables.  Tag numbers are IFD-scoped: the same number means
 * different things in IFD0/Exif, GPS and Interop IFDs, so they MUST live in
 * separate enums (see ExifTagResolver.ResolveTagName).
 */
namespace UniversalParser.Src.Parser.EXIF
{
    /// <summary>TIFF 6.0 / Exif 2.32 field types. 16..18 are BigTIFF-only.</summary>
    internal enum ExifType : ushort
    {
        Byte = 1,
        ASCII = 2,
        Short = 3,
        Long = 4,
        Rational = 5,
        SByte = 6,
        Undefined = 7,
        SShort = 8,
        SLong = 9,
        SRational = 10,
        Float = 11,
        Double = 12,
        IFD = 13,
        Long8 = 16,
        SLong8 = 17,
        IFD8 = 18
    }

    /// <summary>Which tag namespace an IFD uses.</summary>
    internal enum ExifIfdKind
    {
        Tiff,     // IFD0 / IFD1 / SubIFDs
        Exif,     // Exif private IFD (0x8769)
        Gps,      // GPS IFD (0x8825)
        Interop   // Interoperability IFD (0xA005)
    }

    public enum ExifTag : ushort
    {
        // =====================================================
        // TIFF baseline / IFD0 + IFD1
        // =====================================================
        ProcessingSoftware = 0x000B,
        NewSubfileType = 0x00FE,
        SubfileType = 0x00FF,
        ImageWidth = 0x0100,
        ImageLength = 0x0101,
        BitsPerSample = 0x0102,
        Compression = 0x0103,
        PhotometricInterpretation = 0x0106,
        Thresholding = 0x0107,
        FillOrder = 0x010A,
        DocumentName = 0x010D,
        ImageDescription = 0x010E,
        Make = 0x010F,
        Model = 0x0110,
        StripOffsets = 0x0111,
        Orientation = 0x0112,
        SamplesPerPixel = 0x0115,
        RowsPerStrip = 0x0116,
        StripByteCounts = 0x0117,
        XResolution = 0x011A,
        YResolution = 0x011B,
        PlanarConfiguration = 0x011C,
        ResolutionUnit = 0x0128,
        TransferFunction = 0x012D,
        Software = 0x0131,
        DateTime = 0x0132,
        Artist = 0x013B,
        HostComputer = 0x013C,
        WhitePoint = 0x013E,
        PrimaryChromaticities = 0x013F,
        TileWidth = 0x0142,
        TileLength = 0x0143,
        SubIFDs = 0x014A,
        ExtraSamples = 0x0152,
        SampleFormat = 0x0153,
        JPEGInterchangeFormat = 0x0201,        // thumbnail offset
        JPEGInterchangeFormatLength = 0x0202,  // thumbnail length
        YCbCrCoefficients = 0x0211,
        YCbCrSubSampling = 0x0212,
        YCbCrPositioning = 0x0213,
        ReferenceBlackWhite = 0x0214,
        ApplicationNotes = 0x02BC,             // XMP packet
        Rating = 0x4746,
        RatingPercent = 0x4749,
        Copyright = 0x8298,

        // =====================================================
        // IFD pointers (the whole reason SubIFDs were missing)
        // =====================================================
        IPTC_NAA = 0x83BB,
        PhotoshopSettings = 0x8649,
        ExifIFDPointer = 0x8769,
        InterColorProfile = 0x8773,            // ICC profile
        GPSInfoIFDPointer = 0x8825,
        InteroperabilityIFDPointer = 0xA005,

        // =====================================================
        // Exif SubIFD (camera)
        // =====================================================
        ExposureTime = 0x829A,
        FNumber = 0x829D,
        ExposureProgram = 0x8822,
        SpectralSensitivity = 0x8824,
        ISOSpeedRatings = 0x8827,              // a.k.a. PhotographicSensitivity
        OECF = 0x8828,
        SensitivityType = 0x8830,
        StandardOutputSensitivity = 0x8831,
        RecommendedExposureIndex = 0x8832,
        ISOSpeed = 0x8833,
        ExifVersion = 0x9000,
        DateTimeOriginal = 0x9003,
        CreateDate = 0x9004,                   // DateTimeDigitized
        OffsetTime = 0x9010,
        OffsetTimeOriginal = 0x9011,
        OffsetTimeDigitized = 0x9012,
        ComponentsConfiguration = 0x9101,
        CompressedBitsPerPixel = 0x9102,
        ShutterSpeedValue = 0x9201,
        ApertureValue = 0x9202,
        BrightnessValue = 0x9203,
        ExposureBiasValue = 0x9204,
        MaxApertureValue = 0x9205,
        SubjectDistance = 0x9206,
        MeteringMode = 0x9207,
        LightSource = 0x9208,
        Flash = 0x9209,
        FocalLength = 0x920A,
        SubjectArea = 0x9214,
        MakerNote = 0x927C,
        UserComment = 0x9286,
        SubSecTime = 0x9290,
        SubSecTimeOriginal = 0x9291,
        SubSecTimeDigitized = 0x9292,

        // =====================================================
        // Windows XP / extended
        // =====================================================
        XPTitle = 0x9C9B,
        XPComment = 0x9C9C,
        XPAuthor = 0x9C9D,
        XPKeywords = 0x9C9E,
        XPSubject = 0x9C9F,

        // =====================================================
        // Exif 2.2+ capture details
        // =====================================================
        FlashpixVersion = 0xA000,
        ColorSpace = 0xA001,
        PixelXDimension = 0xA002,
        PixelYDimension = 0xA003,
        RelatedSoundFile = 0xA004,
        FlashEnergy = 0xA20B,
        SpatialFrequencyResponse = 0xA20C,
        FocalPlaneXResolution = 0xA20E,
        FocalPlaneYResolution = 0xA20F,
        FocalPlaneResolutionUnit = 0xA210,
        SubjectLocation = 0xA214,
        ExposureIndex = 0xA215,
        SensingMethod = 0xA217,
        FileSource = 0xA300,
        SceneType = 0xA301,
        CFAPattern = 0xA302,
        CustomRendered = 0xA401,
        ExposureMode = 0xA402,
        WhiteBalance = 0xA403,
        DigitalZoomRatio = 0xA404,
        FocalLengthIn35mmFormat = 0xA405,
        SceneCaptureType = 0xA406,
        GainControl = 0xA407,
        Contrast = 0xA408,
        Saturation = 0xA409,
        Sharpness = 0xA40A,
        DeviceSettingDescription = 0xA40B,
        SubjectDistanceRange = 0xA40C,
        ImageUniqueID = 0xA420,
        CameraOwnerName = 0xA430,
        BodySerialNumber = 0xA431,
        LensSpecification = 0xA432,
        LensMake = 0xA433,
        LensModel = 0xA434,
        LensSerialNumber = 0xA435,
        CompositeImage = 0xA460,
        Gamma = 0xA500
    }

    /// <summary>GPS IFD (0x8825) tag namespace.</summary>
    public enum GpsTag : ushort
    {
        GPSVersionID = 0x0000,
        GPSLatitudeRef = 0x0001,
        GPSLatitude = 0x0002,
        GPSLongitudeRef = 0x0003,
        GPSLongitude = 0x0004,
        GPSAltitudeRef = 0x0005,
        GPSAltitude = 0x0006,
        GPSTimeStamp = 0x0007,
        GPSSatellites = 0x0008,
        GPSStatus = 0x0009,
        GPSMeasureMode = 0x000A,
        GPSDOP = 0x000B,
        GPSSpeedRef = 0x000C,
        GPSSpeed = 0x000D,
        GPSTrackRef = 0x000E,
        GPSTrack = 0x000F,
        GPSImgDirectionRef = 0x0010,
        GPSImgDirection = 0x0011,
        GPSMapDatum = 0x0012,
        GPSDestLatitudeRef = 0x0013,
        GPSDestLatitude = 0x0014,
        GPSDestLongitudeRef = 0x0015,
        GPSDestLongitude = 0x0016,
        GPSDestBearingRef = 0x0017,
        GPSDestBearing = 0x0018,
        GPSDestDistanceRef = 0x0019,
        GPSDestDistance = 0x001A,
        GPSProcessingMethod = 0x001B,
        GPSAreaInformation = 0x001C,
        GPSDateStamp = 0x001D,
        GPSDifferential = 0x001E,
        GPSHPositioningError = 0x001F
    }

    /// <summary>Interoperability IFD (0xA005) tag namespace.</summary>
    public enum InteropTag : ushort
    {
        InteroperabilityIndex = 0x0001,
        InteroperabilityVersion = 0x0002,
        RelatedImageFileFormat = 0x1000,
        RelatedImageWidth = 0x1001,
        RelatedImageHeight = 0x1002
    }
}