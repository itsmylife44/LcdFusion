using System;
using System.Diagnostics;
using System.Reflection;

namespace LcdFusion
{
    // Launch-at-logon via a Scheduled Task with highest privileges. Because LCD Fusion
    // runs elevated (sensor driver), a plain HKCU\Run entry would be blocked by UAC at
    // logon; a task with /RL HIGHEST starts it elevated without a prompt.
    internal static class AutoStartService
    {
        private const string TaskName = "LCDFusionAutostart";

        private static string ExePath()
        {
            return Process.GetCurrentProcess().MainModule.FileName;
        }

        public static bool IsEnabled()
        {
            try { return Run("/Query /TN \"" + TaskName + "\"") == 0; }
            catch { return false; }
        }

        public static bool Enable()
        {
            try
            {
                string exe = ExePath();
                string args = "/Create /F /TN \"" + TaskName + "\" /SC ONLOGON /RL HIGHEST /TR \"\\\"" + exe + "\\\"\"";
                return Run(args) == 0;
            }
            catch { return false; }
        }

        public static bool Disable()
        {
            try { return Run("/Delete /F /TN \"" + TaskName + "\"") == 0; }
            catch { return false; }
        }

        private static int Run(string arguments)
        {
            ProcessStartInfo psi = new ProcessStartInfo("schtasks.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (Process p = Process.Start(psi))
            {
                p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                p.WaitForExit(8000);
                return p.HasExited ? p.ExitCode : -1;
            }
        }
    }
}
