using LenovoLegionToolkit.Lib.Utils;
using System;
using System.Runtime.InteropServices;

namespace LenovoLegionToolkit.Lib.System;
public static class AirplaneMode
{
    private static readonly Guid RadioManagerClsid = new("581333F6-28DB-41BE-BC7A-FF201F12F3F6");

    [ComImport, Guid("DB3AFBFB-08E6-46C6-AA70-BF9A34C30AB7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IRadioManager
    {
        [PreserveSig] int VtblSlot3();
        [PreserveSig] int VtblSlot4();
        [PreserveSig] int GetSystemRadioState(out int state, out int arg2, out int arg3);
        [PreserveSig] int SetSystemRadioState(int state);
    }

    public static void Toggle()
    {
        var comType = Type.GetTypeFromCLSID(RadioManagerClsid);
        if (comType == null)
        {
            Log.Instance.Trace($"Failed to get COM type for IRadioManager.");
            return;
        }

        var radioManager = (IRadioManager)Activator.CreateInstance(comType)!;

        try
        {
            radioManager.GetSystemRadioState(out var state, out _, out _);
            radioManager.SetSystemRadioState(state == 0 ? 1 : 0);
        }
        finally
        {
            if (radioManager != null)
            {
                Marshal.ReleaseComObject(radioManager);
            }
        }
    }
}