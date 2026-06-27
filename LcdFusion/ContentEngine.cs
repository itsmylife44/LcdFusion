using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace LcdFusion
{
    // Decodes one GIF and exposes the current frame with per-frame timing.
    internal sealed class GifState : IDisposable
    {
        private readonly Image _image;
        private readonly MemoryStream _stream;
        private readonly int[] _delaysMs;
        private readonly int _count;
        private int _index;
        private int _elapsed;

        private GifState(Image image, MemoryStream stream, int[] delays)
        {
            _image = image; _stream = stream; _delaysMs = delays; _count = delays.Length;
        }

        public static GifState Load(string path)
        {
            return LoadBytes(File.ReadAllBytes(path));
        }

        public static GifState LoadBytes(byte[] data)
        {
            MemoryStream stream = new MemoryStream(data);
            Image image = Image.FromStream(stream);
            int count = 1;
            try { count = image.GetFrameCount(FrameDimension.Time); } catch { count = 1; }
            if (count < 1) count = 1;
            int[] delays = new int[count];
            try
            {
                PropertyItem item = image.GetPropertyItem(0x5100);
                for (int i = 0; i < count; i++)
                {
                    int ticks = BitConverter.ToInt32(item.Value, i * 4) * 10;
                    delays[i] = ticks <= 10 ? 100 : ticks;
                }
            }
            catch { for (int i = 0; i < count; i++) delays[i] = 100; }
            return new GifState(image, stream, delays);
        }

        public Image CurrentFrame()
        {
            _image.SelectActiveFrame(FrameDimension.Time, _index);
            return _image;
        }

        public void Advance(int deltaMs)
        {
            if (_count <= 1) return;
            _elapsed += deltaMs;
            int guard = 0;
            while (_elapsed >= _delaysMs[_index] && guard++ < _count + 1)
            {
                _elapsed -= _delaysMs[_index];
                _index = (_index + 1) % _count;
            }
        }

        public void Dispose()
        {
            try { _image.Dispose(); } catch { }
            try { _stream.Dispose(); } catch { }
        }
    }

    internal enum BackgroundKind { None, Color, Image, Gif }
    internal enum OverlayKind { Text, CpuTemp, GpuTemp, CpuLoad, GpuLoad, Clock, Date, CpuClock, GpuClock, CpuPower, GpuPower, GpuVram, RamLoad, GpuFan, CpuCores }
    internal enum FitMode { Fit, Fill }

    // A movable text/sensor layer drawn on top of the background.
    internal sealed class Overlay
    {
        public OverlayKind Kind = OverlayKind.CpuTemp;
        public string Text = "Testo";
        public double X = 0.5;     // center, fraction of width
        public double Y = 0.5;     // center, fraction of height
        public double Size = 0.20; // font height, fraction of canvas height
        public double Rotation;    // degrees
        public Color Color = Color.White;
        public bool ShowLabel = true;

        public Overlay Clone()
        {
            return new Overlay { Kind = Kind, Text = Text, X = X, Y = Y, Size = Size, Rotation = Rotation, Color = Color, ShowLabel = ShowLabel };
        }
    }

    // Everything shown on one LCD: a background plus movable overlays.
    internal sealed class LcdScene
    {
        public BackgroundKind Background = BackgroundKind.None;
        public string MediaName = "";
        public string MediaPath = "";
        public byte[] MediaBytes;
        public Image ImageSource;
        public GifState Gif;
        public Color BgColor = Color.FromArgb(11, 16, 32);
        public double Rotation;    // degrees (free)
        public bool FlipH;
        public double Scale = 1.0;
        public double PanX;        // fraction of width
        public double PanY;        // fraction of height
        public FitMode Fit = FitMode.Fit;
        public readonly List<Overlay> Overlays = new List<Overlay>();

        public bool IsDynamic()
        {
            if (Background == BackgroundKind.Gif) return true;
            foreach (Overlay o in Overlays)
                if (o.Kind != OverlayKind.Text) return true;
            return false;
        }

        public void DisposeSources()
        {
            if (Gif != null) { Gif.Dispose(); Gif = null; }
            if (ImageSource != null) { ImageSource.Dispose(); ImageSource = null; }
        }
    }

    // Renders each LCD's scene independently and streams it, with a live preview
    // of whichever LCD is being edited.
    internal sealed class ContentEngine
    {
        private readonly object _lock = new object();
        private Thread _thread;
        private volatile bool _alive;
        private volatile bool _streaming;

        private readonly LcdScene _valk = new LcdScene();
        private readonly LcdScene _thermal = new LcdScene();
        private bool _targetValk = true;
        private bool _targetThermal;
        private bool _previewValk = true;

        private SensorReading _sensors = new SensorReading();
        private byte[] _previewPng;
        private bool _dirtyValk = true, _dirtyThermal = true, _dirtyPreview = true;

        private string _status = "Pronto";
        private bool _statusOk = true;
        public Action<bool, string> StatusChanged;

        public ContentEngine()
        {
            // Default each screen to a simple live dashboard.
            _valk.Overlays.Add(new Overlay { Kind = OverlayKind.CpuTemp, X = 0.5, Y = 0.30, Size = 0.26 });
            _valk.Overlays.Add(new Overlay { Kind = OverlayKind.GpuTemp, X = 0.5, Y = 0.70, Size = 0.26 });
            _thermal.Overlays.Add(new Overlay { Kind = OverlayKind.CpuTemp, X = 0.20, Y = 0.5, Size = 0.5 });
            _thermal.Overlays.Add(new Overlay { Kind = OverlayKind.GpuTemp, X = 0.52, Y = 0.5, Size = 0.5 });
            _thermal.Overlays.Add(new Overlay { Kind = OverlayKind.Clock, X = 0.83, Y = 0.5, Size = 0.42, ShowLabel = false });
        }

        // ---- lifecycle --------------------------------------------------------------

        public void Start()
        {
            if (_thread != null && _thread.IsAlive) return;
            _alive = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "LCD Fusion Engine" };
            _thread.Start();
        }

        public void Shutdown()
        {
            _alive = false; _streaming = false;
            try { ValkyrieDirectService.Stop(); } catch { }
            try { ThermalrightDirectService.Stop(); } catch { }
            Thread thread = _thread;
            if (thread != null && thread != Thread.CurrentThread) thread.Join(2000);
            lock (_lock) { _valk.DisposeSources(); _thermal.DisposeSources(); }
        }

        // ---- editing API ------------------------------------------------------------

        public LcdScene Scene(bool valkyrie) { return valkyrie ? _valk : _thermal; }

        public void Edit(bool valkyrie, Action<LcdScene> mutation)
        {
            lock (_lock)
            {
                mutation(valkyrie ? _valk : _thermal);
                MarkDirty(valkyrie);
            }
        }

        public void LoadImage(bool valkyrie, string path)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                Image loaded;
                using (MemoryStream ms = new MemoryStream(bytes))
                using (Image tmp = Image.FromStream(ms)) loaded = new Bitmap(tmp);
                lock (_lock)
                {
                    LcdScene scene = valkyrie ? _valk : _thermal;
                    scene.DisposeSources();
                    scene.ImageSource = loaded;
                    scene.MediaBytes = bytes;
                    scene.MediaName = Path.GetFileName(path);
                    scene.MediaPath = path;
                    scene.Background = BackgroundKind.Image;
                    scene.Scale = 1.0; scene.PanX = 0; scene.PanY = 0; scene.Rotation = 0; scene.FlipH = false;
                    MarkDirty(valkyrie);
                }
            }
            catch (Exception ex) { Report(false, Loc.T("err.image") + ex.Message); }
        }

        public void LoadGif(bool valkyrie, string path)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                GifState loaded = GifState.LoadBytes(bytes);
                lock (_lock)
                {
                    LcdScene scene = valkyrie ? _valk : _thermal;
                    scene.DisposeSources();
                    scene.Gif = loaded;
                    scene.MediaBytes = bytes;
                    scene.MediaName = Path.GetFileName(path);
                    scene.MediaPath = path;
                    scene.Background = BackgroundKind.Gif;
                    scene.Scale = 1.0; scene.PanX = 0; scene.PanY = 0; scene.Rotation = 0; scene.FlipH = false;
                    MarkDirty(valkyrie);
                }
            }
            catch (Exception ex) { Report(false, Loc.T("err.gif") + ex.Message); }
        }

        public void SetBackgroundKind(bool valkyrie, BackgroundKind kind)
        {
            lock (_lock)
            {
                LcdScene scene = valkyrie ? _valk : _thermal;
                if (kind == BackgroundKind.None || kind == BackgroundKind.Color)
                {
                    scene.DisposeSources();
                    scene.MediaBytes = null; scene.MediaName = ""; scene.MediaPath = "";
                }
                scene.Background = kind;
                MarkDirty(valkyrie);
            }
        }

        public void SetTargets(bool valkyrie, bool thermalright)
        {
            lock (_lock) { _targetValk = valkyrie; _targetThermal = thermalright; }
        }

        public void SetPreviewTarget(bool valkyrie)
        {
            lock (_lock) { _previewValk = valkyrie; _dirtyPreview = true; }
        }

        public bool PreviewIsValkyrie { get { lock (_lock) { return _previewValk; } } }

        public void StartStreaming() { _streaming = true; lock (_lock) { MarkDirty(true); MarkDirty(false); } Start(); }

        public void StopStreaming()
        {
            _streaming = false;
            try { ValkyrieDirectService.Stop(); } catch { }
            try { ThermalrightDirectService.Stop(); } catch { }
            Report(true, Loc.T("st.stopped"));
        }

        public bool IsStreaming { get { return _streaming; } }

        public byte[] PreviewPng() { lock (_lock) { return _previewPng; } }

        // ---- profiles ---------------------------------------------------------------

        public ProfileData Capture(bool targetValk, bool targetThermal, bool activeValk)
        {
            lock (_lock)
            {
                return new ProfileData
                {
                    Valk = ToData(_valk),
                    Thermal = ToData(_thermal),
                    TargetValk = targetValk,
                    TargetThermal = targetThermal,
                    ActiveValk = activeValk,
                    Streaming = _streaming
                };
            }
        }

        public void Apply(ProfileData p)
        {
            if (p == null) return;
            ApplyScene(true, p.Valk);
            ApplyScene(false, p.Thermal);
        }

        private static SceneData ToData(LcdScene s)
        {
            SceneData d = new SceneData
            {
                Background = (int)s.Background,
                MediaPath = s.MediaPath,
                MediaData = s.MediaBytes,
                BgColorArgb = s.BgColor.ToArgb(),
                Rotation = s.Rotation,
                FlipH = s.FlipH,
                Scale = s.Scale,
                PanX = s.PanX,
                PanY = s.PanY,
                Fit = (int)s.Fit
            };
            foreach (Overlay o in s.Overlays)
                d.Overlays.Add(new OverlayData
                {
                    Kind = (int)o.Kind, Text = o.Text, X = o.X, Y = o.Y, Size = o.Size,
                    Rotation = o.Rotation, ColorArgb = o.Color.ToArgb(), ShowLabel = o.ShowLabel
                });
            return d;
        }

        private void ApplyScene(bool valkyrie, SceneData d)
        {
            if (d == null) return;
            BackgroundKind bg = (BackgroundKind)d.Background;
            byte[] data = d.MediaData;
            Image img = null; GifState gif = null;
            try
            {
                if (bg == BackgroundKind.Image && data != null && data.Length > 0)
                    using (MemoryStream ms = new MemoryStream(data)) using (Image tmp = Image.FromStream(ms)) img = new Bitmap(tmp);
                else if (bg == BackgroundKind.Gif && data != null && data.Length > 0)
                    gif = GifState.LoadBytes(data);
                else if (bg == BackgroundKind.Image && !string.IsNullOrEmpty(d.MediaPath) && File.Exists(d.MediaPath))
                {
                    data = File.ReadAllBytes(d.MediaPath);
                    using (MemoryStream ms = new MemoryStream(data)) using (Image tmp = Image.FromStream(ms)) img = new Bitmap(tmp);
                }
                else if (bg == BackgroundKind.Gif && !string.IsNullOrEmpty(d.MediaPath) && File.Exists(d.MediaPath))
                {
                    data = File.ReadAllBytes(d.MediaPath);
                    gif = GifState.LoadBytes(data);
                }
                else if (bg == BackgroundKind.Image || bg == BackgroundKind.Gif)
                    bg = BackgroundKind.None;
            }
            catch { bg = BackgroundKind.None; img = null; gif = null; data = null; }

            lock (_lock)
            {
                LcdScene s = valkyrie ? _valk : _thermal;
                s.DisposeSources();
                s.ImageSource = img;
                s.Gif = gif;
                s.MediaBytes = data;
                s.Background = bg;
                s.MediaPath = d.MediaPath ?? "";
                s.MediaName = string.IsNullOrEmpty(d.MediaPath) ? "" : Path.GetFileName(d.MediaPath);
                s.BgColor = Color.FromArgb(d.BgColorArgb);
                s.Rotation = d.Rotation;
                s.FlipH = d.FlipH;
                s.Scale = d.Scale <= 0 ? 1.0 : d.Scale;
                s.PanX = d.PanX;
                s.PanY = d.PanY;
                s.Fit = (FitMode)d.Fit;
                s.Overlays.Clear();
                if (d.Overlays != null)
                    foreach (OverlayData od in d.Overlays)
                        s.Overlays.Add(new Overlay
                        {
                            Kind = (OverlayKind)od.Kind, Text = od.Text, X = od.X, Y = od.Y,
                            Size = od.Size, Rotation = od.Rotation, Color = Color.FromArgb(od.ColorArgb), ShowLabel = od.ShowLabel
                        });
                MarkDirty(valkyrie);
            }
        }

        private void MarkDirty(bool valkyrie)
        {
            if (valkyrie) _dirtyValk = true; else _dirtyThermal = true;
            _dirtyPreview = true;
        }

        // ---- engine loop ------------------------------------------------------------

        private void Loop()
        {
            long lastValk = 0, lastThermal = 0, lastPreview = 0, lastSensor = 0;
            long lastTick = Environment.TickCount;

            while (_alive)
            {
                long now = Environment.TickCount;
                int delta = (int)(now - lastTick);
                lastTick = now;

                bool needSensors;
                lock (_lock) { needSensors = _valk.IsDynamic() || _thermal.IsDynamic(); }
                if (needSensors && now - lastSensor >= 1000)
                {
                    SensorReading r = SensorService.Read();
                    lock (_lock) { _sensors = r; }
                    lastSensor = now;
                }

                lock (_lock)
                {
                    if (_valk.Gif != null) _valk.Gif.Advance(delta);
                    if (_thermal.Gif != null) _thermal.Gif.Advance(delta);
                }

                bool valkDyn, thermalDyn, previewValk, streaming, tv, tt;
                lock (_lock)
                {
                    valkDyn = _valk.IsDynamic(); thermalDyn = _thermal.IsDynamic();
                    previewValk = _previewValk; streaming = _streaming; tv = _targetValk; tt = _targetThermal;
                }

                bool previewDyn = previewValk ? valkDyn : thermalDyn;
                if (now - lastPreview >= 33 && (previewDyn || _dirtyPreview))
                {
                    RenderPreview(previewValk);
                    lastPreview = now;
                    _dirtyPreview = false;
                }

                if (streaming)
                {
                    if (tv && now - lastValk >= (valkDyn ? 50 : 120) && (valkDyn || _dirtyValk))
                    { PushValkyrie(); lastValk = now; _dirtyValk = false; }
                    if (tt && now - lastThermal >= (thermalDyn ? 150 : 320) && (thermalDyn || _dirtyThermal))
                    { PushThermalright(); lastThermal = now; _dirtyThermal = false; }
                }

                Thread.Sleep(15);
            }
        }

        private void PushValkyrie()
        {
            try
            {
                using (Bitmap bmp = RenderScene(true, ValkyrieDirectService.Width, ValkyrieDirectService.Height))
                {
                    DirectSendResult result = ValkyrieDirectService.ShowBgra(BgraFromBitmap(bmp));
                    Report(result.Success, result.Success ? Loc.T("st.active") : "Valkyrie: " + result.Message);
                }
            }
            catch (Exception ex) { Report(false, "Valkyrie: " + ex.Message); }
        }

        private void PushThermalright()
        {
            try
            {
                using (Bitmap bmp = RenderScene(false, ThermalrightDirectService.CanvasWidth, ThermalrightDirectService.CanvasHeight))
                {
                    DirectSendResult result = ThermalrightDirectService.ShowBitmap(bmp);
                    Report(result.Success, result.Success ? Loc.T("st.active") : "Thermalright: " + result.Message);
                }
            }
            catch (Exception ex) { Report(false, "Thermalright: " + ex.Message); }
        }

        private void RenderPreview(bool valkyrie)
        {
            try
            {
                int nativeW = valkyrie ? ValkyrieDirectService.Width : ThermalrightDirectService.CanvasWidth;
                int nativeH = valkyrie ? ValkyrieDirectService.Height : ThermalrightDirectService.CanvasHeight;
                double sc = Math.Min(1.0, 768.0 / nativeW);
                int w = Math.Max(1, (int)Math.Round(nativeW * sc));
                int h = Math.Max(1, (int)Math.Round(nativeH * sc));
                using (Bitmap bmp = RenderScene(valkyrie, w, h))
                using (MemoryStream stream = new MemoryStream())
                {
                    bmp.Save(stream, ImageFormat.Png);
                    byte[] png = stream.ToArray();
                    lock (_lock) { _previewPng = png; }
                }
            }
            catch { }
        }

        // ---- scene rendering --------------------------------------------------------

        private static readonly Color BgTop = Color.FromArgb(11, 16, 32);
        private static readonly Color BgBottom = Color.FromArgb(20, 27, 45);
        private static readonly Color Muted = Color.FromArgb(150, 165, 190);

        private Bitmap RenderScene(bool valkyrie, int w, int h)
        {
            // Snapshot scene fields + overlay copies under lock, then render unlocked.
            BackgroundKind bg; Color bgColor; Image img = null; Image gifFrame = null;
            double rotation; bool flip; double scale, panX, panY; FitMode fit;
            List<Overlay> overlays = new List<Overlay>();
            SensorReading sensors;
            lock (_lock)
            {
                LcdScene scene = valkyrie ? _valk : _thermal;
                bg = scene.Background; bgColor = scene.BgColor;
                rotation = scene.Rotation; flip = scene.FlipH; scale = scene.Scale; panX = scene.PanX; panY = scene.PanY; fit = scene.Fit;
                if (bg == BackgroundKind.Image) img = scene.ImageSource;
                else if (bg == BackgroundKind.Gif && scene.Gif != null) gifFrame = scene.Gif.CurrentFrame();
                foreach (Overlay o in scene.Overlays) overlays.Add(o.Clone());
                sensors = _sensors;
            }

            Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;

                if (bg == BackgroundKind.Color)
                    using (SolidBrush b = new SolidBrush(bgColor)) g.FillRectangle(b, 0, 0, w, h);
                else
                    using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(0, 0, w, h), BgTop, BgBottom, 90f))
                        g.FillRectangle(b, 0, 0, w, h);

                Image source = img != null ? img : gifFrame;
                if (source != null) DrawTransformed(g, source, w, h, rotation, flip, scale, panX, panY, fit);

                foreach (Overlay o in overlays) DrawOverlay(g, o, sensors, w, h);
            }
            return bmp;
        }

        private static void DrawTransformed(Graphics g, Image src, int w, int h, double rotation, bool flip,
                                            double scale, double panX, double panY, FitMode fit)
        {
            double baseScale = fit == FitMode.Fill
                ? Math.Max((double)w / src.Width, (double)h / src.Height)
                : Math.Min((double)w / src.Width, (double)h / src.Height);
            double sc = baseScale * (scale <= 0 ? 0.01 : scale);
            float dw = (float)(src.Width * sc);
            float dh = (float)(src.Height * sc);

            GraphicsState state = g.Save();
            g.TranslateTransform((float)(w / 2.0 + panX * w), (float)(h / 2.0 + panY * h));
            g.RotateTransform((float)rotation);
            g.ScaleTransform(flip ? -1f : 1f, 1f);
            g.DrawImage(src, -dw / 2f, -dh / 2f, dw, dh);
            g.Restore(state);
        }

        private static void DrawOverlay(Graphics g, Overlay o, SensorReading sensors, int w, int h)
        {
            string value = OverlayValue(o, sensors);
            string label = o.ShowLabel ? OverlayLabel(o) : "";
            float fontPx = Math.Max(8f, (float)(o.Size * h));
            float cx = (float)(o.X * w);
            float cy = (float)(o.Y * h);

            GraphicsState st = g.Save();
            g.TranslateTransform(cx, cy);
            if (o.Rotation != 0) g.RotateTransform((float)o.Rotation);
            if (label.Length > 0)
            {
                float labelPx = Math.Max(7f, fontPx * 0.32f);
                using (Font labelFont = new Font("Segoe UI", labelPx, FontStyle.Regular, GraphicsUnit.Pixel))
                using (Brush labelBrush = new SolidBrush(Muted))
                    DrawCenteredAt(g, label, labelFont, labelBrush, 0f, -fontPx * 0.55f, w, h);
            }
            if (o.Kind == OverlayKind.CpuCores)
            {
                DrawCoreBars(g, o, sensors, h);
            }
            else
            {
                using (Font font = new Font("Segoe UI", fontPx, FontStyle.Bold, GraphicsUnit.Pixel))
                using (Brush brush = new SolidBrush(o.Color))
                    DrawCenteredAt(g, value, font, brush, 0f, 0f, w, h);
            }
            g.Restore(st);
        }

        private static void DrawCoreBars(Graphics g, Overlay o, SensorReading s, int h)
        {
            double[] loads = s != null ? s.CpuCoreLoads : null;
            int n = loads != null ? loads.Length : 0;
            float boxH = Math.Max(10f, (float)(o.Size * h));
            if (n == 0)
            {
                using (Font f = new Font("Segoe UI", Math.Max(8f, boxH * 0.5f), FontStyle.Bold, GraphicsUnit.Pixel))
                using (Brush b = new SolidBrush(Muted))
                    DrawCenteredAt(g, "CPU --", f, b, 0f, 0f, 4000, 4000);
                return;
            }
            float barW = boxH * 0.16f;
            float gap = barW * 0.45f;
            float totalW = n * barW + (n - 1) * gap;
            float left = -totalW / 2f;
            float bottom = boxH / 2f;
            using (Pen baseline = new Pen(Muted, 1f))
                g.DrawLine(baseline, left, bottom, left + totalW, bottom);
            for (int i = 0; i < n; i++)
            {
                double load = loads[i];
                if (load < 0) load = 0; else if (load > 100) load = 100;
                float bh = (float)(load / 100.0) * boxH;
                float x = left + i * (barW + gap);
                using (SolidBrush br = new SolidBrush(LoadColor(load)))
                    g.FillRectangle(br, x, bottom - bh, barW, bh);
            }
        }

        private static Color LoadColor(double v)
        {
            if (v < 50) return Color.FromArgb(63, 209, 139);
            if (v < 80) return Color.FromArgb(245, 200, 80);
            return Color.FromArgb(255, 107, 129);
        }

        private static void DrawCenteredAt(Graphics g, string text, Font font, Brush brush, float cx, float cy, int w, int h)
        {
            using (StringFormat fmt = new StringFormat())
            {
                fmt.Alignment = StringAlignment.Center;
                fmt.LineAlignment = StringAlignment.Center;
                g.DrawString(text, font, brush, new RectangleF(cx - w, cy - h, w * 2f, h * 2f), fmt);
            }
        }

        private static string OverlayValue(Overlay o, SensorReading s)
        {
            switch (o.Kind)
            {
                case OverlayKind.Text: return o.Text;
                case OverlayKind.CpuTemp: return s != null && s.HasCpuTemp ? Round(s.CpuTempC) + "°C" : "--";
                case OverlayKind.GpuTemp: return s != null && s.HasGpuTemp ? Round(s.GpuTempC) + "°C" : "--";
                case OverlayKind.CpuLoad: return s != null && s.HasCpuLoad ? Round(s.CpuLoad) + "%" : "--";
                case OverlayKind.GpuLoad: return s != null && s.HasGpuLoad ? Round(s.GpuLoad) + "%" : "--";
                case OverlayKind.CpuClock: return s != null && s.HasCpuClock ? (s.CpuClockMhz / 1000.0).ToString("0.0") + " GHz" : "--";
                case OverlayKind.GpuClock: return s != null && s.HasGpuClock ? Round(s.GpuClockMhz) + " MHz" : "--";
                case OverlayKind.CpuPower: return s != null && s.HasCpuPower ? Round(s.CpuPowerW) + " W" : "--";
                case OverlayKind.GpuPower: return s != null && s.HasGpuPower ? Round(s.GpuPowerW) + " W" : "--";
                case OverlayKind.GpuVram: return s != null && s.HasGpuVram ? (s.GpuVramUsedMb / 1024.0).ToString("0.0") + " GB" : "--";
                case OverlayKind.RamLoad: return s != null && s.HasRamLoad ? Round(s.RamLoad) + "%" : "--";
                case OverlayKind.GpuFan: return s != null && s.HasGpuFan ? Round(s.GpuFanRpm) + " RPM" : "--";
                case OverlayKind.CpuCores: return "";
                case OverlayKind.Clock: return DateTime.Now.ToString("HH:mm:ss");
                case OverlayKind.Date: return DateTime.Now.ToString("ddd dd MMM");
            }
            return "";
        }

        private static string OverlayLabel(Overlay o)
        {
            switch (o.Kind)
            {
                case OverlayKind.CpuTemp: return "CPU";
                case OverlayKind.GpuTemp: return "GPU";
                case OverlayKind.CpuLoad: return "CPU";
                case OverlayKind.GpuLoad: return "GPU";
                case OverlayKind.CpuClock: return "CPU CLOCK";
                case OverlayKind.GpuClock: return "GPU CLOCK";
                case OverlayKind.CpuPower: return "CPU";
                case OverlayKind.GpuPower: return "GPU";
                case OverlayKind.GpuVram: return "VRAM";
                case OverlayKind.RamLoad: return "RAM";
                case OverlayKind.GpuFan: return "VENTOLA";
                case OverlayKind.CpuCores: return "CORE CPU";
                default: return "";
            }
        }

        private static string Round(double v) { return ((int)Math.Round(v)).ToString(); }

        // ---- pixel access -----------------------------------------------------------

        private static byte[] BgraFromBitmap(Bitmap bmp)
        {
            Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            BitmapData data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int rowBytes = bmp.Width * 4;
                byte[] buffer = new byte[rowBytes * bmp.Height];
                for (int y = 0; y < bmp.Height; y++)
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), buffer, y * rowBytes, rowBytes);
                return buffer;
            }
            finally { bmp.UnlockBits(data); }
        }

        // ---- status -----------------------------------------------------------------

        private void Report(bool ok, string message)
        {
            bool changed;
            lock (_lock) { changed = message != _status || ok != _statusOk; _status = message; _statusOk = ok; }
            if (changed && StatusChanged != null) StatusChanged(ok, message);
        }
    }
}
