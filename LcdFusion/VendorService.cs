using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace LcdFusion
{
    internal static class VendorService
    {
        public const string MythCoolExe = @"C:\Program Files (x86)\Myth.Cool\MythCool.exe";
        public const string MythCoolLauncher = @"C:\Program Files (x86)\Myth.Cool\MythCoolLauncher.exe";
        public const string TrccExe = @"C:\Program Files\TRCCCAP\TRCC.exe";

        public static bool IsMythCoolRunning()
        {
            return Process.GetProcessesByName("MythCool").Any();
        }

        public static bool IsTrccRunning()
        {
            return Process.GetProcessesByName("TRCC").Any() ||
                   Process.GetProcessesByName("USBLCD").Any() ||
                   Process.GetProcessesByName("USBLCDNEW").Any();
        }

        public static bool LaunchMythCool()
        {
            return Launch(File.Exists(MythCoolLauncher) ? MythCoolLauncher : MythCoolExe);
        }

        public static bool LaunchTrcc()
        {
            return Launch(TrccExe);
        }

        public static bool StopConflictingSoftware()
        {
            try
            {
                if (!IsMythCoolRunning() && !IsTrccRunning()) return true;
                var info = new ProcessStartInfo("taskkill.exe",
                    "/F /IM MythCool.exe /IM TRCC.exe /IM USBLCD.exe /IM USBLCDNEW.exe");
                info.UseShellExecute = true;
                info.Verb = "runas";
                info.WindowStyle = ProcessWindowStyle.Hidden;
                using (var process = Process.Start(info))
                {
                    if (process == null) return false;
                    process.WaitForExit();
                }
                System.Threading.Thread.Sleep(500);
                return !IsMythCoolRunning() && !IsTrccRunning();
            }
            catch { return false; }
        }

        public static string VersionOf(string path)
        {
            try
            {
                if (!File.Exists(path)) return "non installato";
                return FileVersionInfo.GetVersionInfo(path).FileVersion ?? "installato";
            }
            catch { return "installato"; }
        }

        private static bool Launch(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                return true;
            }
            catch { return false; }
        }
    }
}
