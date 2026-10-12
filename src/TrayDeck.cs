// TrayDeck: regola la luminosità dei monitor via DDC/CI dall'area di notifica, in stile Windows 11.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;


// Un monitor fisico controllabile via DDC/CI. Le scritture avvengono su un thread
// dedicato e vengono "accorpate": DDC/CI è lento, quindi si invia solo l'ultimo valore.
class Monitor : IDisposable
{
    const byte VCP_BRIGHTNESS = 0x10;

    public readonly string Name;
    public readonly bool IsPrimary;
    public uint Max = 100;
    public int Current = -1;

    readonly IntPtr handle;
    readonly object sync = new object();
    readonly AutoResetEvent signal = new AutoResetEvent(false);
    int pending = -1;
    volatile bool disposed;

    public Monitor(IntPtr handle, string name, bool primary)
    {
        this.handle = handle;
        Name = name;
        IsPrimary = primary;
        var t = new Thread(Worker) { IsBackground = true };
        t.Start();
    }

    public bool Read()
    {
        uint cur, max;
        lock (sync)
        {
            if (!Native.GetVCPFeatureAndVCPFeatureReply(handle, VCP_BRIGHTNESS, IntPtr.Zero, out cur, out max)) return false;
        }
        Max = max == 0 ? 100 : max;
        Current = (int)cur;
        return true;
    }

    public void Set(int value)
    {
        Interlocked.Exchange(ref pending, value);
        signal.Set();
    }

    void Worker()
    {
        while (!disposed)
        {
            signal.WaitOne();
            int v = Interlocked.Exchange(ref pending, -1);
            if (v < 0 || disposed) continue;
            lock (sync) { Native.SetVCPFeature(handle, VCP_BRIGHTNESS, (uint)v); }
            Current = v;
        }
    }

    public void Dispose()
    {
        disposed = true;
        signal.Set();
        lock (sync) { Native.DestroyPhysicalMonitor(handle); }
    }

    public static List<Monitor> Enumerate()
    {
        var names = FriendlyNames();
        var list = new List<Monitor>();
        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMon, hdc, rect, data) =>
        {
            var info = new Native.MONITORINFOEX();
            info.cbSize = Marshal.SizeOf(info);
            Native.GetMonitorInfo(hMon, ref info);
            bool primary = (info.dwFlags & 1) != 0;
            string name = LookupName(info.szDevice, names);

            uint n;
            if (!Native.GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, out n) || n == 0) return true;
            var arr = new Native.PHYSICAL_MONITOR[n];
            if (!Native.GetPhysicalMonitorsFromHMONITOR(hMon, n, arr)) return true;
            foreach (var pm in arr)
                list.Add(new Monitor(pm.Handle, name ?? pm.Description, primary));
            return true;
        }, IntPtr.Zero);
        list.Sort((a, b) => b.IsPrimary.CompareTo(a.IsPrimary));
        return list;
    }

    // Nome modello da EDID (WMI), indicizzato per "DISPLAY\GSM5BEF\5&xxx&UID..."
    static Dictionary<string, string> FriendlyNames()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using (var s = new ManagementObjectSearcher(@"root\wmi", "SELECT InstanceName, UserFriendlyName FROM WmiMonitorID"))
                foreach (ManagementObject o in s.Get())
                {
                    var chars = (ushort[])o["UserFriendlyName"];
                    var sb = new System.Text.StringBuilder();
                    if (chars != null) foreach (var c in chars) if (c != 0) sb.Append((char)c);
                    string inst = (string)o["InstanceName"];
                    int us = inst.LastIndexOf('_');
                    if (us > 0) inst = inst.Substring(0, us);
                    if (sb.Length > 0) d[inst] = sb.ToString();
                }
        }
        catch { }
        return d;
    }

    static string LookupName(string adapterDevice, Dictionary<string, string> names)
    {
        var dd = new Native.DISPLAY_DEVICE();
        dd.cb = Marshal.SizeOf(dd);
        // flag 1 = EDD_GET_DEVICE_INTERFACE_NAME -> DeviceID tipo \\?\DISPLAY#GSM5BEF#5&xxx&UID4352#{guid}
        for (uint i = 0; Native.EnumDisplayDevices(adapterDevice, i, ref dd, 1); i++)
        {
            var parts = dd.DeviceID.Replace(@"\\?\", "").Split('#');
            if (parts.Length >= 3)
            {
                string key = parts[0] + "\\" + parts[1] + "\\" + parts[2];
                string n;
                if (names.TryGetValue(key, out n)) return n;
            }
            dd.cb = Marshal.SizeOf(dd);
        }
        return null;
    }
}

