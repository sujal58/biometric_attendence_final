using System;
using System.Runtime.InteropServices;

namespace AttendanceBridge.Interop
{
    /// <summary>
    /// Raw P/Invoke bindings for the TimeWatch FKAttend.dll native SDK.
    ///
    /// These signatures are copied verbatim from the vendor sample
    /// (D:\SDK_bio\frmMain.cs, the "FKAttend dll APIs" block) so that the
    /// marshalling behaviour is identical to the reference implementation.
    /// In particular FK_GetDeviceTime/FK_SetDeviceTime pass System.DateTime
    /// the same way the sample does, and FK_GetProductData returns an
    /// ANSI string via [MarshalAs(LPStr)] ref string.
    ///
    /// IMPORTANT: FKAttend.dll and its sibling native DLLs are 32-bit (PE32).
    /// The hosting process MUST run as x86 or every call here throws
    /// BadImageFormatException. See <see cref="Bitness"/>.
    ///
    /// This is the load-bearing subset needed for Phase 1 (connect, device
    /// info/status, product data, time). Log-download imports are added in
    /// Phase 2.
    /// </summary>
    internal static class FkAttend
    {
        private const string Dll = "FKAttend.dll";

        // ---- Connection -------------------------------------------------

        [DllImport(Dll, CharSet = CharSet.Ansi)]
        public static extern int FK_ConnectNet(
            int anMachineNo,
            string astrIpAddress,
            int anNetPort,
            int anTimeOut,
            int anProtocolType,
            int anNetPassword,
            int anLicense);

        [DllImport(Dll, CharSet = CharSet.Ansi)]
        public static extern int FK_ConnectUSB(int anMachineNo, int anLicense);

        [DllImport(Dll, CharSet = CharSet.Ansi)]
        public static extern void FK_DisConnect(int anHandleIndex);

        // ---- Error processing -------------------------------------------

        [DllImport(Dll, CharSet = CharSet.Ansi)]
        public static extern int FK_GetLastError(int anHandleIndex);

        // ---- Device setting ---------------------------------------------

        [DllImport(Dll, CharSet = CharSet.Ansi)]
        public static extern int FK_EnableDevice(int anHandleIndex, byte anEnableFlag);

        [DllImport(Dll, CharSet = CharSet.Ansi)]
        public static extern int FK_PowerOffDevice(int anHandleIndex);

        [DllImport(Dll, CharSet = CharSet.Ansi)]
        public static extern int FK_GetDeviceStatus(
            int anHandleIndex,
            int anStatusIndex,
            ref int apnValue);

        [DllImport(Dll, CharSet = CharSet.Ansi)]
        public static extern int FK_GetDeviceTime(
            int anHandleIndex,
            ref DateTime apnDateTime);

        [DllImport(Dll, CharSet = CharSet.Ansi)]
        public static extern int FK_SetDeviceTime(
            int anHandleIndex,
            DateTime anDateTime);

        [DllImport(Dll, CharSet = CharSet.Ansi)]
        public static extern int FK_GetDeviceInfo(
            int anHandleIndex,
            int anInfoIndex,
            ref int apnValue);

        [DllImport(Dll, CharSet = CharSet.Ansi)]
        public static extern int FK_SetDeviceInfo(
            int anHandleIndex,
            int anInfoIndex,
            int anValue);

        [DllImport(Dll, CharSet = CharSet.Ansi)]
        public static extern int FK_GetProductData(
            int anHandleIndex,
            int anDataIndex,
            [MarshalAs(UnmanagedType.LPStr)] ref string apstrValue);

        // ---- Attendance (general) log -----------------------------------
        // Usage: FK_LoadGeneralLogData loads the dataset into the SDK, then
        // FK_GetTemperatureLogData / FK_GetGeneralLogData are called in a loop
        // until they return RUNERR_DATAARRAY_END (-7). See frmLog.cs:911-952.

        [DllImport(Dll, CharSet = CharSet.Ansi)]
        public static extern int FK_LoadGeneralLogData(int anHandleIndex, int anReadMark);

        [DllImport(Dll, CharSet = CharSet.Ansi)]
        public static extern int FK_GetGeneralLogData(
            int anHandleIndex,
            ref int apnEnrollNumber,
            ref int apnVerifyMode,
            ref int apnInOutMode,
            ref DateTime apnDateTime);

        [DllImport(Dll, CharSet = CharSet.Ansi)]
        public static extern int FK_GetTemperatureLogData(
            int anHandleIndex,
            ref int apnEnrollNumber,
            ref int apnVerifyMode,
            ref int apnInOutMode,
            ref DateTime apnDateTime,
            ref int apnTemperature);

        [DllImport(Dll, CharSet = CharSet.Ansi)]
        public static extern int FK_EmptyGeneralLogData(int anHandleIndex);
    }
}
