using System;
using System.Collections.Generic;
using System.Diagnostics;
using UniversalParser.Src.Parser.EBML.Elements;

namespace UniversalParser.Src.Parser.EBML
{
    internal delegate ParseResult EBMLElementHandler(
        EBMLParser parser,
        Node node,
        EBMLElementHeader header);

    internal static class EBMLDispatcher
    {
        private static readonly Dictionary<ulong, EBMLElementHandler> Handlers = [];

        static EBMLDispatcher()
        {
            // EBML Header
            Register(EBMLHeader.ParseHeader, 0x1A45DFA3);
            Register(EBMLHeader.ParseVersion, 0x4286);
            Register(EBMLHeader.ParseReadVersion, 0x42F7);
            Register(EBMLHeader.ParseMaxIDLength, 0x42F2);
            Register(EBMLHeader.ParseMaxSizeLength, 0x42F3);
            Register(EBMLHeader.ParseDocType, 0x4282);
            Register(EBMLHeader.ParseDocTypeVersion, 0x4287);
            Register(EBMLHeader.ParseDocTypeReadVersion, 0x4285);
            
            // SeekHead
            Register(
                SeekHead.ParseSeekHead,
                0x114D9B74);
            Register(
                SeekHead.ParseSeek,
                0x4DBB);
            Register(
                SeekHead.ParseSeekID,
                0x53AB);
            Register(
                SeekHead.ParseSeekPosition,
                0x53AC);

            // Global Elements
            Register(GlobalElements.ParseVoid, 0xEC);
            Register(GlobalElements.ParseCRC32, 0xBF);

            // Segment Information
            Register(SegmentInfo.ParseInfo, 0x1549A966);
            Register(SegmentInfo.ParseSegmentUUID, 0x73A4);
            Register(SegmentInfo.ParseSegmentFilename, 0x7384);
            Register(SegmentInfo.ParsePrevUUID, 0x3CB923);
            Register(SegmentInfo.ParsePrevFilename, 0x3C83AB);
            Register(SegmentInfo.ParseNextUUID, 0x3EB923);
            Register(SegmentInfo.ParseNextFilename, 0x3E83BB);
            Register(SegmentInfo.ParseSegmentFamily, 0x4444);
            Register(SegmentInfo.ParseChapterTranslate, 0x6924);
            Register(SegmentInfo.ParseChapterTranslateID, 0x69A5);
            Register(SegmentInfo.ParseChapterTranslateCodec, 0x69BF);
            Register(SegmentInfo.ParseChapterTranslateEditionUID, 0x69FC);
            Register(SegmentInfo.ParseTimestampScale, 0x2AD7B1);
            Register(SegmentInfo.ParseDuration, 0x4489);
            Register(SegmentInfo.ParseDateUTC, 0x4461);
            Register(SegmentInfo.ParseTitle, 0x7BA9);
            Register(SegmentInfo.ParseMuxingApp, 0x4D80);
            Register(SegmentInfo.ParseWritingApp, 0x5741);
            // Tracks
            Register(TrackElements.ParseTracks, 0x1654AE6B);
            Register(TrackElements.ParseTrackEntry, 0xAE);
            Register(TrackElements.ParseTrackNumber, 0xD7);
            Register(TrackElements.ParseTrackUID, 0x73C5);
            Register(TrackElements.ParseTrackType, 0x83);
            Register(TrackElements.ParseFlagEnabled, 0xB9);
            Register(TrackElements.ParseFlagDefault, 0x88);
            Register(TrackElements.ParseFlagForced, 0x55AA);
            Register(TrackElements.ParseFlagLacing, 0x9C);
            Register(TrackElements.ParseMinCache, 0x6DE7);
            Register(TrackElements.ParseMaxCache, 0x6DF8);
            Register(TrackElements.ParseDefaultDuration, 0x23E383);
            Register(TrackElements.ParseDefaultDecodedFieldDuration, 0x234E7A);
            Register(TrackElements.ParseTrackTimestampScale, 0x23314F);
            Register(TrackElements.ParseMaxBlockAdditionID, 0x55EE);
            Register(TrackElements.ParseTrackOverlay, 0x6FAB);
            Register(TrackElements.ParseAttachmentLink, 0x7446);
            Register(TrackElements.ParseName, 0x536E);
            Register(TrackElements.ParseLanguage, 0x22B59C);
            Register(TrackElements.ParseLanguageBCP47, 0x22B59D);
            Register(TrackElements.ParseCodecID, 0x86);
            Register(TrackElements.ParseCodecPrivate, 0x63A2);
            Register(TrackElements.ParseCodecName, 0x258688);
            Register(TrackElements.ParseCodecSettings, 0x3A9697);
            Register(TrackElements.ParseCodecInfo, 0x3B4040);
            Register(TrackElements.ParseCodecDownloadURL, 0x26B240);
            Register(TrackElements.ParseCodecDecodeAll, 0xAA);

// Video
            Register(TrackElements.ParseVideo, 0xE0);
            Register(TrackElements.ParseFlagInterlaced, 0x9A);
            Register(TrackElements.ParseFieldOrder, 0x9D);
            Register(TrackElements.ParseStereoMode, 0x53B8);
            Register(TrackElements.ParseOldStereoMode, 0x53B9);
            Register(TrackElements.ParseAlphaMode, 0x53C0);
            Register(TrackElements.ParsePixelWidth, 0xB0);
            Register(TrackElements.ParsePixelHeight, 0xBA);
            Register(TrackElements.ParsePixelCropBottom, 0x54AA);
            Register(TrackElements.ParsePixelCropTop, 0x54BB);
            Register(TrackElements.ParsePixelCropLeft, 0x54CC);
            Register(TrackElements.ParsePixelCropRight, 0x54DD);
            Register(TrackElements.ParseDisplayWidth, 0x54B0);
            Register(TrackElements.ParseDisplayHeight, 0x54BA);
            Register(TrackElements.ParseDisplayUnit, 0x54B2);
            Register(TrackElements.ParseAspectRatioType, 0x54B3);
            Register(TrackElements.ParseUncompressedFourCC, 0x2EB524);
            Register(TrackElements.ParseGamma, 0x2FB523);

// Colour
            Register(TrackElements.ParseColour, 0x55B0);
            Register(TrackElements.ParseMatrixCoefficients, 0x55B1);
            Register(TrackElements.ParseBitsPerChannel, 0x55B2);
            Register(TrackElements.ParseChromaSubsamplingHorz, 0x55B3);
            Register(TrackElements.ParseChromaSubsamplingVert, 0x55B4);
            Register(TrackElements.ParseCbSubsamplingHorz, 0x55B5);
            Register(TrackElements.ParseCbSubsamplingVert, 0x55B6);
            Register(TrackElements.ParseChromaSitingHorz, 0x55B7);
            Register(TrackElements.ParseChromaSitingVert, 0x55B8);
            Register(TrackElements.ParseRange, 0x55B9);
            Register(TrackElements.ParseTransferCharacteristics, 0x55BA);
            Register(TrackElements.ParsePrimaries, 0x55BB);
            Register(TrackElements.ParseMaxCLL, 0x55BC);
            Register(TrackElements.ParseMaxFALL, 0x55BD);

// MasteringMetadata
            Register(TrackElements.ParseMasteringMetadata, 0x55D0);
            Register(TrackElements.ParsePrimaryRChromaticityX, 0x55D1);
            Register(TrackElements.ParsePrimaryRChromaticityY, 0x55D2);
            Register(TrackElements.ParsePrimaryGChromaticityX, 0x55D3);
            Register(TrackElements.ParsePrimaryGChromaticityY, 0x55D4);
            Register(TrackElements.ParsePrimaryBChromaticityX, 0x55D5);
            Register(TrackElements.ParsePrimaryBChromaticityY, 0x55D6);
            Register(TrackElements.ParseWhitePointChromaticityX, 0x55D7);
            Register(TrackElements.ParseWhitePointChromaticityY, 0x55D8);
            Register(TrackElements.ParseLuminanceMax, 0x55D9);
            Register(TrackElements.ParseLuminanceMin, 0x55DA);

// Projection
            Register(TrackElements.ParseProjection, 0x7670);
            Register(TrackElements.ParseProjectionType, 0x7671);
            Register(TrackElements.ParseProjectionPrivate, 0x7672);
            Register(TrackElements.ParseProjectionPoseYaw, 0x7673);
            Register(TrackElements.ParseProjectionPosePitch, 0x7674);
            Register(TrackElements.ParseProjectionPoseRoll, 0x7675);

// Audio
            Register(TrackElements.ParseAudio, 0xE1);
            Register(TrackElements.ParseSamplingFrequency, 0xB5);
            Register(TrackElements.ParseOutputSamplingFrequency, 0x78B5);
            Register(TrackElements.ParseChannels, 0x9F);
            Register(TrackElements.ParseChannelPositions, 0x7D7B);
            Register(TrackElements.ParseBitDepth, 0x6264);

// TrackTranslate
            Register(TrackElements.ParseTrackTranslate, 0x6624);
            Register(TrackElements.ParseTrackTranslateEditionUID, 0x66FC);
            Register(TrackElements.ParseTrackTranslateCodec, 0x66BF);
            Register(TrackElements.ParseTrackTranslateTrackID, 0x66A5);

// TrackOperation
            Register(TrackElements.ParseTrackOperation, 0xE2);
            Register(TrackElements.ParseTrackCombinePlanes, 0xE3);
            Register(TrackElements.ParseTrackPlane, 0xE4);
            Register(TrackElements.ParseTrackPlaneUID, 0xE5);
            Register(TrackElements.ParseTrackPlaneType, 0xE6);
            Register(TrackElements.ParseTrackJoinBlocks, 0xE9);
            Register(TrackElements.ParseTrackJoinUID, 0xED);

// ContentEncodings
            Register(TrackElements.ParseContentEncodings, 0x6D80);
            Register(TrackElements.ParseContentEncoding, 0x6240);
            Register(TrackElements.ParseContentEncodingOrder, 0x5031);
            Register(TrackElements.ParseContentEncodingScope, 0x5032);
            Register(TrackElements.ParseContentEncodingType, 0x5033);
            Register(TrackElements.ParseContentCompression, 0x5034);
            Register(TrackElements.ParseContentCompAlgo, 0x4254);
            Register(TrackElements.ParseContentCompSettings, 0x4255);
            Register(TrackElements.ParseContentEncryption, 0x5035);
            Register(TrackElements.ParseContentEncAlgo, 0x47E1);
            Register(TrackElements.ParseContentEncKeyID, 0x47E2);
            Register(TrackElements.ParseContentEncAESSettings, 0x47E7);
            Register(TrackElements.ParseAESSettingsCipherMode, 0x47E8);
            
            // Tags
            Register(TagElements.ParseTags, 0x1254C367);
            Register(TagElements.ParseTag, 0x7373);
            Register(TagElements.ParseTargets, 0x63C0);
            Register(TagElements.ParseTargetTypeValue, 0x68CA);
            Register(TagElements.ParseTargetType, 0x63CA);
            Register(TagElements.ParseTagTrackUID, 0x63C5);
            Register(TagElements.ParseTagEditionUID, 0x63C9);
            Register(TagElements.ParseTagChapterUID, 0x63C4);
            Register(TagElements.ParseTagAttachmentUID, 0x63C6);
            Register(TagElements.ParseSimpleTag, 0x67C8);
            Register(TagElements.ParseTagName, 0x45A3);
            Register(TagElements.ParseTagLanguage, 0x447A);
            Register(TagElements.ParseTagLanguageBCP47, 0x447B);
            Register(TagElements.ParseTagDefault, 0x4484);
            Register(TagElements.ParseTagDefaultBogus, 0x44B4);
            Register(TagElements.ParseTagString, 0x4487);
            Register(TagElements.ParseTagBinary, 0x4485);
            
            // Cluster
            Register(ClusterElements.ParseCluster, 0x1F43B675);
            Register(ClusterElements.ParseTimestamp, 0xE7);
            Register(ClusterElements.ParseSilentTracks, 0x5854);
            Register(ClusterElements.ParseSilentTrackNumber, 0x58D7);
            Register(ClusterElements.ParsePosition, 0xA7);
            Register(ClusterElements.ParsePrevSize, 0xAB);
            Register(ClusterElements.ParseSimpleBlock, 0xA3);
            Register(ClusterElements.ParseBlockGroup, 0xA0);
            Register(ClusterElements.ParseBlock, 0xA1);
            Register(ClusterElements.ParseBlockVirtual, 0xA2);
            Register(ClusterElements.ParseBlockAdditions, 0x75A1);
            Register(ClusterElements.ParseBlockMore, 0xA6);
            Register(ClusterElements.ParseBlockAddID, 0xEE);
            Register(ClusterElements.ParseBlockAdditional, 0xA5);
            Register(ClusterElements.ParseBlockDuration, 0x9B);
            Register(ClusterElements.ParseReferencePriority, 0xFA);
            Register(ClusterElements.ParseReferenceBlock, 0xFB);
            Register(ClusterElements.ParseReferenceVirtual, 0xFD);
            Register(ClusterElements.ParseCodecState, 0xA4);
            Register(ClusterElements.ParseDiscardPadding, 0x75A2);
            Register(ClusterElements.ParseSlices, 0x8E);
            Register(ClusterElements.ParseTimeSlice, 0xE8);
            Register(ClusterElements.ParseLaceNumber, 0xCC);
            Register(ClusterElements.ParseFrameNumber, 0xCD);
            Register(ClusterElements.ParseBlockAdditionID, 0xC8);
            Register(ClusterElements.ParseDelay, 0xC9);
            Register(ClusterElements.ParseSliceDuration, 0xCF);
            Register(ClusterElements.ParseEncryptedBlock, 0xAF);
            // Cues
            Register(CueElements.ParseCues, 0x1C53BB6B);
            Register(CueElements.ParseCuePoint, 0xBB);
            Register(CueElements.ParseCueTime, 0xB3);
            Register(CueElements.ParseCueTrackPositions, 0xB7);
            Register(CueElements.ParseCueTrack, 0xF7);
            Register(CueElements.ParseCueClusterPosition, 0xF1);
            Register(CueElements.ParseCueRelativePosition, 0xF0);
            Register(CueElements.ParseCueDuration, 0xB2);
            Register(CueElements.ParseCueBlockNumber, 0x5378);
            Register(CueElements.ParseCueCodecState, 0xEA);
            Register(CueElements.ParseCueReference, 0xDB);
            Register(CueElements.ParseCueRefTime, 0x96);
            Register(CueElements.ParseCueRefCluster, 0x97);
            Register(CueElements.ParseCueRefNumber, 0x535F);
            Register(CueElements.ParseCueRefCodecState, 0xEB);
        }

