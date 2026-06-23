using System;

namespace AttendanceBridge.Interop
{
    // Enumerations copied from the vendor sample (D:\SDK_bio\clasFunction.cs).
    // Names/values are kept identical to the SDK so they can be cross-checked
    // against the vendor documentation.

    /// <summary>Transport passed to FK_ConnectNet (anProtocolType).</summary>
    public enum ProtocolType
    {
        TcpIp = 0,
        Udp = 1,
    }

    /// <summary>Return / error codes shared by every FKAttend call.</summary>
    public enum FkError
    {
        Success = 1,            // RUN_SUCCESS
        NoSupport = 0,          // RUNERR_NOSUPPORT (feature absent on this firmware)
        UnknownError = -1,
        NoOpenComm = -2,
        WriteFail = -3,
        ReadFail = -4,
        InvalidParam = -5,
        NonCarryout = -6,
        DataArrayEnd = -7,      // RUNERR_DATAARRAY_END (end of a data loop)
        DataArrayNone = -8,
        Memory = -9,
        MisPassword = -10,      // wrong comm password / license
        MemoryOver = -11,
        DataDouble = -12,
        ManagerOver = -14,
        FpDataVersion = -15,
    }

    /// <summary>Index passed to FK_GetDeviceStatus (record counters).</summary>
    public enum DeviceStatusIndex
    {
        Managers = 1,
        Users = 2,
        Fingerprints = 3,
        Passwords = 4,
        SuperLogs = 5,
        GeneralLogs = 6,
        AvailableSuperLogs = 7,
        AvailableGeneralLogs = 8,
        Cards = 9,
        AllCount = 10,
        Faces = 11,
        Photos = 12,
    }

    /// <summary>Index passed to FK_GetDeviceInfo / FK_SetDeviceInfo.</summary>
    public enum DeviceInfoIndex
    {
        Managers = 1,
        MachineNumber = 2,
        Language = 3,
        PowerOffTime = 4,
        LockControl = 5,
        GeneralLogWarning = 6,
        SuperLogWarning = 7,
        VerifyIntervals = 8,
        RsComBaudrate = 9,
        DateSeparate = 10,
        NetEnable = 14,
        VerifyKind = 24,
        VerifyFunc = 25,
        MacAddr0 = 42,          // 6 consecutive indices (42..47) hold the MAC bytes
        AlarmDelay = 66,
        SensorDelay = 67,
        MultiUsers = 77,
    }

    /// <summary>Index passed to FK_GetProductData.</summary>
    public enum ProductInfoIndex
    {
        SerialNumber = 1,
        BackupNumber = 2,
        Code = 3,
        Name = 4,
        Web = 5,
        Date = 6,
        SendTo = 7,
    }

    /// <summary>Verify method recorded against each punch (general log).</summary>
    public enum VerifyMode
    {
        Fingerprint = 1,
        Password = 2,
        Card = 3,
        PasswordFp = 4,
        CardFp = 5,
        FpPassword = 6,
        FpCard = 7,
        JobNumber = 8,
        CardPassword = 9,
        Face = 20,
        FaceCard = 21,
        FacePassword = 22,
        CardFace = 23,
        PasswordFace = 24,
        FaceFp = 25,
    }

    /// <summary>In/out direction recorded against each punch (general log).</summary>
    public enum IoMode
    {
        General = 0,
        In1 = 1,
        In2 = 2,
        In3 = 3,
        Out1 = 4,
        Out2 = 5,
        Out3 = 6,
    }
}
