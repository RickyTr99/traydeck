// Parti condivise da TrayDeck e dal suo installer: API native, tema Windows 11 e controlli Fluent.
// Alcuni controlli non sono usati da entrambi i programmi: niente avvisi per i campi mai assegnati.
#pragma warning disable 0649
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

static class Native
{
    public delegate bool MonitorEnumProc(IntPtr hMon, IntPtr hdc, IntPtr rect, IntPtr data);
    [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc cb, IntPtr data);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr hMon, ref MONITORINFOEX info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool EnumDisplayDevices(string device, uint index, ref DISPLAY_DEVICE dd, uint flags);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWA_BORDER_COLOR = 34;
    public const int DWMWCP_ROUND = 2;

    [DllImport("dxva2.dll")] public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMon, out uint count);
    [DllImport("dxva2.dll")] public static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMon, uint count, [Out] PHYSICAL_MONITOR[] arr);
    [DllImport("dxva2.dll")] public static extern bool DestroyPhysicalMonitor(IntPtr h);
    [DllImport("dxva2.dll")] public static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr h, byte code, IntPtr type, out uint cur, out uint max);
    [DllImport("dxva2.dll")] public static extern bool SetVCPFeature(IntPtr h, byte code, uint value);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct PHYSICAL_MONITOR
    {
        public IntPtr Handle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public int l, t, r, b, wl, wt, wr, wb;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }
}


// Palette Windows 11 (flyout di Impostazioni rapide), con il colore d'accento dell'utente.
class Theme
{
    public bool Dark;
    public Color Back, Footer, Divider, Border, Fore, Sub, Accent, OnAccent, Rail, ThumbFill, ThumbBorder, Hover, ToggleStroke;

    const string Personalize = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    static bool ReadFlag(string name, bool fallback)
    {
        try
        {
            var v = Registry.GetValue(Personalize, name, null);
            return v is int ? (int)v != 0 : fallback;
        }
        catch { return fallback; }
    }

    public static bool TaskbarLight() { return ReadFlag("SystemUsesLightTheme", false); }

    // AccentPalette: 8 colori RGBA (Light3, Light2, Light1, Base, Dark1, Dark2, Dark3, ...)
    static Color AccentShade(int index, Color fallback)
    {
        try
        {
            var p = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Accent", "AccentPalette", null) as byte[];
            if (p != null && p.Length >= 32) return Color.FromArgb(p[index * 4], p[index * 4 + 1], p[index * 4 + 2]);
        }
        catch { }
        return fallback;
    }

    public static Theme Current()
    {
        bool dark = !ReadFlag("AppsUseLightTheme", true);
        if (dark)
            return new Theme
            {
                Dark = true,
                Back = Color.FromArgb(44, 44, 44),
                Footer = Color.FromArgb(32, 32, 32),
                Divider = Color.FromArgb(22, 22, 22),
                Border = Color.FromArgb(70, 70, 70),
                Fore = Color.White,
                Sub = Color.FromArgb(197, 197, 197),
                Accent = AccentShade(1, Color.FromArgb(153, 235, 255)),
                OnAccent = Color.Black,
                Rail = Color.FromArgb(138, 138, 138),
                ThumbFill = Color.FromArgb(69, 69, 69),
                ThumbBorder = Color.FromArgb(80, 80, 80),
                Hover = Color.FromArgb(61, 61, 61),
                ToggleStroke = Color.FromArgb(207, 207, 207),
            };
        return new Theme
        {
            Dark = false,
            Back = Color.FromArgb(243, 243, 243),
            Footer = Color.FromArgb(235, 235, 235),
            Divider = Color.FromArgb(222, 222, 222),
            Border = Color.FromArgb(200, 200, 200),
            Fore = Color.FromArgb(27, 27, 27),
            Sub = Color.FromArgb(96, 96, 96),
            Accent = AccentShade(4, Color.FromArgb(0, 95, 184)),
            OnAccent = Color.White,
            Rail = Color.FromArgb(134, 134, 134),
            ThumbFill = Color.White,
            ThumbBorder = Color.FromArgb(215, 215, 215),
            Hover = Color.FromArgb(230, 230, 230),
            ToggleStroke = Color.FromArgb(96, 96, 96),
        };
    }

