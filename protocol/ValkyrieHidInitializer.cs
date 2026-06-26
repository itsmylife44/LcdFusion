using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

internal static class ValkyrieHidInitializer
{
    private static string _lastReply;
    private const uint DigcfPresent = 0x2;
    private const uint DigcfDeviceInterface = 0x10;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint ShareRead = 0x1;
    private const uint ShareWrite = 0x2;
    private const uint OpenExisting = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInterfaceData
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidAttributes
    {
        public int Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidCaps
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_GetAttributes(IntPtr device, ref HidAttributes attributes);
    [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_SetFeature(IntPtr device, byte[] report, int length);
    [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_GetFeature(IntPtr device, byte[] report, int length);
    [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_GetPreparsedData(IntPtr device, out IntPtr preparsedData);
    [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr preparsedData, out HidCaps capabilities);

    [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr infoSet, IntPtr deviceInfo, ref Guid classGuid,
                                                           uint index, ref DeviceInterfaceData data);

    [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr infoSet, ref DeviceInterfaceData data,
                                                               IntPtr detail, uint detailSize,
                                                               out uint requiredSize, IntPtr deviceInfo);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr infoSet);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security,
                                            uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    // Opens the Valkyrie HID interface (MI_00). Caller must Close() the returned handle.
    public static IntPtr Open()
    {
        IntPtr hid = OpenValkyrieHid();
        if (hid == new IntPtr(-1))
            throw new InvalidOperationException("Interfaccia HID Valkyrie non trovata.");
        return hid;
    }

    public static void Close(IntPtr hid)
    {
        if (hid != IntPtr.Zero && hid != new IntPtr(-1)) CloseHandle(hid);
    }

    // Full cold-start initialization sequence, transcribed byte-for-byte from the
    // Myth.Cool USB capture (mythcool-cold-start-long.pcap). Each entry is an 8-byte
    // HID feature report (SET_REPORT, interface 0); ReadReply mirrors the GET_REPORT
    // poll Myth.Cool performs after the command.
    public static string RunInit(IntPtr hid)
    {
        int featureLength = GetFeatureLength(hid);
        foreach (HidCommand command in BuildInitSequence())
            Command(hid, command.Payload, command.ReadReply);
        return "HID inizializzato (feature report " + featureLength + " byte, reply " + _lastReply + ")";
    }

    // Commands Myth.Cool issues immediately after the first bulk frame (start scan-out).
    public static void RunPostFrame(IntPtr hid)
    {
        Command(hid, Payload(0xB5, 0xF5, 0x07), true);
        Command(hid, Payload(0xB6, 0xF5, 0x07, 0x00), false);
        Command(hid, Payload(0xA6, 0x05, 0x01, 0x00), false);
        Command(hid, Payload(0xB5, 0xC5, 0x55), true);
    }

    // Periodic refresh/keepalive Myth.Cool sends between frames during streaming.
    public static void Commit(IntPtr hid)
    {
        Command(hid, Payload(0xB5, 0x00, 0x32), true);
    }

    private struct HidCommand
    {
        public byte[] Payload;
        public bool ReadReply;
    }

    private static byte[] Payload(params byte[] head)
    {
        byte[] full = new byte[8];
        Buffer.BlockCopy(head, 0, full, 0, Math.Min(head.Length, 8));
        return full;
    }

    private static HidCommand Hc(bool readReply, params byte[] head)
    {
        return new HidCommand { Payload = Payload(head), ReadReply = readReply };
    }

    private static List<HidCommand> BuildInitSequence()
    {
        List<HidCommand> seq = new List<HidCommand>();

        // Power/source selection performed by Myth.Cool before reading device metadata.
        seq.Add(Hc(false, 0xA6, 0x03, 0x03));
        seq.Add(Hc(true,  0xB5, 0xC5, 0x58));
        seq.Add(Hc(false, 0xB6, 0xDE, 0xEE, 0x01));
        seq.Add(Hc(false, 0xA6, 0x07, 0x01, 0x02));
        seq.Add(Hc(true,  0xB5, 0xC4, 0x54));
        seq.Add(Hc(false, 0xA6, 0x05, 0x00));
        seq.Add(Hc(true,  0xB5, 0xC5, 0x55));
        seq.Add(Hc(true,  0xB5, 0xF0, 0x04));
        seq.Add(Hc(false, 0xB6, 0xF0, 0x04, 0x6D));

        // Panel controller programming block (previously missing). The b5 c0 ramp loads
        // the controller register/gamma table; without it the panel stays dark.
        seq.Add(Hc(true,  0xF5, 0x00, 0x1F, 0xE0));
        seq.Add(Hc(true,  0xF5, 0x00, 0x1F, 0xE8));
        seq.Add(Hc(true,  0xF5, 0x00, 0x1F, 0xF0));
        seq.Add(Hc(true,  0xF5, 0x00, 0x1F, 0xF8));
        seq.Add(Hc(true,  0xB5, 0xC4, 0x24));
        seq.Add(Hc(true,  0xB5, 0xC6, 0x50));
        seq.Add(Hc(true,  0xB5, 0xC3, 0x0F));
        seq.Add(Hc(true,  0xB5, 0xFF, 0x00));
        seq.Add(Hc(true,  0xB5, 0xF0, 0x00));
        seq.Add(Hc(true,  0xB5, 0x00, 0x31));
        seq.Add(Hc(true,  0xB5, 0x00, 0x30));
        seq.Add(Hc(true,  0xB5, 0x00, 0x32));
        seq.Add(Hc(true,  0xB5, 0x00, 0x32));
        for (int value = 0x00; value <= 0x7C; value += 0x04)
            seq.Add(Hc(true, 0xB5, 0xC0, (byte)value));
        seq.Add(Hc(true,  0xF5, 0x00, 0x1F, 0xE0));
        seq.Add(Hc(true,  0xF5, 0x00, 0x1F, 0xE8));
        seq.Add(Hc(true,  0xF5, 0x00, 0x1F, 0xF0));
        seq.Add(Hc(true,  0xF5, 0x00, 0x1F, 0xF8));
        seq.Add(Hc(true,  0xB5, 0x00, 0x32));

        // Geometry / final enable. a6 01 and a6 02 carry 0x0140 (320) and 0x00F0 (240).
        seq.Add(Hc(false, 0xA6, 0x05, 0x00));
        seq.Add(Hc(true,  0xB5, 0xC5, 0x55));
        seq.Add(Hc(true,   0xB5, 0xF5, 0x07));
        seq.Add(Hc(false, 0xB6, 0xF5, 0x07, 0x02));
        seq.Add(Hc(false, 0xA6, 0x04, 0x00));
        seq.Add(Hc(true,  0xB5, 0xC5, 0x55));
        seq.Add(Hc(true,  0xB5, 0x00, 0x32));
        seq.Add(Hc(false, 0xA6, 0x03, 0x03));
        seq.Add(Hc(true,  0xB5, 0xC5, 0x58));
        seq.Add(Hc(false, 0xA6, 0x01, 0x01, 0x40, 0x00, 0xF0, 0x22, 0x00));
        seq.Add(Hc(true,  0xB5, 0xC5, 0x55));
        seq.Add(Hc(false, 0xA6, 0x02, 0x94, 0x00, 0x01, 0x40, 0x00, 0xF0));
        seq.Add(Hc(true,  0xB5, 0xC5, 0x57));
        seq.Add(Hc(false, 0xA6, 0x04, 0x01, 0x00));
        seq.Add(Hc(true,  0xB5, 0xC5, 0x55));
        seq.Add(Hc(false, 0xB6, 0xF2, 0x4E, 0x00));
        seq.Add(Hc(true,  0xB5, 0x00, 0x32));
        seq.Add(Hc(true,  0xB5, 0xF2, 0x42, 0x00));

        return seq;
    }

    private static int GetFeatureLength(IntPtr hid)
    {
        IntPtr data;
        if (!HidD_GetPreparsedData(hid, out data)) return -1;
        try
        {
            HidCaps caps;
            return HidP_GetCaps(data, out caps) >= 0 ? caps.FeatureReportByteLength : -2;
        }
        finally { HidD_FreePreparsedData(data); }
    }

    private static void Command(IntPtr hid, byte[] payload, bool readReply)
    {
        byte[] report = new byte[9];
        Buffer.BlockCopy(payload, 0, report, 1, 8);
        if (!HidD_SetFeature(hid, report, report.Length))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "HidD_SetFeature " + BitConverter.ToString(payload));
        if (readReply)
        {
            byte[] reply = new byte[9];
            if (!HidD_GetFeature(hid, reply, reply.Length))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "HidD_GetFeature " + BitConverter.ToString(payload));
            _lastReply = BitConverter.ToString(reply);
        }
    }

    private static IntPtr OpenValkyrieHid()
    {
        Guid guid;
        HidD_GetHidGuid(out guid);
        IntPtr set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (set == new IntPtr(-1)) return new IntPtr(-1);

        try
        {
            for (uint index = 0; ; index++)
            {
                DeviceInterfaceData data = new DeviceInterfaceData { cbSize = Marshal.SizeOf(typeof(DeviceInterfaceData)) };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref data)) break;

                uint required;
                SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out required, IntPtr.Zero);
                IntPtr detail = Marshal.AllocHGlobal((int)required);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref data, detail, required, out required, IntPtr.Zero)) continue;
                    string path = Marshal.PtrToStringAuto(IntPtr.Add(detail, 4));
                    IntPtr handle = CreateFile(path, GenericRead | GenericWrite, ShareRead | ShareWrite,
                                               IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
                    if (handle == new IntPtr(-1)) continue;

                    HidAttributes attributes = new HidAttributes { Size = Marshal.SizeOf(typeof(HidAttributes)) };
                    if (HidD_GetAttributes(handle, ref attributes) && attributes.VendorID == 0x345F && attributes.ProductID == 0x9132)
                        return handle;
                    CloseHandle(handle);
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return new IntPtr(-1);
    }
}
