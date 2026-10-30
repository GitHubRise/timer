using System;
using System.Collections.Generic;
using System.IO;
using System.Media;

namespace GameClock
{
    internal static class AlarmSound
    {
        private static readonly object Sync = new object();
        private static SoundPlayer _player;

        public static void Play()
        {
            try
            {
                lock (Sync)
                {
                    if (_player == null)
                    {
                        _player = new SoundPlayer(BuildWave());
                        _player.Load();
                    }
                    _player.Play();
                }
            }
            catch (Exception)
            {
            }
        }

        public static void DisposePlayer()
        {
            lock (Sync)
            {
                if (_player != null)
                {
                    try
                    {
                        _player.Dispose();
                    }
                    catch (Exception)
                    {
                    }
                    _player = null;
                }
            }
        }

        private static Stream BuildWave()
        {
            const int sampleRate = 44100;
            List<short> samples = new List<short>();

            AddTone(samples, sampleRate, 880.0, 0.16);
            AddSilence(samples, sampleRate, 0.05);
            AddTone(samples, sampleRate, 1174.7, 0.16);
            AddSilence(samples, sampleRate, 0.08);
            AddTone(samples, sampleRate, 880.0, 0.16);
            AddSilence(samples, sampleRate, 0.05);
            AddTone(samples, sampleRate, 1174.7, 0.16);
            AddSilence(samples, sampleRate, 0.10);

            int dataSize = samples.Count * sizeof(short);
            MemoryStream stream = new MemoryStream(44 + dataSize);
            BinaryWriter writer = new BinaryWriter(stream);
            writer.Write(new char[] { 'R', 'I', 'F', 'F' });
            writer.Write(36 + dataSize);
            writer.Write(new char[] { 'W', 'A', 'V', 'E' });
            writer.Write(new char[] { 'f', 'm', 't', ' ' });
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(sampleRate);
            writer.Write(sampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(new char[] { 'd', 'a', 't', 'a' });
            writer.Write(dataSize);
            for (int i = 0; i < samples.Count; i++)
            {
                writer.Write(samples[i]);
            }
            writer.Flush();
            stream.Position = 0;
            return stream;
        }

        private static void AddTone(List<short> samples, int sampleRate, double frequency, double seconds)
        {
            int count = Math.Max(1, (int)Math.Round(sampleRate * seconds));
            int fade = Math.Max(1, (int)(sampleRate * 0.006));
            for (int i = 0; i < count; i++)
            {
                double envelope = 1.0;
                if (i < fade)
                {
                    envelope = (double)i / fade;
                }
                int remaining = count - i;
                if (remaining < fade)
                {
                    envelope = Math.Min(envelope, (double)remaining / fade);
                }
                double value = Math.Sin(2.0 * Math.PI * frequency * i / sampleRate);
                samples.Add((short)(value * 0.24 * short.MaxValue * envelope));
            }
        }

        private static void AddSilence(List<short> samples, int sampleRate, double seconds)
        {
            int count = Math.Max(0, (int)Math.Round(sampleRate * seconds));
            for (int i = 0; i < count; i++)
            {
                samples.Add(0);
            }
        }
    }
}
