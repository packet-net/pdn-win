using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PdnWin.Hardware;

/// <summary>
/// Notices devices arriving and leaving on Windows: any device interface (audio endpoints, HID
/// collections, COM ports), through the configuration manager's notifications, with no window to
/// receive <c>WM_DEVICECHANGE</c> on. One plug is a burst of interfaces, so the burst is reported
/// once, after it goes quiet.
/// </summary>
internal sealed unsafe partial class WindowsDeviceWatcher : IDisposable
{
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(750);

    private readonly Timer _settle;
    private GCHandle _self;
    private IntPtr _registration;

    /// <summary>Starts watching. Where the notification cannot be registered the watcher is
    /// silent, and the station's periodic retry is all there is.</summary>
    public WindowsDeviceWatcher()
    {
        _settle = new Timer(_ => Changed?.Invoke(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _self = GCHandle.Alloc(this);
        var filter = new CmNotifyFilter
        {
            Size = (uint)sizeof(CmNotifyFilter),
            Flags = AllInterfaceClasses,
            FilterType = FilterTypeDeviceInterface,
        };
        if (CM_Register_Notification(&filter, GCHandle.ToIntPtr(_self), &OnNotify, out _registration) != 0)
        {
            _registration = IntPtr.Zero;
        }
    }

    /// <summary>Raised, on a timer thread, once a burst of arrivals or departures has settled.</summary>
    public event Action? Changed;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_registration != IntPtr.Zero)
        {
            _ = CM_Unregister_Notification(_registration);
            _registration = IntPtr.Zero;
        }

        _settle.Dispose();
        if (_self.IsAllocated)
        {
            _self.Free();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint OnNotify(IntPtr notification, IntPtr context, int action, IntPtr data, uint size)
    {
        if (action is ActionArrival or ActionRemoval && GCHandle.FromIntPtr(context).Target is WindowsDeviceWatcher watcher)
        {
            try
            {
                watcher._settle.Change(Settle, Timeout.InfiniteTimeSpan);
            }
            catch (ObjectDisposedException)
            {
                // A notification racing the unregistration.
            }
        }

        return 0;
    }

    private const uint AllInterfaceClasses = 0x1;      // CM_NOTIFY_FILTER_FLAG_ALL_INTERFACE_CLASSES
    private const int FilterTypeDeviceInterface = 0;    // CM_NOTIFY_FILTER_TYPE_DEVICEINTERFACE
    private const int ActionArrival = 0;                // CM_NOTIFY_ACTION_DEVICEINTERFACEARRIVAL
    private const int ActionRemoval = 1;                // CM_NOTIFY_ACTION_DEVICEINTERFACEREMOVAL

    /// <summary>
    /// <c>CM_NOTIFY_FILTER</c>: four DWORDs and a union whose largest member is a
    /// <c>WCHAR[MAX_DEVICE_ID_LEN]</c> (200), 416 bytes in all, and the API checks
    /// <c>cbSize</c> against exactly that.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 416)]
    private struct CmNotifyFilter
    {
        public uint Size;
        public uint Flags;
        public int FilterType;
        public uint Reserved;
        public Guid ClassGuid;
    }

    [LibraryImport("cfgmgr32.dll")]
    private static partial uint CM_Register_Notification(
        CmNotifyFilter* filter, IntPtr context, delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, IntPtr, uint, uint> callback, out IntPtr registration);

    [LibraryImport("cfgmgr32.dll")]
    private static partial uint CM_Unregister_Notification(IntPtr registration);
}
