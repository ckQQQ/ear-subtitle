using System.Text.Json;

namespace EarSubtitle;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length == 2 && args[0] == "--probe")
        {
            try
            {
                File.WriteAllText(args[1], JsonSerializer.Serialize(Loopback.Devices()));
                return 0;
            }
            catch (Exception e) { File.WriteAllText(args[1], e.ToString()); return 1; }
        }
        if (args.Length == 2 && args[0] is "--capture-test" or "--tone-test")
        {
            try
            {
                var devices = Loopback.Devices();
                using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                Task tone = args[0] == "--tone-test" ? Task.Run(async () =>
                {
                    await Task.Delay(600);
                    using var wav = new MemoryStream();
                    using (var writer = new BinaryWriter(wav, System.Text.Encoding.ASCII, true))
                    {
                        writer.Write("RIFF"u8); writer.Write(36 + 32000); writer.Write("WAVEfmt "u8);
                        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(16000);
                        writer.Write(32000); writer.Write((short)2); writer.Write((short)16);
                        writer.Write("data"u8); writer.Write(32000);
                        for (int i = 0; i < 16000; i++) writer.Write((short)(655 * Math.Sin(i * 2 * Math.PI * 440 / 16000)));
                    }
                    wav.Position = 0;
                    using var player = new System.Media.SoundPlayer(wav); player.PlaySync();
                }) : Task.CompletedTask;
                var count = 0; double peak = 0;
                Loopback.Run(devices[0].Id, samples => { count += samples.Length; foreach(var v in samples) peak = Math.Max(peak, Math.Abs(v)); }, stop.Token);
                tone.GetAwaiter().GetResult();
                File.WriteAllText(args[1], JsonSerializer.Serialize(new { device = devices[0].Name, samples = count, peak, seconds = count / 16000d }));
                if (args[0] == "--tone-test" && (count == 0 || peak < 0.00001)) return 2;
                return 0;
            }
            catch (Exception e) { File.WriteAllText(args[1], e.ToString()); return 1; }
        }
        Application.Run(new MainForm());
        return 0;
    }
}
