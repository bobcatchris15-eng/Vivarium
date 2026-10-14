using System;
using System.Runtime.InteropServices;
namespace Vivarium.Game.App;

// Layout copied from pinned installed renderdoc_app.h RENDERDOC_API_1_7_0.
// Union aliases occupy one pointer; API 1.0.0 prefix is backwards compatible.
public sealed class RenderDocDiagnostic
{
    [StructLayout(LayoutKind.Sequential)] private struct Api {
        public IntPtr GetAPIVersion, SetCaptureOptionU32, SetCaptureOptionF32,
            GetCaptureOptionU32, GetCaptureOptionF32, SetFocusToggleKeys, SetCaptureKeys,
            GetOverlayBits, MaskOverlayBits, RemoveHooks, UnloadCrashHandler,
            SetCaptureFilePathTemplate, GetCaptureFilePathTemplate, GetNumCaptures,
            GetCapture, TriggerCapture;
    }
    [DllImport("kernel32", CharSet=CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32", CharSet=CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetApi(int version, out IntPtr api);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Trigger();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint Count();
    private readonly Trigger trigger;
    private readonly Count count;
    private RenderDocDiagnostic(Api api) {
        trigger = Marshal.GetDelegateForFunctionPointer<Trigger>(api.TriggerCapture);
        count = Marshal.GetDelegateForFunctionPointer<Count>(api.GetNumCaptures);
    }
    // Call only under an explicit diagnostic argument. Never loads/injects a DLL.
    public static RenderDocDiagnostic AttachToLaunchedProcess() {
        var module = GetModuleHandle("renderdoc.dll");
        if (module == IntPtr.Zero) throw new InvalidOperationException("RenderDoc was not loaded by the launcher");
        var entry = GetProcAddress(module, "RENDERDOC_GetAPI");
        if (entry == IntPtr.Zero) throw new InvalidOperationException("Missing RENDERDOC_GetAPI");
        var get = Marshal.GetDelegateForFunctionPointer<GetApi>(entry);
        if (get(10000, out var ptr) != 1 || ptr == IntPtr.Zero) throw new InvalidOperationException("RenderDoc API 1.0.0 unavailable");
        return new RenderDocDiagnostic(Marshal.PtrToStructure<Api>(ptr));
    }
    // Schedules the next presented frame; let Godot render several more frames before quitting.
    public void CaptureNextPresentedFrame() => trigger();
    public uint CompletedCaptureCount => count();
}
