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

        // CPU
        public bool HasCpuTemp; public double CpuTempC;
        public bool HasCpuLoad; public double CpuLoad;
        public bool HasCpuClock; public double CpuClockMhz;
        public bool HasCpuPower; public double CpuPowerW;
        public double[] CpuCoreLoads;

        // GPU (primary / discrete)
        public string GpuName = "";
        public bool HasGpuTemp; public double GpuTempC;
        public bool HasGpuLoad; public double GpuLoad;
        public bool HasGpuClock; public double GpuClockMhz;
        public bool HasGpuPower; public double GpuPowerW;
        public bool HasGpuVram; public double GpuVramUsedMb; public double GpuVramTotalMb;
        public bool HasGpuFan; public double GpuFanRpm;

        // RAM
        public bool HasRamLoad; public double RamLoad; public double RamUsedGb; public double RamTotalGb;
    }

    // Reads CPU/GPU/RAM sensors. Primary source is the bundled LibreHardwareMonitorLib
    // (self-contained, needs admin for full CPU data). Falls back to a running HWiNFO
    // (shared memory) or LibreHardwareMonitor / OpenHardwareMonitor (WMI) for temp/load.
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
                    Computer computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true };
                    computer.Open();
                    _computer = computer;
                }
                _computer.Accept(Visitor);

                IHardware cpu = null, memory = null;
                List<IHardware> gpus = new List<IHardware>();
                foreach (IHardware h in _computer.Hardware)
                {
                    if (h.HardwareType == HardwareType.Cpu) { if (cpu == null) cpu = h; }
                    else if (h.HardwareType == HardwareType.Memory) { if (memory == null) memory = h; }
                    else if (h.HardwareType == HardwareType.GpuAmd || h.HardwareType == HardwareType.GpuNvidia || h.HardwareType == HardwareType.GpuIntel)
                        gpus.Add(h);
                }

                if (cpu != null) ReadCpu(cpu, reading);
                IHardware gpu = PickGpu(gpus);
                if (gpu != null) ReadGpu(gpu, reading);
                if (memory != null) ReadMemory(memory, reading);

                reading.Available = reading.HasCpuTemp || reading.HasCpuLoad || reading.HasGpuTemp || reading.HasGpuLoad;
                if (reading.Available) reading.Source = "LibreHardwareMonitor";
            }
            return reading;
        }

        private static void ReadCpu(IHardware cpu, SensorReading r)
        {
            List<KeyValuePair<int, double>> cores = new List<KeyValuePair<int, double>>();
            double bestTemp = double.NaN; int bestTempScore = int.MinValue;
            double maxClock = double.NaN;
            foreach (ISensor s in cpu.Sensors)
            {
                if (s.Value == null) continue;
                double v = (double)s.Value.Value;
                string low = (s.Name ?? "").ToLowerInvariant();
                switch (s.SensorType)
                {
                    case SensorType.Temperature:
                        int sc = (low.Contains("tctl") || low.Contains("tdie")) ? 3 : (low.Contains("package") ? 2 : (low.Contains("ccd") ? 0 : 1));
                        if (sc > bestTempScore) { bestTempScore = sc; bestTemp = v; }
                        break;
                    case SensorType.Load:
                        if (low == "cpu total") { r.HasCpuLoad = true; r.CpuLoad = v; }
                        else { int idx = CoreIndex(s.Name); if (idx > 0) cores.Add(new KeyValuePair<int, double>(idx, v)); }
                        break;
                    case SensorType.Clock:
                        if (low.StartsWith("core") && !double.IsNaN(v))
                            if (double.IsNaN(maxClock) || v > maxClock) maxClock = v;
                        break;
                    case SensorType.Power:
                        if (low == "package") { r.HasCpuPower = true; r.CpuPowerW = v; }
                        break;
                }
            }
            if (!double.IsNaN(bestTemp)) { r.HasCpuTemp = true; r.CpuTempC = bestTemp; }
            if (!double.IsNaN(maxClock)) { r.HasCpuClock = true; r.CpuClockMhz = maxClock; }
            if (cores.Count > 0)
            {
                cores.Sort(delegate (KeyValuePair<int, double> a, KeyValuePair<int, double> b) { return a.Key.CompareTo(b.Key); });
                double[] arr = new double[cores.Count];
                for (int i = 0; i < cores.Count; i++) arr[i] = cores[i].Value;
                r.CpuCoreLoads = arr;
            }
        }

        // Among multiple GPUs (iGPU + dGPU), pick the discrete one: the largest VRAM.
        private static IHardware PickGpu(List<IHardware> gpus)
        {
            if (gpus.Count == 0) return null;
            if (gpus.Count == 1) return gpus[0];
            IHardware best = null; double bestVram = -1;
            foreach (IHardware g in gpus)
            {
                double vram = 0;
                foreach (ISensor s in g.Sensors)
                    if (s.SensorType == SensorType.SmallData && s.Value != null && (s.Name ?? "").ToLowerInvariant() == "gpu memory total")
                        vram = (double)s.Value.Value;
                if (vram > bestVram) { bestVram = vram; best = g; }
            }
            return best ?? gpus[0];
        }

        private static void ReadGpu(IHardware gpu, SensorReading r)
        {
            r.GpuName = gpu.Name ?? "";
            double tempCore = double.NaN, tempAny = double.NaN, power = double.NaN, vramUsed = double.NaN, vramTotal = double.NaN;
            foreach (ISensor s in gpu.Sensors)
            {
                if (s.Value == null) continue;
                double v = (double)s.Value.Value;
                string low = (s.Name ?? "").ToLowerInvariant();
                switch (s.SensorType)
                {
                    case SensorType.Temperature:
                        if (low == "gpu core") tempCore = v; else if (double.IsNaN(tempAny)) tempAny = v;
                        break;
                    case SensorType.Load:
                        if (low == "gpu core") { r.HasGpuLoad = true; r.GpuLoad = v; }
                        break;
                    case SensorType.Clock:
                        if (low == "gpu core") { r.HasGpuClock = true; r.GpuClockMhz = v; }
                        break;
                    case SensorType.Power:
                        if (low.Contains("package") || low.Contains("ppt") || low == "gpu power") { if (double.IsNaN(power) || v > power) power = v; }
                        else if (double.IsNaN(power) && low.Contains("gpu")) power = v;
                        break;
                    case SensorType.Fan:
                        if (low.Contains("fan")) { r.HasGpuFan = true; r.GpuFanRpm = v; }
                        break;
                    case SensorType.SmallData:
                        if (low == "gpu memory used") vramUsed = v;
                        else if (low == "gpu memory total") vramTotal = v;
                        break;
                }
            }
            double t = !double.IsNaN(tempCore) ? tempCore : tempAny;
            if (!double.IsNaN(t)) { r.HasGpuTemp = true; r.GpuTempC = t; }
            if (!double.IsNaN(power)) { r.HasGpuPower = true; r.GpuPowerW = power; }
            if (!double.IsNaN(vramUsed) && !double.IsNaN(vramTotal) && vramTotal > 0)
            { r.HasGpuVram = true; r.GpuVramUsedMb = vramUsed; r.GpuVramTotalMb = vramTotal; }
        }

        private static void ReadMemory(IHardware mem, SensorReading r)
        {
            double used = double.NaN, avail = double.NaN;
            foreach (ISensor s in mem.Sensors)
            {
                if (s.Value == null) continue;
                double v = (double)s.Value.Value;
                string low = (s.Name ?? "").ToLowerInvariant();
                if (s.SensorType == SensorType.Load && low == "memory") { r.HasRamLoad = true; r.RamLoad = v; }
                else if (s.SensorType == SensorType.Data && low == "memory used") used = v;
                else if (s.SensorType == SensorType.Data && low == "memory available") avail = v;
            }
            if (!double.IsNaN(used)) { r.RamUsedGb = used; if (!double.IsNaN(avail)) r.RamTotalGb = used + avail; }
        }

        private static int CoreIndex(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            int hash = name.IndexOf('#');
            if (hash < 0) return -1;
            int n;
            return int.TryParse(name.Substring(hash + 1).Trim(), out n) ? n : -1;
        }

        // ---- HWiNFO shared memory (HWiNFO_SENS_SM2) — temp/load fallback ------------

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

        // ---- LibreHardwareMonitor / OpenHardwareMonitor (WMI) — temp fallback -------

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
