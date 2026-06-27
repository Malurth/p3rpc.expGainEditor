using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;

namespace p3rpc.expGainEditor.Configuration;

/// <summary>
/// The mod's configuration UI, shown instead of Reloaded's default property grid
/// (see <see cref="ConfiguratorMixin.TryRunCustomConfiguration"/>).
///
/// Built entirely in code (no XAML) on purpose: the configurator assembly is loaded by the launcher
/// into a collectible plugin AssemblyLoadContext, and XAML/BAML resource loading resolves the assembly
/// by name via pack URIs, which lands in the wrong load context. Constructing the controls directly
/// sidesteps that and keeps the window unload-friendly.
///
/// Each multiplier is a slider paired with a textbox (the textbox can hold over-range values the slider
/// can't reach). The centrepiece is the level-gap EXP curve graph, which redraws live as the scaling
/// slider moves so you can see exactly what the strength knob does. Everything is edited on the live
/// Config object; nothing is persisted until "Save &amp; Close".
/// </summary>
internal sealed class ExpConfigWindow : Window
{
    private static readonly Brush Bg     = Frozen("#FF1E1E1E");
    private static readonly Brush Panel  = Frozen("#FF252526");
    private static readonly Brush Alt    = Frozen("#FF2A2A2D");
    private static readonly Brush Text   = Frozen("#FFEAEAEA");
    private static readonly Brush Sub    = Frozen("#FFB0B0B0");
    private static readonly Brush Edge   = Frozen("#FF3F3F46");
    private static readonly Brush Accent = Frozen("#FF4C7DF0");
    private static readonly Brush Chip   = Frozen("#FF333337");
    private static readonly Brush Guide  = Frozen("#FFC8C8CE");   // hover crosshair guide lines
    private static readonly Brush LabelBg = Frozen("#F0121214");  // hover readout chip background

    private static SolidColorBrush Frozen(string hex)
    {
        var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        b.Freeze();
        return b;
    }

    // Vanilla level-gap EXP ratios, indexed by (gap + 10) for gap = enemy_lvl - your_lvl, clamped +/-10.
    // gap -10 (you're 10+ over the enemy) = 0.32x ... gap +10 (enemy is 10+ over you) = 4.0x.
    private static readonly double[] Vanilla =
        { 0.32, 0.40, 0.48, 0.53, 0.59, 0.64, 0.73, 0.80, 0.86, 0.91, 1.0, 1.0, 1.0, 1.0, 1.04, 1.19, 1.46, 1.77, 2.30, 3.10, 4.0 };

    private readonly Config _config;
    private Knob _global = null!, _normal = null!, _strong = null!, _rare = null!, _miniboss = null!, _boss = null!, _strength = null!, _wand = null!;
    private CheckBox _log = null!;
    private Canvas _graph = null!;

    // hover crosshair: a transparent overlay canvas (never cleared by RedrawGraph) tracks the cursor and
    // reads the current curve. _curRatios/_maxY are the last-drawn current curve so hover can interpolate.
    private Canvas _overlay = null!;
    private double _maxY = 4.0;
    private double[] _curRatios = new double[Vanilla.Length];
    private Line _hvV = null!;
    private Ellipse _hvDot = null!;
    private Border _hvLabel = null!;
    private TextBlock _hvText = null!;

    /// <summary>Entry point: show the editor modally for the given config (on the WPF UI thread).</summary>
    public static void Edit(Config config)
    {
        var app = Application.Current;
        if (app?.Dispatcher != null && !app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.Invoke(() => Edit(config));
            return;
        }

        var win = new ExpConfigWindow(config);
        try
        {
            if (app?.MainWindow != null && app.MainWindow.IsLoaded && app.MainWindow != win)
                win.Owner = app.MainWindow;
        }
        catch { /* owner is best-effort */ }
        win.ShowDialog();
    }

    private ExpConfigWindow(Config config)
    {
        _config = config;

        Title = "P3R EXP Gain Editor";
        Background = Bg;
        Foreground = Text;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;

        Content = BuildLayout();
        RedrawGraph();
    }

