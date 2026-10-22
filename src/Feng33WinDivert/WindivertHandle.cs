using System.Runtime.InteropServices;

namespace Feng33WinDivert;

/// <summary>
/// 一個攔截 handle。開啟後符合過濾條件的封包會被攔下來，
/// 呼叫端必須把每個收到的封包 <see cref="Send"/> 回去，否則它就不會繼續傳遞。
/// </summary>
public sealed class WindivertHandle : IDisposable
{
    /// <summary>單一封包的最大長度（含 IPv6 標頭）。</summary>
    public const int MtuMax = 40 + 0xFFFF;

    private IntPtr _handle;
    private IntPtr _addressBuffer;
    private bool _disposed;

    private WindivertHandle(IntPtr handle)
    {
        _handle = handle;
        _addressBuffer = Marshal.AllocHGlobal(WindivertAddress.Size);
    }

    /// <summary>開啟一個攔截 handle。需要系統管理員權限。</summary>
    /// <param name="filter">WinDivert 過濾語言，例如 <c>"tcp and tcp.DstPort == 443"</c>。</param>
    /// <param name="layer">攔截層級。</param>
    /// <param name="priority">優先順序，越高越先看到封包。</param>
    /// <param name="flags">開啟旗標。</param>
    /// <exception cref="WindivertException">驅動不存在、權限不足，或過濾條件不合法。</exception>
    public static WindivertHandle Open(
        string filter,
        WindivertLayer layer = WindivertLayer.Network,
        short priority = 0,
        WindivertOpenFlags flags = WindivertOpenFlags.None)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filter);

        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("WinDivert 只支援 Windows。");

        IntPtr handle;
        try
        {
            handle = WindivertNative.WinDivertOpen(filter, (int)layer, priority, (ulong)flags);
        }
        catch (DllNotFoundException exception)
        {
            throw new WindivertException(
                WindivertError.DriverMissing,
                WindivertError.Describe(WindivertError.DriverMissing),
                exception);
        }
        catch (BadImageFormatException exception)
        {
            throw new WindivertException(
                WindivertError.ModuleNotFound,
                WindivertError.Describe(WindivertError.ModuleNotFound),
                exception);
        }

        if (handle == WindivertNative.InvalidHandle)
        {
            var error = Marshal.GetLastWin32Error();
            throw new WindivertException(error, WindivertError.Describe(error));
        }

        return new WindivertHandle(handle);
    }

    /// <summary>handle 是否還開著。</summary>
    public bool IsOpen => !_disposed && _handle != WindivertNative.InvalidHandle;

    /// <summary>原生 handle，供進階用途。</summary>
    public IntPtr DangerousHandle => _handle;

    /// <summary>
    /// 阻塞等待下一個符合條件的封包。回傳收到的位元組數；
    /// handle 被關閉時回傳 0。
    /// </summary>
    public unsafe int Receive(Span<byte> packet, out WindivertAddress address)
    {
        if (packet.IsEmpty)
            throw new ArgumentException("緩衝區不能是空的。", nameof(packet));

        if (!IsOpen)
        {
            address = default;
            return 0;
        }

        fixed (byte* buffer = packet)
        {
            if (!WindivertNative.WinDivertRecv(
                    _handle,
                    (IntPtr)buffer,
                    (uint)packet.Length,
                    out var length,
                    _addressBuffer))
            {
                var error = Marshal.GetLastWin32Error();
                address = default;
                if (!IsOpen || error == WindivertError.OperationAborted)
                    return 0;
                throw new WindivertException(error, WindivertError.Describe(error));
            }

            address = WindivertAddress.ReadFrom(_addressBuffer);
            return (int)length;
        }
    }

    /// <summary>把封包注入回去。失敗時回傳 false，不擲例外。</summary>
    public unsafe bool TrySend(ReadOnlySpan<byte> packet, in WindivertAddress address)
    {
        if (!IsOpen || packet.IsEmpty)
            return false;

        fixed (byte* buffer = packet)
        {
            address.WriteTo(_addressBuffer);
            if (WindivertNative.WinDivertSend(
                    _handle,
                    (IntPtr)buffer,
                    (uint)packet.Length,
                    out _,
                    _addressBuffer))
            {
                return true;
            }

            var error = Marshal.GetLastWin32Error();
            if (error == WindivertError.OperationAborted || !IsOpen)
                return false;
            throw new WindivertException(error, WindivertError.Describe(error));
        }
    }

    /// <summary>把封包注入回去，失敗時擲出例外。</summary>
    public void Send(ReadOnlySpan<byte> packet, in WindivertAddress address)
    {
        if (!TrySend(packet, address) && IsOpen)
            throw new WindivertException(0, "封包沒有送出。");
    }

    /// <summary>調整 handle 參數，例如佇列長度。</summary>
    public bool TrySetParam(WindivertParam param, ulong value)
    {
        if (!IsOpen)
            return false;
        return WindivertNative.WinDivertSetParam(_handle, (int)param, value);
    }

    /// <summary>關閉攔截。正在 <see cref="Receive"/> 的執行緒會收到 0 並返回。</summary>
    public void Close()
    {
        if (_disposed)
            return;

        _disposed = true;
        var handle = _handle;
        _handle = WindivertNative.InvalidHandle;
        if (handle != WindivertNative.InvalidHandle)
        {
            try
            {
                WindivertNative.WinDivertClose(handle);
            }
            catch (Exception)
            {
            }
        }
    }

    public void Dispose()
    {
        Close();
        if (_addressBuffer == IntPtr.Zero)
            return;

        // 收封包的執行緒必須先結束，這裡才安全。
        Marshal.FreeHGlobal(_addressBuffer);
        _addressBuffer = IntPtr.Zero;
    }
}
