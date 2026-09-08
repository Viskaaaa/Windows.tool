using System;
using System.IO;
using System.Reflection;
using Microsoft.Win32;
using ViskaTweak.Models;

namespace ViskaTweak.Services;

/// <summary>
/// Installs the single exe onto the machine properly: a fixed location, a desktop and Start menu
/// shortcut, and an entry in Add or Remove Programs.
///
/// Everything is per-user, under LocalAppData and HKCU, so it never needs administrator rights and
/// never touches another account on the same PC.
/// </summary>
public static class InstallService
{
    private const string UninstallKey =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ViskaTweak";

    public const string ProductName = "Viska Tweak";

    public static string InstallFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Viska Tweak");

    public static string InstalledExe => Path.Combine(InstallFolder, "ViskaTweak.exe");

    private static string DesktopShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ProductName + ".lnk");

    private static string StartMenuShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), ProductName + ".lnk");

    public static bool IsInstalled => File.Exists(InstalledExe);

    /// <summary>True when the copy the user is looking at is the installed one.</summary>
    public static bool RunningFromInstall =>
        string.Equals(Environment.ProcessPath, InstalledExe, StringComparison.OrdinalIgnoreCase);

    public static TweakResult Install(bool desktopShortcut = true)
    {
        var source = Environment.ProcessPath;
        if (string.IsNullOrEmpty(source))
            return new(false, "Could not work out where this app is running from.");

        if (RunningFromInstall)
            return new(true, "Already installed and running from the installed copy.");

        // dotnet run produces a host exe in bin/, which is useless once the folder is deleted.
        if (source.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            return new(false, "This is a development build. Publish the single exe with build-exe.bat, "
                              + "then run that and install from it.");

        try
        {
            Directory.CreateDirectory(InstallFolder);
            File.Copy(source, InstalledExe, overwrite: true);

            var shortcuts = 0;
            if (desktopShortcut && CreateShortcut(DesktopShortcut)) shortcuts++;
            if (CreateShortcut(StartMenuShortcut)) shortcuts++;

            RegisterUninstall();

            JournalService.Record("Install", "Installed on this PC", InstalledExe,
                $"Copied from {source}. {shortcuts} shortcut(s) created.");

            return new(true, shortcuts > 0
                ? $"Installed to your user folder with {shortcuts} shortcut(s). It now appears in Add or Remove Programs."
                : "Installed, but Windows blocked the shortcuts. You can still launch it from the install folder.", 1);
        }
        catch (IOException ex)
        {
            return new(false, $"Could not copy the app: {ex.Message}");
        }
        catch (Exception ex)
        {
            return new(false, $"Install failed: {ex.Message}");
        }
    }

    public static TweakResult Uninstall()
    {
        var removed = 0;
        try
        {
            foreach (var link in new[] { DesktopShortcut, StartMenuShortcut })
                if (File.Exists(link)) { File.Delete(link); removed++; }

            using (var key = Registry.CurrentUser.OpenSubKey(
                       @"Software\Microsoft\Windows\CurrentVersion\Uninstall", writable: true))
                key?.DeleteSubKeyTree("ViskaTweak", throwOnMissingSubKey: false);

            BackgroundPrefs.SetStartWithWindows(false);

            JournalService.Record("Install", "Uninstalled", InstallFolder,
                $"{removed} shortcut(s) and the Add or Remove Programs entry removed.", reversible: false);

            // The running exe cannot delete itself, so say so rather than failing silently.
            return new(true, RunningFromInstall
                ? $"Shortcuts and the listing are gone. Close the app, then delete {InstallFolder} to finish."
                : $"Shortcuts and the listing are gone. Delete {InstallFolder} to remove the files.", 1);
        }
        catch (Exception ex) { return new(false, $"Could not uninstall: {ex.Message}"); }
    }

    /// <summary>
    /// Shortcuts are made through the Windows Script Host COM object, reached by reflection.
    /// Deliberately not `dynamic`: that drags in Microsoft.CSharp, and assemblies failing to
    /// resolve in the published bundle is a fault this app has already hit twice.
    /// </summary>
    private static bool CreateShortcut(string path)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return false;

            var shell = Activator.CreateInstance(shellType);
            if (shell is null) return false;

            var link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell,
                new object[] { path });
            if (link is null) return false;

            var linkType = link.GetType();
            void Set(string name, object value) =>
                linkType.InvokeMember(name, BindingFlags.SetProperty, null, link, new[] { value });

            Set("TargetPath", InstalledExe);
            Set("WorkingDirectory", InstallFolder);
            Set("IconLocation", InstalledExe + ",0");
            Set("Description", "Game and FiveM optimizer");

            linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
            return File.Exists(path);
        }
        catch { return false; }
    }

    private static void RegisterUninstall()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
            if (key is null) return;

            key.SetValue("DisplayName", ProductName);
            key.SetValue("DisplayIcon", InstalledExe);
            key.SetValue("DisplayVersion", "1.0.0");
            key.SetValue("Publisher", ProductName);
            key.SetValue("InstallLocation", InstallFolder);
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", EstimatedSizeKb(), RegistryValueKind.DWord);

            // Add or Remove Programs runs this; the app then finishes the job from its own UI.
            key.SetValue("UninstallString", $"\"{InstalledExe}\" --uninstall");
        }
        catch { /* the listing is a nicety, not the install */ }
    }

    private static int EstimatedSizeKb()
    {
        try { return (int)(new FileInfo(InstalledExe).Length / 1024); }
        catch { return 0; }
    }
}