    // ---- layout ---------------------------------------------------------
    private UIElement BuildLayout()
    {
        var root = new StackPanel { Margin = new Thickness(14), Width = 460 };

        // 1. Global
        var global = new StackPanel();
        _global = AddKnob(global, "All Enemy EXP",
            "Multiplies every enemy's EXP. Stacks on top of the per-group multipliers below.", _config.GlobalEnemyExp);
        root.Children.Add(Section("1.  Global", global));

        // 2. Enemy Groups
        var groups = new StackPanel();
        _normal   = AddKnob(groups, "Normal Shadows",  "Regular field shadows (plain minimap dot). Baseline — 1× EXP.", _config.NormalShadowExp);
        _strong   = AddKnob(groups, "Strong Shadows",  "Tankier glowing field shadows. ~4× a normal's EXP.", _config.StrongShadowExp);
        _rare     = AddKnob(groups, "Rare Shadows",    "Gold-bordered fleeing shadows. ~15× normal EXP.", _config.RareShadowExp);
        _miniboss = AddKnob(groups, "Minibosses",      "Tanky 'guardian' encounters (gatekeepers / Monad). ~2× normal.", _config.MinibossExp);
        _boss     = AddKnob(groups, "Bosses",          "Story / endgame bosses & superbosses. ~30× normal (roughly 25–45×).", _config.BossExp);
        root.Children.Add(Section("2.  Enemy Groups", groups));

        // 3. Level Scaling (slider + the live curve graph)
        var scaling = new StackPanel();
        _strength = AddKnob(scaling, "Level-Gap Scaling Strength",
            "Blends the level-gap curve below toward flat. 1 = vanilla, 0 = flat (level ignored, every kill gives base EXP), >1 exaggerates the bonus/penalty.",
            _config.LevelGapScalingStrength, sliderMax: 3.0, onChanged: RedrawGraph);
        scaling.Children.Add(BuildGraphCard());
        root.Children.Add(Section("3.  Level Scaling", scaling));

        // 4. Shuffle Time
        var shuffle = new StackPanel();
        _wand = AddKnob(shuffle, "Wand Card EXP",
            "EXP granted by Wand minor-arcana cards during Shuffle Time.", _config.ShuffleWandExp);
        root.Children.Add(Section("4.  Shuffle Time", shuffle));

        // 5. Debug
        var debug = new StackPanel();
        _log = new CheckBox
        {
            Content = "Log what the mod baked to the Reloaded console",
            Foreground = Text,
            IsChecked = _config.LogToConsole,
            VerticalAlignment = VerticalAlignment.Center,
        };
        debug.Children.Add(_log);
        root.Children.Add(Section("5.  Debug", debug));

        // footer
        var bottom = new DockPanel { Margin = new Thickness(2, 14, 2, 0) };
        var hint = new TextBlock { Text = "Changes take effect on the next game launch.", Foreground = Sub, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(hint, Dock.Left);
        bottom.Children.Add(hint);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var save = MakeButton("Save & Close", OnSave);
        save.Background = Accent;
        save.MinWidth = 110;
        save.IsDefault = true;
        actions.Children.Add(save);
        var cancel = MakeButton("Cancel", (_, __) => { DialogResult = false; Close(); });
        cancel.IsCancel = true;
        actions.Children.Add(cancel);
        bottom.Children.Add(actions);
        root.Children.Add(bottom);

        return root;
    }

    /// <summary>A titled card: header strip + a padded body panel.</summary>
    private Border Section(string title, UIElement body)
    {
        var stack = new StackPanel();
        stack.Children.Add(new Border
        {
            Background = Chip,
            BorderBrush = Edge,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new TextBlock { Text = title, Foreground = Text, FontWeight = FontWeights.Bold, Margin = new Thickness(10, 6, 10, 6) },
        });
        // Background MUST be set explicitly: the launcher's WPF theme puts an implicit (bright) Background
        // on bg-less Borders, which leaks through any container we don't paint ourselves.
        stack.Children.Add(new Border { Background = Panel, Padding = new Thickness(12, 10, 12, 6), Child = body });

        return new Border
        {
            Background = Panel,
            BorderBrush = Edge,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 10),
            Child = stack,
        };
    }

    // ---- knob (label + description + slider + textbox), two-way synced ----
    private sealed class Knob
    {
        public double Value;
    }

