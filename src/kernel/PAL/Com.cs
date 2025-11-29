// netos mernel - PAL COM Stubs
// Win32-compatible COM API stubs for PAL compatibility.
// COM is not supported in netos, but some NativeAOT code paths call these.

using System.Runtime.InteropServices;
using Kernel.Threading;

namespace Kernel.PAL;

/// <summary>
/// COM initialization flags.
/// </summary>
public static class CoInitFlags
{
    public const uint COINIT_APARTMENTTHREADED = 0x2;
    public const uint COINIT_MULTITHREADED = 0x0;
    public const uint COINIT_DISABLE_OLE1DDE = 0x4;
    public const uint COINIT_SPEED_OVER_MEMORY = 0x8;
}

/// <summary>
/// COM wait flags.
/// </summary>
public static class CoWaitFlags
{
    public const uint COWAIT_DEFAULT = 0x00000000;
    public const uint COWAIT_WAITALL = 0x00000001;
    public const uint COWAIT_ALERTABLE = 0x00000002;
    public const uint COWAIT_INPUTAVAILABLE = 0x00000004;
    public const uint COWAIT_DISPATCH_CALLS = 0x00000008;
    public const uint COWAIT_DISPATCH_WINDOW_MESSAGES = 0x00000010;
}

/// <summary>
/// HRESULT values.
/// </summary>
public static class HResult
{
    public const int S_OK = 0;
    public const int S_FALSE = 1;
    public const int E_NOTIMPL = unchecked((int)0x80004001);
    public const int E_FAIL = unchecked((int)0x80004005);
    public const int E_INVALIDARG = unchecked((int)0x80070057);
    public const int RPC_E_CHANGED_MODE = unchecked((int)0x80010106);
}

/// <summary>
/// PAL COM APIs - Stubs for COM functionality.
/// COM is not supported in netos, but these stubs allow code that calls
/// these APIs to continue without crashing.
/// </summary>
public static unsafe class Com
{
    private static bool _comInitialized;
    private static uint _comFlags;

    /// <summary>
    /// Initialize COM for the current thread.
    /// In netos, this is a no-op that always succeeds.
    /// </summary>
    /// <param name="pvReserved">Reserved, must be null</param>
    /// <param name="dwCoInit">Initialization flags</param>
    /// <returns>S_OK on success, S_FALSE if already initialized</returns>
    public static int CoInitializeEx(void* pvReserved, uint dwCoInit)
    {
        if (_comInitialized)
        {
            // Already initialized - check for mode change
            if ((dwCoInit & CoInitFlags.COINIT_APARTMENTTHREADED) !=
                (_comFlags & CoInitFlags.COINIT_APARTMENTTHREADED))
            {
                return HResult.RPC_E_CHANGED_MODE;
            }
            return HResult.S_FALSE;
        }

        _comInitialized = true;
        _comFlags = dwCoInit;
        return HResult.S_OK;
    }

    /// <summary>
    /// Uninitialize COM for the current thread.
    /// </summary>
    public static void CoUninitialize()
    {
        _comInitialized = false;
        _comFlags = 0;
    }

    /// <summary>
    /// Wait for handles with COM message pumping.
    /// In netos, this maps directly to WaitForMultipleObjectsEx.
    /// </summary>
    /// <param name="dwFlags">Wait flags</param>
    /// <param name="dwTimeout">Timeout in milliseconds</param>
    /// <param name="cHandles">Number of handles</param>
    /// <param name="pHandles">Array of handles to wait on</param>
    /// <param name="lpdwindex">Receives index of signaled handle</param>
    /// <returns>S_OK on success, other HRESULT on failure</returns>
    public static int CoWaitForMultipleHandles(
        uint dwFlags,
        uint dwTimeout,
        uint cHandles,
        void** pHandles,
        uint* lpdwindex)
    {
        if (pHandles == null || lpdwindex == null || cHandles == 0)
            return HResult.E_INVALIDARG;

        // Map CoWait flags to WaitForMultipleObjectsEx parameters
        bool waitAll = (dwFlags & CoWaitFlags.COWAIT_WAITALL) != 0;
        bool alertable = (dwFlags & CoWaitFlags.COWAIT_ALERTABLE) != 0;

        // Call our sync wait function
        uint result = Sync.WaitForMultipleObjectsEx(cHandles, pHandles, waitAll, dwTimeout, alertable);

        if (result >= WaitResult.Object0 && result < WaitResult.Object0 + cHandles)
        {
            *lpdwindex = result - WaitResult.Object0;
            return HResult.S_OK;
        }

        if (result == WaitResult.Timeout)
        {
            *lpdwindex = 0;
            return 1; // RPC_S_CALLPENDING equivalent - timeout
        }

        if (result == WaitResult.IoCompletion)
        {
            *lpdwindex = 0;
            return HResult.S_OK; // APC delivered
        }

        *lpdwindex = 0;
        return HResult.E_FAIL;
    }

    /// <summary>
    /// Extended version of CoWaitForMultipleHandles.
    /// </summary>
    public static int CoWaitForMultipleObjects(
        uint dwFlags,
        uint dwTimeout,
        uint cHandles,
        void** pHandles,
        uint* lpdwindex)
    {
        // Same implementation as CoWaitForMultipleHandles
        return CoWaitForMultipleHandles(dwFlags, dwTimeout, cHandles, pHandles, lpdwindex);
    }
}
