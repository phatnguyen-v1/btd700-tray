// Btd700: reads and sets the Sennheiser BTD 700 dongle's audio link mode (Standard / Gaming / Broadcast),
// the same command Sennheiser Dongle Control sends when you click a mode.
// Protocol (from Sennheiser Dongle Control 1.0.5): HID output report 0x34 on the dongle's vendor collection
// (usage page 0xFFA2), payload FE <cmd> <len> <args...>. Replies come back as input report 0x34:
//   34 FF <cmd> <len> <data...>   (34 FC ... are messages the dongle sends on its own, ignored here)
//   cmd 01 = get mode and transport -> data: mode, transport, connected transport (1 = BT Classic, 2 = LE Audio)
//   cmd 02 = set mode and transport (args: mode, transport)
//   cmd 03 = get supported codecs   -> data: bit mask (bit 2 = aptX Adaptive, needed for Gaming over BT Classic)
//   cmd 06 = get link state         -> data: 0 in between (seen while reconnecting), 1 disconnected, 2 connected,
//                                      3 streaming audio, 4 streaming voice
//   cmd 14 = connect / disconnect the headset (args: 1 / 0) -> 01 00; the link state follows within ~2 s
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

public enum BtdMode { Standard = 0, Gaming = 1, Broadcast = 2 }

public class BtdState
{
    public BtdMode Mode;
    public int Transport;          // 1 = BT Classic, 2 = LE Audio
    public int ConnectedTransport; // 0 = no headset connected
    public int Link;               // 0 in between, 1 disconnected, 2 connected, 3 streaming audio, 4 streaming voice

    public bool HeadsetConnected { get { return Link >= 2; } }
}

public static class Btd700
{
    const string DeviceIds = "vid_3542&pid_3001";
    const byte ReportId = 0x34;
    const byte HostCommand = 0xFE, Reply = 0xFF;
    const byte GetModeCmd = 1, SetModeCmd = 2, GetLinkCmd = 6, ConnectCmd = 20;
    const int TimeoutMs = 2000;

    public static bool IsPresent { get { return FindPath() != null; } }

    public static BtdState GetState()
    {
        byte[] r = Request(GetModeCmd, null);
        BtdState s = new BtdState();
        s.Mode = (BtdMode)r[4];
        s.Transport = r[5];
        s.ConnectedTransport = r[6];
        byte[] d = Request(GetLinkCmd, null);
        s.Link = d[4];
        return s;
    }

    // Keeps the current transport, like the Sennheiser app does.
    public static BtdState SetMode(BtdMode mode)
    {
        BtdState now = GetState();
        if (now.Mode == mode) return now;
        Request(SetModeCmd, new byte[] { (byte)mode, (byte)now.Transport });
        return GetState();
    }

    // Drops the headset's link to this dongle (so a phone or another device can have it), or brings it back.
    // Same as Connect / Disconnect in Sennheiser Dongle Control's Settings page.
    public static void SetConnected(bool connect)
    {
        Request(ConnectCmd, new byte[] { (byte)(connect ? 1 : 0) });
    }

    static byte[] Request(byte cmd, byte[] args)
    {
        string path = FindPath();
        if (path == null) throw new InvalidOperationException("BTD 700 dongle not found");
        using (SafeFileHandle h = Open(path))
        using (FileStream fs = new FileStream(h, FileAccess.ReadWrite, 64, true))
        {
            HIDP_CAPS caps = Caps(h);
            byte[] report = new byte[caps.OutputReportByteLength];
            report[0] = ReportId;
            report[1] = HostCommand;
            report[2] = cmd;
            report[3] = (byte)(args == null ? 0 : args.Length);
            if (args != null) Array.Copy(args, 0, report, 4, args.Length);
            fs.Write(report, 0, report.Length);

            // The dongle also sends unrelated reports; wait for the reply to this command.
            DateTime until = DateTime.Now.AddMilliseconds(TimeoutMs);
            byte[] buf = new byte[caps.InputReportByteLength];
            while (DateTime.Now < until)
            {
                IAsyncResult ar = fs.BeginRead(buf, 0, buf.Length, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(until - DateTime.Now)) break;
                int n = fs.EndRead(ar);
                if (n < 4 || buf[0] != ReportId) continue;
                if (buf[1] == Reply && buf[2] == cmd) return (byte[])buf.Clone();
            }
            throw new TimeoutException("BTD 700 did not answer");
        }
    }

