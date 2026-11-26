using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace EarSubtitle;

sealed class Backend : IDisposable
{
    readonly Process process;
    readonly Task stderr;
    public Backend(string python, string root, Action<string> log)
    {
        var info = StartInfo(python, root);
        info.ArgumentList.Add("-u"); info.ArgumentList.Add(Path.Combine(root, "backend.py")); info.ArgumentList.Add("--worker");
        process = Process.Start(info) ?? throw new Exception("無法啟動辨識引擎。");
        stderr = Task.Run(async () => { while (await process.StandardError.ReadLineAsync() is { } line) log(line); });
    }
    public static ProcessStartInfo StartInfo(string exe, string root)
    {
        var info = new ProcessStartInfo(exe) { WorkingDirectory = root, UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        info.Environment["PYTHONUTF8"] = "1";
        return info;
    }
    async Task<JsonElement> Read(CancellationToken token)
    {
        var line = await process.StandardOutput.ReadLineAsync(token);
        if (line == null) throw new Exception("辨識引擎已結束，請查看狀態紀錄。");
        using var doc = JsonDocument.Parse(line);
        var result = doc.RootElement.Clone();
        var type = result.GetProperty("type").GetString();
        if (type is "fatal" or "error") throw new Exception(result.GetProperty("message").GetString());
        return result;
    }
    public async Task Ready(CancellationToken token)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(TimeSpan.FromMinutes(3));
        var data = await Read(limit.Token);
        if (data.GetProperty("type").GetString() != "ready") throw new Exception("辨識引擎回應不正確。");
    }
    public async Task<(string En, string Zh, double Seconds)> Recognize(float[] samples, CancellationToken token)
    {
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { id = 1, pcm = Convert.ToBase64String(Segmenter.Pcm16(samples)) }));
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(TimeSpan.FromSeconds(90));
        var data = await Read(limit.Token);
        return (data.GetProperty("en").GetString()!, data.GetProperty("zh").GetString()!, data.GetProperty("elapsed").GetDouble());
    }
    public void Dispose()
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        process.Dispose();
    }
}
