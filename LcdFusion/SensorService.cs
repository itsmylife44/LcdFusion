using System;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.Management;
using System.Text;
using LibreHardwareMonitor.Hardware;

namespace LcdFusion
{
    internal sealed class SensorReading
    {
        public bool Available;
        public string Source = "Nessuna sorgente";
        public bool HasCpuTemp;
        public double CpuTempC;
        public bool HasGpuTemp;
        public double GpuTempC;
        public bool HasCpuLoad;
        public double CpuLoad;
        public bool HasGpuLoad;
        public double GpuLoad;
    }

    // Reads CPU/GPU sensors. Primary source is the bundled LibreHardwareMonitorLib
    // (self-contained, needs admin for Ryzen CPU temperature). Falls back to a running
    // HWiNFO (shared memory) or LibreHardwareMonitor/OpenHardwareMonitor (WMI).
    internal static class SensorService
    {
        public static SensorReading Read()
        {
            SensorReading reading;
            try { reading = ReadLhm(); if (reading.Available) return reading; } catch { }

            reading = ReadHwInfo();
            if (reading.Available) return reading;

            reading = ReadWmi("root\\LibreHardwareMonitor", "LibreHardwareMonitor");
            if (reading.Available) return reading;

            reading = ReadWmi("root\\OpenHardwareMonitor", "OpenHardwareMonitor");
            if (reading.Available) return reading;

            return new SensorReading();
        }

        public static void Close()
        {
            lock (LhmLock)
            {
                if (_computer != null)
                {
                    try { _computer.Close(); } catch { }
                    _computer = null;
                }
            }
        }

        // ---- LibreHardwareMonitorLib (bundled) --------------------------------------

        private static readonly object LhmLock = new object();
        private static Computer _computer;
        private static readonly UpdateVisitor Visitor = new UpdateVisitor();

        private sealed class UpdateVisitor : IVisitor
        {
            public void VisitComputer(IComputer computer) { computer.Traverse(this); }
            public void VisitHardware(IHardware hardware)
            {
                hardware.Update();
                foreach (IHardware sub in hardware.SubHardware) sub.Accept(this);
            }
            public void VisitSensor(ISensor sensor) { }
            public void VisitParameter(IParameter parameter) { }
        }

        private static SensorReading ReadLhm()
        {
            SensorReading reading = new SensorReading();
            lock (LhmLock)
            {
                if (_computer == null)
                {
                    Computer computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true };
                    computer.Open();
                    _computer = computer;
                }
                _computer.Accept(Visitor);

                List<KeyValuePair<string, double>> cpuTemps = new List<KeyValuePair<string, double>>();
                List<KeyValuePair<string, double>> gpuTemps = new List<KeyValuePair<string, double>>();
                double cpuLoad = double.NaN, gpuLoad = double.NaN;

                foreach (IHardware hardware in _computer.Hardware)
                {
                    bool isCpu = hardware.HardwareType == HardwareType.Cpu;
                    bool isGpu = hardware.HardwareType == HardwareType.GpuAmd ||
                                 hardware.HardwareType == HardwareType.GpuNvidia ||
                                 hardware.HardwareType == HardwareType.GpuIntel;
                    if (!isCpu && !isGpu) continue;

                    foreach (ISensor sensor in hardware.Sensors)
                    {
                        if (sensor.Value == null) continue;
                        double value = (double)sensor.Value.Value;
                        string name = sensor.Name ?? "";

                        if (sensor.SensorType == SensorType.Temperature)
                        {
                            if (isCpu) cpuTemps.Add(new KeyValuePair<string, double>(name, value));
                            else gpuTemps.Add(new KeyValuePair<string, double>(name, value));
                        }
                        else if (sensor.SensorType == SensorType.Load)
                        {
                            string lower = name.ToLowerInvariant();
                            if (isCpu && lower.Contains("total") && double.IsNaN(cpuLoad)) cpuLoad = value;
                            else if (isGpu && lower.Contains("core") && double.IsNaN(gpuLoad)) gpuLoad = value;
                        }
                    }
                }

                double cpuTemp = PickCpuTemp(cpuTemps);
                double gpuTemp = PickGpuTemp(gpuTemps);
                Fill(reading, "LibreHardwareMonitor", cpuTemp, gpuTemp, cpuLoad, gpuLoad);
            }
            return reading;
        }

        private static double PickCpuTemp(List<KeyValuePair<string, double>> temps)
        {
            return PickByPriority(temps, new string[] { "tctl", "tdie", "package", "core (tctl" }, "ccd");
        }

        private static double PickGpuTemp(List<KeyValuePair<string, double>> temps)
        {
            return PickByPriority(temps, new string[] { "gpu core", "core", "edge", "gpu" }, "hot");
        }

        private static double PickByPriority(List<KeyValuePair<string, double>> temps, string[] preferred, string avoid)
        {
            if (temps.Count == 0) return double.NaN;
            int bestScore = int.MinValue;
            double best = double.NaN;
            foreach (KeyValuePair<string, double> pair in temps)
            {
                string lower = pair.Key.ToLowerInvariant();
                int score = 0;
                for (int i = 0; i < preferred.Length; i++)
                    if (lower.Contains(preferred[i])) { score = preferred.Length - i + 1; break; }
                if (avoid != null && lower.Contains(avoid)) score -= 5;
                if (score > bestScore) { bestScore = score; best = pair.Value; }
            }
            return best;
        }

        // ---- HWiNFO shared memory (HWiNFO_SENS_SM2) ---------------------------------

