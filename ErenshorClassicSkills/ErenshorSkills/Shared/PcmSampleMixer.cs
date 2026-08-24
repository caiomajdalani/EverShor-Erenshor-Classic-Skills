using System;
using System.Collections.Generic;
using UnityEngine;

namespace ErenshorSkills
{
    /// <summary>
    /// Loads the embedded CC0 PCM WAV layers and mixes them under a
    /// runtime-synthesized clip.
    ///
    /// This lived privately inside MonkAudio, which is why the Necromancer had
    /// no recorded layer at all and read as a bare synth patch next to the
    /// Monk's sampled hits. Both classes now share one implementation and one
    /// parse cache.
    /// </summary>
    internal static class PcmSampleMixer
    {
        private const int SampleRate = 44100;

        internal sealed class WavData
        {
            internal float[] Samples;
            internal int Rate;
        }

        private static readonly Dictionary<string, WavData> Cache =
            new Dictionary<string, WavData>(StringComparer.OrdinalIgnoreCase);

        /// <summary>One recorded layer inside a composited clip.</summary>
        internal struct Layer
        {
            internal string Sample;
            internal float Gain;
            internal float Pitch;
            internal float Offset;
            /// <summary>Seconds of the source to keep, or 0 for all of it.</summary>
            internal float Length;

            internal Layer(string sample, float gain, float pitch,
                float offset = 0f, float length = 0f)
            {
                Sample = sample;
                Gain = gain;
                Pitch = pitch;
                Offset = offset;
                Length = length;
            }
        }

        /// <summary>
        /// Builds a clip purely by compositing recorded layers, with no
        /// oscillator content at all.
        ///
        /// Both classes previously generated their sounds from sine partials
        /// and mixed a recording underneath. Peak normalisation then let the
        /// dense synthesized bed mask the short recorded transient by roughly
        /// 14 dB, so every ability still sounded like its old synth sweep. The
        /// recordings are now the entire signal; per-ability identity comes
        /// from which layers are chosen and how they are pitched and stacked.
        /// </summary>
        internal static AudioClip BuildLayered(string name,
            params Layer[] layers)
        {
            if (layers == null || layers.Length == 0) return null;

            var resolved = new List<(float[] Data, Layer Spec, int Offset)>();
            int total = 0;
            foreach (Layer layer in layers)
            {
                WavData wav = LoadWav(layer.Sample);
                if (wav?.Samples == null || wav.Samples.Length == 0) continue;
                float step = Mathf.Max(0.10f, layer.Pitch) *
                    (wav.Rate / (float)SampleRate);
                int available = wav.Samples.Length;
                if (layer.Length > 0f)
                    available = Mathf.Min(available,
                        Mathf.CeilToInt(layer.Length * wav.Rate));
                int length = Mathf.CeilToInt(available / step);
                int offset = Mathf.Max(0,
                    Mathf.RoundToInt(layer.Offset * SampleRate));
                var data = new float[length];
                for (int index = 0; index < length; index++)
                {
                    float source = index * step;
                    int lower = Mathf.Clamp((int)source, 0, available - 1);
                    int upper = Mathf.Min(lower + 1, available - 1);
                    data[index] = Mathf.Lerp(wav.Samples[lower],
                        wav.Samples[upper], source - lower) * layer.Gain;
                }
                resolved.Add((data, layer, offset));
                total = Mathf.Max(total, offset + length);
            }
            if (resolved.Count == 0 || total <= 0) return null;

            var mixed = new float[total];
            foreach ((float[] data, Layer spec, int offset) in resolved)
                for (int index = 0; index < data.Length &&
                    offset + index < total; index++)
                    mixed[offset + index] = SoftClip(
                        mixed[offset + index] + data[index]);

            // Short fades so a trimmed layer cannot click at either end.
            int fade = Mathf.Min(160, total / 8);
            for (int index = 0; index < fade; index++)
            {
                float ramp = index / (float)fade;
                mixed[index] *= ramp;
                mixed[total - 1 - index] *= ramp;
            }

            Normalize(mixed, 0.96f);
            AudioClip clip = AudioClip.Create(name, mixed.Length, 1,
                SampleRate, false);
            clip.hideFlags = HideFlags.HideAndDontSave;
            clip.SetData(mixed, 0);
            return clip;
        }

        internal static WavData LoadWav(string filename)
        {
            if (Cache.TryGetValue(filename, out WavData cached))
                return cached;
            byte[] bytes = EmbeddedResources.GetBytesByFilename(filename);
            WavData parsed = ParsePcmWav(bytes);
            Cache[filename] = parsed;
            return parsed;
        }