    // The dongle has several HID collections; the command one (vendor page 0xFFA2) declares output report 0x34.
    // Returns null when the dongle isn't plugged in.
    static string FindPath()
    {
        foreach (string path in HidPaths())
        {
            if (path.IndexOf(DeviceIds, StringComparison.OrdinalIgnoreCase) < 0) continue;
            try
            {
                using (SafeFileHandle h = Open(path))
                {
                    HIDP_CAPS c = Caps(h);
                    if (c.OutputReportByteLength > 4 && HasOutputReport(h, ReportId)) return path;
                }
            }
            catch (Win32Exception) { }
        }
        return null;
    }

    // ---- Win32 HID plumbing ----

    [StructLayout(LayoutKind.Sequential)]
    struct HIDP_CAPS
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices;
        public ushort NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr data);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data, ref HIDP_CAPS caps);
    [DllImport("hid.dll")] static extern int HidP_GetValueCaps(int type, [Out] byte[] caps, ref ushort len, IntPtr data);
    [DllImport("hid.dll")] static extern int HidP_GetButtonCaps(int type, [Out] byte[] caps, ref ushort len, IntPtr data);
    [DllImport("setupapi.dll", SetLastError = true)] static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr hwnd, int flags);
    [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid g, int i, ref SP_DEVICE_INTERFACE_DATA d);
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA d, IntPtr detail, int size, out int needed, IntPtr info);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern SafeFileHandle CreateFile(string name, uint access, int share, IntPtr sec, int disp, int flags, IntPtr tmpl);

    static List<string> HidPaths()
    {
        List<string> paths = new List<string>();
        Guid g; HidD_GetHidGuid(out g);
        IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12); // present | interface
        try
        {
            SP_DEVICE_INTERFACE_DATA d = new SP_DEVICE_INTERFACE_DATA();
            d.cbSize = Marshal.SizeOf(d);
            for (int i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref d); i++)
            {
                int needed;
                SetupDiGetDeviceInterfaceDetail(set, ref d, IntPtr.Zero, 0, out needed, IntPtr.Zero);
                IntPtr buf = Marshal.AllocHGlobal(needed);
                try
                {
                    Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6); // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W
                    if (SetupDiGetDeviceInterfaceDetail(set, ref d, buf, needed, out needed, IntPtr.Zero))
                        paths.Add(Marshal.PtrToStringUni(new IntPtr(buf.ToInt64() + 4)));
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return paths;
    }

    static SafeFileHandle Open(string path)
    {
        SafeFileHandle h = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero); // read/write, share r/w, overlapped
        if (h.IsInvalid) throw new Win32Exception();
        return h;
    }

    static HIDP_CAPS Caps(SafeFileHandle h)
    {
        IntPtr data;
        if (!HidD_GetPreparsedData(h, out data)) throw new Win32Exception();
        try
        {
            HIDP_CAPS caps = new HIDP_CAPS();
            HidP_GetCaps(data, ref caps);
            return caps;
        }
        finally { HidD_FreePreparsedData(data); }
    }

    // True if the collection declares the given output report ID (HIDP_VALUE_CAPS / HIDP_BUTTON_CAPS are 72 bytes,
    // ReportID is their 3rd byte).
    static bool HasOutputReport(SafeFileHandle h, byte id)
    {
        IntPtr data;
        if (!HidD_GetPreparsedData(h, out data)) return false;
        try
        {
            const int Output = 1, CapSize = 72;
            ushort n = 32;
            byte[] caps = new byte[CapSize * n];
            if (HidP_GetValueCaps(Output, caps, ref n, data) == 0x110000)
                for (int i = 0; i < n; i++) if (caps[i * CapSize + 2] == id) return true;
            n = 32;
            if (HidP_GetButtonCaps(Output, caps, ref n, data) == 0x110000)
                for (int i = 0; i < n; i++) if (caps[i * CapSize + 2] == id) return true;
            return false;
        }
        finally { HidD_FreePreparsedData(data); }
    }
}