// Slider Fluent: binario sottile, pollice con punto d'accento che cresce al passaggio del mouse.
class Slider : Control
{
    int value, max = 100;
    bool dragging, hover;
    public Theme Theme;
    public event Action<int> ValueChanged;

    public Slider()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
    }

    public int Max { get { return max; } set { max = Math.Max(1, value); Invalidate(); } }
    public int Value
    {
        get { return value; }
        set { this.value = Math.Max(0, Math.Min(max, value)); Invalidate(); }
    }

    void SetFromUser(int v)
    {
        v = Math.Max(0, Math.Min(max, v));
        if (v == value) return;
        value = v;
        Invalidate();
        if (ValueChanged != null) ValueChanged(v);
    }

    float ThumbR { get { return Height * 0.5f * 0.8f; } }
    float Left0 { get { return ThumbR + 1; } }
    float Right0 { get { return Width - ThumbR - 1; } }

    int ValueAt(int x)
    {
        return (int)Math.Round((x - Left0) / (Right0 - Left0) * max);
    }

    protected override void OnMouseDown(MouseEventArgs e) { Focus(); dragging = true; Capture = true; SetFromUser(ValueAt(e.X)); Invalidate(); }
    protected override void OnMouseMove(MouseEventArgs e) { if (dragging) SetFromUser(ValueAt(e.X)); }
    protected override void OnMouseUp(MouseEventArgs e) { dragging = false; Capture = false; Invalidate(); }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); }
    protected override void OnMouseWheel(MouseEventArgs e) { SetFromUser(value + (e.Delta > 0 ? 5 : -5)); }
    protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down || base.IsInputKey(k); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down) SetFromUser(value - 1);
        if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up) SetFromUser(value + 1);
        if (e.KeyCode == Keys.PageDown) SetFromUser(value - 10);
        if (e.KeyCode == Keys.PageUp) SetFromUser(value + 10);
        if (e.KeyCode == Keys.Home) SetFromUser(0);
        if (e.KeyCode == Keys.End) SetFromUser(max);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent.BackColor);
        float cy = Height / 2f, th = Math.Max(4f, Height / 8f);
        float x = Left0 + (Right0 - Left0) * value / max;
        var c = Enabled ? Theme.Accent : Theme.Rail;

        using (var p = new Pen(Theme.Rail, th) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawLine(p, Left0, cy, Right0, cy);
        if (x > Left0)
            using (var p = new Pen(c, th) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(p, Left0, cy, x, cy);

        float r = ThumbR;
        using (var b = new SolidBrush(Theme.ThumbFill)) g.FillEllipse(b, x - r, cy - r, 2 * r, 2 * r);
        using (var p = new Pen(Theme.ThumbBorder, 1f)) g.DrawEllipse(p, x - r, cy - r, 2 * r, 2 * r);
        float ri = r * (dragging ? 0.45f : hover ? 0.7f : 0.55f);
        using (var b = new SolidBrush(c)) g.FillEllipse(b, x - ri, cy - ri, 2 * ri, 2 * ri);
    }
}

class Popup : Form
{
    const string GlyphBrightness = "";
    const string GlyphMonitor = "";
    const string GlyphSettings = "";
    const string GlyphBack = "";
    const string GlyphPower = "";

