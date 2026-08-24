namespace UniversalParser.Src.Parser.MPEG.Boxes.StsdEx
{
    /// <summary>
    /// Information gathered from the sample entry header, needed by child config boxes.
    /// e.g. 'damr' means different things under 'samr' vs 'sawb'; 'pcmC' under 'ipcm' vs 'fpcm'.
    /// </summary>
    internal sealed class ParseCtx
    {
        public string EntryType = "";
        public string HandlerType;
        public SampleEntry.Kind Kind;

        // AudioSampleEntry header values (for cross-checking against codec configs)
        public int ChannelCount;
        public int SampleSize;
        public double SampleRate;

        // VisualSampleEntry header values
        public int Width;
        public int Height;
    }
}