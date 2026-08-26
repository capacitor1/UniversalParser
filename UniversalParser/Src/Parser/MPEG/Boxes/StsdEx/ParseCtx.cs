namespace UniversalParser.Src.Parser.MPEG.Boxes.StsdEx
{
    /// <summary>
    /// Information gathered from the sample entry header, needed by child config boxes.
    /// e.g. 'damr' means different things under 'samr' vs 'sawb'; 'pcmC' under 'ipcm' vs 'fpcm'.
    /// </summary>
    internal sealed class ParseCtx
    {
        public string EntryType = "";
        public string HandlerType = string.Empty;
        public SampleEntry.Kind Kind;

        // AudioSampleEntry header values (for cross-checking against codec configs)
        public int ChannelCount = 0;
        public int SampleSize = 0;
        public double SampleRate = 0;

        // VisualSampleEntry header values
        public int Width = 0;
        public int Height = 0;
    }
}