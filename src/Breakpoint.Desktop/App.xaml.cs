using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using Breakpoint.Core;

namespace Breakpoint.Desktop;

public partial class App : System.Windows.Application
{
    public static bool Preview { get; private set; }
    public static ServiceClient Client { get; private set; } = null!;
    private Process? worker;
    private DesktopController? controller;
    private System.Threading.Mutex? instance;
    private static string previewRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Breakpoint","Preview");
    public static string PreferencePath => Path.Combine(Preview ? previewRoot :
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Breakpoint"), "appearance.txt");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if(e.Args.Contains("--package-check"))
        {
            try{if(!File.Exists(Path.Combine(AppContext.BaseDirectory,"Breakpoint.Service.exe")))throw new IOException("Companion service is missing.");_ = BrandAssets.WindowIcon();using var icon=BrandAssets.TrayIcon();if(!new Typeface(AppTypography.Family,FontStyles.Normal,FontWeights.Normal,FontStretches.Normal).TryGetGlyphTypeface(out var font)||!font.FamilyNames.Values.Contains("ABC Areal"))throw new IOException("Packaged Areal font is missing.");using var trayFont=AppTypography.TrayFont(10);Shutdown(0);}
            catch{Shutdown(1);}return;
        }
        Preview = e.Args.Contains("--preview")||File.Exists(Path.Combine(AppContext.BaseDirectory,"portable-preview.flag"));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(AppContext.BaseDirectory)))[..12];
        previewRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Breakpoint","Preview",hash);
        if(Preview)
        {
            Directory.CreateDirectory(previewRoot);
            var old=Path.Combine(AppContext.BaseDirectory,"preview-data");
            if(!File.Exists(Path.Combine(previewRoot,"state.json"))&&Directory.Exists(old))
                foreach(var file in Directory.EnumerateFiles(old)){var target=Path.Combine(previewRoot,Path.GetFileName(file));if(!File.Exists(target))File.Copy(file,target,false);}
        }
        instance = new System.Threading.Mutex(true, $"Local\\Breakpoint.Desktop.{(Preview ? hash : e.Args.Contains("--setup") ? "Setup" : "Live")}", out var created);
        if (!created) { System.Windows.MessageBox.Show("Breakpoint is already running. Open it from the tray icon.", "Breakpoint"); Shutdown(); return; }
        var pipe = Preview ? Protocol.LivePipe + ".Preview." + hash : Protocol.LivePipe;
        Client = new ServiceClient(pipe,Preview);
        if (Preview)
        {
            var service = Path.Combine(AppContext.BaseDirectory, "Breakpoint.Service.exe");
            if (!File.Exists(service)) { System.Windows.MessageBox.Show("Build the app using scripts/build.ps1 first.", "Breakpoint"); Shutdown(); return; }
            var info = new ProcessStartInfo(service) { UseShellExecute = false, CreateNoWindow = true };
            info.ArgumentList.Add("--preview"); info.ArgumentList.Add("--pipe"); info.ArgumentList.Add(pipe);
            info.ArgumentList.Add("--data"); info.ArgumentList.Add(previewRoot);
            worker = Process.Start(info);
        }
        ApplyTheme(File.Exists(PreferencePath) ? File.ReadAllText(PreferencePath).Trim() : "Dark");
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += (_, _) => Dispatcher.Invoke(() => ApplyTheme(CurrentTheme));
        controller = new DesktopController(e.Args.Contains("--setup")); MainWindow = controller.Island; controller.Island.Show();
    }
    public static string CurrentTheme { get; private set; } = "Dark";
    public static void ApplyTheme(string theme)
    {
        CurrentTheme = theme == "Light" ? "Light" : "Dark";
        var light = CurrentTheme == "Light";
        var colors = light ? new[] { "#FFFFFF", "#F0F3F5", "#192027", "#515D69", "#D6DCE2", "#275E46", "#FFFFFF", "#865500" }
            : new[] { "#1B1D20", "#292D31", "#F1F2F3", "#BEC5CE", "#3A4047", "#B9D8BE", "#14251A", "#F2C278" };
        var names = new[] { "Bg", "Raised", "Ink", "Dim", "Line", "Accent", "AccentInk", "Warning" };
        for (var i = 0; i < names.Length; i++) Current.Resources[names[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
        if (SystemParameters.HighContrast)
        {
            Current.Resources["Bg"] = SystemColors.WindowBrush; Current.Resources["Raised"] = SystemColors.ControlBrush;
            Current.Resources["Ink"] = SystemColors.WindowTextBrush; Current.Resources["Dim"] = SystemColors.WindowTextBrush;
            Current.Resources["Line"] = SystemColors.WindowTextBrush; Current.Resources["Accent"] = SystemColors.HighlightBrush;
            Current.Resources["AccentInk"] = SystemColors.HighlightTextBrush; Current.Resources["Warning"] = SystemColors.WindowTextBrush;
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        // Only the private no-op preview worker is terminated. Never stop the live Windows service here.
        if (worker is { HasExited: false }) worker.Kill();
        worker?.Dispose(); instance?.Dispose(); base.OnExit(e);
    }
}
