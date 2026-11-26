using System.Diagnostics;

namespace EarSubtitle;

record AudioChunk(float[] Samples, double Start, double End);
record Caption(double Start, double End, string English, string Chinese);

sealed class Segmenter(Action<AudioChunk> ready)
{
    readonly List<float> pending = new();
    readonly Stopwatch clock = Stopwatch.StartNew();
    double start, lastPacket;
    int quiet, speech;
    public void Add(float[] samples)
    {
        lastPacket = clock.Elapsed.TotalSeconds;
        double rms = Math.Sqrt(samples.Sum(x => (double)x * x) / Math.Max(1, samples.Length));
        bool voiced = rms > 0.003;
        if (pending.Count == 0 && !voiced) return;
        if (pending.Count == 0) start = Math.Max(0, lastPacket - samples.Length / 16000d);
        pending.AddRange(samples);
        if (voiced) { quiet = 0; speech += samples.Length; } else quiet += samples.Length;
        if (pending.Count >= 16000 * 6 || quiet >= 11200) Flush();
    }
    public void Tick()
    {
        // WASAPI may produce no buffers at all when playback stops.
        if (pending.Count > 0 && clock.Elapsed.TotalSeconds - lastPacket > 0.75) Flush();
    }
    public void Flush()
    {
        if (speech >= 3200) ready(new AudioChunk(pending.ToArray(), start, start + pending.Count / 16000d));
        pending.Clear(); quiet = 0; speech = 0;
    }
    public static byte[] Pcm16(float[] samples)
    {
        var pcm = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short value = (short)Math.Clamp(samples[i] * 32767, -32768, 32767);
            pcm[2 * i] = (byte)value; pcm[2 * i + 1] = (byte)(value >> 8);
        }
        return pcm;
    }
    public static string Srt(IEnumerable<Caption> captions)
    {
        static string Stamp(double seconds) => TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss\,fff");
        return string.Join("\r\n", captions.Select((c, i) => $"{i + 1}\r\n{Stamp(c.Start)} --> {Stamp(c.End)}\r\n{c.Chinese}\r\n{c.English}\r\n"));
    }
}
