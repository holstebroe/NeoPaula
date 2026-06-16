using System.Buffers;
using NAudio.Wave;
using NAudio.Dsp;
using NeoPaula.Formats;

namespace NeoPaula.Engine
{
    public class TrackerSampleProvider : ISampleProvider
    {
        private readonly Module _module;
        private readonly WaveFormat _waveFormat;

        // State
        private int _currentOrder;
        private int _currentRow;
        private int _tick;

        private int _speed;
        private int _tempo;

        private int _samplesPerTick;
        private int _tickSamplePosition;

        private readonly ChannelState[] _channels;
        private readonly float[][] _channelBuffers;

        // Constants
        private const float AmigaClock = 7093789.2f;

        // Period table for standard notes (FinteTune 0)
        private static readonly int[] PeriodTable =
        [
            1712,1616,1525,1440,1357,1281,1209,1141,1077,1017, 961, 907, // Octave 0
             856, 808, 762, 720, 678, 640, 604, 570, 538, 508, 480, 453, // Octave 1
             428, 404, 381, 360, 339, 320, 302, 285, 269, 254, 240, 226, // Octave 2
             214, 202, 190, 180, 170, 160, 151, 143, 135, 127, 120, 113, // Octave 3
             107, 101,  95,  90,  85,  80,  76,  71,  67,  64,  60,  57  // Octave 4
        ];

        private static readonly int[] SineTable =
        [
            0,  24,  49,  74,  97, 120, 141, 161,
            180, 197, 212, 224, 235, 244, 250, 253,
            255, 253, 250, 244, 235, 224, 212, 197,
            180, 161, 141, 120,  97,  74,  49,  24
        ];


        // Oversampling state
        private readonly bool _oversamplingEnabled;
        private readonly int _oversamplingFactor;
        private readonly BiQuadFilter[] _leftFilters = Array.Empty<BiQuadFilter>();
        private readonly BiQuadFilter[] _rightFilters = Array.Empty<BiQuadFilter>();

        private int _nextOrder = -1;

        private int _nextRow = -1;

        public ChannelStereoMode StereoMode
        {
            get => field;
            set
            {
                field = value;
                switch (value)
                {
                    case ChannelStereoMode.Mono:
                        foreach (var t in _channels) t.Panning = 0;
                        break;
                    case ChannelStereoMode.HardPanning:
                        for (int i = 0; i < _channels.Length/2; i++) _channels[i].Panning = -1;
                        for (int i = _channels.Length / 2; i < _channels.Length; i++) _channels[i].Panning = 1;
                        break;
                    case ChannelStereoMode.MidPanning:
                        for (int i = 0; i < _channels.Length / 2; i++) _channels[i].Panning = -0.5f;
                        for (int i = _channels.Length / 2; i < _channels.Length; i++) _channels[i].Panning = 0.5f;
                        break;
                    case ChannelStereoMode.HardSpread:
                        for (int i = 0; i < _channels.Length; i++) _channels[i].Panning = -1 + (float)i / (_channels.Length - 1) * 2;
                        break;
                    case ChannelStereoMode.MidSpread:
                        for (int i = 0; i < _channels.Length; i++) _channels[i].Panning = -0.5f + (float)i / (_channels.Length - 1) * 1;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(value), value, null);
                }
            }
        } = ChannelStereoMode.HardPanning;

        public TrackerSampleProvider(Module module, int sampleRate = 44100, bool enableOversampling = false)
        {
            _module = module;
            _waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);

            _oversamplingEnabled = enableOversampling;
            _oversamplingFactor = enableOversampling ? 4 : 1;

            if (enableOversampling)
            {
                int internalSampleRate = sampleRate * _oversamplingFactor;
                _leftFilters = new BiQuadFilter[2];
                _rightFilters = new BiQuadFilter[2];
                for (int i = 0; i < 2; i++)
                {
                    _leftFilters[i] = BiQuadFilter.LowPassFilter(internalSampleRate, 20000, 0.707f);
                    _rightFilters[i] = BiQuadFilter.LowPassFilter(internalSampleRate, 20000, 0.707f);
                }
            }

