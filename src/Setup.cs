// Installer di TrayDeck: wizard guidato in stile Windows 11 che installa (o disinstalla)
// l'app per l'utente corrente, senza richiedere i permessi di amministratore.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;


static class Installer
{
    public static readonly string InstallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppInfo.Name);
    public static readonly string AppExe = Path.Combine(InstallDir, AppInfo.Name + ".exe");
    static readonly string StartMenuLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppInfo.Name + ".lnk");
    static readonly string DesktopLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppInfo.Name + ".lnk");
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppInfo.Name;

    public static string InstalledVersion()
    {
        using (var k = Registry.CurrentUser.OpenSubKey(UninstallKey))
            return k == null ? null : k.GetValue("DisplayVersion") as string;
    }

    public static bool HasDesktopLink() { return File.Exists(DesktopLink); }

    // TrayDeck in esecuzione blocca il file: prima di sovrascriverlo o rimuoverlo gli si chiede
    // di chiudersi da solo (evento con nome); la chiusura forzata resta solo come ultima risorsa.
    static void StopRunning()
    {
        try
        {
            using (var quit = EventWaitHandle.OpenExisting(AppInfo.QuitEventName)) quit.Set();
        }
        catch (WaitHandleCannotBeOpenedException) { }
        int self = Process.GetCurrentProcess().Id;
        foreach (var p in Process.GetProcessesByName(AppInfo.Name))
        {
            if (p.Id == self) continue;
            try { if (!p.WaitForExit(3000)) { p.Kill(); p.WaitForExit(3000); } } catch { }
        }
    }

    // Il file può restare bloccato per un attimo dopo la chiusura del processo.
    static void Retry(Action action)
    {
        for (int i = 0; ; i++)
        {
            try { action(); return; }
            catch (IOException) { if (i >= 15) throw; }
            catch (UnauthorizedAccessException) { if (i >= 15) throw; }
            Thread.Sleep(200);
        }
    }

    public static void Install(bool startup, bool desktop)
    {
        StopRunning();
        Directory.CreateDirectory(InstallDir);

        // Installer e app sono lo stesso file: l'installazione copia sé stessa come TrayDeck.exe.
        Retry(() => File.Copy(Application.ExecutablePath, AppExe, true));

        CreateShortcut(StartMenuLink);
        if (desktop) CreateShortcut(DesktopLink);
        else if (File.Exists(DesktopLink)) File.Delete(DesktopLink);
        AppInfo.SetStartup(startup, AppExe);

        long size = new FileInfo(AppExe).Length;
        using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            k.SetValue("DisplayName", AppInfo.Name);
            k.SetValue("DisplayVersion", AppInfo.Version);
            k.SetValue("Publisher", AppInfo.Author);
            k.SetValue("DisplayIcon", AppExe);
            k.SetValue("InstallLocation", InstallDir);
            k.SetValue("UninstallString", "\"" + AppExe + "\" --uninstall");
            k.SetValue("URLInfoAbout", AppInfo.Url);
            k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            k.SetValue("EstimatedSize", (int)(size / 1024), RegistryValueKind.DWord);
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }
    }

    public static void Uninstall()
    {
        StopRunning();
        foreach (var link in new[] { StartMenuLink, DesktopLink })
            if (File.Exists(link)) File.Delete(link);
        AppInfo.SetStartup(false, null);
        Registry.CurrentUser.DeleteSubKeyTree(AppInfo.SettingsKey, false);
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
        // Se la disinstallazione non è potuta ripartire dalla cartella temporanea,
        // TrayDeck.exe è ancora in uso: in quel caso la cartella resta e basta.
        try { if (Directory.Exists(InstallDir)) Retry(() => Directory.Delete(InstallDir, true)); } catch { }
    }

    // Un programma non può cancellare il proprio file mentre è in esecuzione. Quando la
    // disinstallazione parte da TrayDeck.exe installato, il programma si copia nella cartella temporanea
    // e prosegue da lì, così la cartella di installazione può essere rimossa per intero.
    public static bool RelaunchFromTemp(string[] args)
    {
        string self = Path.GetFullPath(Application.ExecutablePath);
        if (!self.StartsWith(Path.GetFullPath(InstallDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return false;
        try
        {
            string copy = Path.Combine(Path.GetTempPath(), AppInfo.Name + "-uninstall.exe");
            File.Copy(self, copy, true);
            Process.Start(new ProcessStartInfo(copy, string.Join(" ", args)) { WorkingDirectory = Path.GetTempPath() });
            return true;
        }
        catch { return false; }
    }

    public static void Launch() { Process.Start(new ProcessStartInfo(AppExe) { WorkingDirectory = InstallDir }); }

    // Collegamento .lnk tramite l'interfaccia nativa di Windows (IShellLink).
    static void CreateShortcut(string path)
    {
        var link = (IShellLinkW)new ShellLink();
        link.SetPath(AppExe);
        link.SetWorkingDirectory(InstallDir);
        link.SetDescription(L.T("Luminosità dei monitor dall'area di notifica", "Monitor brightness from the notification area"));
        link.SetIconLocation(AppExe, 0);
        ((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Save(path, true);
        Marshal.ReleaseComObject(link);
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    class ShellLink { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder file, int max, IntPtr findData, int flags);
        void GetIDList(out IntPtr pidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder name, int max);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder dir, int max);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder args, int max);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int cmd);
        void SetShowCmd(int cmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder path, int max, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, int reserved);
        void Resolve(IntPtr hwnd, int flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }
}

// Pulsante di chiusura della barra del titolo, rosso al passaggio del mouse come in Windows 11.
class CloseButton : Control
{
    public Theme Theme;
    public Font GlyphFont;
    bool hover, pressed;

    public CloseButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        bool active = Enabled && (hover || pressed);
        g.Clear(active ? (pressed ? Color.FromArgb(200, 196, 43, 28) : Color.FromArgb(196, 43, 28)) : BackColor);
        var fore = active ? Color.White : Enabled ? Theme.Fore : Theme.Rail;
        TextRenderer.DrawText(g, "", GlyphFont, ClientRectangle, fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

class SetupForm : Form
{
    enum Page { Welcome, Working, Done, Failed, ConfirmRemove, Removing, Removed }

    struct TextItem
    {
        public string Text;
        public Font Font;
        public Rectangle Rect;
        public Color Color;
        public TextFormatFlags Flags;
    }

    const TextFormatFlags AlignL = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
    const TextFormatFlags Center = TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding | TextFormatFlags.WordBreak;

    readonly bool uninstallMode;
    readonly float scale;
    readonly Font fTitleBar, fText, fSub, fHero, fHeading, fGlyph, fGlyphBig, fCaption;
    readonly Bitmap appIcon, appIconSmall;
    Theme theme;
    Page page;

    readonly List<TextItem> texts = new List<TextItem>();
    readonly List<Rectangle> dividers = new List<Rectangle>();
    readonly List<KeyValuePair<Rectangle, Toggle>> toggleRows = new List<KeyValuePair<Rectangle, Toggle>>();
    Rectangle footer, iconRect, checkRect, progressRect;
    CloseButton closeButton;
    int titleH, footerH;

    // Scelte dell'utente
    bool optStartup = true, optDesktop, optLaunch = true;
    string error;

    readonly System.Windows.Forms.Timer progressTimer = new System.Windows.Forms.Timer { Interval = 15 };
    DateTime progressStart;

    public SetupForm(bool uninstall)
    {
        uninstallMode = uninstall;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Text = uninstall ? L.T("Disinstallazione di TrayDeck", "Uninstall TrayDeck") : L.T("Installazione di TrayDeck", "TrayDeck Setup");
        DoubleBuffered = true;
        KeyPreview = true;
        SetStyle(ControlStyles.StandardDoubleClick, false);
        using (var g = CreateGraphics()) scale = g.DpiX / 96f;

        string ui = Theme.UiFont, ic = Theme.IconFont;
        fTitleBar = new Font(ui, 9f);
        fCaption = new Font(ic, 7.5f);
        fText = new Font(ui, 10f);
        fSub = new Font(ui, 9.5f);
        fHero = new Font(ui, 20f, FontStyle.Bold);
        fHeading = new Font(ui, 15f, FontStyle.Bold);
        fGlyph = new Font(ic, 9f);
        fGlyphBig = new Font(ic, 20f);

        Icon = LoadIcon(32);
        appIcon = LoadIcon(S(64)).ToBitmap();
        appIconSmall = LoadIcon(S(16)).ToBitmap();
        optDesktop = Installer.HasDesktopLink();

        titleH = S(40);
        footerH = S(80);
        Size = new Size(S(500), S(440));
        progressTimer.Tick += (s, e) => Invalidate(progressRect);

        Build(uninstall ? Page.ConfirmRemove : Page.Welcome);
    }

    static Icon LoadIcon(int size)
    {
        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))
            return new Icon(s, size, size);
    }

    int S(float v) { return (int)Math.Round(v * scale); }

    // Finestra senza barra del titolo di sistema ma con ombra, angoli e aggancio nativi:
    // l'area non client viene azzerata e la barra del titolo la disegniamo noi.
    protected override void WndProc(ref Message m)
    {
        const int WM_NCCALCSIZE = 0x83, WM_NCHITTEST = 0x84, HTCLIENT = 1, HTCAPTION = 2;
        if (m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero) { m.Result = IntPtr.Zero; return; }
        if (m.Msg == WM_NCHITTEST)
        {
            var p = PointToClient(new Point((short)(m.LParam.ToInt64() & 0xFFFF), (short)((m.LParam.ToInt64() >> 16) & 0xFFFF)));
            bool onClose = closeButton != null && closeButton.Bounds.Contains(p);
            m.Result = (IntPtr)(p.Y < titleH && !onClose ? HTCAPTION : HTCLIENT);
            return;
        }
        base.WndProc(ref m);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Forza il ricalcolo dell'area client con il nuovo WM_NCCALCSIZE.
        const uint SWP_FRAMECHANGED = 0x20, SWP_NOMOVE = 0x2, SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4;
        SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

    bool Busy { get { return page == Page.Working || page == Page.Removing; } }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (Busy) e.Cancel = true;
        base.OnFormClosing(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape && !Busy) Close();
    }

    // ---------- Costruzione delle pagine ----------

    void AddText(string text, Font font, Rectangle rect, Color color, TextFormatFlags flags)
    {
        texts.Add(new TextItem { Text = text, Font = font, Rect = rect, Color = color, Flags = flags });
    }

    void AddToggleRow(string label, bool on, Action<bool> changed, int w, int pad, ref int y)
    {
        int h = S(44);
        AddText(label, fText, new Rectangle(pad, y, w - 2 * pad - S(50), h), theme.Fore, AlignL);
        var t = new Toggle { Theme = theme, On = on, Size = new Size(S(40), S(20)) };
        t.Location = new Point(w - pad - t.Width, y + (h - t.Height) / 2);
        t.Changed += changed;
        Controls.Add(t);
        toggleRows.Add(new KeyValuePair<Rectangle, Toggle>(new Rectangle(0, y, w, h), t));
        y += h;
    }

    // Pulsanti del piè di pagina, allineati a destra come nelle finestre di dialogo di Windows 11.
    void AddFooterButtons(params Tuple<string, bool, Action>[] buttons)
    {
        int bw = S(120), bh = S(32), gap = S(8), pad = S(24);
        int x = ClientSize.Width - pad - buttons.Length * bw - (buttons.Length - 1) * gap;
        foreach (var b in buttons)
        {
            var btn = new PushButton { Theme = theme, Primary = b.Item2, Text = b.Item1, Font = fText, BackColor = theme.Footer, Bounds = new Rectangle(x, footer.Y + (footerH - bh) / 2, bw, bh) };
            var action = b.Item3;
            btn.Click += (s, e) => action();
            Controls.Add(btn);
            if (b.Item2) btn.Select();
            x += bw + gap;
        }
    }

    static Tuple<string, bool, Action> Btn(string text, bool primary, Action a) { return Tuple.Create(text, primary, a); }

    void Build(Page p)
    {
        page = p;
        SuspendLayout();
        var old = new List<Control>();
        foreach (Control c in Controls) if (c != transition) old.Add(c);
        foreach (var c in old) { Controls.Remove(c); c.Dispose(); }
        texts.Clear();
        dividers.Clear();
        toggleRows.Clear();
        iconRect = checkRect = progressRect = Rectangle.Empty;
        progressTimer.Stop();

        theme = Theme.Current();
        BackColor = theme.Back;
        if (!IsHandleCreated) CreateHandle();
        Theme.Apply(this, theme);

        int w = ClientSize.Width, h = ClientSize.Height, pad = S(28);
        footer = new Rectangle(0, h - footerH, w, footerH);

        closeButton = new CloseButton { Theme = theme, GlyphFont = fCaption, BackColor = theme.Back, Bounds = new Rectangle(w - S(46), 0, S(46), S(32)), Enabled = !Busy };
        closeButton.Click += (s, e) => Close();
        Controls.Add(closeButton);

        switch (p)
        {
            case Page.Welcome: BuildWelcome(w, pad); break;
            case Page.Working: BuildStatus(w, pad, L.T("Installazione in corso…", "Installing…"), L.T("Ci vuole solo un attimo.", "This will only take a moment.")); break;
            case Page.Removing: BuildStatus(w, pad, L.T("Rimozione in corso…", "Removing…"), L.T("Ci vuole solo un attimo.", "This will only take a moment.")); break;
            case Page.Done: BuildDone(w, pad); break;
            case Page.Failed: BuildFailed(w, pad); break;
            case Page.ConfirmRemove: BuildConfirmRemove(w, pad); break;
            case Page.Removed: BuildRemoved(w, pad); break;
        }

        ResumeLayout();
        Invalidate();
    }

    void BuildWelcome(int w, int pad)
    {
        int y = titleH + S(12);
        int isz = appIcon.Width;
        iconRect = new Rectangle(pad, y, isz, isz);
        AddText(AppInfo.Name, fHero, new Rectangle(pad + isz + S(16), y + S(2), w - 2 * pad - isz, S(36)), theme.Fore, AlignL);
        AddText(L.T("Versione ", "Version ") + AppInfo.Version + "  ·  " + AppInfo.Author, fSub, new Rectangle(pad + isz + S(18), y + S(38), w - 2 * pad - isz, S(20)), theme.Sub, AlignL);
        y += isz + S(18);

        AddText(L.T("Regola la luminosità dei monitor esterni direttamente dall'area di notifica, senza usare i tasti del monitor.", "Adjust the brightness of your external monitors right from the notification area, without touching the monitor's buttons."),
            fText, new Rectangle(pad, y, w - 2 * pad, S(44)), theme.Fore, TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        y += S(54);

        dividers.Add(new Rectangle(pad, y, w - 2 * pad, 1));
        y += S(4);
        AddToggleRow(L.T("Avvia con Windows", "Start with Windows"), optStartup, on => optStartup = on, w, pad, ref y);
        AddToggleRow(L.T("Collegamento sul desktop", "Desktop shortcut"), optDesktop, on => optDesktop = on, w, pad, ref y);

        string installed = Installer.InstalledVersion();
        string action = installed == null ? L.T("Installa", "Install") : installed == AppInfo.Version ? L.T("Reinstalla", "Reinstall") : L.T("Aggiorna", "Update");
        string note = installed == null ? L.T("Verrà installato in ", "Will be installed in ") + Installer.InstallDir
                    : L.T("Versione installata: ", "Installed version: ") + installed + L.T(". Le impostazioni verranno mantenute.", ". Your settings will be kept.");
        AddText(note, fSub, new Rectangle(pad, footer.Y - S(34), w - 2 * pad, S(20)), theme.Sub, AlignL | TextFormatFlags.PathEllipsis);

        AddFooterButtons(Btn(L.T("Annulla", "Cancel"), false, Close), Btn(action, true, StartInstall));
    }

    // Pagina di attesa: icona, titolo, sottotitolo e barra di avanzamento indeterminata.
    void BuildStatus(int w, int pad, string title, string sub)
    {
        int y = titleH + S(40);
        int isz = appIcon.Width;
        iconRect = new Rectangle((w - isz) / 2, y, isz, isz);
        y += isz + S(24);
        AddText(title, fHeading, new Rectangle(pad, y, w - 2 * pad, S(32)), theme.Fore, Center);
        y += S(36);
        AddText(sub, fSub, new Rectangle(pad, y, w - 2 * pad, S(22)), theme.Sub, Center);
        y += S(40);
        progressRect = new Rectangle((w - S(240)) / 2, y, S(240), S(6));
        progressStart = DateTime.Now;
        progressTimer.Start();
    }

    void BuildDone(int w, int pad)
    {
        int y = titleH + S(28);
        int csz = S(64);
        checkRect = new Rectangle((w - csz) / 2, y, csz, csz);
        y += csz + S(22);
        AddText(L.T("TrayDeck è pronto", "TrayDeck is ready"), fHeading, new Rectangle(pad, y, w - 2 * pad, S(32)), theme.Fore, Center);
        y += S(38);
        AddText(L.T("Trovi l'icona del sole nell'area di notifica, in basso a destra. Puoi aprire TrayDeck anche dal menu Start.", "Look for the sun icon in the notification area, at the bottom right. You can also open TrayDeck from the Start menu."),
            fSub, new Rectangle(pad + S(16), y, w - 2 * pad - S(32), S(44)), theme.Sub, Center);
        y = footer.Y - S(56);
        dividers.Add(new Rectangle(pad, y, w - 2 * pad, 1));
        y += S(6);
        AddToggleRow(L.T("Avvia TrayDeck ora", "Launch TrayDeck now"), optLaunch, on => optLaunch = on, w, pad, ref y);

        AddFooterButtons(Btn(L.T("Fine", "Finish"), true, () =>
        {
            if (optLaunch) { try { Installer.Launch(); } catch { } }
            Close();
        }));
    }

    void BuildFailed(int w, int pad)
    {
        int y = titleH + S(36);
        AddText("", fGlyphBig, new Rectangle(pad, y, w - 2 * pad, S(48)), Color.FromArgb(232, 17, 35), Center);
        y += S(60);
        AddText(L.T("Qualcosa è andato storto", "Something went wrong"), fHeading, new Rectangle(pad, y, w - 2 * pad, S(32)), theme.Fore, Center);
        y += S(38);
        AddText(error ?? L.T("Errore sconosciuto.", "Unknown error."), fSub, new Rectangle(pad + S(16), y, w - 2 * pad - S(32), S(90)), theme.Sub, Center | TextFormatFlags.EndEllipsis);
        AddFooterButtons(Btn(L.T("Chiudi", "Close"), true, Close));
    }

    void BuildConfirmRemove(int w, int pad)
    {
        int y = titleH + S(40);
        int isz = appIcon.Width;
        iconRect = new Rectangle((w - isz) / 2, y, isz, isz);
        y += isz + S(24);
        AddText(L.T("Disinstallare TrayDeck?", "Uninstall TrayDeck?"), fHeading, new Rectangle(pad, y, w - 2 * pad, S(32)), theme.Fore, Center);
        y += S(38);
        AddText(L.T("Verranno rimossi il programma, i collegamenti e le impostazioni salvate.", "The app, its shortcuts and your saved settings will be removed."),
            fSub, new Rectangle(pad + S(16), y, w - 2 * pad - S(32), S(44)), theme.Sub, Center);
        AddFooterButtons(Btn(L.T("Annulla", "Cancel"), false, Close), Btn(L.T("Disinstalla", "Uninstall"), true, StartUninstall));
    }

    void BuildRemoved(int w, int pad)
    {
        int y = titleH + S(40);
        int csz = S(64);
        checkRect = new Rectangle((w - csz) / 2, y, csz, csz);
        y += csz + S(24);
        AddText(L.T("TrayDeck è stato rimosso", "TrayDeck has been removed"), fHeading, new Rectangle(pad, y, w - 2 * pad, S(32)), theme.Fore, Center);
        y += S(38);
        AddText(L.T("Grazie per averlo provato.", "Thanks for trying it out."), fSub, new Rectangle(pad, y, w - 2 * pad, S(22)), theme.Sub, Center);
        AddFooterButtons(Btn(L.T("Chiudi", "Close"), true, Close));
    }

    // ---------- Azioni ----------

    void StartInstall() { RunWork(Page.Working, () => Installer.Install(optStartup, optDesktop), Page.Done); }
    void StartUninstall() { RunWork(Page.Removing, Installer.Uninstall, Page.Removed); }

    // Esegue il lavoro su un thread separato; la pagina di attesa resta visibile almeno
    // per un momento, così il passaggio non sembra uno scatto.
    void RunWork(Page waiting, Action work, Page next)
    {
        GoTo(waiting);
        var started = DateTime.Now;
        var t = new Thread(() =>
        {
            Exception ex = null;
            try { work(); } catch (Exception e) { ex = e; }
            int wait = 900 - (int)(DateTime.Now - started).TotalMilliseconds;
            if (wait > 0) Thread.Sleep(wait);
            BeginInvoke((Action)(() =>
            {
                if (ex != null) error = ex.Message;
                GoTo(ex == null ? next : Page.Failed);
            }));
        }) { IsBackground = true };
        t.Start();
    }

    // ---------- Transizione tra le pagine (scorrimento + dissolvenza, come TrayDeck) ----------

    Control transition;
    Bitmap fromShot, toShot;
    readonly System.Windows.Forms.Timer pageAnim = new System.Windows.Forms.Timer { Interval = 10 };
    DateTime pageAnimStart;
    bool pageAnimWired;

    Bitmap Render()
    {
        var bmp = new Bitmap(ClientSize.Width, ClientSize.Height);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(theme.Back);
            PaintContent(g);
        }
        foreach (Control c in Controls)
            if (c != transition) c.DrawToBitmap(bmp, c.Bounds);
        return bmp;
    }

    void GoTo(Page p)
    {
        if (transition != null) EndTransition();
        fromShot = Render();
        transition = new Canvas { Bounds = new Rectangle(0, titleH, ClientSize.Width, ClientSize.Height - titleH), Tag = 0.0 };
        transition.Paint += PaintTransition;
        Controls.Add(transition);
        transition.BringToFront();

        Build(p);
        transition.BringToFront();
        toShot = Render();

        if (!pageAnimWired) { pageAnim.Tick += PageAnimTick; pageAnimWired = true; }
        pageAnimStart = DateTime.Now;
        pageAnim.Start();
    }

    void PageAnimTick(object sender, EventArgs e)
    {
        double t = Math.Min(1, (DateTime.Now - pageAnimStart).TotalMilliseconds / 280.0);
        transition.Tag = 1 - Math.Pow(1 - t, 3);
        transition.Invalidate();
        if (t >= 1) EndTransition();
    }

    void EndTransition()
    {
        pageAnim.Stop();
        Controls.Remove(transition);
        transition.Dispose();
        transition = null;
        fromShot.Dispose();
        toShot.Dispose();
    }

    void PaintTransition(object sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        double k = (double)transition.Tag;
        g.Clear(theme.Back);
        int shift = S(40);
        var src = new Rectangle(0, titleH, ClientSize.Width, ClientSize.Height - titleH);
        Faded(g, fromShot, src, (int)Math.Round(-shift * k), (float)(1 - Math.Min(1, k * 1.6)));
        Faded(g, toShot, src, (int)Math.Round(shift * (1 - k)), (float)Math.Max(0, (k - 0.15) / 0.85));
    }

    static void Faded(Graphics g, Bitmap bmp, Rectangle src, int dx, float alpha)
    {
        if (alpha <= 0) return;
        using (var ia = new System.Drawing.Imaging.ImageAttributes())
        {
            ia.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = alpha });
            g.DrawImage(bmp, new Rectangle(dx, 0, src.Width, src.Height), src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel, ia);
        }
    }

    class Canvas : Control
    {
        public Canvas() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true); }
    }

    // ---------- Disegno ----------

    protected override void OnPaint(PaintEventArgs e) { PaintContent(e.Graphics); }

    void PaintContent(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Barra del titolo
        g.DrawImage(appIconSmall, S(14), (S(32) - appIconSmall.Height) / 2);
        TextRenderer.DrawText(g, Text, fTitleBar, new Rectangle(S(40), 0, ClientSize.Width - S(100), S(32)), theme.Sub, AlignL);

        using (var b = new SolidBrush(theme.Footer)) g.FillRectangle(b, footer);
        using (var b = new SolidBrush(theme.Divider))
        {
            g.FillRectangle(b, footer.Left, footer.Top, footer.Width, 1);
            foreach (var d in dividers) g.FillRectangle(b, d);
        }

        if (!iconRect.IsEmpty) g.DrawImage(appIcon, iconRect);

        if (!checkRect.IsEmpty)
        {
            using (var b = new SolidBrush(theme.Accent)) g.FillEllipse(b, checkRect);
            TextRenderer.DrawText(g, "", fGlyphBig, checkRect, theme.OnAccent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        foreach (var t in texts) TextRenderer.DrawText(g, t.Text, t.Font, t.Rect, t.Color, t.Flags);

        if (!progressRect.IsEmpty) PaintProgress(g);
    }

    // Barra indeterminata Fluent: un segmento d'accento che scorre su un binario sottile.
    void PaintProgress(Graphics g)
    {
        var r = progressRect;
        float th = Math.Max(2f, S(3));
        float cy = r.Y + r.Height / 2f;
        using (var p = new Pen(Color.FromArgb(90, theme.Rail), th) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawLine(p, r.Left, cy, r.Right, cy);
        double t = ((DateTime.Now - progressStart).TotalMilliseconds % 1400) / 1400.0;
        double ease = t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
        float seg = r.Width * 0.38f;
        float x0 = (float)(r.Left - seg + (r.Width + seg) * ease);
        float a = Math.Max(r.Left, x0), b = Math.Min(r.Right, x0 + seg);
        if (b - a > th)
            using (var p = new Pen(theme.Accent, th) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(p, a, cy, b, cy);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        foreach (var row in toggleRows)
            if (row.Key.Contains(e.Location)) { row.Value.Flip(); return; }
    }

    protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(BackColor); }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        closeButton.Enabled = !Busy;
    }

    public static void Run(bool uninstall, string[] args)
    {
        if (uninstall && Installer.RelaunchFromTemp(args)) return;
        Application.Run(new SetupForm(uninstall));
    }
}