        /// <summary>
        /// Resamples <paramref name="sampleName"/> by <paramref name="pitch"/>
        /// and sums it into a copy of <paramref name="generated"/>, returning a
        /// new clip and destroying the source. Returns the input unchanged when
        /// the sample is missing so a stripped build still produces audio.
        /// </summary>
        internal static AudioClip Mix(string name, AudioClip generated,
            string sampleName, float gain, float pitch, float offsetSeconds,
            float generatedGain = 1f)
        {
            WavData sample = LoadWav(sampleName);
            if (generated == null || sample?.Samples == null ||
                sample.Samples.Length == 0)
                return generated;

            int offset = Mathf.Max(0,
                Mathf.RoundToInt(offsetSeconds * SampleRate));
            float rateScale = sample.Rate / (float)SampleRate;
            float step = Mathf.Max(0.10f, pitch) * rateScale;
            int sampleLength = Mathf.CeilToInt(sample.Samples.Length / step);
            int count = Mathf.Max(generated.samples, offset + sampleLength);
            var mixed = new float[count];
            var baseData = new float[generated.samples];
            generated.GetData(baseData, 0);
            // generatedGain ducks the synthesized bed under the recording.
            // The final pass normalises to peak, and a dense sustained synth
            // layer has far more energy than a short recorded transient at the
            // same peak, so without ducking the synthesis masks the sample
            // completely: measured at roughly -14 dB for a Necromancer cast,
            // which is why every spell still sounded like its old sine sweep.
            if (generatedGain != 1f)
                for (int index = 0; index < baseData.Length; index++)
                    baseData[index] *= generatedGain;
            Array.Copy(baseData, mixed, baseData.Length);

            for (int index = 0; index < sampleLength &&
                offset + index < mixed.Length; index++)
            {
                float sourceIndex = index * step;
                int lower = Mathf.Clamp((int)sourceIndex, 0,
                    sample.Samples.Length - 1);
                int upper = Mathf.Min(lower + 1, sample.Samples.Length - 1);
                float value = Mathf.Lerp(sample.Samples[lower],
                    sample.Samples[upper], sourceIndex - lower);
                mixed[offset + index] = SoftClip(
                    mixed[offset + index] + value * gain);
            }

            Normalize(mixed, 0.94f);
            AudioClip result = AudioClip.Create(name, mixed.Length, 1,
                SampleRate, false);
            result.hideFlags = HideFlags.HideAndDontSave;
            result.SetData(mixed, 0);
            UnityEngine.Object.Destroy(generated);
            return result;
        }

        private static WavData ParsePcmWav(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 44 ||
                ReadTag(bytes, 0) != "RIFF" || ReadTag(bytes, 8) != "WAVE")
                return null;
            int channels = 0;
            int rate = 0;
            int bits = 0;
            int format = 0;
            int dataOffset = -1;
            int dataSize = 0;
            for (int cursor = 12; cursor + 8 <= bytes.Length;)
            {
                string tag = ReadTag(bytes, cursor);
                int size = ReadInt32(bytes, cursor + 4);
                int payload = cursor + 8;
                if (size < 0 || payload + size > bytes.Length) break;
                if (tag == "fmt " && size >= 16)
                {
                    format = ReadInt16(bytes, payload);
                    channels = ReadInt16(bytes, payload + 2);
                    rate = ReadInt32(bytes, payload + 4);
                    bits = ReadInt16(bytes, payload + 14);
                }
                else if (tag == "data")
                {
                    dataOffset = payload;
                    dataSize = size;
                    break;
                }
                cursor = payload + size + (size & 1);
            }
            if (format != 1 || channels < 1 || rate < 8000 || bits != 16 ||
                dataOffset < 0 || dataSize < channels * 2)
                return null;
            int frames = dataSize / (channels * 2);
            var samples = new float[frames];
            for (int frame = 0; frame < frames; frame++)
            {
                float sum = 0f;
                for (int channel = 0; channel < channels; channel++)
                {
                    int offset = dataOffset + (frame * channels + channel) * 2;
                    short value = (short)(bytes[offset] |
                        bytes[offset + 1] << 8);
                    sum += value / 32768f;
                }
                samples[frame] = sum / channels;
            }
            return new WavData { Samples = samples, Rate = rate };
        }

        private static string ReadTag(byte[] bytes, int offset) =>
            offset + 4 <= bytes.Length
                ? System.Text.Encoding.ASCII.GetString(bytes, offset, 4)
                : "";

        private static int ReadInt16(byte[] bytes, int offset) =>
            bytes[offset] | bytes[offset + 1] << 8;

        private static int ReadInt32(byte[] bytes, int offset) =>
            bytes[offset] | bytes[offset + 1] << 8 |
            bytes[offset + 2] << 16 | bytes[offset + 3] << 24;

        private static void Normalize(float[] values, float target)
        {
            float peak = 0f;
            for (int index = 0; index < values.Length; index++)
                peak = Mathf.Max(peak, Mathf.Abs(values[index]));
            if (peak <= 0.0001f) return;
            float scale = target / peak;
            for (int index = 0; index < values.Length; index++)
                values[index] *= scale;
        }

        private static float SoftClip(float value) =>
            value / (1f + Mathf.Abs(value) * 0.45f);
    }
}
