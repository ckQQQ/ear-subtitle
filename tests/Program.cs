using EarSubtitle;

void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); }
var chunks = new List<AudioChunk>();
var splitter = new Segmenter(chunks.Add);
splitter.Add(new float[16000]);
splitter.Flush();
Check(chunks.Count == 0, "silence does not generate subtitles");
splitter.Add(Enumerable.Repeat(0.1f, 16000).ToArray());
splitter.Add(new float[11200]);
Check(chunks.Count == 1 && chunks[0].Samples.Length == 27200, "speech ends after quiet interval");
Check(chunks[0].End > chunks[0].Start && chunks[0].Start >= 0, "nonnegative chronological timestamps");
splitter.Add(Enumerable.Repeat(0.1f, 96000).ToArray());
Check(chunks.Count == 2, "continuous speech has bounded segment length");
var pcm = Segmenter.Pcm16(new float[] { -2, 0, 1, 2 });
Check(BitConverter.ToInt16(pcm, 0) == -32768 && BitConverter.ToInt16(pcm, 2) == 0 && BitConverter.ToInt16(pcm, 4) == 32767 && BitConverter.ToInt16(pcm, 6) == 32767, "PCM clipping and little endian");
var srt = Segmenter.Srt(new[] { new Caption(1.25, 3.5, "Hello.", "你好。") });
Check(srt.Contains("00:00:01,250 --> 00:00:03,500") && srt.Contains("你好。\r\nHello."), "bilingual SRT timestamps");
