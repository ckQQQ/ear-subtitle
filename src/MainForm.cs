using System.Diagnostics;
using System.Text;
using System.Threading.Channels;

namespace EarSubtitle;

sealed class MainForm : Form
{
    readonly ComboBox devices = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 580 };
    readonly TextBox python = new() { Width = 580 };
    readonly Button refresh = new() { Text = "重新整理", AutoSize = true };
    readonly Button browse = new() { Text = "選擇 Python", AutoSize = true };
    readonly Button setup = new() { Text = "① 安裝模型", AutoSize = true };
    readonly Button test = new() { Text = "測試音源（5 秒）", AutoSize = true };
    readonly Button start = new() { Text = "② 開始字幕", AutoSize = true };
    readonly Button stop = new() { Text = "停止", AutoSize = true, Enabled = false };
    readonly Button export = new() { Text = "匯出 SRT", AutoSize = true };
    readonly CheckBox floating = new() { Text = "浮動字幕", AutoSize = true };
    readonly Label status = new() { Text = "待命｜首次使用請先安裝模型", AutoSize = true };
    readonly Label chinese = new() { Text = "繁體中文字幕會顯示在這裡", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Microsoft JhengHei UI", 23, FontStyle.Bold) };
    readonly Label english = new() { Text = "English transcription", Dock = DockStyle.Fill, ForeColor = Color.LightGray, Font = new Font("Segoe UI", 12) };
    readonly RichTextBox history = new() { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.FromArgb(26, 34, 47), ForeColor = Color.WhiteSmoke, BorderStyle = BorderStyle.None };
    readonly RichTextBox logs = new() { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.FromArgb(16, 22, 32), ForeColor = Color.Silver, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 9) };
    readonly ProgressBar meter = new() { Width = 210, Maximum = 100 };
    readonly List<Caption> captions = new();
    readonly System.Windows.Forms.Timer levelTimer = new() { Interval = 100 };
    readonly Form overlay = new() { Text = "繁體中文字幕", TopMost = true, Size = new Size(850, 170), MinimumSize = new Size(400, 120), BackColor = Color.FromArgb(20, 25, 33), ShowInTaskbar = false };
    readonly Label overlayText = new() { Text = "等待英文語音…", Dock = DockStyle.Fill, ForeColor = Color.White, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Microsoft JhengHei UI", 22, FontStyle.Bold), Padding = new Padding(12) };
    readonly string root;
    CancellationTokenSource? cancellation;
    Task? session;
    Task? installTask;
    Process? installer;
    volatile float level;
    bool closing;

    public MainForm()
    {
        root = FindRoot();
        Text = "耳機英文 → 繁體中文字幕｜本機模式";
        Size = new Size(1020, 850); MinimumSize = new Size(900, 730);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(16, 22, 32); ForeColor = Color.WhiteSmoke;
        Font = new Font("Microsoft JhengHei UI", 10);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 1, RowCount = 11 };
        Controls.Add(layout);
        float[] heights = { 48, 35, 40, 40, 44, 34, 104, 58, 24, 0, 105 };
        foreach (var h in heights) layout.RowStyles.Add(h == 0 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.Absolute, h));
        layout.Controls.Add(new Label { Text = "耳機英文即時字幕", AutoSize = true, Font = new Font(Font.FontFamily, 22, FontStyle.Bold) }, 0, 0);
        layout.Controls.Add(new Label { Text = "本機辨識・離線翻譯・繁體中文　｜　Teams 的喇叭輸出請選同一副耳機", AutoSize = true, ForeColor = Color.LightSkyBlue }, 0, 1);
        layout.Controls.Add(Row(new Label { Text = "播放裝置", Width = 82, AutoSize = false }, devices, refresh), 0, 2);
        layout.Controls.Add(Row(new Label { Text = "Python", Width = 82, AutoSize = false }, python, browse), 0, 3);
        layout.Controls.Add(Row(setup, test, start, stop, floating, export), 0, 4);
        layout.Controls.Add(Row(status, meter), 0, 5);
        layout.Controls.Add(chinese, 0, 6); layout.Controls.Add(english, 0, 7);
        layout.Controls.Add(new Label { Text = "字幕紀錄（只保留於本次視窗，按匯出才存檔）", AutoSize = true }, 0, 8);
        layout.Controls.Add(history, 0, 9); layout.Controls.Add(logs, 0, 10);
        overlay.Controls.Add(overlayText);
        overlay.FormClosing += (_, e) => { if (!closing) { e.Cancel = true; floating.Checked = false; } };
        floating.CheckedChanged += (_, _) => { if (floating.Checked) overlay.Show(this); else overlay.Hide(); };
        refresh.Click += (_, _) => RefreshDevices();
        browse.Click += (_, _) => { using var d = new OpenFileDialog { Filter = "Python|python.exe", Title = "選擇 Python 3.10～3.12 x64 的 python.exe" }; if (d.ShowDialog() == DialogResult.OK) python.Text = d.FileName; };
        setup.Click += async (_, _) => { installTask = Install(); await installTask; installTask = null; };
        test.Click += async (_, _) => await TestAudio();
        start.Click += (_, _) => Begin();
        stop.Click += async (_, _) => await Stop();
        export.Click += (_, _) => Export();
        levelTimer.Tick += (_, _) => meter.Value = Math.Clamp((int)(level * 250), 0, 100);
        levelTimer.Start();
        Shown += (_, _) => { RefreshDevices(); python.Text = DefaultPython(); Log("音訊只經由本機記憶體傳給辨識引擎，不會上傳或儲存錄音。第一次安裝模型需要網路。"); };
        FormClosing += async (_, e) =>
        {
            if (closing) return;
            e.Cancel = true; closing = true;
            await Stop(); overlay.Close(); Close();
        };
    }
    static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = Padding.Empty };
        foreach (var c in controls) { c.Margin = new Padding(0, 2, 10, 0); row.Controls.Add(c); }
        return row;
    }
    static string FindRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null) { if (File.Exists(Path.Combine(d.FullName, "backend.py"))) return d.FullName; d = d.Parent; }
        return AppContext.BaseDirectory;
    }
    string DefaultPython()
    {
        var saved = Path.Combine(root, "python-path.txt");
        if (File.Exists(saved) && File.Exists(File.ReadAllText(saved).Trim())) return File.ReadAllText(saved).Trim();
        var conda = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "anaconda3");
        foreach (var p in new[] { Path.Combine(conda, "envs", "ChatTTS-Enhanced", "python.exe"), Path.Combine(conda, "python.exe") }) if (File.Exists(p)) return p;
        return "python";
    }
    void UI(Action action) { if (IsDisposed || !IsHandleCreated) return; if (InvokeRequired) BeginInvoke(() => { if (!IsDisposed) action(); }); else action(); }
    void Log(string text) => UI(() => { if (logs.TextLength > 30000) logs.Clear(); logs.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}\n"); logs.ScrollToCaret(); });
    void Status(string text) => UI(() => status.Text = text);
    void Busy(bool busy) { start.Enabled = setup.Enabled = test.Enabled = refresh.Enabled = devices.Enabled = python.Enabled = browse.Enabled = !busy; stop.Enabled = busy; }
    void RefreshDevices()
    {
        try { var id = (devices.SelectedItem as AudioDevice)?.Id; devices.Items.Clear(); foreach(var d in Loopback.Devices()) devices.Items.Add(d); if (devices.Items.Count > 0) devices.SelectedIndex = 0;
            if (id != null) for (int i = 0; i < devices.Items.Count; i++) if (((AudioDevice)devices.Items[i]!).Id == id) devices.SelectedIndex = i;
        } catch (Exception e) { Log(e.Message); }
    }
    async Task Install()
    {
        Busy(true); Status("正在安裝套件與模型…");
        try
        {
            File.WriteAllText(Path.Combine(root, "python-path.txt"), python.Text.Trim());
            var info = Backend.StartInfo(python.Text.Trim(), root);
            info.ArgumentList.Add("-u"); info.ArgumentList.Add(Path.Combine(root, "setup_models.py"));
            installer = Process.Start(info) ?? throw new Exception("無法啟動 Python。");
            var p = installer;
            async Task Read(StreamReader reader) { while (await reader.ReadLineAsync() is {} line) Log(line); }
            await Task.WhenAll(Read(p.StandardOutput), Read(p.StandardError), p.WaitForExitAsync());
            Status(p.ExitCode == 0 ? "模型就緒｜請選音源並開始字幕" : "安裝未完成｜請查看下方錯誤");
        }
        catch (Exception e) { Log(e.Message); Status("安裝失敗"); }
        finally { installer?.Dispose(); installer = null; Busy(false); }
    }
    async Task TestAudio()
    {
        if (devices.SelectedItem is not AudioDevice device) return;
        Busy(true); cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Status("測試中｜請讓 Teams 或影片播放英文");
        int samples = 0; float max = 0;
        try
        {
            var token = cancellation.Token;
            session = Task.Run(() => Loopback.Run(device.Id, s => { samples += s.Length; level = s.Length > 0 ? s.Max(Math.Abs) : 0; max = Math.Max(max, level); }, token));
            await session;
            Status(max > 0.001 ? "音源測試通過｜已收到播放聲音" : "未收到聲音｜請確認播放裝置與 Teams 設定");
            Log($"音源測試：{samples} samples，峰值 {max:F4}。不會保存測試錄音。");
        }
        catch (Exception e) { Log(e.Message); Status("音源測試失敗"); }
        finally { cancellation?.Dispose(); cancellation = null; session = null; level = 0; Busy(false); }
    }
    void Begin()
    {
        if (devices.SelectedItem is not AudioDevice device) { Log("請先選擇耳機播放裝置。"); return; }
        var workerPython = Path.Combine(root, ".venv", "Scripts", "python.exe");
        if (!File.Exists(workerPython) || !File.Exists(Path.Combine(root, "models", "whisper-base.en", "model.bin")))
        { Status("模型尚未就緒｜請先按『安裝模型』"); Log("需要先完成套件與模型安裝，才能辨識與翻譯。"); return; }
        captions.Clear(); history.Clear(); chinese.Text = "載入本機模型…"; english.Text = "";
        Busy(true); cancellation?.Dispose(); cancellation = new CancellationTokenSource();
        session = Run(device.Id, workerPython, cancellation.Token);
    }
    async Task Run(string device, string workerPython, CancellationToken token)
    {
        try
        {
            using var backend = new Backend(workerPython, root, Log);
            Status("載入模型中…"); await backend.Ready(token);
            var queue = Channel.CreateBounded<AudioChunk>(new BoundedChannelOptions(2) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
            int dropped = 0;
            var segmenter = new Segmenter(chunk => { if (!queue.Writer.TryWrite(chunk)) { dropped++; Status($"處理速度不足｜已略過 {dropped} 段音訊"); } });
            Status("擷取中｜等待英文語音"); chinese.Text = "等待英文語音…";
            var gate = new object();
            var captureTask = Task.Run(() =>
            {
                try { Loopback.Run(device, s => { level = s.Length > 0 ? s.Max(Math.Abs) : 0; lock(gate) segmenter.Add(s); }, token); }
                finally { lock(gate) segmenter.Flush(); queue.Writer.TryComplete(); }
            }, CancellationToken.None);
            var timerTask = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested && !captureTask.IsCompleted)
                { lock (gate) segmenter.Tick(); await Task.Delay(100, token); }
            }, CancellationToken.None);
            try
            {
                await foreach (var chunk in queue.Reader.ReadAllAsync(token))
                {
                    Status("辨識／翻譯中…");
                    var result = await backend.Recognize(chunk.Samples, token);
                    if (!string.IsNullOrWhiteSpace(result.En))
                    {
                        chinese.Text = overlayText.Text = result.Zh; english.Text = result.En;
                        captions.Add(new Caption(chunk.Start, chunk.End, result.En, result.Zh));
                        history.AppendText($"[{TimeSpan.FromSeconds(chunk.Start):mm\\:ss}] {result.Zh}\n{result.En}\n\n"); history.ScrollToCaret();
                    }
                    Status($"擷取中｜上段處理 {result.Seconds:F1} 秒｜略過 {dropped} 段");
                }
                await captureTask;
            }
            finally
            {
                cancellation?.Cancel();
                try { await captureTask; } catch (OperationCanceledException) { }
                try { await timerTask; } catch (OperationCanceledException) { }
            }
        }
        catch (OperationCanceledException) { Status("已停止"); }
        catch (Exception e) { Log(e.Message); Status("發生錯誤｜請查看下方紀錄"); }
        finally { level = 0; Busy(false); }
    }
    async Task Stop()
    {
        cancellation?.Cancel();
        if (installer != null) { try { if (!installer.HasExited) installer.Kill(true); } catch (InvalidOperationException) { } }
        if (installTask != null) await installTask;
        if (session != null) await session;
        session = null; cancellation?.Dispose(); cancellation = null;
    }
    void Export()
    {
        if (captions.Count == 0) { Log("目前沒有可匯出的字幕。"); return; }
        using var d = new SaveFileDialog { Filter = "SRT 字幕|*.srt", FileName = $"字幕_{DateTime.Now:yyyyMMdd_HHmmss}.srt" };
        if (d.ShowDialog() == DialogResult.OK) { try { File.WriteAllText(d.FileName, Segmenter.Srt(captions), new UTF8Encoding(true)); Log("已匯出字幕。"); } catch (Exception e) { Log(e.Message); } }
    }
}
