using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Ludryn.Services;

public static class WindowsTouchKeyboardService
{
    public static void Show(IntPtr windowHandle)
    {
        try
        {
            var invocation = (ITipInvocation)new UIHostNoLaunch();
            invocation.Toggle(windowHandle);
            Marshal.FinalReleaseComObject(invocation);
            return;
        }
        catch (Exception ex)
        {
            LudrynLogger.Log("library", $"Falha ao abrir teclado de toque: {ex.Message}");
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "osk.exe",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            LudrynLogger.Log("library", $"Falha ao abrir teclado virtual: {ex.Message}");
        }
    }

    [ComImport]
    [Guid("4CE576FA-83DC-4F88-951C-9D0782B4E376")]
    private class UIHostNoLaunch;

    [ComImport]
    [Guid("37C994E7-432B-4834-A2F7-DCE1F13B834B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITipInvocation
    {
        void Toggle(IntPtr windowHandle);
    }
}