    public static string UiFont { get { return FontExists("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI"; } }
    public static string IconFont { get { return FontExists("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets"; } }

    static bool FontExists(string name)
    {
        using (var f = new Font(name, 10f)) return f.Name == name;
    }

    public static void Apply(Form f, Theme t)
    {
        int round = Native.DWMWCP_ROUND, dark = t.Dark ? 1 : 0;
        int border = t.Border.R | (t.Border.G << 8) | (t.Border.B << 16);
        Native.DwmSetWindowAttribute(f.Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, 4);
        Native.DwmSetWindowAttribute(f.Handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4);
        Native.DwmSetWindowAttribute(f.Handle, Native.DWMWA_BORDER_COLOR, ref border, 4);
    }

    public static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

// Interruttore Fluent (ToggleSwitch) con animazione del pallino e del colore.
class Toggle : Control
{
    bool on, hover, pressed;
    float pos;      // 0 = spento, 1 = acceso (animato)
    float squash;   // allungamento del pallino mentre è premuto (animato)
    public Theme Theme;
    public event Action<bool> Changed;

    readonly System.Windows.Forms.Timer anim = new System.Windows.Forms.Timer { Interval = 10 };
    DateTime last;

    public Toggle()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        // Ogni clic conta, anche se ravvicinato: niente doppio clic che "mangia" il secondo.
        SetStyle(ControlStyles.StandardDoubleClick, false);
        Cursor = Cursors.Hand;
        anim.Tick += Step;
    }

    public bool On
    {
        get { return on; }
        set { on = value; pos = value ? 1 : 0; Invalidate(); }
    }

    public void Flip()
    {
        on = !on;
        Animate();
        if (Changed != null) Changed(on);
    }

    void Animate()
    {
        last = DateTime.Now;
        anim.Start();
    }

    // Ease-out esponenziale verso il valore di destinazione: veloce all'inizio, morbido alla fine.
    void Step(object sender, EventArgs e)
    {
        var now = DateTime.Now;
        float dt = (float)(now - last).TotalMilliseconds;
        last = now;
        float k = 1 - (float)Math.Exp(-dt / 35f);
        pos += ((on ? 1 : 0) - pos) * k;
        squash += ((pressed ? 1 : 0) - squash) * k;
        if (Math.Abs((on ? 1 : 0) - pos) < 0.005f && Math.Abs((pressed ? 1 : 0) - squash) < 0.005f)
        {
            pos = on ? 1 : 0;
            squash = pressed ? 1 : 0;
            anim.Stop();
        }
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { pressed = true; Animate(); } }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (!pressed) return;
        pressed = false;
        if (ClientRectangle.Contains(e.Location)) Flip(); else Animate();
    }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) Flip(); }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); }
    protected override void Dispose(bool disposing) { if (disposing) anim.Dispose(); base.Dispose(disposing); }

    static Color Mix(Color a, Color b, float t)
    {
        return Color.FromArgb(
            (int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent.BackColor);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        float rad = r.Height / 2;
        using (var path = Theme.RoundRect(r, rad))
        {
            using (var b = new SolidBrush(Color.FromArgb((int)(255 * pos), Theme.Accent))) g.FillPath(b, path);
            using (var p = new Pen(Color.FromArgb((int)(255 * (1 - pos)), Theme.ToggleStroke), 1f)) g.DrawPath(p, path);
        }
        float kr = rad * (hover || pressed ? 0.6f : 0.5f);
        float stretch = kr * 0.6f * squash;
        float left = r.X + rad, right = r.Right - rad;
        float kx = left + (right - left) * pos;
        // Mentre è premuto il pallino si allunga verso il centro, come in Windows 11.
        float x0 = kx - kr - stretch * pos, x1 = kx + kr + stretch * (1 - pos);
        using (var b = new SolidBrush(Mix(Theme.ToggleStroke, Theme.OnAccent, pos)))
        using (var knob = Theme.RoundRect(new RectangleF(x0, r.Y + rad - kr, x1 - x0, 2 * kr), kr))
            g.FillPath(b, knob);
    }
}

