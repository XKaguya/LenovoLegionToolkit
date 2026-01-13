using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Win32;
using Windows.Win32.Devices.Display;
using Windows.Win32.Foundation;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Utils;
using WindowsDisplayAPI;
using WindowsDisplayAPI.DisplayConfig;

namespace LenovoLegionToolkit.Lib.System;

public static class InternalDisplay
{
    private readonly struct DisplayHolder
    {
        public static readonly DisplayHolder Empty = new();
        private readonly Display? _display;
        private DisplayHolder(Display? display) => _display = display;
        public static implicit operator DisplayHolder(Display? s) => new(s);
        public static implicit operator Display?(DisplayHolder s) => s._display;
    }

    private static readonly SemaphoreSlim Semaphore = new(1, 1);
    private static DisplayHolder? _displayHolder;

    public static void SetNeedsRefresh()
    {
        _displayHolder = null;
        Log.Instance.Trace($"Resetting holder...");
    }

    public static async Task<Display?> GetAsync()
    {
        if (_displayHolder is not null)
            return _displayHolder;

        await Semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_displayHolder is not null)
                return _displayHolder;

            var result = await FindInternalDisplayLogicAsync().ConfigureAwait(false);

            _displayHolder = result;
            return result;
        }
        finally
        {
            Semaphore.Release();
        }
    }

    private static async Task<DisplayHolder> FindInternalDisplayLogicAsync()
    {
        var displays = await Task.Run(() => Display.GetDisplays().ToArray()).ConfigureAwait(false);

        var internalDisplay = FindInternalDisplay(displays);
        if (internalDisplay is not null)
        {
            Log.Instance.Trace($"Found internal display: {internalDisplay.DevicePath}");
            return internalDisplay;
        }

        var aoDisplay = await FindInternalAdvancedOptimusDisplayAsync(displays).ConfigureAwait(false);
        if (aoDisplay is not null)
        {
            Log.Instance.Trace($"Found internal AO display: {aoDisplay.DevicePath}");
            return aoDisplay;
        }

        Log.Instance.Trace($"No internal displays found.");
        return DisplayHolder.Empty;
    }

    public static Display? Get()
    {
        if (_displayHolder is not null) return _displayHolder;
        return Task.Run(async () => await GetAsync().ConfigureAwait(false)).Result;
    }

    private static Display? FindInternalDisplay(IEnumerable<Display> displays)
    {
        return displays.FirstOrDefault(d => d.GetVideoOutputTechnology().IsInternalOutput());
    }

    private static async Task<Display?> FindInternalAdvancedOptimusDisplayAsync(IEnumerable<Display> displays)
    {
        var exDpDisplays = displays.Where(di => di.GetVideoOutputTechnology().IsExternalDisplayPortOutput()).ToArray();

        if (exDpDisplays.Length < 1)
            return null;

        var exDpDisplay = exDpDisplays[0];
        var exDpPathDisplayTarget = exDpDisplay.ToPathDisplayTarget();
        var exDpPortDisplayEdid = exDpPathDisplayTarget.EDIDManufactureId;

        var otherAdapters = DisplayAdapter.GetDisplayAdapters()
            .Where(da => da.DevicePath != exDpDisplay.Adapter.DevicePath)
            .ToArray();

        var queryTasks = otherAdapters.Select(adapter => Task.Run(() =>
        {
            try
            {
                return adapter.GetDisplayDevices();
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Failed to query adapter {adapter.DevicePath}", ex);
                return [];
            }
        }));

        var allDevicesResults = await Task.WhenAll(queryTasks).ConfigureAwait(false);

        var sameDeviceIsOnAnotherAdapter = allDevicesResults
            .SelectMany(devices => devices)
            .Select(dd => dd.ToPathDisplayTarget())
            .Any(pdt => pdt.EDIDManufactureId == exDpPortDisplayEdid && pdt.GetVideoOutputTechnology().IsInternalOutput());

        return sameDeviceIsOnAnotherAdapter ? exDpDisplay : null;
    }

    private static DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY GetVideoOutputTechnology(this DisplayDevice displayDevice)
    {
        return GetVideoOutputTechnology(displayDevice.ToPathDisplayTarget());
    }

    private static unsafe DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY GetVideoOutputTechnology(this PathDisplayTarget pathDisplayTarget)
    {
        var intPtr = IntPtr.Zero;
        try
        {
            var deviceName = new DISPLAYCONFIG_TARGET_DEVICE_NAME
            {
                header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                {
                    type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                    id = pathDisplayTarget.TargetId,
                    adapterId = new LUID
                    {
                        HighPart = pathDisplayTarget.Adapter.AdapterId.HighPart,
                        LowPart = pathDisplayTarget.Adapter.AdapterId.LowPart,
                    },
                    size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>()
                }
            };

            intPtr = Marshal.AllocHGlobal((int)deviceName.header.size);
            Marshal.StructureToPtr(deviceName, intPtr, false);

            var success = PInvoke.DisplayConfigGetDeviceInfo((DISPLAYCONFIG_DEVICE_INFO_HEADER*)intPtr.ToPointer());
            if (success != PInvokeExtensions.ERROR_SUCCESS)
                PInvokeExtensions.ThrowIfWin32Error("DisplayConfigGetDeviceInfo");

            var deviceNameResponse = Marshal.PtrToStructure<DISPLAYCONFIG_TARGET_DEVICE_NAME>(intPtr);
            return deviceNameResponse.outputTechnology;
        }
        catch
        {
            return DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_OTHER;
        }
        finally
        {
            Marshal.FreeHGlobal(intPtr);
        }
    }

    private static bool IsInternalOutput(this DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY outputTechnology)
    {
        var result = outputTechnology is DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL;
        result |= outputTechnology is DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED;
        return result;
    }

    private static bool IsExternalDisplayPortOutput(this DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY outputTechnology)
    {
        return outputTechnology is DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EXTERNAL;
    }
}