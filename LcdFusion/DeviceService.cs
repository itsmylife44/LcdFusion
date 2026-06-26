using System;
using System.Collections.Generic;
using System.Management;

namespace LcdFusion
{
    internal sealed class DeviceInfo
    {
        public string Label;
        public string HardwareId;
        public string InstanceId;
        public string WindowsName;
        public bool Present;
        public uint ErrorCode;

        public bool IsHealthy
        {
            get { return Present && ErrorCode == 0; }
        }
    }

    internal sealed class DeviceSnapshot
    {
        public DeviceInfo Valkyrie;
        public DeviceInfo Thermalright;
        public bool DescriptorFailure;
        public DateTime CheckedAt;
    }

    internal static class DeviceService
    {
        private const string ValkyrieId = "VID_345F&PID_9132";
        private const string ThermalrightId = "VID_0416&PID_5408";

        public static DeviceSnapshot Read()
        {
            var snapshot = new DeviceSnapshot();
            snapshot.CheckedAt = DateTime.Now;
            snapshot.Valkyrie = Empty("Valkyrie", "345F:9132");
            snapshot.Thermalright = Empty("Thermalright", "0416:5408");

            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT Name, PNPDeviceID, ConfigManagerErrorCode FROM Win32_PnPEntity"))
                using (var results = searcher.Get())
                {
                    foreach (ManagementObject item in results)
                    {
                        var id = Convert.ToString(item["PNPDeviceID"]);
                        var name = Convert.ToString(item["Name"]);
                        var code = ToUInt(item["ConfigManagerErrorCode"]);

                        if (Contains(id, ValkyrieId))
                            Update(snapshot.Valkyrie, id, name, code);
                        if (Contains(id, ThermalrightId))
                            Update(snapshot.Thermalright, id, name, code);
                        if (Contains(id, "VID_0000&PID_0002") ||
                            Contains(name, "descrittore dispositivo non riuscita") ||
                            Contains(name, "Device Descriptor Request Failed"))
                            snapshot.DescriptorFailure = true;
                    }
                }
            }
            catch
            {
                // The UI reports unavailable devices if WMI is temporarily busy.
            }

            return snapshot;
        }

        private static DeviceInfo Empty(string label, string hardwareId)
        {
            return new DeviceInfo
            {
                Label = label,
                HardwareId = hardwareId,
                InstanceId = "Non rilevato",
                WindowsName = "Dispositivo non disponibile",
                Present = false,
                ErrorCode = 0
            };
        }

        private static void Update(DeviceInfo target, string id, string name, uint code)
        {
            target.Present = true;
            target.ErrorCode = Math.Max(target.ErrorCode, code);
            target.InstanceId = id;

            // Prefer the actual display interface over the composite/HID children.
            if (string.IsNullOrEmpty(target.WindowsName) ||
                Contains(name, "Display") || Contains(name, "USBDISPLAY"))
                target.WindowsName = name;
        }

        private static bool Contains(string value, string part)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static uint ToUInt(object value)
        {
            try { return Convert.ToUInt32(value); }
            catch { return 0; }
        }
    }
}