// Pulsante piatto Fluent: glifo e/o testo, sfondo arrotondato al passaggio del mouse.
class FlatButton : Control
{
    public Theme Theme;
    public string Glyph;
    public Font GlyphFont;
    bool hover, pressed;

    public FlatButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) OnClick(EventArgs.Empty); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);
        if (hover || pressed)
        {
            int a = pressed ? 10 : 18;
            var c = Theme.Dark ? Color.FromArgb(a, 255, 255, 255) : Color.FromArgb(a, 0, 0, 0);
            using (var path = Theme.RoundRect(new RectangleF(0, 0, Width - 1, Height - 1), Height * 0.15f))
            using (var b = new SolidBrush(c))
                g.FillPath(b, path);
        }
        var fore = pressed ? Theme.Sub : Theme.Fore;
        const TextFormatFlags vc = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
        if (string.IsNullOrEmpty(Text))
        {
            TextRenderer.DrawText(g, Glyph, GlyphFont, ClientRectangle, fore, vc | TextFormatFlags.HorizontalCenter);
            return;
        }
        int gx = Height / 3;
        int gw = Height / 2;
        TextRenderer.DrawText(g, Glyph, GlyphFont, new Rectangle(gx, 0, gw, Height), fore, vc | TextFormatFlags.HorizontalCenter);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(gx + gw + Height / 4, 0, Width, Height), fore, vc | TextFormatFlags.Left);
    }
}


// Dati dell'app condivisi da TrayDeck e dall'installer.
static class AppInfo
{
    public const string Name = "TrayDeck";
    public const string Version = "1.0.0";
    public const string Author = "RickyTr99";
    public const string Url = "https://github.com/RickyTr99/traydeck";
    public const string SettingsKey = @"Software\TrayDeck";
    public const string QuitEventName = "TrayDeck_Quit";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool StartupEnabled()
    {
        using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue(Name) != null;
    }

    public static void SetStartup(bool on, string exePath)
    {
        using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (on) k.SetValue(Name, "\"" + exePath + "\"");
            else k.DeleteValue(Name, false);
        }
    }
}

// Pulsante Fluent: "principale" pieno nel colore d'accento, "secondario" neutro con bordo.
class PushButton : Control
{
    public Theme Theme;
    public bool Primary;
    bool hover, pressed;

    public PushButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        SetStyle(ControlStyles.StandardDoubleClick, false);
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) OnClick(EventArgs.Empty); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        float radius = Height * 0.14f;
        Color fill, text, border;
        if (Primary)
        {
            fill = !Enabled ? Theme.Rail : Color.FromArgb(pressed ? 205 : hover ? 230 : 255, Theme.Accent);
            text = !Enabled ? Theme.Back : pressed ? Color.FromArgb(200, Theme.OnAccent) : Theme.OnAccent;
            border = fill;
        }
        else
        {
            var baseFill = Theme.Dark ? Color.FromArgb(55, 55, 55) : Color.FromArgb(253, 253, 253);
            var hoverFill = Theme.Dark ? Color.FromArgb(61, 61, 61) : Color.FromArgb(246, 246, 246);
            var pressFill = Theme.Dark ? Color.FromArgb(50, 50, 50) : Color.FromArgb(240, 240, 240);
            fill = pressed ? pressFill : hover ? hoverFill : baseFill;
            text = pressed || !Enabled ? Theme.Sub : Theme.Fore;
            border = Theme.Dark ? Color.FromArgb(66, 66, 66) : Color.FromArgb(214, 214, 214);
        }
        using (var path = Theme.RoundRect(r, radius))
        {
            using (var b = new SolidBrush(fill)) g.FillPath(b, path);
            using (var p = new Pen(border)) g.DrawPath(p, path);
        }
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}
// Lingua dell'interfaccia: italiano se Windows è in italiano, inglese per tutte le altre lingue.
// TRAYDECK_LANG=it|en forza una lingua (utile per provarle entrambe).
static class L
{
    public static readonly bool It = Detect();

    static bool Detect()
    {
        var forced = Environment.GetEnvironmentVariable("TRAYDECK_LANG");
        if (!string.IsNullOrEmpty(forced)) return forced.StartsWith("it", StringComparison.OrdinalIgnoreCase);
        return System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "it";
    }

    public static string T(string it, string en) { return It ? it : en; }
}