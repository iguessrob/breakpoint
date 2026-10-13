using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Breakpoint.Desktop;

internal static class IslandNative
{
    private const int ExtendedStyle=-20,ToolWindow=0x80,AppWindow=0x40000;
    // Extended styles are 32-bit values on both Windows architectures.
    [DllImport("user32.dll",EntryPoint="GetWindowLongW",SetLastError=true)]
    private static extern int GetWindowLong(IntPtr window,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongW",SetLastError=true)]
    private static extern int SetWindowLong(IntPtr window,int index,int value);

    internal static void ExcludeFromSwitcher(IntPtr window)
    {
        var style=GetWindowLong(window,ExtendedStyle);
        Marshal.SetLastPInvokeError(0);
        var previous=SetWindowLong(window,ExtendedStyle,(style|ToolWindow)&~AppWindow);
        var error=Marshal.GetLastWin32Error();
        if(previous==0&&error!=0)throw new Win32Exception(error);
    }
}