    private Knob AddKnob(StackPanel parent, string label, string desc, double initial, double sliderMax = 10.0, Action? onChanged = null)
    {
        var knob = new Knob { Value = initial };

        var box = new TextBox
        {
            Width = 60,
            Background = Bg,
            Foreground = Text,
            BorderBrush = Edge,
            BorderThickness = new Thickness(1),
            CaretBrush = Text,
            TextAlignment = TextAlignment.Right,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(4, 3, 4, 3),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var slider = new Slider
        {
            Minimum = 0,
            Maximum = sliderMax,
            SmallChange = 0.05,
            LargeChange = 0.5,
            TickFrequency = 0.05,
            IsSnapToTickEnabled = true,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Accent,
            Margin = new Thickness(0, 4, 10, 0),
        };

        bool syncing = false;
        slider.ValueChanged += (_, __) =>
        {
            if (syncing) return;
            syncing = true;
            knob.Value = slider.Value;
            box.Text = Fmt(knob.Value);
            syncing = false;
            onChanged?.Invoke();
        };
        box.TextChanged += (_, __) =>
        {
            if (syncing) return;
            if (double.TryParse(box.Text, out var v) && v >= 0)
            {
                syncing = true;
                knob.Value = v;
                slider.Value = Math.Min(v, slider.Maximum);   // clamp the slider display; knob keeps the real value
                syncing = false;
                onChanged?.Invoke();
            }
        };
        // One click = whole value selected, ready to type. Without this, WPF places the caret at the
        // click point and cancels the select-all; handling the first mouse-down (when not yet focused)
        // suppresses that. A second click in an already-focused box still places the caret to edit.
        box.PreviewMouseLeftButtonDown += (s, e) =>
        {
            var t = (TextBox)s;
            if (!t.IsKeyboardFocusWithin) { t.Focus(); e.Handled = true; }
        };
        box.GotKeyboardFocus += (s, _) => ((TextBox)s).SelectAll();

        // initial values (guarded so the handlers above don't fight each other)
        syncing = true;
        box.Text = Fmt(initial);
        slider.Value = Math.Min(initial, sliderMax);
        syncing = false;

        // header line: label .... textbox
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 1) };
        var name = new TextBlock { Text = label, Foreground = Text, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(name, Dock.Left);
        head.Children.Add(name);
        DockPanel.SetDock(box, Dock.Right);
        head.Children.Add(box);

        var block = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        block.Children.Add(head);
        block.Children.Add(new TextBlock { Text = desc, Foreground = Sub, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 2) });
        block.Children.Add(slider);
        parent.Children.Add(block);