        private const int HeaderReadingOffset = 32;
        private const int HeaderReadingSize = 36;
        private const int HeaderReadingCount = 40;
        private const int ReadType = 0;
        private const int ReadLabelUser = 140;
        private const int ReadValue = 284;

        private static SensorReading ReadHwInfo()
        {
            SensorReading reading = new SensorReading();
            MemoryMappedFile mmf = null;
            MemoryMappedViewAccessor view = null;
            try
            {
                mmf = MemoryMappedFile.OpenExisting("Global\\HWiNFO_SENS_SM2", MemoryMappedFileRights.Read);
                view = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

                uint readingOffset = view.ReadUInt32(HeaderReadingOffset);
                uint readingSize = view.ReadUInt32(HeaderReadingSize);
                uint readingCount = view.ReadUInt32(HeaderReadingCount);
                if (readingSize < 292 || readingCount == 0 || readingCount > 100000) return reading;

                double cpuTemp = double.NaN, gpuTemp = double.NaN, cpuLoad = double.NaN, gpuLoad = double.NaN;
                byte[] labelBuffer = new byte[128];

                for (uint i = 0; i < readingCount; i++)
                {
                    long basePos = readingOffset + (long)i * readingSize;
                    uint type = view.ReadUInt32(basePos + ReadType);
                    view.ReadArray(basePos + ReadLabelUser, labelBuffer, 0, 128);
                    string label = AsciiZ(labelBuffer);
                    double value = view.ReadDouble(basePos + ReadValue);
                    string lower = label.ToLowerInvariant();

                    if (type == 1)
                    {
                        if (IsCpu(lower) && PreferTemp(label, cpuTemp)) cpuTemp = value;
                        else if (IsGpu(lower) && PreferTemp(label, gpuTemp)) gpuTemp = value;
                    }
                    else if (type == 7)
                    {
                        if (IsCpuLoad(lower) && double.IsNaN(cpuLoad)) cpuLoad = value;
                        else if (IsGpu(lower) && double.IsNaN(gpuLoad)) gpuLoad = value;
                    }
                }

                Fill(reading, "HWiNFO", cpuTemp, gpuTemp, cpuLoad, gpuLoad);
                return reading;
            }
            catch { return reading; }
            finally
            {
                if (view != null) view.Dispose();
                if (mmf != null) mmf.Dispose();
            }
        }

        private static bool PreferTemp(string label, double current)
        {
            if (double.IsNaN(current)) return true;
            string lower = label.ToLowerInvariant();
            return lower.Contains("tctl") || lower.Contains("tdie") || lower.Contains("package") ||
                   lower.Contains("gpu temperature") || lower.Contains("edge");
        }

        private static bool IsCpu(string lower)
        {
            return lower.Contains("cpu") && !lower.Contains("gpu") && !lower.Contains("vrm") &&
                   !lower.Contains("chipset") && !lower.Contains("socket");
        }

        private static bool IsCpuLoad(string lower)
        {
            return lower.Contains("cpu") && (lower.Contains("total") || lower.Contains("usage")) && !lower.Contains("gpu");
        }

        private static bool IsGpu(string lower) { return lower.Contains("gpu"); }

        // ---- LibreHardwareMonitor / OpenHardwareMonitor (WMI) -----------------------

        private static SensorReading ReadWmi(string scope, string sourceName)
        {
            SensorReading reading = new SensorReading();
            try
            {
                double cpuTemp = double.NaN, gpuTemp = double.NaN;
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope,
                    "SELECT Name, Identifier, Value FROM Sensor WHERE SensorType = 'Temperature'"))
                {
                    foreach (ManagementBaseObject mo in searcher.Get())
                    {
                        string id = Convert.ToString(mo["Identifier"]).ToLowerInvariant();
                        string name = Convert.ToString(mo["Name"]);
                        double value = ToDouble(mo["Value"]);
                        if (double.IsNaN(value)) continue;
                        if (id.Contains("cpu") && (double.IsNaN(cpuTemp) || PreferTemp(name, cpuTemp))) cpuTemp = value;
                        else if (id.Contains("gpu") && (double.IsNaN(gpuTemp) || PreferTemp(name, gpuTemp))) gpuTemp = value;
                    }
                }

                Fill(reading, sourceName, cpuTemp, gpuTemp, double.NaN, double.NaN);
                return reading;
            }
            catch { return reading; }
        }

        // ---- helpers ----------------------------------------------------------------

        private static void Fill(SensorReading reading, string source, double cpuTemp, double gpuTemp, double cpuLoad, double gpuLoad)
        {
            if (!double.IsNaN(cpuTemp)) { reading.HasCpuTemp = true; reading.CpuTempC = cpuTemp; }
            if (!double.IsNaN(gpuTemp)) { reading.HasGpuTemp = true; reading.GpuTempC = gpuTemp; }
            if (!double.IsNaN(cpuLoad)) { reading.HasCpuLoad = true; reading.CpuLoad = cpuLoad; }
            if (!double.IsNaN(gpuLoad)) { reading.HasGpuLoad = true; reading.GpuLoad = gpuLoad; }
            reading.Available = reading.HasCpuTemp || reading.HasGpuTemp || reading.HasCpuLoad || reading.HasGpuLoad;
            if (reading.Available) reading.Source = source;
        }

        private static double ToDouble(object value)
        {
            try { return value == null ? double.NaN : Convert.ToDouble(value); }
            catch { return double.NaN; }
        }

        private static string AsciiZ(byte[] buffer)
        {
            int length = 0;
            while (length < buffer.Length && buffer[length] != 0) length++;
            return Encoding.ASCII.GetString(buffer, 0, length);
        }
    }
}
