// Punto d'ingresso unico: TrayDeck e il suo installer sono lo stesso eseguibile.
//   --uninstall                        -> disinstallazione
//   avviato dalla cartella installata  -> TrayDeck
//   --app (o --show/--options/--menu)  -> TrayDeck anche da fuori, per le prove
//   altrimenti                         -> installazione
using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("TrayDeck")]
[assembly: AssemblyProduct("TrayDeck")]
[assembly: AssemblyDescription("Monitor brightness control for the Windows notification area, with its own installer.")]
[assembly: AssemblyCompany(AppInfo.Author)]
[assembly: AssemblyCopyright("© " + AppInfo.Author)]
[assembly: AssemblyVersion(AppInfo.Version + ".0")]
[assembly: AssemblyFileVersion(AppInfo.Version + ".0")]

static class Entry
{
    [STAThread]
    static void Main(string[] args)
    {
        Native.SetProcessDPIAware();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        if (Has(args, "--uninstall")) { SetupForm.Run(true, args); return; }

        bool installed = string.Equals(
            Path.GetDirectoryName(Path.GetFullPath(Application.ExecutablePath)),
            Path.GetFullPath(Installer.InstallDir), StringComparison.OrdinalIgnoreCase);
        if (installed || Has(args, "--app") || Has(args, "--show") || Has(args, "--options") || Has(args, "--menu"))
            Program.Run(args);
        else
            SetupForm.Run(false, args);
    }

    static bool Has(string[] args, string flag) { return Array.IndexOf(args, flag) >= 0; }
}
