using System;
using System.IO;
using System.Text;

namespace NeoPaula.Formats
{
    public class ModParser
    {
        public static Module Parse(Stream stream)
        {
            BinaryReader reader = new BinaryReader(stream);
            Module mod = new Module();

            // 20 bytes Title
            byte[] titleBytes = reader.ReadBytes(20);
            mod.Title = GetString(titleBytes);

            mod.Samples = new Sample[31];
            for (int i = 0; i < 31; i++)
            {
                mod.Samples[i] = new Sample();
                mod.Samples[i].Name = GetString(reader.ReadBytes(22));

                int lenWord = ReadBigEndianUInt16(reader);
                mod.Samples[i].Length = lenWord * 2; // in bytes

                byte finetune = reader.ReadByte();
                // Lower 4 bits, signed nibble
                int ft = finetune & 0x0F;
                if (ft > 7) ft -= 16;
                mod.Samples[i].FineTune = ft;

                mod.Samples[i].Volume = reader.ReadByte();

                int repWord = ReadBigEndianUInt16(reader);
                mod.Samples[i].RepeatOffset = repWord * 2;

                int repLenWord = ReadBigEndianUInt16(reader);
                mod.Samples[i].RepeatLength = repLenWord * 2;
            }

            mod.SongLength = reader.ReadByte();
            mod.RestartPosition = reader.ReadByte();

            mod.Sequence = new int[128];
            int maxPattern = 0;
            for (int i = 0; i < 128; i++)
            {
                mod.Sequence[i] = reader.ReadByte();
                if (mod.Sequence[i] > maxPattern)
                    maxPattern = mod.Sequence[i];
            }

            // 4 bytes Magic
            string magic = GetString(reader.ReadBytes(4));
            mod.NumberOfChannels = GetChannelsFromMagic(magic);

            if (mod.NumberOfChannels == 0)
            {
                // Probably a 15-instrument mod if magic isn't known, but we'll assume 4 channels standard
                // To properly support 15-instrument mods, we'd have to rewind and re-read, but standard specifies 31 for now
                mod.NumberOfChannels = 4;
            }

            int numPatterns = maxPattern + 1;
            mod.Patterns = new Pattern[numPatterns];

            for (int p = 0; p < numPatterns; p++)
            {
                mod.Patterns[p] = new Pattern(64, mod.NumberOfChannels);
                for (int r = 0; r < 64; r++)
                {
                    for (int c = 0; c < mod.NumberOfChannels; c++)
                    {
                        byte[] noteBytes = reader.ReadBytes(4);

                        // byte 0: sample upper 4 bits, period upper 4 bits
                        // byte 1: period lower 8 bits
                        // byte 2: sample lower 4 bits, effect command
                        // byte 3: effect param

                        int sample = (noteBytes[0] & 0xF0) | ((noteBytes[2] >> 4) & 0x0F);
                        int period = ((noteBytes[0] & 0x0F) << 8) | noteBytes[1];
                        int effect = noteBytes[2] & 0x0F;
                        int effectParam = noteBytes[3];

                        mod.Patterns[p].Notes[r, c].Sample = sample;
                        mod.Patterns[p].Notes[r, c].Period = period;
                        mod.Patterns[p].Notes[r, c].Effect = effect;
                        mod.Patterns[p].Notes[r, c].EffectParam = effectParam;
                    }
                }
            }

            // Read Sample Data
            for (int i = 0; i < 31; i++)
            {
                if (mod.Samples[i].Length > 0)
                {
                    mod.Samples[i].Data = reader.ReadBytes(mod.Samples[i].Length);
                }
            }

            return mod;
        }

        private static int ReadBigEndianUInt16(BinaryReader reader)
        {
            byte b1 = reader.ReadByte();
            byte b2 = reader.ReadByte();
            return (b1 << 8) | b2;
        }

        private static string GetString(byte[] bytes)
        {
            int len = 0;
            while (len < bytes.Length && bytes[len] != 0) len++;
            return Encoding.ASCII.GetString(bytes, 0, len).Trim();
        }

        public static int GetChannelsFromMagic(string magic)
        {
            switch (magic)
            {
                case "M.K.":
                case "M!K!":
                case "4CHN":
                case "FLT4":
                    return 4;
                case "6CHN":
                    return 6;
                case "8CHN":
                case "FLT8":
                    return 8;
                default:
                    if (magic.EndsWith("CH") && int.TryParse(magic.Substring(0, 2), out int ch1)) return ch1;
                    if (magic.EndsWith("CHN") && int.TryParse(magic.Substring(0, 1), out int ch2)) return ch2;
                    return 0;
            }
        }
    }
}
