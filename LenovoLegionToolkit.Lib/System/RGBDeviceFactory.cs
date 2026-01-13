using System;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using NeoSmart.AsyncLock;

namespace LenovoLegionToolkit.Lib.System;

public class RGBDeviceFactory : IDisposable
{
    private readonly AsyncLock _lock = new();
    private SafeFileHandle? _cachedHandle;

    public bool ForceDisable { get; set; }

    public void Dispose()
    {
        _cachedHandle?.Dispose();
        _cachedHandle = null;
    }

    public async Task<SafeFileHandle?> GetHandleAsync()
    {
        if (ForceDisable) return null;

        if (_cachedHandle is not null && !_cachedHandle.IsInvalid && !_cachedHandle.IsClosed)
        {
            return _cachedHandle;
        }

        using (await _lock.LockAsync().ConfigureAwait(false))
        {
            if (_cachedHandle is not null && !_cachedHandle.IsInvalid && !_cachedHandle.IsClosed)
            {
                return _cachedHandle;
            }

            _cachedHandle?.Dispose();
            _cachedHandle = null;

            _cachedHandle = await Task.Run(() =>
            {
                var handle = Devices.GetRGBKeyboard(false);
                if (handle is { IsInvalid: false, IsClosed: false })
                {
                    return handle;
                }

                handle = Devices.GetRGBKeyboard(true);
                return handle is { IsInvalid: false, IsClosed: false } ? handle : null;
            }).ConfigureAwait(false);

            return _cachedHandle;
        }
    }
}