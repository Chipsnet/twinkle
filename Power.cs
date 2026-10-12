using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Twinkle;

static partial class NativeMethods
{
    public const uint POWER_REQUEST_CONTEXT_VERSION = 0;
    public const uint POWER_REQUEST_CONTEXT_SIMPLE_STRING = 0x1;

    public enum PowerRequestType { DisplayRequired = 0, SystemRequired = 1 }

    [StructLayout(LayoutKind.Sequential)]
    public struct ReasonContext
    {
        public uint Version;
        public uint Flags;
        public IntPtr SimpleReasonString;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial IntPtr PowerCreateRequest(ref ReasonContext context);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PowerSetRequest(IntPtr handle, PowerRequestType type);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PowerClearRequest(IntPtr handle, PowerRequestType type);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(IntPtr handle);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(IntPtr icon);

    [LibraryImport("kernel32.dll")]
    public static partial IntPtr LocalFree(IntPtr mem);

    // powrprof の関数は成功時に 0 (ERROR_SUCCESS) を返し、失敗時は Win32 エラーコードを返す
    [LibraryImport("powrprof.dll")]
    public static partial uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [LibraryImport("powrprof.dll")]
    public static partial uint PowerReadACValueIndex(IntPtr rootPowerKey, in Guid scheme, in Guid subGroup, in Guid setting, out uint value);

    [LibraryImport("powrprof.dll")]
    public static partial uint PowerReadDCValueIndex(IntPtr rootPowerKey, in Guid scheme, in Guid subGroup, in Guid setting, out uint value);

    [LibraryImport("powrprof.dll")]
    public static partial uint PowerWriteACValueIndex(IntPtr rootPowerKey, in Guid scheme, in Guid subGroup, in Guid setting, uint value);

    [LibraryImport("powrprof.dll")]
    public static partial uint PowerWriteDCValueIndex(IntPtr rootPowerKey, in Guid scheme, in Guid subGroup, in Guid setting, uint value);

    [LibraryImport("powrprof.dll")]
    public static partial uint PowerSetActiveScheme(IntPtr userRootPowerKey, in Guid scheme);

    public static void Check(uint err)
    {
        if (err != 0)
        {
            throw new Win32Exception((int)err);
        }
    }
}

/// <summary>カバーを閉じたときの操作 (SUB_BUTTONS / LIDACTION) の読み書き。</summary>
static class Lid
{
    static readonly Guid SubButtons = new("4f971e89-eebd-4455-a8de-9e59040e7347");
    static readonly Guid LidAction = new("5ca83367-6e45-459f-a27b-476b1d01c936");

    public const uint DoNothing = 0;
    public const uint Sleep = 1;

    public static Guid ActiveScheme()
    {
        NativeMethods.Check(NativeMethods.PowerGetActiveScheme(IntPtr.Zero, out var p));
        try
        {
            return Marshal.PtrToStructure<Guid>(p);
        }
        finally
        {
            NativeMethods.LocalFree(p);
        }
    }

    public static uint Read(Guid scheme, bool ac)
    {
        uint value;
        var err = ac
            ? NativeMethods.PowerReadACValueIndex(IntPtr.Zero, scheme, SubButtons, LidAction, out value)
            : NativeMethods.PowerReadDCValueIndex(IntPtr.Zero, scheme, SubButtons, LidAction, out value);
        NativeMethods.Check(err);
        return value;
    }

    public static void Write(Guid scheme, bool ac, uint value)
    {
        var err = ac
            ? NativeMethods.PowerWriteACValueIndex(IntPtr.Zero, scheme, SubButtons, LidAction, value)
            : NativeMethods.PowerWriteDCValueIndex(IntPtr.Zero, scheme, SubButtons, LidAction, value);
        NativeMethods.Check(err);
    }

    /// <summary>書き込んだ値を反映させる。</summary>
    public static void Activate()
    {
        NativeMethods.Check(NativeMethods.PowerSetActiveScheme(IntPtr.Zero, ActiveScheme()));
    }
}

/// <summary>SetThreadExecutionState の代わりに PowerRequest でスリープを防止する。</summary>
sealed class Awake : IDisposable
{
    readonly IntPtr _handle;
    readonly IntPtr _reason;
    bool _system;
    bool _display;
    bool _disposed;

    public Awake()
    {
        _reason = Marshal.StringToHGlobalUni("twinkle keep awake");
        var context = new NativeMethods.ReasonContext
        {
            Version = NativeMethods.POWER_REQUEST_CONTEXT_VERSION,
            Flags = NativeMethods.POWER_REQUEST_CONTEXT_SIMPLE_STRING,
            SimpleReasonString = _reason,
        };
        _handle = NativeMethods.PowerCreateRequest(ref context);
        if (_handle == IntPtr.Zero || _handle == new IntPtr(-1))
        {
            Marshal.FreeHGlobal(_reason);
            throw new Win32Exception();
        }
    }

    public void Set(bool system, bool display)
    {
        if (system != _system)
        {
            SetOne(NativeMethods.PowerRequestType.SystemRequired, system);
            _system = system;
        }
        if (display != _display)
        {
            SetOne(NativeMethods.PowerRequestType.DisplayRequired, display);
            _display = display;
        }
    }

    void SetOne(NativeMethods.PowerRequestType type, bool on)
    {
        var ok = on
            ? NativeMethods.PowerSetRequest(_handle, type)
            : NativeMethods.PowerClearRequest(_handle, type);
        if (!ok)
        {
            throw new Win32Exception();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        Set(false, false);
        NativeMethods.CloseHandle(_handle);
        Marshal.FreeHGlobal(_reason);
    }
}
