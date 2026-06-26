using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace LcdFusion
{
    // Serializable scene snapshot. Public (XmlSerializer requires public types).
    public sealed class OverlayData
    {
        public int Kind;
        public string Text = "";
        public double X, Y, Size, Rotation;
        public int ColorArgb;
        public bool ShowLabel = true;
    }

    public sealed class SceneData
    {
        public int Background;
        public string MediaPath = "";
        public byte[] MediaData;   // embedded image/GIF bytes so profiles are self-contained
        public int BgColorArgb;
        public double Rotation;
        public bool FlipH;
        public double Scale = 1.0;
        public double PanX, PanY;
        public int Fit;
        public List<OverlayData> Overlays = new List<OverlayData>();
    }

    public sealed class ProfileData
    {
        public SceneData Valk = new SceneData();
        public SceneData Thermal = new SceneData();
        public bool TargetValk = true;
        public bool TargetThermal;
        public bool ActiveValk = true;
        public bool Streaming = true;   // streaming is on by default / when not present in old files
    }

    public sealed class AppSettings
    {
        public string Lang = "";
        public string LastProfile = "";
    }

    internal static class ProfileService
    {
        private static readonly string Root =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LcdFusion");
        private static readonly string ProfilesDir = Path.Combine(Root, "profiles");
        private static readonly string LastPath = Path.Combine(Root, "last-session.xml");
        private static readonly string SettingsPath = Path.Combine(Root, "settings.xml");

        private static void EnsureDirs()
        {
            try { Directory.CreateDirectory(ProfilesDir); } catch { }
        }

        public static string[] List()
        {
            try
            {
                EnsureDirs();
                List<string> names = new List<string>();
                foreach (string f in Directory.GetFiles(ProfilesDir, "*.xml"))
                    names.Add(Path.GetFileNameWithoutExtension(f));
                names.Sort(StringComparer.OrdinalIgnoreCase);
                return names.ToArray();
            }
            catch { return new string[0]; }
        }

        public static void Save(string name, ProfileData data)
        {
            EnsureDirs();
            WriteXml(Path.Combine(ProfilesDir, Safe(name) + ".xml"), data);
        }

        public static ProfileData Load(string name)
        {
            return ReadXml<ProfileData>(Path.Combine(ProfilesDir, Safe(name) + ".xml"));
        }

        public static void Delete(string name)
        {
            try { File.Delete(Path.Combine(ProfilesDir, Safe(name) + ".xml")); } catch { }
        }

        public static bool Exists(string name)
        {
            return File.Exists(Path.Combine(ProfilesDir, Safe(name) + ".xml"));
        }

        public static void SaveLast(ProfileData data) { EnsureDirs(); WriteXml(LastPath, data); }
        public static ProfileData LoadLast() { return ReadXml<ProfileData>(LastPath); }

        public static void SaveSettings(AppSettings s) { EnsureDirs(); WriteXml(SettingsPath, s); }
        public static AppSettings LoadSettings() { return ReadXml<AppSettings>(SettingsPath) ?? new AppSettings(); }

        private static string Safe(string name)
        {
            if (string.IsNullOrEmpty(name)) return "profilo";
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Trim();
        }

        private static void WriteXml<T>(string path, T obj)
        {
            try
            {
                XmlSerializer ser = new XmlSerializer(typeof(T));
                using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                    ser.Serialize(fs, obj);
            }
            catch { }
        }

        private static T ReadXml<T>(string path) where T : class
        {
            try
            {
                if (!File.Exists(path)) return null;
                XmlSerializer ser = new XmlSerializer(typeof(T));
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                    return (T)ser.Deserialize(fs);
            }
            catch { return null; }
        }
    }
}
