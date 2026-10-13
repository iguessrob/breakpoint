using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace Breakpoint.Desktop;

internal static class AppTypography
{
    internal static FontFamily Family { get; } = new(new Uri("pack://application:,,,/"),"./Breakpoint.Desktop;component/Assets/Fonts/#ABC Areal");
    private static readonly Lazy<TrayFonts> tray = new(()=>new TrayFonts());
    internal static System.Drawing.Font TrayFont(float size,System.Drawing.FontStyle style=System.Drawing.FontStyle.Regular)
        => new(tray.Value.Family,size,style);

    // Both GDI text rendering and GDI+ need access to the packaged fonts.
    // Registration is private to this process, and its memory lives until process exit.
    private sealed class TrayFonts
    {
        private readonly System.Drawing.Text.PrivateFontCollection fonts=new();
        private readonly List<IntPtr> memory=[];
        private readonly List<IntPtr> registrations=[];
        internal System.Drawing.FontFamily Family { get; }
        internal TrayFonts()
        {
            foreach(var name in new[]{"ABCAreal-Regular.ttf","ABCAreal-Bold.ttf"})
            {
                using var stream=Application.GetResourceStream(new Uri("/Breakpoint.Desktop;component/Assets/Fonts/"+name,UriKind.Relative))!.Stream;
                using var buffer=new MemoryStream();stream.CopyTo(buffer);var bytes=buffer.ToArray();
                var address=Marshal.AllocHGlobal(bytes.Length);memory.Add(address);Marshal.Copy(bytes,0,address,bytes.Length);
                uint count=0;var handle=AddFontMemResourceEx(address,(uint)bytes.Length,IntPtr.Zero,ref count);
                if(handle==IntPtr.Zero||count==0)throw new IOException("Could not load the packaged Areal tray font.");
                registrations.Add(handle);fonts.AddMemoryFont(address,bytes.Length);
            }
            Family=fonts.Families.First(f=>f.Name=="ABC Areal");
        }
    }
    [DllImport("gdi32.dll",SetLastError=true)]
    private static extern IntPtr AddFontMemResourceEx(IntPtr file,uint size,IntPtr reserved,ref uint count);
}
