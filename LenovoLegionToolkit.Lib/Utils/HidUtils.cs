using System;
using System.Runtime.InteropServices;
using System.Threading;
using Windows.Win32;

namespace LenovoLegionToolkit.Lib.Utils;

public static class HidUtils
{
    private static readonly Lock IoLock = new();

    public static unsafe bool SetFeature<T>(SafeHandle handle, T data) where T : notnull
    {
        lock (IoLock)
        {
            var ptr = IntPtr.Zero;
            try
            {
                int size;
                if (data is byte[] bytes)
                {
                    size = bytes.Length;
                    ptr = Marshal.AllocHGlobal(size);
                    Marshal.Copy(bytes, 0, ptr, size);
                }
                else
                {
                    size = Marshal.SizeOf<T>();
                    ptr = Marshal.AllocHGlobal(size);
                    Marshal.StructureToPtr(data, ptr, false);
                }

                return PInvoke.HidD_SetFeature(handle, ptr.ToPointer(), (uint)size);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
    }

    public static unsafe bool GetFeature<T>(SafeHandle handle, out T result) where T : struct
    {
        lock (IoLock)
        {
            result = default;
            var ptr = IntPtr.Zero;
            try
            {
                var size = Marshal.SizeOf<T>();
                ptr = Marshal.AllocHGlobal(size);
                Marshal.Copy(new byte[] { 7 }, 0, ptr, 1);

                if (!PInvoke.HidD_GetFeature(handle, ptr.ToPointer(), (uint)size))
                    return false;

                result = Marshal.PtrToStructure<T>(ptr);
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
    }

    public static unsafe bool GetFeature(SafeHandle handle, out byte[] bytes, int size)
    {
        lock (IoLock)
        {
            bytes = [];
            var ptr = IntPtr.Zero;
            try
            {
                ptr = Marshal.AllocHGlobal(size);
                Marshal.Copy(new byte[] { 7 }, 0, ptr, 1);

                if (!PInvoke.HidD_GetFeature(handle, ptr.ToPointer(), (uint)size))
                    return false;

                bytes = new byte[size];
                Marshal.Copy(ptr, bytes, 0, size);
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
    }
}