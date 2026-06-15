using System;
using System.IO;
using System.Text;
using NAudio.Wave;
using NeoPaula.Engine;
using NeoPaula.Formats;

namespace NeoPaula
{
    public class NeoPaulaPlayer : IDisposable
    {
        private IWavePlayer _wavePlayer;
        private TrackerSampleProvider? _trackerProvider;
        private MemoryStream? _streamCopy;

        public NeoPaulaPlayer()
        {
            _wavePlayer = new WaveOutEvent();
        }

        public void Play(string filename)
        {
            Stop();
            var fileBytes = File.ReadAllBytes(filename);
            Play(new MemoryStream(fileBytes));
        }

        public void Play(Stream stream)
        {
            Stop();

            // We need a seekable stream to parse and keep in memory for playback
            if (stream is MemoryStream ms)
            {
                _streamCopy = ms;
            }
            else
            {
                _streamCopy = new MemoryStream();
                stream.CopyTo(_streamCopy);
            }
            _streamCopy.Position = 0;

            // Detect format and parse
            var info = GetTrackInfo(_streamCopy);
            _streamCopy.Position = 0;

            Module? module = null;

            if (info.Format == "Protracker MOD")
            {
                module = ModParser.Parse(_streamCopy);
            }
            else if (info.Format.StartsWith("OctaMED"))
            {
                module = MmdParser.Parse(_streamCopy);
            }
            else
            {
                throw new NotSupportedException($"Unsupported format: {info.Format}");
            }

            _trackerProvider = new TrackerSampleProvider(module, 44100);

            _wavePlayer.Init(_trackerProvider);
            _wavePlayer.Play();
        }

        public void Pause()
        {
            if (_wavePlayer.PlaybackState == PlaybackState.Playing)
            {
                _wavePlayer.Pause();
            }
        }

        public void Resume()
        {
            if (_wavePlayer.PlaybackState == PlaybackState.Paused)
            {
                _wavePlayer.Play();
            }
        }

        public void Stop()
        {
            if (_wavePlayer.PlaybackState != PlaybackState.Stopped)
            {
                _wavePlayer.Stop();
            }
            _streamCopy?.Dispose();
            _streamCopy = null;
            _trackerProvider = null;
        }

        public void SeekOrder(int order)
        {
            if (_trackerProvider != null)
            {
                _trackerProvider.Seek(order, 0);
            }
        }

        public TrackInfo GetTrackInfo(string filename)
        {
            using (var stream = File.OpenRead(filename))
            {
                return GetTrackInfo(stream);
            }
        }

        public TrackInfo GetTrackInfo(Stream stream)
        {
            var info = new TrackInfo();
            long originalPosition = stream.Position;

            // Check for MMD (OctaMED) magics
            byte[] magic = new byte[4];
            stream.ReadExactly(magic, 0, 4);
            string magicStr = Encoding.ASCII.GetString(magic);

            if (magicStr == "MMD0" || magicStr == "MMD1" || magicStr == "MMD2" || magicStr == "MMD3")
            {
                info.Format = "OctaMED (" + magicStr + ")";
                info.Title = "OctaMED Module";
                info.Channels = 4;
                stream.Position = originalPosition;
                return info;
            }

            // Check for MOD magics at offset 1080
            if (stream.Length >= 1084)
            {
                stream.Position = originalPosition + 1080;
                stream.ReadExactly(magic, 0, 4);
                magicStr = Encoding.ASCII.GetString(magic);

                int channels = ModParser.GetChannelsFromMagic(magicStr);
                if (channels > 0)
                {
                    info.Format = "Protracker MOD";
                    info.Channels = channels;

                    // Title is at offset 0, 20 bytes
                    stream.Position = originalPosition;
                    byte[] titleBytes = new byte[20];
                    stream.ReadExactly(titleBytes, 0, 20);

                    int len = 0;
                    while(len < 20 && titleBytes[len] != 0) len++;
                    info.Title = Encoding.ASCII.GetString(titleBytes, 0, len).Trim();

                    stream.Position = originalPosition;
                    return info;
                }
            }

            stream.Position = originalPosition;
            info.Format = "Unknown";
            return info;
        }

        public void Dispose()
        {
            Stop();
            _wavePlayer?.Dispose();
        }
    }
}
