using Microsoft.Win32;
using LibUsbDotNet;
using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace LcdFusion
{
    internal sealed class MainWindow : Window
    {
        private readonly Brush Bg = Br("#0A0D15");
        private readonly Brush Surface = Br("#141A28");
        private readonly Brush Surface2 = Br("#1B2336");
        private readonly Brush Panel = Br("#0E131F");
        private readonly Brush BorderBr = Br("#28324A");
        private readonly Brush SoftBr = Br("#1E2740");
        private readonly Brush TextBr = Br("#ECF0F7");
        private readonly Brush MutedBr = Br("#8B96AB");
        private readonly Brush AccentBr = Br("#6C8CFF");
        private readonly Brush AccentTextBr = Br("#0A0E18");
        private readonly Brush GreenBr = Br("#3FD18B");
        private readonly Brush RedBr = Br("#FF6B81");

        private enum DragMode { None, Overlay, Pan }

        private static readonly string[] MetricKeys =
            { "metric.cpuTemp", "metric.gpuTemp", "metric.cpuLoad", "metric.gpuLoad",
              "metric.cpuClock", "metric.gpuClock", "metric.cpuPower", "metric.gpuPower",
              "metric.gpuVram", "metric.gpuFan", "metric.ramLoad", "metric.cpuCores",
              "metric.clock", "metric.date", "metric.text" };
        private static readonly OverlayKind[] MetricKinds =
            { OverlayKind.CpuTemp, OverlayKind.GpuTemp, OverlayKind.CpuLoad, OverlayKind.GpuLoad,
              OverlayKind.CpuClock, OverlayKind.GpuClock, OverlayKind.CpuPower, OverlayKind.GpuPower,
              OverlayKind.GpuVram, OverlayKind.GpuFan, OverlayKind.RamLoad, OverlayKind.CpuCores,
              OverlayKind.Clock, OverlayKind.Date, OverlayKind.Text };
        private static readonly string[] SwatchHex = { "#FFFFFF", "#5BE1FF", "#33D69F", "#F5C850", "#FF647C", "#6C8CFF" };
        private static readonly string[] SwatchKeys =
            { "color.white", "color.cyan", "color.green", "color.amber", "color.red", "color.blue" };
        private static string MetricName(int i) { return Loc.T(MetricKeys[i]); }
        private static string SwatchName(int i) { return Loc.T(SwatchKeys[i]); }

        private readonly ContentEngine _engine = new ContentEngine();
        private bool _activeValk = true;
        private int _sel = -1;
        private bool _tgtValk = true;
        private bool _tgtThermal;

        private DragMode _drag = DragMode.None;
        private double _dragStartX, _dragStartY, _panOrigX, _panOrigY;

        private Ellipse _valkDot, _trDot;
        private TextBlock _valkState, _trState, _summary;
        private Button _tabValk, _tabThermal;
        private StackPanel _editorPanel;
        private Border _previewBorder;
        private TextBlock _previewCaption;
        private Image _preview;
        private TextBlock _statusText;
        private Ellipse _statusDot;
        private Button _startButton, _stopButton;

        private DeviceSnapshot _snapshot;
        private byte[] _lastPng;
        private ComboBox _profileCombo;
        private CheckBox _autoStartCheck;
        private bool _autoStart;
        private string _currentProfile = "";
        private System.Windows.Forms.NotifyIcon _tray;
        private System.Drawing.Icon _trayIcon;
        private System.Windows.Forms.ToolStripMenuItem _trayOpen, _trayExit;
        private bool _trayHinted;
        private volatile bool _refreshing;
        private readonly DispatcherTimer _timer;

        public MainWindow()
        {
            Resources.MergedDictionaries.Add(Theme.Build());

            AppSettings settings = ProfileService.LoadSettings();
            if (!string.IsNullOrEmpty(settings.Lang) && Array.IndexOf(Loc.Codes, settings.Lang) >= 0) Loc.Lang = settings.Lang;
            _currentProfile = settings.LastProfile ?? "";
            _autoStart = AutoStartService.IsEnabled();

            Title = "LCD Fusion";
            Width = 1240; Height = 860; MinWidth = 1040; MinHeight = 740;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Bg; Foreground = TextBr;
            FontFamily = new FontFamily("Segoe UI");
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            Icon = BuildWindowIcon();

            // Restore the last session before the UI is built so it reflects the saved state.
            ProfileData last = ProfileService.LoadLast();
            bool autoStream = last == null || last.Streaming;
            if (last != null)
            {
                _engine.Apply(last);
                _tgtValk = last.TargetValk; _tgtThermal = last.TargetThermal; _activeValk = last.ActiveValk;
            }

            Content = BuildUi();

            _engine.StatusChanged = OnEngineStatus;
            _engine.SetTargets(_tgtValk, _tgtThermal);
            _engine.SetPreviewTarget(_activeValk);
            _engine.Start();
            RebuildEditor();
            ApplyPreviewAspect();
            SetStreaming(autoStream);

            Loc.Changed += OnLanguageChanged;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            _timer.Tick += delegate { RefreshStatus(); };
            _timer.Start();
            CompositionTarget.Rendering += OnRender;
            RefreshStatus();
            SetupTray();
        }

        private void OnLanguageChanged()
        {
            Content = BuildUi();
            RebuildEditor();
            ApplyPreviewAspect();
            _engine.SetPreviewTarget(_activeValk);
            RefreshStatus();
            if (_startButton != null) _startButton.IsEnabled = !_engine.IsStreaming;
            if (_stopButton != null) _stopButton.IsEnabled = _engine.IsStreaming;
            if (_trayOpen != null) _trayOpen.Text = Loc.T("tray.open");
            if (_trayExit != null) _trayExit.Text = Loc.T("tray.exit");
        }

        // ---- system tray ------------------------------------------------------------

        private void SetupTray()
        {
            try { _trayIcon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location); }
            catch { _trayIcon = null; }

            _tray = new System.Windows.Forms.NotifyIcon { Text = "LCD Fusion", Visible = false };
            if (_trayIcon != null) _tray.Icon = _trayIcon;
            _tray.DoubleClick += delegate { RestoreFromTray(); };

            var menu = new System.Windows.Forms.ContextMenuStrip();
            _trayOpen = new System.Windows.Forms.ToolStripMenuItem(Loc.T("tray.open"));
            _trayOpen.Click += delegate { RestoreFromTray(); };
            _trayExit = new System.Windows.Forms.ToolStripMenuItem(Loc.T("tray.exit"));
            _trayExit.Click += delegate { Close(); };
            menu.Items.Add(_trayOpen);
            menu.Items.Add(_trayExit);
            _tray.ContextMenuStrip = menu;

            StateChanged += OnStateChanged;
        }

        private void OnStateChanged(object sender, EventArgs e)
        {
            if (WindowState != WindowState.Minimized || _tray == null) return;
            Hide();
            _tray.Visible = true;
            if (!_trayHinted)
            {
                _trayHinted = true;
                try { _tray.ShowBalloonTip(3000, "LCD Fusion", Loc.T("tray.hint"), System.Windows.Forms.ToolTipIcon.Info); }
                catch { }
            }
        }

        private void RestoreFromTray()
        {
            if (_tray != null) _tray.Visible = false;
            Show();
            WindowState = WindowState.Normal;
            Activate();
            Topmost = true; Topmost = false;
        }

        // ---- layout -----------------------------------------------------------------

        private UIElement BuildUi()
        {
            var rootGrid = new Grid { Margin = new Thickness(30, 24, 30, 24) };
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rootGrid.RowDefinitions.Add(new RowDefinition());

            // header
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var brand = new StackPanel { Orientation = Orientation.Horizontal };
            var mark = new Image { Source = BuildWindowIcon(), Width = 38, Height = 38, VerticalAlignment = VerticalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(mark, BitmapScalingMode.HighQuality);
            brand.Children.Add(mark);
            var brandText = new StackPanel { Margin = new Thickness(13, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            brandText.Children.Add(new TextBlock { Text = "LCD Fusion", FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = TextBr });
            brandText.Children.Add(new TextBlock { Text = Loc.T("app.subtitle"), FontSize = 12, Foreground = MutedBr, Margin = new Thickness(0, 1, 0, 0) });
            brand.Children.Add(brandText);
            header.Children.Add(brand);

            var headerRight = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            headerRight.Children.Add(DevicePill(true));
            headerRight.Children.Add(DevicePill(false));
            headerRight.Children.Add(LangSelector());
            var free = Btn(Loc.T("btn.free"), delegate { FreeDevices(); }, "BtnGhost");
            free.Margin = new Thickness(8, 0, 0, 0);
            headerRight.Children.Add(free);
            Grid.SetColumn(headerRight, 1);
            header.Children.Add(headerRight);
            Grid.SetRow(header, 0);
            rootGrid.Children.Add(header);

            _summary = new TextBlock { FontSize = 12, Foreground = MutedBr, Margin = new Thickness(2, 12, 0, 0) };
            Grid.SetRow(_summary, 1);
            rootGrid.Children.Add(_summary);

            var toolbar = BuildToolbar();
            Grid.SetRow(toolbar, 2);
            rootGrid.Children.Add(toolbar);

            // body fills the remaining height; only the editor scrolls internally
            var body = new Grid { Margin = new Thickness(0, 18, 0, 0) };
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.18, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.Children.Add(EditorColumn());
            var right = PreviewColumn();
            right.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(right, 2);
            body.Children.Add(right);
            Grid.SetRow(body, 3);
            rootGrid.Children.Add(body);

            return rootGrid;
        }

        private UIElement BuildToolbar()
        {
            var bar = new Border { Background = Surface, CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 9, 14, 9), BorderBrush = SoftBr, BorderThickness = new Thickness(1), Margin = new Thickness(0, 14, 0, 0) };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(new TextBlock { Text = Loc.T("prof.label"), Foreground = MutedBr, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            _profileCombo = new ComboBox { Width = 220, VerticalAlignment = VerticalAlignment.Center };
            PopulateProfiles();
            left.Children.Add(_profileCombo);
            var load = Btn(Loc.T("prof.load"), delegate { LoadSelectedProfile(); }, "BtnGhost"); load.Margin = new Thickness(10, 0, 0, 0); left.Children.Add(load);
            var saveAs = Btn(Loc.T("prof.saveAs"), delegate { SaveProfileAs(); }, "BtnGhost"); saveAs.Margin = new Thickness(8, 0, 0, 0); left.Children.Add(saveAs);
            var del = Btn(Loc.T("prof.delete"), delegate { DeleteSelectedProfile(); }, "BtnGhost"); del.Margin = new Thickness(8, 0, 0, 0); left.Children.Add(del);
            grid.Children.Add(left);

            _autoStartCheck = new CheckBox { Content = Loc.T("prof.autostart"), IsChecked = _autoStart, Foreground = TextBr, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            _autoStartCheck.Checked += delegate { ToggleAutostart(true); };
            _autoStartCheck.Unchecked += delegate { ToggleAutostart(false); };
            Grid.SetColumn(_autoStartCheck, 2);
            grid.Children.Add(_autoStartCheck);

            bar.Child = grid;
            return bar;
        }

        private void PopulateProfiles()
        {
            _profileCombo.Items.Clear();
            string[] names = ProfileService.List();
            foreach (string n in names) _profileCombo.Items.Add(n);
            _profileCombo.IsEnabled = names.Length > 0;
            if (names.Length > 0)
            {
                int idx = Array.IndexOf(names, _currentProfile);
                _profileCombo.SelectedIndex = idx >= 0 ? idx : 0;
            }
        }

        private void LoadSelectedProfile()
        {
            if (_profileCombo.SelectedItem == null) return;
            string name = _profileCombo.SelectedItem.ToString();
            ProfileData p = ProfileService.Load(name);
            if (p == null) return;
            ApplyProfile(p);
            _currentProfile = name;
            SetStatus(true, Loc.T("prof.loaded") + ": " + name);
        }

        private void ApplyProfile(ProfileData p)
        {
            _engine.Apply(p);
            _tgtValk = p.TargetValk; _tgtThermal = p.TargetThermal; _activeValk = p.ActiveValk;
            _engine.SetTargets(_tgtValk, _tgtThermal);
            _sel = -1;
            _engine.SetPreviewTarget(_activeValk);
            ApplyPreviewAspect();
            RebuildEditor();
            SetStreaming(p.Streaming);
        }

        private void SaveProfileAs()
        {
            string def = string.IsNullOrEmpty(_currentProfile) ? "Profile 1" : _currentProfile;
            string name = PromptName(Loc.T("prof.nameTitle"), def);
            if (string.IsNullOrEmpty(name)) return;
            ProfileService.Save(name, _engine.Capture(_tgtValk, _tgtThermal, _activeValk));
            _currentProfile = name;
            PopulateProfiles();
            SetStatus(true, Loc.T("prof.saved") + ": " + name);
        }

        private void DeleteSelectedProfile()
        {
            if (_profileCombo.SelectedItem == null) return;
            string name = _profileCombo.SelectedItem.ToString();
            ProfileService.Delete(name);
            if (_currentProfile == name) _currentProfile = "";
            PopulateProfiles();
            SetStatus(true, Loc.T("prof.deleted") + ": " + name);
        }

        private void ToggleAutostart(bool on)
        {
            bool ok = on ? AutoStartService.Enable() : AutoStartService.Disable();
            if (ok) { _autoStart = on; return; }
            _autoStart = AutoStartService.IsEnabled();
            if (_autoStartCheck != null && _autoStartCheck.IsChecked != _autoStart) _autoStartCheck.IsChecked = _autoStart;
        }

        private string PromptName(string title, string def)
        {
            var dlg = new Window
            {
                Title = title, Width = 400, SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this,
                ResizeMode = ResizeMode.NoResize, Background = Surface, Foreground = TextBr,
                WindowStyle = WindowStyle.ToolWindow, FontFamily = new FontFamily("Segoe UI")
            };
            dlg.Resources.MergedDictionaries.Add(Theme.Build());
            var panel = new StackPanel { Margin = new Thickness(20) };
            var tb = new TextBox { Text = def, FontSize = 14 };
            panel.Children.Add(tb);
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            string result = null;
            var ok = new Button { Content = Loc.T("dlg.ok"), IsDefault = true };
            ok.Style = (Style)dlg.FindResource("BtnPrimary");
            ok.Click += delegate { result = tb.Text; dlg.DialogResult = true; };
            var cancel = new Button { Content = Loc.T("dlg.cancel"), IsCancel = true, Margin = new Thickness(10, 0, 0, 0) };
            cancel.Style = (Style)dlg.FindResource("BtnGhost");
            row.Children.Add(ok);
            row.Children.Add(cancel);
            panel.Children.Add(row);
            dlg.Content = panel;
            dlg.Loaded += delegate { tb.Focus(); tb.SelectAll(); };
            bool? r = dlg.ShowDialog();
            if (r != true || string.IsNullOrEmpty(result)) return null;
            return result.Trim();
        }

        private Border DevicePill(bool valkyrie)
        {
            var pill = new Border { Background = Surface2, CornerRadius = new CornerRadius(20), Padding = new Thickness(13, 7, 15, 7), Margin = new Thickness(0, 0, 8, 0), BorderBrush = SoftBr, BorderThickness = new Thickness(1) };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var dot = new Ellipse { Width = 9, Height = 9, Fill = RedBr, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            row.Children.Add(dot);
            var name = new TextBlock { Text = valkyrie ? "Valkyrie" : "Thermalright", FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = TextBr, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(name);
            var state = new TextBlock { Text = "—", FontSize = 12, Foreground = MutedBr, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            row.Children.Add(state);
            pill.Child = row;
            pill.ToolTip = valkyrie ? ProductCatalog.Valkyrie.Model : ProductCatalog.Thermalright.Model;
            if (valkyrie) { _valkDot = dot; _valkState = state; } else { _trDot = dot; _trState = state; }
            return pill;
        }

        private UIElement LangSelector()
        {
            var combo = new ComboBox { Width = 124, VerticalAlignment = VerticalAlignment.Center, ToolTip = Loc.T("lang.label") };
            foreach (string n in Loc.Names) combo.Items.Add(n);
            combo.SelectedIndex = Loc.CurrentIndex();
            combo.SelectionChanged += delegate { if (combo.SelectedIndex >= 0) Loc.Set(Loc.Codes[combo.SelectedIndex]); };
            return combo;
        }

        private Border EditorColumn()
        {
            var card = Card();
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition());

            var seg = new Border { Background = Panel, CornerRadius = new CornerRadius(11), Padding = new Thickness(4), BorderBrush = SoftBr, BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left };
            var segRow = new StackPanel { Orientation = Orientation.Horizontal };
            _tabValk = SegButton("Valkyrie", true);
            _tabThermal = SegButton("Thermalright", false);
            segRow.Children.Add(_tabValk);
            segRow.Children.Add(_tabThermal);
            seg.Child = segRow;
            Grid.SetRow(seg, 0);
            grid.Children.Add(seg);

            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 16, 0, 0) };
            _editorPanel = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            sv.Content = _editorPanel;
            Grid.SetRow(sv, 1);
            grid.Children.Add(sv);

            card.Child = grid;
            return card;
        }

        private Border PreviewColumn()
        {
            var card = Card();
            var stack = new StackPanel();
            stack.Children.Add(SectionLabel(Loc.T("sec.preview")));

            var frame = new Border { Background = Panel, CornerRadius = new CornerRadius(12), BorderBrush = SoftBr, BorderThickness = new Thickness(1), Padding = new Thickness(16), Margin = new Thickness(0, 12, 0, 0) };
            var center = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            _previewBorder = new Border { Background = Br("#05070C"), CornerRadius = new CornerRadius(8), BorderBrush = BorderBr, BorderThickness = new Thickness(1), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Center };
            _preview = new Image { Stretch = Stretch.Fill, SnapsToDevicePixels = true };
            RenderOptions.SetBitmapScalingMode(_preview, BitmapScalingMode.HighQuality);
            _previewBorder.Child = _preview;
            _previewBorder.MouseLeftButtonDown += PreviewDown;
            _previewBorder.MouseMove += PreviewMove;
            _previewBorder.MouseLeftButtonUp += PreviewUp;
            center.Children.Add(_previewBorder);
            _previewCaption = new TextBlock { Text = "", Foreground = MutedBr, FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 11, 0, 0) };
            center.Children.Add(_previewCaption);
            frame.Child = center;
            stack.Children.Add(frame);

            stack.Children.Add(new TextBlock { Text = Loc.T("preview.hint"), Foreground = MutedBr, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 12, 0, 0) });

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
            _startButton = Btn(Loc.T("btn.start"), delegate { StartStreaming(); }, "BtnPrimary");
            actions.Children.Add(_startButton);
            _stopButton = Btn(Loc.T("btn.stop"), delegate { StopStreaming(); }, "BtnGhost");
            _stopButton.Margin = new Thickness(10, 0, 0, 0); _stopButton.IsEnabled = false;
            actions.Children.Add(_stopButton);
            stack.Children.Add(actions);

            var statusRow = new Border { Background = Panel, CornerRadius = new CornerRadius(9), Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 16, 0, 0), BorderBrush = SoftBr, BorderThickness = new Thickness(1) };
            var sr = new StackPanel { Orientation = Orientation.Horizontal };
            _statusDot = new Ellipse { Width = 9, Height = 9, Fill = GreenBr, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 9, 0) };
            sr.Children.Add(_statusDot);
            _statusText = new TextBlock { Text = Loc.T("st.ready"), Foreground = TextBr, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            sr.Children.Add(_statusText);
            statusRow.Child = sr;
            stack.Children.Add(statusRow);

            card.Child = stack;
            return card;
        }

        // ---- editor rebuild ---------------------------------------------------------

        private void RebuildEditor()
        {
            StyleTab(_tabValk, _activeValk);
            StyleTab(_tabThermal, !_activeValk);

            LcdScene scene = _engine.Scene(_activeValk);
            _editorPanel.Children.Clear();

            var target = new CheckBox { Content = Loc.T("tgt.stream"), IsChecked = _activeValk ? _tgtValk : _tgtThermal, Foreground = TextBr, FontSize = 13.5, FontWeight = FontWeights.SemiBold };
            target.Checked += delegate { SetTarget(true); };
            target.Unchecked += delegate { SetTarget(false); };
            _editorPanel.Children.Add(target);

            _editorPanel.Children.Add(SectionLabel(Loc.T("sec.background"), 18));
            var bgRow = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            bgRow.Children.Add(Chip(Loc.T("bg.image"), delegate { PickMedia(false); }));
            bgRow.Children.Add(Chip("GIF", delegate { PickMedia(true); }));
            bgRow.Children.Add(Chip(Loc.T("bg.color"), delegate { _engine.SetBackgroundKind(_activeValk, BackgroundKind.Color); RebuildEditor(); }));
            bgRow.Children.Add(Chip(Loc.T("bg.none"), delegate { _engine.SetBackgroundKind(_activeValk, BackgroundKind.None); RebuildEditor(); }));
            _editorPanel.Children.Add(bgRow);

            if (scene.Background == BackgroundKind.Image || scene.Background == BackgroundKind.Gif)
            {
                _editorPanel.Children.Add(new TextBlock { Text = scene.MediaName, Foreground = MutedBr, FontSize = 12, Margin = new Thickness(2, 8, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
                var tr = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
                tr.Children.Add(Chip("↺ 90°", delegate { _engine.Edit(_activeValk, s => s.Rotation = (s.Rotation + 270) % 360); RebuildEditor(); }));
                tr.Children.Add(Chip("↻ 90°", delegate { _engine.Edit(_activeValk, s => s.Rotation = (s.Rotation + 90) % 360); RebuildEditor(); }));
                tr.Children.Add(Chip(Loc.T("tf.mirror"), delegate { _engine.Edit(_activeValk, s => s.FlipH = !s.FlipH); }));
                tr.Children.Add(Chip(scene.Fit == FitMode.Fit ? Loc.T("tf.fit") : Loc.T("tf.fill"), delegate { _engine.Edit(_activeValk, s => s.Fit = s.Fit == FitMode.Fit ? FitMode.Fill : FitMode.Fit); RebuildEditor(); }));
                tr.Children.Add(Chip(Loc.T("tf.reset"), delegate { _engine.Edit(_activeValk, s => { s.Rotation = 0; s.FlipH = false; s.Scale = 1; s.PanX = 0; s.PanY = 0; }); RebuildEditor(); }));
                _editorPanel.Children.Add(tr);
                _editorPanel.Children.Add(Slider(Loc.T("sl.zoom"), 0.2, 3.0, scene.Scale, v => _engine.Edit(_activeValk, s => s.Scale = v), v => v.ToString("0.00") + "×"));
                _editorPanel.Children.Add(Slider(Loc.T("sl.rotation"), 0, 360, scene.Rotation, v => _engine.Edit(_activeValk, s => s.Rotation = v), v => ((int)v) + "°"));
            }

            _editorPanel.Children.Add(SectionLabel(Loc.T("sec.layers"), 20));
            var add = new WrapPanel { Margin = new Thickness(0, 10, 0, 6) };
            add.Children.Add(Chip(Loc.T("ly.addSensor"), delegate { AddOverlay(OverlayKind.CpuTemp); }));
            add.Children.Add(Chip(Loc.T("ly.addText"), delegate { AddOverlay(OverlayKind.Text); }));
            _editorPanel.Children.Add(add);

            for (int i = 0; i < scene.Overlays.Count; i++)
            {
                int index = i;
                _editorPanel.Children.Add(LayerRow(scene.Overlays[i], index));
            }

            if (_sel >= 0 && _sel < scene.Overlays.Count)
                _editorPanel.Children.Add(OverlayEditor(scene.Overlays[_sel]));
        }

        private UIElement LayerRow(Overlay o, int index)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(3), Background = WpfColor(o.Color), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            row.Children.Add(new TextBlock { Text = LayerLabel(o), Foreground = TextBr, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });

            var sel = new Button { Content = row, HorizontalContentAlignment = HorizontalAlignment.Left, HorizontalAlignment = HorizontalAlignment.Stretch };
            sel.Style = (Style)FindResource("BtnGhost");
            sel.Click += delegate { _sel = index; RebuildEditor(); };
            if (index == _sel) { sel.Background = Br("#1E2D49"); sel.BorderBrush = AccentBr; }
            Grid.SetColumn(sel, 0);
            grid.Children.Add(sel);

            var remove = new Button { Content = "✕", ToolTip = Loc.T("ov.remove"), Margin = new Thickness(8, 0, 0, 0), MinWidth = 42 };
            remove.Style = (Style)FindResource("BtnDanger");
            remove.Click += delegate { RemoveOverlayAt(index); };
            Grid.SetColumn(remove, 1);
            grid.Children.Add(remove);

            return grid;
        }

        private UIElement OverlayEditor(Overlay o)
        {
            var box = new Border { Background = Panel, CornerRadius = new CornerRadius(10), Padding = new Thickness(14), Margin = new Thickness(0, 6, 0, 0), BorderBrush = SoftBr, BorderThickness = new Thickness(1) };
            var stack = new StackPanel();

            stack.Children.Add(new TextBlock { Text = Loc.T("ov.type"), Foreground = MutedBr, FontSize = 12 });
            var combo = new ComboBox { Margin = new Thickness(0, 5, 0, 0) };
            for (int i = 0; i < MetricKinds.Length; i++) combo.Items.Add(MetricName(i));
            combo.SelectedIndex = KindIndex(o.Kind);
            combo.SelectionChanged += delegate
            {
                int idx = combo.SelectedIndex;
                if (idx < 0) return;
                _engine.Edit(_activeValk, s => { if (_sel < s.Overlays.Count) s.Overlays[_sel].Kind = MetricKinds[idx]; });
                RebuildEditor();
            };
            stack.Children.Add(combo);

            if (o.Kind == OverlayKind.Text)
            {
                var tb = new TextBox { Text = o.Text, Margin = new Thickness(0, 10, 0, 0) };
                tb.TextChanged += delegate { _engine.Edit(_activeValk, s => { if (_sel < s.Overlays.Count) s.Overlays[_sel].Text = tb.Text; }); };
                stack.Children.Add(tb);
            }

            stack.Children.Add(Slider(Loc.T("sl.size"), 0.05, 0.9, o.Size, v => _engine.Edit(_activeValk, s => { if (_sel < s.Overlays.Count) s.Overlays[_sel].Size = v; }), v => (int)(v * 100) + "%"));
            stack.Children.Add(Slider(Loc.T("sl.rotation"), 0, 360, o.Rotation, v => _engine.Edit(_activeValk, s => { if (_sel < s.Overlays.Count) s.Overlays[_sel].Rotation = v; }), v => ((int)v) + "°"));

            stack.Children.Add(new TextBlock { Text = Loc.T("ov.color"), Foreground = MutedBr, FontSize = 12, Margin = new Thickness(0, 10, 0, 0) });
            var colors = new WrapPanel { Margin = new Thickness(0, 7, 0, 0) };
            for (int i = 0; i < SwatchHex.Length; i++)
            {
                string hex = SwatchHex[i];
                bool selected = SameColor(o.Color, hex);
                var sw = new Button { Width = 28, Height = 28, Margin = new Thickness(0, 0, 9, 0), Background = Br(hex), BorderBrush = selected ? AccentBr : SoftBr, BorderThickness = new Thickness(selected ? 2 : 1), Cursor = Cursors.Hand, ToolTip = SwatchName(i) };
                sw.Template = SwatchTemplate();
                sw.Click += delegate { _engine.Edit(_activeValk, s => { if (_sel < s.Overlays.Count) s.Overlays[_sel].Color = Sd(hex); }); RebuildEditor(); };
                colors.Children.Add(sw);
            }
            stack.Children.Add(colors);

            var bottom = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
            var labelToggle = new CheckBox { Content = Loc.T("ov.label"), IsChecked = o.ShowLabel, Foreground = TextBr, FontSize = 13, Margin = new Thickness(0, 4, 16, 0), VerticalAlignment = VerticalAlignment.Center };
            labelToggle.Checked += delegate { _engine.Edit(_activeValk, s => { if (_sel < s.Overlays.Count) s.Overlays[_sel].ShowLabel = true; }); };
            labelToggle.Unchecked += delegate { _engine.Edit(_activeValk, s => { if (_sel < s.Overlays.Count) s.Overlays[_sel].ShowLabel = false; }); };
            bottom.Children.Add(labelToggle);
            bottom.Children.Add(Chip(Loc.T("ov.center"), delegate { _engine.Edit(_activeValk, s => { if (_sel < s.Overlays.Count) { s.Overlays[_sel].X = 0.5; s.Overlays[_sel].Y = 0.5; } }); }));
            stack.Children.Add(bottom);

            box.Child = stack;
            return box;
        }

        // ---- actions ----------------------------------------------------------------

        private void PickMedia(bool gif)
        {
            var dialog = new OpenFileDialog
            {
                Title = gif ? Loc.T("dlg.pickGif") : Loc.T("dlg.pickImage"),
                Filter = gif ? "GIF|*.gif" : Loc.T("dlg.images") + "|*.png;*.jpg;*.jpeg;*.bmp"
            };
            if (dialog.ShowDialog(this) != true) return;
            if (gif) _engine.LoadGif(_activeValk, dialog.FileName);
            else _engine.LoadImage(_activeValk, dialog.FileName);
            RebuildEditor();
        }

        private void AddOverlay(OverlayKind kind)
        {
            _engine.Edit(_activeValk, s =>
            {
                Overlay o = new Overlay { Kind = kind, X = 0.5, Y = 0.5, Size = _activeValk ? 0.22 : 0.45 };
                if (kind == OverlayKind.Text) o.Text = Loc.T("text.default");
                s.Overlays.Add(o);
                _sel = s.Overlays.Count - 1;
            });
            RebuildEditor();
        }

        private void RemoveOverlayAt(int index)
        {
            _engine.Edit(_activeValk, s => { if (index >= 0 && index < s.Overlays.Count) s.Overlays.RemoveAt(index); });
            if (_sel == index) _sel = -1;
            else if (_sel > index) _sel--;
            RebuildEditor();
        }

        private void SetTarget(bool on)
        {
            if (_activeValk) _tgtValk = on; else _tgtThermal = on;
            _engine.SetTargets(_tgtValk, _tgtThermal);
        }

        private void SwitchTab(bool valkyrie)
        {
            if (_activeValk == valkyrie) return;
            _activeValk = valkyrie;
            _sel = -1;
            _engine.SetPreviewTarget(valkyrie);
            ApplyPreviewAspect();
            RebuildEditor();
        }

        private void StartStreaming()
        {
            if (!_tgtValk && !_tgtThermal)
            {
                MessageBox.Show(this, Loc.T("msg.noTarget"), "LCD Fusion");
                return;
            }
            SetStreaming(true);
        }

        private void StopStreaming() { SetStreaming(false); }

        private void SetStreaming(bool on)
        {
            if (on && !_tgtValk && !_tgtThermal) on = false;
            if (on)
            {
                _engine.SetTargets(_tgtValk, _tgtThermal);
                _engine.StartStreaming();
                SetStatus(true, Loc.T("st.starting"));
            }
            else
            {
                _engine.StopStreaming();
            }
            if (_startButton != null) _startButton.IsEnabled = !on;
            if (_stopButton != null) _stopButton.IsEnabled = on;
        }

        private void FreeDevices()
        {
            SetStatus(true, Loc.T("st.freeing"));
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool stopped = VendorService.StopConflictingSoftware();
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    RefreshStatus();
                    SetStatus(stopped, stopped ? Loc.T("st.freed") : Loc.T("st.freeFail"));
                }));
            });
        }

        // ---- preview drag -----------------------------------------------------------

        private void PreviewDown(object sender, MouseButtonEventArgs e)
        {
            double fx, fy; Frac(e, out fx, out fy);
            LcdScene scene = _engine.Scene(_activeValk);
            int hit = HitTest(scene, fx, fy);
            if (hit >= 0) { _sel = hit; _drag = DragMode.Overlay; RebuildEditor(); }
            else if (scene.Background == BackgroundKind.Image || scene.Background == BackgroundKind.Gif)
            { _drag = DragMode.Pan; _dragStartX = fx; _dragStartY = fy; _panOrigX = scene.PanX; _panOrigY = scene.PanY; }
            else _drag = DragMode.None;
            _previewBorder.CaptureMouse();
        }

        private void PreviewMove(object sender, MouseEventArgs e)
        {
            if (_drag == DragMode.None || e.LeftButton != MouseButtonState.Pressed) return;
            double fx, fy; Frac(e, out fx, out fy);
            if (_drag == DragMode.Overlay)
            {
                double cx = Clamp01(fx), cy = Clamp01(fy);
                _engine.Edit(_activeValk, s => { if (_sel >= 0 && _sel < s.Overlays.Count) { s.Overlays[_sel].X = cx; s.Overlays[_sel].Y = cy; } });
            }
            else if (_drag == DragMode.Pan)
            {
                double nx = _panOrigX + (fx - _dragStartX);
                double ny = _panOrigY + (fy - _dragStartY);
                _engine.Edit(_activeValk, s => { s.PanX = nx; s.PanY = ny; });
            }
        }

        private void PreviewUp(object sender, MouseButtonEventArgs e)
        {
            _drag = DragMode.None;
            _previewBorder.ReleaseMouseCapture();
        }

        private void Frac(MouseEventArgs e, out double fx, out double fy)
        {
            Point p = e.GetPosition(_previewBorder);
            double aw = _previewBorder.ActualWidth, ah = _previewBorder.ActualHeight;
            fx = aw > 0 ? p.X / aw : 0;
            fy = ah > 0 ? p.Y / ah : 0;
        }

        private static int HitTest(LcdScene scene, double fx, double fy)
        {
            int best = -1; double bestDist = 0.13;
            for (int i = 0; i < scene.Overlays.Count; i++)
            {
                Overlay o = scene.Overlays[i];
                double d = Math.Max(Math.Abs(o.X - fx), Math.Abs(o.Y - fy));
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }

        // ---- timers / status --------------------------------------------------------

        private void OnEngineStatus(bool ok, string message)
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                SetStatus(ok, message);
                if (!ok) { _startButton.IsEnabled = true; _stopButton.IsEnabled = _engine.IsStreaming; }
            }));
        }

        private void SetStatus(bool ok, string message)
        {
            _statusText.Text = message;
            _statusDot.Fill = ok ? GreenBr : RedBr;
        }

        private void OnRender(object sender, EventArgs e)
        {
            byte[] png = _engine.PreviewPng();
            if (png == null || ReferenceEquals(png, _lastPng)) return;
            _lastPng = png;
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = new MemoryStream(png);
                image.EndInit();
                image.Freeze();
                _preview.Source = image;
            }
            catch { }
        }

        private void ApplyPreviewAspect()
        {
            if (_activeValk) { _previewBorder.Width = 384; _previewBorder.Height = 288; _previewCaption.Text = "Valkyrie · 320 × 240"; }
            else { _previewBorder.Width = 760; _previewBorder.Height = 183; _previewCaption.Text = "Thermalright · 1920 × 462"; }
        }

        // Device status uses a slow WMI query (Win32_PnPEntity) plus process lookups.
        // Run it on a background thread so dragging/resizing the window never blocks the UI.
        private void RefreshStatus()
        {
            if (_refreshing) return;
            _refreshing = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                DeviceSnapshot snap = null;
                bool myth = false, trcc = false;
                try
                {
                    snap = DeviceService.Read();
                    myth = VendorService.IsMythCoolRunning();
                    trcc = VendorService.IsTrccRunning();
                }
                catch { }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    try { if (snap != null) ApplyStatus(snap, myth, trcc); }
                    finally { _refreshing = false; }
                }));
            });
        }

        private void ApplyStatus(DeviceSnapshot snap, bool myth, bool trcc)
        {
            _snapshot = snap;
            SetDevice(snap.Valkyrie, _valkDot, _valkState);
            SetDevice(snap.Thermalright, _trDot, _trState);
            int healthy = (snap.Valkyrie.IsHealthy ? 1 : 0) + (snap.Thermalright.IsHealthy ? 1 : 0);
            string summary = healthy == 2 ? Loc.T("sum.both") : Loc.T("sum.count", healthy);
            if (myth || trcc)
                summary = Loc.T("sum.busy", myth && trcc ? "Myth.Cool + TRCC" : myth ? "Myth.Cool" : "TRCC");
            _summary.Text = summary + "   ·   " + Loc.T("sum.updated") + " " + snap.CheckedAt.ToString("HH:mm:ss");
        }

        private void SetDevice(DeviceInfo info, Ellipse dot, TextBlock state)
        {
            dot.Fill = info.IsHealthy ? GreenBr : RedBr;
            state.Foreground = info.IsHealthy ? GreenBr : MutedBr;
            state.Text = info.IsHealthy ? Loc.T("dev.ready") : info.Present ? Loc.T("dev.error") + " " + info.ErrorCode : Loc.T("dev.absent");
        }

        // ---- widgets ----------------------------------------------------------------

        private string LayerLabel(Overlay o)
        {
            if (o.Kind == OverlayKind.Text) return Loc.T("layer.textPrefix") + (o.Text ?? "");
            return MetricName(KindIndex(o.Kind));
        }

        private static int KindIndex(OverlayKind kind)
        {
            for (int i = 0; i < MetricKinds.Length; i++) if (MetricKinds[i] == kind) return i;
            return 0;
        }

        private UIElement Slider(string label, double min, double max, double value, Action<double> onChange, Func<double, string> fmt)
        {
            var stack = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Children.Add(new TextBlock { Text = label, Foreground = MutedBr, FontSize = 12 });
            double v0 = Math.Min(max, Math.Max(min, value));
            var valueText = new TextBlock { Text = fmt(v0), Foreground = TextBr, FontSize = 12, FontWeight = FontWeights.SemiBold };
            Grid.SetColumn(valueText, 1);
            head.Children.Add(valueText);
            stack.Children.Add(head);
            var slider = new Slider { Minimum = min, Maximum = max, Value = v0, Margin = new Thickness(0, 3, 0, 0) };
            slider.ValueChanged += delegate { valueText.Text = fmt(slider.Value); onChange(slider.Value); };
            stack.Children.Add(slider);
            return stack;
        }

        private Button SegButton(string text, bool valkyrie)
        {
            var b = new Button { Content = text, MinWidth = 132 };
            b.Style = (Style)FindResource("SegBtn");
            b.Click += delegate { SwitchTab(valkyrie); };
            return b;
        }

        private void StyleTab(Button b, bool active)
        {
            b.Background = active ? AccentBr : Brushes.Transparent;
            b.Foreground = active ? AccentTextBr : MutedBr;
        }

        private Button Btn(string text, RoutedEventHandler handler, string styleKey)
        {
            var b = new Button { Content = text };
            b.Style = (Style)FindResource(styleKey);
            b.Click += handler;
            return b;
        }

        private Button Chip(string text, RoutedEventHandler handler)
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 0, 8, 8) };
            b.Style = (Style)FindResource("BtnChip");
            b.Click += handler;
            return b;
        }

        private ControlTemplate SwatchTemplate()
        {
            var ct = new ControlTemplate(typeof(Button));
            var border = new System.Windows.FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
            border.SetValue(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.SetValue(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.SetValue(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            ct.VisualTree = border;
            return ct;
        }

        private Border Card()
        {
            return new Border { Background = Surface, CornerRadius = new CornerRadius(16), Padding = new Thickness(22), BorderBrush = SoftBr, BorderThickness = new Thickness(1) };
        }

        private TextBlock SectionLabel(string text) { return SectionLabel(text, 0); }
        private TextBlock SectionLabel(string text, double topMargin)
        {
            return new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = MutedBr, Margin = new Thickness(0, topMargin, 0, 0) };
        }

        private bool SameColor(System.Drawing.Color c, string hex) { return ColorsEqual(c, Sd(hex)); }
        private static bool ColorsEqual(System.Drawing.Color a, System.Drawing.Color b) { return a.R == b.R && a.G == b.G && a.B == b.B; }

        private Brush WpfColor(System.Drawing.Color c)
        {
            var brush = new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));
            brush.Freeze();
            return brush;
        }

        private ImageSource BuildWindowIcon()
        {
            const int s = 64;
            var dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                dc.DrawRoundedRectangle(AccentBr, null, new Rect(0, 0, s, s), s * 0.22, s * 0.22);
                var screen = new Rect(s * 0.17, s * 0.19, s * 0.66, s * 0.62);
                dc.DrawRoundedRectangle(Br("#0E131F"), null, screen, s * 0.10, s * 0.10);
                double bx = screen.X + screen.Width * 0.16, bw = screen.Width * 0.68;
                double bh = screen.Height * 0.15, gap = screen.Height * 0.20;
                double by = screen.Y + screen.Height * 0.26;
                dc.DrawRoundedRectangle(GreenBr, null, new Rect(bx, by, bw, bh), bh / 2, bh / 2);
                dc.DrawRoundedRectangle(Br("#F5C850"), null, new Rect(bx, by + bh + gap, bw * 0.62, bh), bh / 2, bh / 2);
            }
            var rtb = new RenderTargetBitmap(s, s, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        private static double Clamp01(double v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }

        private static SolidColorBrush Br(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        private static System.Drawing.Color Sd(string hex)
        {
            Color c = (Color)ColorConverter.ConvertFromString(hex);
            return System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
            if (_trayIcon != null) { _trayIcon.Dispose(); _trayIcon = null; }
            try { ProfileService.SaveLast(_engine.Capture(_tgtValk, _tgtThermal, _activeValk)); } catch { }
            try { ProfileService.SaveSettings(new AppSettings { Lang = Loc.Lang, LastProfile = _currentProfile }); } catch { }
            Loc.Changed -= OnLanguageChanged;
            CompositionTarget.Rendering -= OnRender;
            _engine.Shutdown();
            SensorService.Close();
            UsbDevice.Exit();
            base.OnClosed(e);
        }
    }
}