    struct TextItem
    {
        public string Text;
        public Font Font;
        public Rectangle Rect;
        public Color Color;
        public TextFormatFlags Flags;
    }

    readonly float scale;
    Theme theme;
    bool optionsView;
    List<Monitor> monitors = new List<Monitor>();
    readonly List<Slider> sliders = new List<Slider>();
    readonly List<Rectangle> valueRects = new List<Rectangle>();
    readonly List<TextItem> texts = new List<TextItem>();
    readonly List<Rectangle> dividers = new List<Rectangle>();
    readonly List<KeyValuePair<Rectangle, Toggle>> toggleRows = new List<KeyValuePair<Rectangle, Toggle>>();
    Rectangle footer;
    Toggle link;
    bool syncing;
    int anchorBottom;
    readonly Font fText, fSub, fValue, fTitle, fIcon, fIconSmall, fIconButton;

    readonly System.Windows.Forms.Timer anim = new System.Windows.Forms.Timer { Interval = 10 };
    DateTime animStart;
    int animFromY, animToY;

    const TextFormatFlags AlignL = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
    const TextFormatFlags AlignR = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;

    public Popup()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        KeyPreview = true;
        DoubleBuffered = true;
        SetStyle(ControlStyles.StandardDoubleClick, false); // clic rapidi sulle righe con interruttore
        using (var g = CreateGraphics()) scale = g.DpiX / 96f;
        string ui = Theme.UiFont, ic = Theme.IconFont;
        fText = new Font(ui, 10f);
        fSub = new Font(ui, 9f);
        fValue = new Font(ui, 10f, FontStyle.Bold);
        fTitle = new Font(ui, 11f, FontStyle.Bold);
        fIcon = new Font(ic, 12f);
        fIconSmall = new Font(ic, 9f);
        fIconButton = new Font(ic, 10.5f); // 14 px a 100%, come i pulsanti delle Impostazioni rapide
        anim.Tick += AnimTick;
        viewAnim.Tick += ViewAnimTick;
    }

    int S(float v) { return (int)Math.Round(v * scale); }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW
            return cp;
        }
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        anim.Stop();
        if (transition != null) FinishTransition();
        Hide();
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Escape) return;
        if (optionsView) SwitchView(false); else Hide();
    }

    public void ShowNear(Point cursor, bool options = false)
    {
        foreach (var m in monitors) m.Dispose();
        monitors = Monitor.Enumerate();
        foreach (var m in monitors) m.Read();
        optionsView = options;
        Build();

        var wa = Screen.FromPoint(cursor).WorkingArea;
        int margin = S(12);
        anchorBottom = wa.Bottom - margin;
        animToY = anchorBottom - Height;
        animFromY = animToY + S(40);
        Location = new Point(wa.Right - Width - margin, animFromY);
        Show();
        Activate();
        if (sliders.Count > 0) sliders[0].Focus();
        animStart = DateTime.Now;
        anim.Start();
    }

    // Entrata dal basso come i flyout di sistema (ease-out cubica).
    void AnimTick(object sender, EventArgs e)
    {
        double t = Math.Min(1, (DateTime.Now - animStart).TotalMilliseconds / 220.0);
        double k = 1 - Math.Pow(1 - t, 3);
        Top = animFromY + (int)Math.Round((animToY - animFromY) * k);
        if (t >= 1) anim.Stop();
    }

    // Transizione tra le viste come nelle Impostazioni di Windows 11: la vista nuova entra
    // scorrendo di lato con una dissolvenza, mentre l'altezza del pannello si adatta.
    // Durante l'animazione si disegnano due "fotografie" delle viste su una superficie sopra a tutto.
    Bitmap fromShot, toShot;
    Control transition;
    readonly System.Windows.Forms.Timer viewAnim = new System.Windows.Forms.Timer { Interval = 10 };
    DateTime viewAnimStart;
    int fromH, toH, direction;
    const double ViewAnimMs = 260;

    // Disegna la vista corrente in un'immagine dell'altezza data, senza ridimensionare la finestra.
    Bitmap RenderView(int height)
    {
        var bmp = new Bitmap(ClientSize.Width, height);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(theme.Back);
            PaintContent(g);
        }
        foreach (Control c in Controls)
            if (c != transition) c.DrawToBitmap(bmp, c.Bounds);
        return bmp;
    }

    void SwitchView(bool options)
    {
        if (transition != null) return;
        anim.Stop();
        Top = anchorBottom - Height;

        fromShot = RenderView(ClientSize.Height);
        fromH = ClientSize.Height;
        direction = options ? 1 : -1;

        // La superficie copre subito la vista vecchia, così la ricostruzione sotto non si vede.
        transition = new DoubleBufferedCanvas { Dock = DockStyle.Fill, Tag = 0.0 };
        transition.Paint += PaintTransition;
        Controls.Add(transition);
        transition.BringToFront();

        optionsView = options;
        toH = Build(false);
        transition.BringToFront();
        toShot = RenderView(toH);

        viewAnimStart = DateTime.Now;
        viewAnim.Start();
    }

    void ViewAnimTick(object sender, EventArgs e)
    {
        double t = Math.Min(1, (DateTime.Now - viewAnimStart).TotalMilliseconds / ViewAnimMs);
        double k = 1 - Math.Pow(1 - t, 3);
        int h = (int)Math.Round(fromH + (toH - fromH) * k);
        if (h != ClientSize.Height)
        {
            ClientSize = new Size(ClientSize.Width, h);
            Top = anchorBottom - Height; // il pannello resta ancorato in basso
        }
        transition.Tag = k;
        transition.Invalidate();
        if (t >= 1) FinishTransition();
    }

    void FinishTransition()
    {
        viewAnim.Stop();
        if (ClientSize.Height != toH)
        {
            ClientSize = new Size(ClientSize.Width, toH);
            Top = anchorBottom - Height;
        }
        Controls.Remove(transition);
        transition.Dispose();
        transition = null;
        fromShot.Dispose();
        toShot.Dispose();
        if (!optionsView && sliders.Count > 0) sliders[0].Focus();
    }

    void PaintTransition(object sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        double k = transition.Tag is double ? (double)transition.Tag : 0;
        g.Clear(theme.Back);
        int shift = S(36);
        int h = transition.Height;
        // Le viste sono allineate in basso, così il piè di pagina resta fermo.
        DrawFaded(g, fromShot, (int)Math.Round(-direction * shift * k), h - fromShot.Height, (float)(1 - Math.Min(1, k * 1.6)));
        if (toShot != null) DrawFaded(g, toShot,(int)Math.Round(direction * shift * (1 - k)), h - toShot.Height, (float)Math.Max(0, (k - 0.15) / 0.85));
    }

    static void DrawFaded(Graphics g, Bitmap bmp, int x, int y, float alpha)
    {
        if (alpha <= 0) return;
        using (var ia = new System.Drawing.Imaging.ImageAttributes())
        {
            var cm = new System.Drawing.Imaging.ColorMatrix { Matrix33 = alpha };
            ia.SetColorMatrix(cm);
            g.DrawImage(bmp, new Rectangle(x, y, bmp.Width, bmp.Height), 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, ia);
        }
    }

    class DoubleBufferedCanvas : Control
    {
        public DoubleBufferedCanvas()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }
    }

    void AddText(string text, Font font, Rectangle rect, Color color, TextFormatFlags flags)
    {
        texts.Add(new TextItem { Text = text, Font = font, Rect = rect, Color = color, Flags = flags });
    }

    Toggle AddToggleRow(string label, bool on, Action<bool> changed, int w, int pad, ref int y)
    {
        int h = S(40);
        AddText(label, fText, new Rectangle(pad, y, w - 2 * pad - S(50), h), theme.Fore, AlignL);
        var t = new Toggle { Theme = theme, On = on, Size = new Size(S(40), S(20)) };
        t.Location = new Point(w - pad - t.Width, y + (h - t.Height) / 2);
        t.Changed += changed;
        Controls.Add(t);
        toggleRows.Add(new KeyValuePair<Rectangle, Toggle>(new Rectangle(0, y, w, h), t));
        y += h;
        return t;
    }

    FlatButton AddButton(string glyph, string text, Rectangle bounds, Color back, Action click)
    {
        var b = new FlatButton { Theme = theme, Glyph = glyph, GlyphFont = text == null ? fIconButton : fIconSmall, Text = text, Font = fSub, Bounds = bounds, BackColor = back };
        b.Click += (s, e) => click();
        Controls.Add(b);
        return b;
    }

    // Ricostruisce la vista corrente e restituisce l'altezza necessaria.
    // Con resize = false la finestra non cambia dimensione (ci pensa l'animazione).
    int Build(bool resize = true)
    {
        SuspendLayout();
        var old = new List<Control>();
        foreach (Control c in Controls) if (c != transition) old.Add(c);
        foreach (var c in old) { Controls.Remove(c); c.Dispose(); }
        sliders.Clear();
        valueRects.Clear();
        texts.Clear();
        dividers.Clear();
        toggleRows.Clear();
        link = null;

        theme = Theme.Current();
        BackColor = theme.Back;
        ForeColor = theme.Fore;
        if (!IsHandleCreated) CreateHandle();
        Theme.Apply(this, theme);

        int w = S(360), pad = S(20);
        int y = optionsView ? BuildOptions(w, pad) : BuildMain(w, pad);

        int fh = S(48);
        footer = new Rectangle(0, y, w, fh);
        int bh = S(32);
        if (optionsView)
            AddButton(GlyphPower, L.T("Chiudi programma", "Quit TrayDeck"), new Rectangle(pad - S(8), y + (fh - bh) / 2, S(160), bh), theme.Footer, Program.Quit);
        else
            AddButton(GlyphSettings, null, new Rectangle(w - pad - S(36) + S(8), y + (fh - S(36)) / 2, S(36), S(36)), theme.Footer, () => SwitchView(true));
        y += fh;

        if (resize) ClientSize = new Size(w, y);
        ResumeLayout();
        Invalidate();
        return y;
    }

    int BuildMain(int w, int pad)
    {
        int y = S(18);
        if (monitors.Count == 0)
        {
            AddText(L.T("Nessun monitor DDC/CI trovato", "No DDC/CI monitor found"), fText, new Rectangle(pad, y, w - 2 * pad, S(22)), theme.Fore, AlignL);
            AddText(L.T("Attiva DDC/CI nel menu del monitor.", "Enable DDC/CI in the monitor's menu."), fSub, new Rectangle(pad, y + S(24), w - 2 * pad, S(20)), theme.Sub, AlignL);
            return y + S(62);
        }

        int iconW = S(32), valW = S(40), rowH = S(32);
        for (int i = 0; i < monitors.Count; i++)
        {
            var m = monitors[i];
            AddText(GlyphMonitor, fIconSmall, new Rectangle(pad, y, S(20), S(20)), theme.Sub, AlignL);
            string label = m.Name + (m.IsPrimary && monitors.Count > 1 ? L.T("  ·  principale", "  ·  primary") : "");
            AddText(label, fSub, new Rectangle(pad + S(22), y, w - 2 * pad - S(22), S(20)), theme.Sub, AlignL);
            y += S(24);

            bool ok = m.Current >= 0;
            AddText(GlyphBrightness, fIcon, new Rectangle(pad - S(2), y, S(28), rowH), theme.Fore, AlignL);
            var sl = new Slider
            {
                Theme = theme,
                Max = (int)m.Max,
                Value = ok ? m.Current : 0,
                Enabled = ok,
                Location = new Point(pad + iconW, y),
                Size = new Size(w - 2 * pad - iconW - valW, rowH),
            };
            valueRects.Add(new Rectangle(w - pad - valW, y, valW, rowH));
            int idx = i;
            sl.ValueChanged += v => OnSlider(idx, v);
            Controls.Add(sl);
            sliders.Add(sl);
            y += rowH + S(14);
        }

        if (monitors.Count > 1)
        {
            dividers.Add(new Rectangle(pad, y - S(4), w - 2 * pad, 1));
            y += S(2);
            link = AddToggleRow(L.T("Regola tutti insieme", "Adjust all together"), Program.LinkAll, on => Program.LinkAll = on, w, pad, ref y);
            y += S(8);
        }
        else y += S(4);
        return y;
    }

    int BuildOptions(int w, int pad)
    {
        int y = S(12), hh = S(36);
        AddButton(GlyphBack, null, new Rectangle(pad - S(10), y, hh, hh), theme.Back, () => SwitchView(false));
        AddText(L.T("Opzioni", "Settings"), fTitle, new Rectangle(pad + hh - S(4), y, w - 2 * pad - hh, hh), theme.Fore, AlignL);
        y += hh + S(10);

        AddToggleRow(L.T("Avvia con Windows", "Start with Windows"), Program.StartupEnabled, on => Program.StartupEnabled = on, w, pad, ref y);
        y += S(6);
        dividers.Add(new Rectangle(pad, y, w - 2 * pad, 1));
        y += S(12);

        int rh = S(28);
        AddText(L.T("Versione", "Version"), fText, new Rectangle(pad, y, w / 2, rh), theme.Sub, AlignL);
        AddText(AppInfo.Version, fText, new Rectangle(w / 2, y, w / 2 - pad, rh), theme.Fore, AlignR);
        y += rh;
        AddText(L.T("Autore", "Author"), fText, new Rectangle(pad, y, w / 2, rh), theme.Sub, AlignL);
        AddText(AppInfo.Author, fText, new Rectangle(w / 2, y, w / 2 - pad, rh), theme.Fore, AlignR);
        y += rh + S(14);
        return y;
    }

    void OnSlider(int idx, int v)
    {
        if (syncing) return;
        monitors[idx].Current = v;
        monitors[idx].Set(v);
        Invalidate(valueRects[idx]);
        if (link != null && link.On)
        {
            syncing = true;
            double frac = (double)v / sliders[idx].Max;
            for (int j = 0; j < sliders.Count; j++)
            {
                if (j == idx || !sliders[j].Enabled) continue;
                int vj = (int)Math.Round(frac * sliders[j].Max);
                sliders[j].Value = vj;
                monitors[j].Current = vj;
                monitors[j].Set(vj);
                Invalidate(valueRects[j]);
            }
            syncing = false;
        }
    }

    protected override void OnPaint(PaintEventArgs e) { PaintContent(e.Graphics); }

    void PaintContent(Graphics g)
    {
        using (var b = new SolidBrush(theme.Footer)) g.FillRectangle(b, footer);
        using (var b = new SolidBrush(theme.Divider))
        {
            g.FillRectangle(b, footer.Left, footer.Top, footer.Width, 1);
            foreach (var d in dividers) g.FillRectangle(b, d);
        }
        foreach (var t in texts) TextRenderer.DrawText(g, t.Text, t.Font, t.Rect, t.Color, t.Flags);
        if (!optionsView)
            for (int i = 0; i < valueRects.Count; i++)
            {
                string val = sliders[i].Enabled ? sliders[i].Value.ToString() : L.T("n/d", "n/a");
                TextRenderer.DrawText(g, val, fValue, valueRects[i], theme.Fore, AlignR);
            }
    }

    // Cliccare l'etichetta di una riga con interruttore equivale a cliccare l'interruttore.
    protected override void OnMouseClick(MouseEventArgs e)
    {
        foreach (var row in toggleRows)
            if (row.Key.Contains(e.Location)) { row.Value.Flip(); return; }
    }
}

