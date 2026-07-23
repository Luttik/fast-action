using System.Runtime.InteropServices;

namespace FastAction.Services;

/// <summary>
/// Ensures a Windows.System.DispatcherQueue exists for system backdrop controllers.
/// </summary>
internal sealed class WindowsSystemDispatcherQueueHelper
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        public int DwSize;
        public int ThreadType;
        public int ApartmentType;
    }

    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(
        [In] DispatcherQueueOptions options,
        [In, Out, MarshalAs(UnmanagedType.IUnknown)] ref object? dispatcherQueueController);

    private object? _dispatcherQueueController;

    public void EnsureWindowsSystemDispatcherQueueController()
    {
        if (Windows.System.DispatcherQueue.GetForCurrentThread() is not null)
        {
            return;
        }

        if (_dispatcherQueueController is not null)
        {
            return;
        }

        var options = new DispatcherQueueOptions
        {
            DwSize = Marshal.SizeOf<DispatcherQueueOptions>(),
            ThreadType = 2, // DQTYPE_THREAD_CURRENT
            ApartmentType = 2, // DQTAT_COM_STA
        };

        _ = CreateDispatcherQueueController(options, ref _dispatcherQueueController);
    }
}