            _speed = module.DefaultSpeed;
            _tempo = module.DefaultTempo;
            if (_speed == 0) _speed = 6;
            if (_tempo == 0) _tempo = 125;

            _channels = new ChannelState[module.NumberOfChannels];
            for (int i = 0; i < _channels.Length; i++)
            {
                _channels[i] = new ChannelState
                {
                    Panning = (i % 4 == 0) || (i % 4 == 3) ? -1f : 1f
                };
            }
            _channelBuffers = new float[_channels.Length][];

            UpdateSamplesPerTick();
        }

        public WaveFormat WaveFormat => _waveFormat;

        /// <summary>
        /// Reads audio data into the provided output buffer.
        /// The output buffer data format is 0..1f stereo interleaved.
        /// </summary>
        public int Read(float[] outBuffer, int offset, int count)
        {
            int samplesWritten = 0;

            while (samplesWritten < count)
            {
                if (_tickSamplePosition >= _samplesPerTick)
                {
                    ProcessTick();
                    _tickSamplePosition = 0;
                }

                int outputSamplesNeeded = count - samplesWritten;
                int outputFramesNeeded = outputSamplesNeeded / 2;

                int internalSamplesAvailable = _samplesPerTick - _tickSamplePosition;
                int outputFramesAvailableInTick = (int)Math.Ceiling((double)internalSamplesAvailable / (2 * _oversamplingFactor));

                int outputFramesToRender = Math.Min(outputFramesNeeded, outputFramesAvailableInTick);
                int internalFramesToRender = outputFramesToRender * _oversamplingFactor;

                if (internalFramesToRender <= 0) break;

                for (int ch = 0; ch < _channels.Length; ch++)
                {
                    _channelBuffers[ch] = ArrayPool<float>.Shared.Rent(internalFramesToRender);
                    Array.Clear(_channelBuffers[ch], 0, internalFramesToRender);
                }

                for (int ch = 0; ch < _channels.Length; ch++)
                {
                    var state = _channels[ch];
                    float[] chBuffer = _channelBuffers[ch];

                    if (state is { IsPlaying: true, Sample.FloatData.Length: > 0 })
                    {
                        for (int i = 0; i < internalFramesToRender; i++)
                        {
                            int sIndex = (int)state.SamplePosition;
                            if (sIndex < state.Sample.Length)
                            {
                                float sVal = state.Sample.FloatData[sIndex];

                                float currentVolume = state.Volume;

                                // Tremolo
                                if (state.TremoloActive)
                                {
                                    int tremVol = SineTable[state.TremoloPos & 31];
                                    if ((state.TremoloPos & 32) != 0) tremVol = -tremVol;
                                    float tremoloMod = tremVol * state.TremoloDepth / 128f;
                                    currentVolume += tremoloMod;
                                    if (currentVolume < 0) currentVolume = 0;
                                    if (currentVolume > 64) currentVolume = 64;
                                }

                                sVal *= (currentVolume / 64f);

                                chBuffer[i] = sVal;

                                int currentPeriod = state.Period;

                                // Arpeggio processing
                                if (state.ArpeggioActive)
                                {
                                    int arpStep = _tick % 3;
                                    if (arpStep > 0)
                                    {
                                        int halfTones = arpStep == 1 ? (state.ArpeggioParam >> 4) : (state.ArpeggioParam & 0x0F);
                                        int noteIndex = GetNoteIndex(state.Period);
                                        if (noteIndex >= 0)
                                        {
                                            int newIndex = noteIndex + halfTones;
                                            if (newIndex >= PeriodTable.Length) newIndex = PeriodTable.Length - 1;
                                            currentPeriod = PeriodTable[newIndex];
                                        }
                                    }
                                }

                                // Vibrato processing
                                if (state.VibratoActive)
                                {
                                    int vibVal = SineTable[state.VibratoPos & 31];
                                    if ((state.VibratoPos & 32) != 0) vibVal = -vibVal;
                                    int vibMod = vibVal * state.VibratoDepth / 128;
                                    currentPeriod += vibMod;
                                }

                                if (currentPeriod < 1) currentPeriod = 1; // Prevent div by zero

                                float frequency = AmigaClock / (currentPeriod * 2.0f);
                                float advance = frequency / (_waveFormat.SampleRate * _oversamplingFactor);

                                // Adjust advance since the sample table is upsampled from 8KHz to 44.1KHz
                                advance *= (float)SamplePreprocessor.L / SamplePreprocessor.M;

                                state.SamplePosition += advance;

                                if (state.SamplePosition >= state.Sample.Length)
                                {
                                    if (state.Sample.RepeatLength > 2)
                                    {
                                        state.SamplePosition -= state.Sample.RepeatLength;
                                        if (state.SamplePosition < state.Sample.RepeatOffset)
                                            state.SamplePosition = state.Sample.RepeatOffset;
                                    }
                                    else
                                    {
                                        state.IsPlaying = false;
                                        break;
                                    }
                                }
                            }
                            else
                            {
                                state.IsPlaying = false;
                                break;
                            }
                        }
                    }
                }

                MixChannels(outBuffer, offset, internalFramesToRender, ref samplesWritten);

                for (int ch = 0; ch < _channels.Length; ch++)
                {
                    ArrayPool<float>.Shared.Return(_channelBuffers[ch]);
                    _channelBuffers[ch] = null!;
                }
            }

            return samplesWritten;
        }

        private void MixChannels(float[] outBuffer, int offset, int internalFramesToRender, ref int samplesWritten)
        {
            for (int i = 0; i < internalFramesToRender; i++)
            {
                float leftMixed = 0;
                float rightMixed = 0;

                for (int ch = 0; ch < _channels.Length; ch++)
                {
                    float sVal = _channelBuffers[ch][i];
                    var state = _channels[ch];

                    float leftMix = sVal * (1 - state.Panning) * 0.5f;
                    float rightMix = sVal * (1 + state.Panning) * 0.5f;

                    leftMixed += leftMix;
                    rightMixed += rightMix;
                }

                if (leftMixed > 1f) leftMixed = 1f;
                if (leftMixed < -1f) leftMixed = -1f;
                if (rightMixed > 1f) rightMixed = 1f;
                if (rightMixed < -1f) rightMixed = -1f;

                if (_oversamplingEnabled)
                {
                    for (int f = 0; f < _leftFilters.Length; f++) leftMixed = _leftFilters[f].Transform(leftMixed);
                    for (int f = 0; f < _rightFilters.Length; f++) rightMixed = _rightFilters[f].Transform(rightMixed);
                }

                if (!_oversamplingEnabled || (i % _oversamplingFactor) == 0)
                {
                    outBuffer[offset + samplesWritten++] = leftMixed;
                    outBuffer[offset + samplesWritten++] = rightMixed;
                }

                _tickSamplePosition += 2;
            }
        }

        private void ProcessTick()
        {
            if (_tick == 0)
            {
                if (_nextOrder != -1)
                {
                    _currentOrder = _nextOrder;
                    _currentRow = (_nextRow != -1) ? _nextRow : 0;
                    _nextOrder = -1;
                    _nextRow = -1;
                }

                if (_currentOrder >= _module.SongLength)
                {
                    _currentOrder = _module.RestartPosition;
                    if (_currentOrder >= _module.SongLength) _currentOrder = 0;
                    _currentRow = 0;
                }

                ProcessRow();
            }
            else
            {
                ProcessEffects();
            }

            _tick++;
            if (_tick >= _speed)
            {
                _tick = 0;
                _currentRow++;

                if (_nextOrder == -1) // If no jump occurred
                {
                    int currentPatternIdx = _module.Sequence[_currentOrder];
                    if (currentPatternIdx < _module.Patterns.Length)
                    {
                        var pattern = _module.Patterns[currentPatternIdx];
                        if (_currentRow >= pattern.Rows)
                        {
                            _currentRow = 0;
                            _currentOrder++;
                        }
                    }
                    else
                    {
                        _currentOrder = _module.RestartPosition;
                        _currentRow = 0;
                    }
                }
            }
        }

        private void ProcessRow()
        {
            int currentPatternIdx = _module.Sequence[_currentOrder];
            if (currentPatternIdx >= _module.Patterns.Length) return;

            var pattern = _module.Patterns[currentPatternIdx];

            for (int c = 0; c < _module.NumberOfChannels; c++)
            {
                var note = pattern.Notes[_currentRow, c];
                var state = _channels[c];

                int cmd = note.Effect;
                int param = note.EffectParam;
                int x = (param >> 4) & 0xF;
                int y = param & 0xF;

                if (note.Sample > 0 && note.Sample <= _module.Samples.Length)
                {
                    state.Sample = _module.Samples[note.Sample - 1];
                    state.Volume = state.Sample.Volume;
                }

                if (note.Period > 0)
                {
                    if (cmd != 0x03 && cmd != 0x05) // Tone Portamento
                    {
                        state.Period = note.Period;
                        state.SamplePosition = 0;
                        state.IsPlaying = true;

                        if (cmd == 0x09) // Sample Offset
                        {
                            if (param > 0) state.SampleOffsetParam = param;
                            state.SamplePosition = state.SampleOffsetParam * 256 * ((float)SamplePreprocessor.L / SamplePreprocessor.M);
                        }
                    }

                    state.TargetPeriod = note.Period;
                }

                // Reset per-row effect state
                state.ArpeggioActive = false;

                if (cmd != 0x04 && cmd != 0x06) // Vibrato
                {
                    state.VibratoActive = false;
                }

                if (cmd != 0x07) // Tremolo
                {
                    state.TremoloActive = false;
                }

                // Process immediate effects
                switch (cmd)
                {
                    case 0x00: // Arpeggio
                        if (param > 0)
                        {
                            state.ArpeggioActive = true;
                            state.ArpeggioParam = param;
                        }
                        break;
                    case 0x01: // Portamento Up
                        if (param > 0) state.SlideSpeed = param;
                        break;
                    case 0x02: // Portamento Down
                        if (param > 0) state.SlideSpeed = param;
                        break;
                    case 0x03: // Tone Portamento
                        if (param > 0) state.TonePortamentoSpeed = param;
                        break;
                    case 0x04: // Vibrato
                        if (x > 0) state.VibratoSpeed = x;
                        if (y > 0) state.VibratoDepth = y;
                        state.VibratoActive = true;
                        // Vibrato position updates per tick, not per row unless restarted
                        break;
                    case 0x05: // Tone Portamento + Vol Slide
                        // Uses previous tone portamento speed
                        break;
                    case 0x06: // Vibrato + Vol Slide
                        // Uses previous vibrato state
                        state.VibratoActive = true;
                        break;
                    case 0x07: // Tremolo
                        if (x > 0) state.TremoloSpeed = x;
                        if (y > 0) state.TremoloDepth = y;
                        state.TremoloActive = true;
                        break;
                    case 0x08: // Panning (rare in standard MOD, ignored for now)
                        break;
                    case 0x0A: // Volume Slide
                        // Params are processed per tick
                        break;
                    case 0x0B: // Position Jump
                        _nextOrder = param;
                        _nextRow = 0;
                        break;
                    case 0x0C: // Set Volume
                        state.Volume = param;
                        if (state.Volume > 64) state.Volume = 64;
                        if (state.Volume < 0) state.Volume = 0;
                        break;
                    case 0x0D: // Pattern Break
                        _nextOrder = _currentOrder + 1;
                        _nextRow = x * 10 + y; // BCD format
                        break;
                    case 0x0E: // Extended Commands
                        if (x == 0xA) // Fine Volume Slide Up
                        {
                            state.Volume += y;
                            if (state.Volume > 64) state.Volume = 64;
                        }
                        else if (x == 0xB) // Fine Volume Slide Down
                        {
                            state.Volume -= y;
                            if (state.Volume < 0) state.Volume = 0;
                        }
                        break;
                    case 0x0F: // Set Speed/Tempo
                        if (param > 0)
                        {
                            if (param < 32)
                            {
                                _speed = param;
                            }
                            else
                            {
                                _tempo = param;
                                UpdateSamplesPerTick();
                            }
                        }
                        break;
                }
            }
        }

        private void ProcessEffects()
        {
            int currentPatternIdx = _module.Sequence[_currentOrder];
            if (currentPatternIdx >= _module.Patterns.Length) return;
            var pattern = _module.Patterns[currentPatternIdx];

            for (int c = 0; c < _module.NumberOfChannels; c++)
            {
                var state = _channels[c];
                var note = pattern.Notes[_currentRow, c];

                int cmd = note.Effect;
                int param = note.EffectParam;
                int x = (param >> 4) & 0xF;
                int y = param & 0xF;

                // Tone Portamento shared logic
                if (cmd == 0x03 || cmd == 0x05)
                {
                    if (state.Period < state.TargetPeriod)
                    {
                        state.Period += state.TonePortamentoSpeed;
                        if (state.Period > state.TargetPeriod) state.Period = state.TargetPeriod;
                    }
                    else if (state.Period > state.TargetPeriod)
                    {
                        state.Period -= state.TonePortamentoSpeed;
                        if (state.Period < state.TargetPeriod) state.Period = state.TargetPeriod;
                    }
                }

                // Volume Slide shared logic
                if (cmd == 0x05 || cmd == 0x06 || cmd == 0x0A)
                {
                    if (x > 0) state.Volume += x;
                    else if (y > 0) state.Volume -= y;

                    if (state.Volume > 64) state.Volume = 64;
                    if (state.Volume < 0) state.Volume = 0;
                }

                switch (cmd)
                {
                    case 0x01: // Portamento Up
                        state.Period -= state.SlideSpeed;
                        if (state.Period < 113) state.Period = 113; // B-3
                        break;
                    case 0x02: // Portamento Down
                        state.Period += state.SlideSpeed;
                        if (state.Period > 856) state.Period = 856; // C-1
                        break;
                    case 0x04: // Vibrato
                    case 0x06: // Vibrato + Vol Slide
                        state.VibratoPos = (state.VibratoPos + state.VibratoSpeed) & 63;
                        break;
                    case 0x07: // Tremolo
                        state.TremoloPos = (state.TremoloPos + state.TremoloSpeed) & 63;
                        break;
                }
            }
        }

        private int GetNoteIndex(int period)
        {
            // Find closest period
            int closestIndex = -1;
            int minDiff = int.MaxValue;
            for (int i = 0; i < PeriodTable.Length; i++)
            {
                int diff = Math.Abs(PeriodTable[i] - period);
                if (diff < minDiff)
                {
                    minDiff = diff;
                    closestIndex = i;
                }
            }
            return closestIndex;
        }

        private void UpdateSamplesPerTick()
        {
            double secondsPerTick = 2.5 / _tempo;
            _samplesPerTick = (int)(WaveFormat.SampleRate * secondsPerTick * _oversamplingFactor) * 2;
        }

        public void Seek(int order, int row)
        {
            _currentOrder = order;
            if (_currentOrder >= _module.SongLength) _currentOrder = 0;
            _currentRow = row;
            _tick = 0;
            _tickSamplePosition = 0;
            _nextOrder = -1;
            _nextRow = -1;

            foreach (var ch in _channels)
            {
                ch.IsPlaying = false;
                ch.Sample = null;
            }
        }

        public double GetCurrentTimeInSeconds()
        {
            // Dummy approximation
            return _currentOrder * 10.0;
        }
    }

    public class ChannelState
    {
        public Sample? Sample;
        public float SamplePosition;
        public int Period;
        public int Volume;
        public bool IsPlaying;

        // Effect State
        public int TargetPeriod;
        public int SlideSpeed;
        public int TonePortamentoSpeed;

        public bool VibratoActive;
        public int VibratoPos;
        public int VibratoSpeed;
        public int VibratoDepth;

        public bool TremoloActive;
        public int TremoloPos;
        public int TremoloSpeed;
        public int TremoloDepth;

        public bool ArpeggioActive;
        public int ArpeggioParam;

        public int SampleOffsetParam;

        public float Panning { get; set; }
    }

    public enum ChannelStereoMode
    {
        Mono,
        HardPanning,
        MidPanning,
        HardSpread,
        MidSpread
    }
}
