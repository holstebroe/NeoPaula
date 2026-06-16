using System;
using NeoPaula.Formats;

namespace NeoPaula.Engine
{
    public static class SamplePreprocessor
    {
        public const int L = 441;
        public const int M = 80;

        public static void Preprocess(Module module, InterpolationMode mode)
        {
            foreach (var sample in module.Samples)
            {
                if (sample.Data == null || sample.Length == 0)
                {
                    sample.FloatData = [];
                    continue;
                }

                switch (mode)
                {
                    case InterpolationMode.Raw:
                        PreprocessRaw(sample);
                        break;
                    case InterpolationMode.Linear:
                        PreprocessLinear(sample);
                        break;
                    case InterpolationMode.Sinc:
                        PreprocessSinc(sample);
                        break;
                }
            }
        }

        private static void PreprocessRaw(Sample sample)
        {
            int originalLength = sample.Length;
            int newLength = (int)Math.Ceiling(originalLength * (double)L / M);
            float[] newData = new float[newLength];

            for (int i = 0; i < newLength; i++)
            {
                int originalIndex = (int)(i * (double)M / L);
                if (originalIndex >= originalLength) originalIndex = originalLength - 1;

                float val = (sbyte)sample.Data[originalIndex] / 128f;
                newData[i] = val;
            }

            sample.FloatData = newData;
            sample.Length = newLength;
            sample.RepeatOffset = (int)(sample.RepeatOffset * (double)L / M);
            sample.RepeatLength = (int)(sample.RepeatLength * (double)L / M);
        }

        private static void PreprocessLinear(Sample sample)
        {
            int originalLength = sample.Length;
            int newLength = (int)Math.Ceiling(originalLength * (double)L / M);
            float[] newData = new float[newLength];

            for (int i = 0; i < newLength; i++)
            {
                double exactIndex = i * (double)M / L;
                int index1 = (int)Math.Floor(exactIndex);
                int index2 = index1 + 1;
                double fraction = exactIndex - index1;

                float val1 = 0f;
                if (index1 < originalLength)
                {
                    val1 = (sbyte)sample.Data[index1] / 128f;
                }

                float val2 = 0f;
                if (index2 < originalLength)
                {
                    val2 = (sbyte)sample.Data[index2] / 128f;
                }
                else if (sample.RepeatLength > 2)
                {
                    // Loop it
                    int loopIndex = sample.RepeatOffset + (index2 - sample.RepeatOffset) % sample.RepeatLength;
                    if (loopIndex < originalLength)
                    {
                        val2 = (sbyte)sample.Data[loopIndex] / 128f;
                    }
                }
                else
                {
                    val2 = val1; // Hold last sample
                }

                newData[i] = val1 + (float)(fraction * (val2 - val1));
            }

            sample.FloatData = newData;
            sample.Length = newLength;
            sample.RepeatOffset = (int)(sample.RepeatOffset * (double)L / M);
            sample.RepeatLength = (int)(sample.RepeatLength * (double)L / M);
        }

        private static void PreprocessSinc(Sample sample)
        {
            int originalLength = sample.Length;
            int newLength = (int)Math.Ceiling(originalLength * (double)L / M);
            float[] newData = new float[newLength];

            // 1. Zero-stuffing (Upsampling by L)
            int upsampledLength = originalLength * L;

            // 2. Low-Pass Filtering & Decimation (Polyphase approach)
            // We need a FIR filter with cutoff at Pi / L.
            // For practical purposes we use a windowed sinc filter.
            int numTaps = 31 * L; // Filter length
            float[] fir = GenerateSincFilter(numTaps, 1.0f / L);

            int halfTaps = numTaps / 2;

            for (int i = 0; i < newLength; i++)
            {
                // The position in the upsampled signal we want to sample
                int upsampledIndex = i * M;

                float sum = 0f;

                // Convolve FIR with the zero-stuffed signal.
                // Since most of the upsampled signal is 0, we only iterate over non-zero points.
                // A non-zero point occurs when (upsampledIndex - j) is a multiple of L.
                // Let (upsampledIndex - j) = k * L => j = upsampledIndex - k * L

                int min_k = (upsampledIndex - halfTaps) / L;
                int max_k = (upsampledIndex + halfTaps) / L;

                for (int k = min_k; k <= max_k; k++)
                {
                    int j = upsampledIndex - k * L;
                    if (j >= -halfTaps && j <= halfTaps)
                    {
                        // FIR index is j + halfTaps
                        float coeff = fir[j + halfTaps];

                        // Sample data index is k
                        int sampleIndex = k;
                        float val = 0f;

                        if (sampleIndex >= 0 && sampleIndex < originalLength)
                        {
                            val = (sbyte)sample.Data[sampleIndex] / 128f;
                        }
                        else if (sampleIndex >= originalLength && sample.RepeatLength > 2)
                        {
                            // Loop
                            int loopIndex = sample.RepeatOffset + (sampleIndex - sample.RepeatOffset) % sample.RepeatLength;
                            if (loopIndex < originalLength)
                            {
                                val = (sbyte)sample.Data[loopIndex] / 128f;
                            }
                        }
                        else if (sampleIndex < 0 && sample.RepeatLength > 2)
                        {
                            // This is a simplified wrap for negative index if looping
                            val = 0f; // For simplicity in negative bounds we assume 0 or handle better if needed.
                        }

                        sum += val * coeff;
                    }
                }

                // Multiply by L to maintain amplitude after zero stuffing
                newData[i] = sum * L;
            }

            sample.FloatData = newData;
            sample.Length = newLength;
            sample.RepeatOffset = (int)(sample.RepeatOffset * (double)L / M);
            sample.RepeatLength = (int)(sample.RepeatLength * (double)L / M);
        }

        private static float[] GenerateSincFilter(int taps, float cutoff)
        {
            float[] fir = new float[taps];
            int half = taps / 2;
            float sum = 0f;

            for (int i = 0; i < taps; i++)
            {
                int n = i - half;
                if (n == 0)
                {
                    fir[i] = 2.0f * cutoff;
                }
                else
                {
                    fir[i] = (float)(Math.Sin(2.0 * Math.PI * cutoff * n) / (Math.PI * n));
                }

                // Blackman window
                fir[i] *= (float)(0.42 - 0.5 * Math.Cos(2.0 * Math.PI * i / (taps - 1)) + 0.08 * Math.Cos(4.0 * Math.PI * i / (taps - 1)));
                sum += fir[i];
            }

            // Normalize
            for (int i = 0; i < taps; i++)
            {
                fir[i] /= sum;
            }

            return fir;
        }
    }
}
