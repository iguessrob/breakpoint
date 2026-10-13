using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Breakpoint.Desktop;

internal static class BrandAssets
{
    // The chosen Stopping Point master: one path and its separate endpoint.
    internal static Geometry CreateMark()=>Geometry.Parse("M28 32 H76 V152 Q76 176 100 176 H156 V224 H92 C52 224 28 200 28 160 Z M228 200 A24 24 0 1 1 180 200 A24 24 0 1 1 228 200 Z");
    internal static BitmapFrame WindowIcon()=>BitmapFrame.Create(new Uri("pack://application:,,,/Breakpoint.Desktop;component/Assets/breakpoint.ico",UriKind.Absolute));
    private static Uri Resource(string name)=>new($"/Breakpoint.Desktop;component/Assets/{name}",UriKind.Relative);
    internal static System.Drawing.Icon TrayIcon()
    {
        using Stream stream=Application.GetResourceStream(Resource("breakpoint-tray.ico"))!.Stream;
        using var icon=new System.Drawing.Icon(stream,new System.Drawing.Size(16,16));
        return (System.Drawing.Icon)icon.Clone();
    }
}
