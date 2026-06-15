namespace NeoPaula.Formats
{
    public class Module
    {
        public string Title { get; set; } = string.Empty;
        public int NumberOfChannels { get; set; }
        public int SongLength { get; set; }
        public int RestartPosition { get; set; }
        public int[] Sequence { get; set; } = [];

        public Sample[] Samples { get; set; } = [];
        public Pattern[] Patterns { get; set; } = [];

        // Default speeds
        public int DefaultSpeed { get; set; } = 6;
        public int DefaultTempo { get; set; } = 125;
    }

    public class Sample
    {
        public string Name { get; set; } = string.Empty;
        public int Length { get; set; }
        public int FineTune { get; set; }
        public int Volume { get; set; }
        public int RepeatOffset { get; set; }
        public int RepeatLength { get; set; }
        public byte[] Data { get; set; } = [];

        // Some MMD specific
        public bool Is16Bit { get; set; }
        public bool IsStereo { get; set; }
    }

    public class Pattern
    {
        public int Rows { get; set; }
        public int Channels { get; set; }
        public Note[,] Notes { get; set; } // [row, channel]

        public Pattern(int rows, int channels)
        {
            Rows = rows;
            Channels = channels;
            Notes = new Note[rows, channels];
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < channels; c++)
                {
                    Notes[r, c] = new Note();
                }
            }
        }
    }

    public class Note
    {
        public int Period { get; set; }
        public int Sample { get; set; }
        public int Effect { get; set; }
        public int EffectParam { get; set; }
    }
}