        private static void Register(
            EBMLElementHandler handler,
            params ulong[] elementIds)
        {
            ArgumentNullException.ThrowIfNull(handler);
            ArgumentNullException.ThrowIfNull(elementIds);

            foreach (ulong elementId in elementIds)
                Handlers[elementId] = handler;
        }

        public static ParseResult Dispatch(
            EBMLParser parser,
            Node node)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            EBMLElementHeader header;

            try
            {
                if (!parser.TryGetElementHeader(node, out header))
                    return Default.ParseRaw(parser, node);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[EBMLDispatcher] Header read failed at 0x{node.Position:X}: {ex}");

                return Default.ParseRaw(parser, node);
            }

            try
            {
                if (Handlers.TryGetValue(header.ElementId, out EBMLElementHandler? handler))
                    return handler(parser, node, header);

                return Default.Parse(parser, node, header);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[EBMLDispatcher] Failed to parse {header.FormattedId} " +
                    $"at 0x{header.ElementStart:X}: {ex}");

                long unparsedLength = header.IsMaster
                    ? 0
                    : header.PayloadLength;

                var dataLines = new List<(string K, string V)>
                {
                    ("<Error>", ex.GetType().Name),
                    ("<Message>", ex.Message),
                };

                if (unparsedLength > 0)
                {
                    dataLines.Add((
                        "<PayloadLength>",
                        EBMLUtil.FormatBytes(unparsedLength)));
                }

                return new ParseResult
                {
                    Title = EBMLUtil.MakeTitle(
                        "ParseError",
                        header.ElementId,
                        header.ElementIdLength),

                    Position = node.Position,
                    Length = node.Length,
                    DataLines = dataLines,

                    RawData = parser.CreateRawStream(
                        header.ElementStart,
                        header.ElementLength),
                };
            }
        }
    }
}