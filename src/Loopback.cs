using System.Runtime.InteropServices;

namespace EarSubtitle;

record AudioDevice(string Id, string Name) { public override string ToString() => Name; }

static class Loopback
{
    static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);
    static void Release(object? o) { if (o != null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o); }
    public static List<AudioDevice> Devices()
    {
        var result = new List<AudioDevice>();
        var en = (IMMDeviceEnumerator)new MMDeviceEnumerator(); IMMDeviceCollection? collection = null;
        try
        {
            Check(en.EnumAudioEndpoints(0, 1, out collection)); Check(collection.GetCount(out var count));
            for (uint i = 0; i < count; i++)
            {
                Check(collection.Item(i, out var device)); IPropertyStore? props = null;
                try
                {
                    Check(device.GetId(out var id)); var name = id;
                    Check(device.OpenPropertyStore(0, out props));
                    var key = new PropertyKey { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };
                    Check(props.GetValue(ref key, out var value));
                    try { if (value.vt == 31) name = Marshal.PtrToStringUni(value.pointer) ?? id; }
                    finally { PropVariantClear(ref value); }
                    result.Add(new AudioDevice(id, name));
                }
                finally { Release(props); Release(device); }
            }
        }
        finally { Release(collection); Release(en); }
        return result;
    }
    // Runs entirely on one MTA worker; COM interfaces never cross threads.
    public static void Run(string id, Action<float[]> onSamples, CancellationToken token)
    {
        var en = (IMMDeviceEnumerator)new MMDeviceEnumerator(); IMMDevice? device = null;
        IAudioClient? client = null; IAudioCaptureClient? capture = null; IntPtr format = IntPtr.Zero;
        bool started = false;
        try
        {
            Check(en.GetDevice(id, out device)); var iid = typeof(IAudioClient).GUID;
            Check(device.Activate(ref iid, 23, IntPtr.Zero, out var obj)); client = (IAudioClient)obj;
            Check(client.GetMixFormat(out format));
            var tag = (ushort)Marshal.ReadInt16(format, 0); var channels = (ushort)Marshal.ReadInt16(format, 2);
            var rate = Marshal.ReadInt32(format, 4); var align = (ushort)Marshal.ReadInt16(format, 12);
            var bits = (ushort)Marshal.ReadInt16(format, 14);
            if (tag == 65534) tag = (ushort)Marshal.ReadInt16(format, 24);
            if (!((tag == 3 && bits == 32) || (tag == 1 && (bits == 16 || bits == 24 || bits == 32))))
                throw new NotSupportedException($"不支援此音效格式：tag={tag}, bits={bits}");
            Check(client.Initialize(0, 0x20000, 1000000, 0, format, IntPtr.Zero));
            iid = typeof(IAudioCaptureClient).GUID; Check(client.GetService(ref iid, out obj)); capture = (IAudioCaptureClient)obj;
            Check(client.Start()); started = true;
            var pcm = new List<float>(3200); double sum = 0; int n = 0, phase = 0;
            while (!token.IsCancellationRequested)
            {
                Check(capture.GetNextPacketSize(out var available));
                while (available > 0 && !token.IsCancellationRequested)
                {
                    Check(capture.GetBuffer(out var data, out var frames, out var flags, out _, out _));
                    try
                    {
                        var bytes = new byte[checked((int)frames * align)];
                        if ((flags & 2) == 0) Marshal.Copy(data, bytes, 0, bytes.Length);
                        for (int f = 0; f < frames; f++)
                        {
                            double mono = 0;
                            for (int ch = 0; ch < channels; ch++)
                            {
                                int offset = f * align + ch * (bits / 8);
                                mono += tag == 3 ? BitConverter.ToSingle(bytes, offset) : bits switch
                                {
                                    16 => BitConverter.ToInt16(bytes, offset) / 32768d,
                                    24 => ((bytes[offset] << 8 | bytes[offset + 1] << 16 | bytes[offset + 2] << 24) >> 8) / 8388608d,
                                    _ => BitConverter.ToInt32(bytes, offset) / 2147483648d
                                };
                            }
                            sum += mono / channels; n++; phase += 16000;
                            if (phase >= rate)
                            {
                                pcm.Add((float)(sum / n)); sum = 0; n = 0; phase -= rate;
                            }
                        }
                    }
                    finally { Check(capture.ReleaseBuffer(frames)); }
                    Check(capture.GetNextPacketSize(out available));
                }
                if (pcm.Count > 0) { onSamples(pcm.ToArray()); pcm.Clear(); }
                token.WaitHandle.WaitOne(15);
            }
        }
        finally
        {
            if (started) client?.Stop();
            if (format != IntPtr.Zero) Marshal.FreeCoTaskMem(format);
            Release(capture); Release(client); Release(device); Release(en);
        }
    }
    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVariant value);
    [StructLayout(LayoutKind.Sequential)] struct PropertyKey { public Guid fmtid; public uint pid; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] struct PropVariant { [FieldOffset(0)] public ushort vt; [FieldOffset(8)] public IntPtr pointer; }
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr callback);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr callback);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection { [PreserveSig] int GetCount(out uint count); [PreserveSig] int Item(uint index, out IMMDevice device); }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }
    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }
    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioClient
    {
        [PreserveSig] int Initialize(int share, uint flags, long duration, long periodicity, IntPtr format, IntPtr session);
        [PreserveSig] int GetBufferSize(out uint frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint frames);
        [PreserveSig] int IsFormatSupported(int mode, IntPtr format, out IntPtr closest);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long normal, out long minimum);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr handle);
        [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }
    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong position, out ulong counter);
        [PreserveSig] int ReleaseBuffer(uint frames);
        [PreserveSig] int GetNextPacketSize(out uint frames);
    }
}