// Menu del tasto destro: piccolo popup disegnato a mano come i menu contestuali di Windows 11.
class TrayMenu : Form
{
    readonly List<KeyValuePair<string, Action>> items = new List<KeyValuePair<string, Action>>();
    readonly List<Rectangle> rects = new List<Rectangle>();
    readonly float scale;
    readonly Font font;
    Theme theme;
    int hover = -1;

    public TrayMenu()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        KeyPreview = true;
        DoubleBuffered = true;
        using (var g = CreateGraphics()) scale = g.DpiX / 96f;
        font = new Font(Theme.UiFont, 9f);
    }

    int S(float v) { return (int)Math.Round(v * scale); }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW
            return cp;
        }
    }

    public void Add(string text, Action click) { items.Add(new KeyValuePair<string, Action>(text, click)); }

    public void ShowAt(Point cursor)
    {
        theme = Theme.Current();
        BackColor = theme.Back;
        if (!IsHandleCreated) CreateHandle();
        Theme.Apply(this, theme);

        // Larghezza adattata al testo più lungo, come i menu di sistema.
        int textW = 0;
        foreach (var it in items) textW = Math.Max(textW, TextRenderer.MeasureText(it.Key, font).Width);
        int pad = S(4), ih = S(28), y = pad;
        int w = Math.Max(S(120), 2 * pad + S(24) + textW + S(24));
        rects.Clear();
        foreach (var it in items) { rects.Add(new Rectangle(pad, y, w - 2 * pad, ih)); y += ih; }
        ClientSize = new Size(w, y + pad);
        hover = -1;

        // Sopra il cursore, sempre dentro l'area di lavoro.
        var wa = Screen.FromPoint(cursor).WorkingArea;
        int x = Math.Max(wa.Left, Math.Min(cursor.X - Width / 2, wa.Right - Width - S(8)));
        int top = Math.Max(wa.Top, Math.Min(cursor.Y - Height - S(8), wa.Bottom - Height - S(8)));
        Location = new Point(x, top);
        Show();
        Activate();
    }

    protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); Hide(); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) Hide();
        if (e.KeyCode == Keys.Down) { hover = (hover + 1) % items.Count; Invalidate(); }
        if (e.KeyCode == Keys.Up) { hover = (hover <= 0 ? items.Count : hover) - 1; Invalidate(); }
        if (e.KeyCode == Keys.Enter && hover >= 0) Run(hover);
    }

    int HitTest(Point p)
    {
        for (int i = 0; i < rects.Count; i++) if (rects[i].Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = HitTest(e.Location);
        if (h != hover) { hover = h; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        int h = HitTest(e.Location);
        if (h >= 0) Run(h);
    }

    void Run(int i)
    {
        Hide();
        items[i].Value();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        for (int i = 0; i < items.Count; i++)
        {
            var r = rects[i];
            if (i == hover)
                using (var path = Theme.RoundRect(new RectangleF(r.X, r.Y + 1, r.Width, r.Height - 2), S(4)))
                using (var b = new SolidBrush(theme.Hover))
                    g.FillPath(b, path);
            var textRect = new Rectangle(r.X + S(12), r.Y, r.Width - S(24), r.Height);
            TextRenderer.DrawText(g, items[i].Key, font, textRect, theme.Fore, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}

static class Program
{
    static NotifyIcon tray;

    public static bool LinkAll
    {
        get { using (var k = Registry.CurrentUser.OpenSubKey(AppInfo.SettingsKey)) return k != null && (k.GetValue("LinkAll") as int? ?? 0) != 0; }
        set { using (var k = Registry.CurrentUser.CreateSubKey(AppInfo.SettingsKey)) k.SetValue("LinkAll", value ? 1 : 0); }
    }

    public static bool StartupEnabled
    {
        get { return AppInfo.StartupEnabled(); }
        set { AppInfo.SetStartup(value, Application.ExecutablePath); }
    }

    public static void Quit()
    {
        tray.Visible = false;
        Application.Exit();
    }

    // Icona tray: un sole disegnato, nel colore adatto alla barra delle applicazioni.
    static Icon MakeIcon()
    {
        int sz = 32;
        using (var bmp = new Bitmap(sz, sz))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var c = Theme.TaskbarLight() ? Color.FromArgb(30, 30, 30) : Color.White;
                using (var b = new SolidBrush(c))
                using (var p = new Pen(c, 2.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.FillEllipse(b, 10, 10, 12, 12);
                    for (int i = 0; i < 8; i++)
                    {
                        double a = i * Math.PI / 4;
                        float cx = 16, cy = 16;
                        g.DrawLine(p, cx + (float)Math.Cos(a) * 10.5f, cy + (float)Math.Sin(a) * 10.5f,
                                      cx + (float)Math.Cos(a) * 14.5f, cy + (float)Math.Sin(a) * 14.5f);
                    }
                }
            }
            IntPtr h = bmp.GetHicon();
            var icon = (Icon)Icon.FromHandle(h).Clone();
            Native.DestroyIcon(h);
            return icon;
        }
    }

    public static void Run(string[] args)
    {
        bool created;
        using (var mutex = new Mutex(true, "TrayDeck_SingleInstance", out created))
        {
            if (!created) return;

            var popup = new Popup();
            tray = new NotifyIcon { Icon = MakeIcon(), Text = "TrayDeck", Visible = true };

            // L'installer chiede a TrayDeck di chiudersi (per aggiornarlo o rimuoverlo) segnalando questo evento.
            var quit = new EventWaitHandle(false, EventResetMode.AutoReset, AppInfo.QuitEventName);
            var ui = popup.Handle; // crea la finestra, necessaria per tornare sul thread dell'interfaccia
            new Thread(() => { quit.WaitOne(); popup.BeginInvoke((Action)Quit); }) { IsBackground = true }.Start();

            // Aggiorna l'icona se cambia il tema chiaro/scuro.
            SystemEvents.UserPreferenceChanged += (s, e) =>
            {
                var old = tray.Icon;
                tray.Icon = MakeIcon();
                old.Dispose();
            };

            var menu = new TrayMenu();
            menu.Add(L.T("Chiudi", "Quit"), Quit);

            DateTime lastHide = DateTime.MinValue;
            popup.VisibleChanged += (s, e) => { if (!popup.Visible) lastHide = DateTime.Now; };
            tray.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Right)
                {
                    popup.Hide();
                    menu.ShowAt(Cursor.Position);
                    return;
                }
                if (e.Button != MouseButtons.Left) return;
                // Il click sull'icona disattiva il popup prima di arrivare qui: evita di riaprirlo subito.
                if (popup.Visible || (DateTime.Now - lastHide).TotalMilliseconds < 250) { popup.Hide(); return; }
                popup.ShowNear(Cursor.Position);
            };

            // --show / --options / --menu: apre subito il pannello o il menu (utile per provarli senza cliccare l'icona).
            bool show = Array.IndexOf(args, "--show") >= 0, options = Array.IndexOf(args, "--options") >= 0, showMenu = Array.IndexOf(args, "--menu") >= 0;
            if (show || options || showMenu)
            {
                var wa = Screen.PrimaryScreen.WorkingArea;
                var corner = new Point(wa.Right - 150, wa.Bottom + 20);
                var t0 = new System.Windows.Forms.Timer { Interval = 300 };
                t0.Tick += (s, e) =>
                {
                    t0.Stop();
                    if (showMenu) menu.ShowAt(corner);
                    else popup.ShowNear(corner, options);
                };
                t0.Start();
            }

            Application.Run();
            tray.Dispose();
        }
    }
}