        return knob;
    }

    // ---- the live level-gap EXP curve graph -----------------------------
    private const double GraphW = 432, GraphH = 200;
    private const double PadL = 34, PadR = 10, PadT = 12, PadB = 24;

    private Border BuildGraphCard()
    {
        _graph = new Canvas { Width = GraphW, Height = GraphH, Background = Bg, ClipToBounds = true };

        // Transparent overlay on top of the graph: receives hover, draws the crosshair. RedrawGraph only
        // clears _graph, so the crosshair survives slider changes. Transparent bg still hit-tests.
        _overlay = new Canvas { Width = GraphW, Height = GraphH, Background = Brushes.Transparent, ClipToBounds = true };
        BuildCrosshair();
        _overlay.MouseMove += OnGraphHover;
        _overlay.MouseLeave += (_, __) => SetCrosshair(false);

        var stack = new Grid { Width = GraphW, Height = GraphH };
        stack.Children.Add(_graph);
        stack.Children.Add(_overlay);

        var legend = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 6, 0, 0) };
        legend.Children.Add(LegendSwatch(Sub, "vanilla", dashed: true));
        legend.Children.Add(LegendSwatch(Accent, "current", dashed: false));
        var note = new TextBlock { Text = "x = enemy lvl − yours (±10)   •   hover to read a point", Foreground = Sub, FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
        legend.Children.Add(note);

        var wrap = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
        wrap.Children.Add(new Border { Background = Bg, BorderBrush = Edge, BorderThickness = new Thickness(1), Child = stack });
        wrap.Children.Add(legend);
        return new Border { Background = Panel, Child = wrap };   // explicit bg (see Section: theme leak)
    }

    private void BuildCrosshair()
    {
        _hvV = new Line { Stroke = Guide, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 2, 2 }, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        _hvDot = new Ellipse { Width = 9, Height = 9, Fill = Accent, Stroke = Text, StrokeThickness = 1.5, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        _hvText = new TextBlock { Foreground = Text, FontSize = 11 };
        _hvLabel = new Border
        {
            Background = LabelBg,
            BorderBrush = Edge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 3, 6, 3),
            Child = _hvText,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        _overlay.Children.Add(_hvV);
        _overlay.Children.Add(_hvDot);
        _overlay.Children.Add(_hvLabel);
    }

    private void OnGraphHover(object sender, System.Windows.Input.MouseEventArgs e)
    {
        double plotW = GraphW - PadL - PadR, plotH = GraphH - PadT - PadB;
        double baseY = PadT + plotH;                          // y for ratio = 0 (the x-axis)
        double mx = Math.Clamp(e.GetPosition(_overlay).X, PadL, GraphW - PadR);
        double t = (mx - PadL) / plotW;                       // 0..1 across the gap axis

        // Level gaps are integers: snap to the nearest one and read its exact ratio (no interpolation -
        // the curve is a staircase). The dot rides the flat top of that step.
        int gap = Math.Clamp((int)Math.Round(-10 + t * 20.0), -10, 10);
        double ratio = _curRatios[gap + 10];
        double cy = PadT + plotH * (1.0 - ratio / _maxY);

        // A single vertical guide rising from the x-axis to the dot on the curve. No horizontal line.
        _hvV.X1 = mx; _hvV.Y1 = baseY; _hvV.X2 = mx; _hvV.Y2 = cy;
        Canvas.SetLeft(_hvDot, mx - _hvDot.Width / 2);
        Canvas.SetTop(_hvDot, cy - _hvDot.Height / 2);

        _hvText.Text = $"{(gap > 0 ? "+" : "")}{gap} lvl   →   {ratio:0.00}×";
        _hvLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double lw = _hvLabel.DesiredSize.Width, lh = _hvLabel.DesiredSize.Height;
        double lx = mx + 10; if (lx + lw > GraphW - PadR) lx = mx - 10 - lw;   // flip near right edge
        lx = Math.Max(PadL, lx);
        double ly = cy - lh - 8; if (ly < PadT) ly = cy + 10;                  // flip below if near top
        Canvas.SetLeft(_hvLabel, lx);
        Canvas.SetTop(_hvLabel, ly);

        SetCrosshair(true);
    }

    private void SetCrosshair(bool on)
    {
        if (_hvV == null) return;
        var v = on ? Visibility.Visible : Visibility.Collapsed;
        _hvV.Visibility = v; _hvDot.Visibility = v; _hvLabel.Visibility = v;
    }

    private UIElement LegendSwatch(Brush color, string label, bool dashed)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0) };
        var line = new Line { X1 = 0, Y1 = 7, X2 = 20, Y2 = 7, Stroke = color, StrokeThickness = 2, VerticalAlignment = VerticalAlignment.Center };
        if (dashed) line.StrokeDashArray = new DoubleCollection { 3, 2 };
        sp.Children.Add(line);
        sp.Children.Add(new TextBlock { Text = label, Foreground = Sub, FontSize = 11, Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        return sp;
    }

    private static double Transform(double vanilla, double s) => Math.Max(0.0, 1.0 + (vanilla - 1.0) * s);

    private void RedrawGraph()
    {
        if (_graph == null) return;
        _graph.Children.Clear();
        double s = _strength?.Value ?? 1.0;

        // current curve + dynamic y-axis ceiling
        var cur = new double[Vanilla.Length];
        double curMax = 0;
        for (int i = 0; i < Vanilla.Length; i++) { cur[i] = Transform(Vanilla[i], s); if (cur[i] > curMax) curMax = cur[i]; }
        double maxY = Math.Max(4.0, Math.Ceiling(Math.Max(curMax, Vanilla[^1])));
        _curRatios = cur; _maxY = maxY;          // publish for the hover crosshair
        SetCrosshair(false);                     // hide stale crosshair while the curve changes
        int yStep = maxY <= 4 ? 1 : maxY <= 8 ? 2 : (int)Math.Ceiling(maxY / 4.0);

        double plotW = GraphW - PadL - PadR, plotH = GraphH - PadT - PadB;
        double X(double i) => PadL + plotW * (i / (Vanilla.Length - 1));   // accepts fractional index for step edges
        double Y(double r) => PadT + plotH * (1.0 - r / maxY);

        // horizontal gridlines + y labels
        for (int r = 0; r <= maxY; r += yStep)
        {
            double y = Y(r);
            bool one = r == 1;   // the "no change" reference line, drawn brighter
            _graph.Children.Add(new Line { X1 = PadL, Y1 = y, X2 = GraphW - PadR, Y2 = y, Stroke = one ? Edge : Frozen("#FF2C2C30"), StrokeThickness = one ? 1 : 1 });
            _graph.Children.Add(Label($"{r}×", PadL - 30, y - 8, Sub, 10, TextAlignment.Right, 26));
        }

        // vertical gridlines + x labels at gap = -10, -5, 0, +5, +10
        foreach (int gap in new[] { -10, -5, 0, 5, 10 })
        {
            int i = gap + 10;
            double x = X(i);
            _graph.Children.Add(new Line { X1 = x, Y1 = PadT, X2 = x, Y2 = GraphH - PadB, Stroke = Frozen("#FF2C2C30"), StrokeThickness = 1 });
            _graph.Children.Add(Label(gap > 0 ? $"+{gap}" : gap.ToString(), x - 14, GraphH - PadB + 4, Sub, 10, TextAlignment.Center, 28));
        }

        // vanilla curve (gray dashed) then current curve (accent solid, on top)
        _graph.Children.Add(MakeCurve(Vanilla, Y, X, Sub, 1.5, dashed: true));
        _graph.Children.Add(MakeCurve(cur, Y, X, Accent, 2.4, dashed: false));

        // live readout dots + chips at the gaps that actually change (-10 and +10)
        AddReadout(-10, cur[0], X(0), Y(cur[0]), above: cur[0] < 1.2);
        AddReadout(10, cur[^1], X(Vanilla.Length - 1), Y(cur[^1]), above: true);
    }

    // Level gaps are integers, so the ratio is a discrete lookup, not a continuous function. Draw it as a
    // centered step (staircase): each gap holds a flat value across its ±0.5 band; the end gaps (clamped at
    // ±10) extend to the plot edges.
    private Polyline MakeCurve(double[] data, Func<double, double> Y, Func<double, double> X, Brush stroke, double thick, bool dashed)
    {
        int n = data.Length;
        var pts = new PointCollection(n * 2);
        for (int i = 0; i < n; i++)
        {
            double xl = i == 0 ? X(0) : X(i - 0.5);
            double xr = i == n - 1 ? X(n - 1) : X(i + 0.5);
            double y = Y(data[i]);
            pts.Add(new Point(xl, y));
            pts.Add(new Point(xr, y));
        }
        var pl = new Polyline { Points = pts, Stroke = stroke, StrokeThickness = thick, StrokeLineJoin = PenLineJoin.Miter };
        if (dashed) pl.StrokeDashArray = new DoubleCollection { 3, 2 };
        return pl;
    }

    private void AddReadout(int gap, double ratio, double x, double y, bool above)
    {
        var dot = new Ellipse { Width = 6, Height = 6, Fill = Accent };
        Canvas.SetLeft(dot, x - 3);
        Canvas.SetTop(dot, y - 3);
        _graph.Children.Add(dot);

        var chip = Label($"{ratio:0.##}×", x - 22, above ? y - 20 : y + 8, Text, 11, TextAlignment.Center, 44);
        _graph.Children.Add(chip);
    }

    private TextBlock Label(string text, double left, double top, Brush color, double size, TextAlignment align, double width)
    {
        var tb = new TextBlock { Text = text, Foreground = color, FontSize = size, TextAlignment = align, Width = width };
        Canvas.SetLeft(tb, left);
        Canvas.SetTop(tb, top);
        return tb;
    }

    // ---- helpers --------------------------------------------------------
    private static string Fmt(double v) => v.ToString("0.###");

    private Button MakeButton(string content, RoutedEventHandler onClick)
    {
        var b = new Button
        {
            Content = content,
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(12, 5, 12, 5),
            Background = Chip,
            Foreground = Text,
            BorderBrush = Edge,
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        b.Click += onClick;
        return b;
    }

    private void OnSave(object? s, RoutedEventArgs e)
    {
        _config.GlobalEnemyExp = _global.Value;
        _config.NormalShadowExp = _normal.Value;
        _config.StrongShadowExp = _strong.Value;
        _config.RareShadowExp = _rare.Value;
        _config.MinibossExp = _miniboss.Value;
        _config.BossExp = _boss.Value;
        _config.LevelGapScalingStrength = _strength.Value;
        _config.ShuffleWandExp = _wand.Value;
        _config.LogToConsole = _log.IsChecked == true;
        _config.Save?.Invoke();

        DialogResult = true;
        Close();
    }
}
