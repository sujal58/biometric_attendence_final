using System;
using AttendanceBridge.Interop;
using AttendanceBridge.Logging;

namespace AttendanceBridge.Device
{
    /// <summary>Connection parameters for one device (from the API device list).</summary>
    public sealed class DeviceParams
    {
        public int MachineNo = 1;
        public string IpAddress;
        public int NetPort = 5005;
        public int TimeoutMs = 5000;
        public int ProtocolType = 0;   // 0 = TCP/IP, 1 = UDP
        public int NetPassword = 0;
        public int License = 1261;
    }

    /// <summary>
    /// Owns a single network session to one biometric device. The TimeWatch
    /// devices are single-client, so each connection holds one handle.
    ///
    /// All device operations should be wrapped in <see cref="EnableScope"/> so
    /// the device is locked (FK_EnableDevice 0) for the duration of the call
    /// and re-enabled (FK_EnableDevice 1) afterwards, mirroring the vendor
    /// sample (frmLog.cs).
    /// </summary>
    public sealed class DeviceConnection : IDisposable
    {
        private readonly DeviceParams _p;

        public int Handle { get; private set; } = -1;
        public bool IsConnected => Handle > 0;

        public DeviceConnection(DeviceParams p)
        {
            _p = p ?? throw new ArgumentNullException(nameof(p));
        }

        /// <summary>
        /// Opens the TCP/IP session. Returns true on success. On failure the
        /// reason is logged and <see cref="Handle"/> stays &lt;= 0.
        /// </summary>
        public bool Connect()
        {
            Log.Info(string.Format(
                "Connecting to device #{0} at {1}:{2} (protocol={3}, timeout={4}ms)...",
                _p.MachineNo, _p.IpAddress, _p.NetPort,
                (ProtocolType)_p.ProtocolType, _p.TimeoutMs));

            int result;
            try
            {
                result = FkAttend.FK_ConnectNet(
                    _p.MachineNo,
                    _p.IpAddress,
                    _p.NetPort,
                    _p.TimeoutMs,
                    _p.ProtocolType,
                    _p.NetPassword,
                    _p.License);
            }
            catch (DllNotFoundException ex)
            {
                Log.Error("FKAttend.dll (or a sibling native DLL) could not be loaded. " +
                          "Ensure the native DLLs sit next to the exe and the VC++ x86 " +
                          "runtime is installed.", ex);
                return false;
            }
            catch (BadImageFormatException ex)
            {
                Log.Error("FKAttend.dll failed to load - this almost always means the " +
                          "process is running 64-bit. Build/run as x86.", ex);
                return false;
            }

            if (result > 0)
            {
                Handle = result;
                Log.Info("Connected. Session handle = " + Handle);
                return true;
            }

            Handle = -1;
            Log.Error(string.Format(
                "Connection failed (code {0} / {1}). Check the IP/port reachability, the " +
                "comm password, and the license value.", result, Describe(result)));
            return false;
        }

        public void Disconnect()
        {
            if (!IsConnected) return;
            try
            {
                FkAttend.FK_DisConnect(Handle);
                Log.Info("Disconnected from device.");
            }
            catch (Exception ex)
            {
                Log.Warn("Error while disconnecting: " + ex.Message);
            }
            finally
            {
                Handle = -1;
            }
        }

        /// <summary>
        /// Locks the device for an operation and guarantees it is re-enabled
        /// afterwards: using (conn.EnableScope()) { ...device calls... }
        /// </summary>
        public IDisposable EnableScope()
        {
            EnsureConnected();
            FkAttend.FK_EnableDevice(Handle, 0); // 0 = disable UI / take control
            return new Scope(this);
        }

        public void EnsureConnected()
        {
            if (!IsConnected)
                throw new InvalidOperationException("Device is not connected.");
        }

        /// <summary>Maps an FKAttend return code onto a readable label.</summary>
        public static string Describe(int code)
        {
            if (Enum.IsDefined(typeof(FkError), code))
                return ((FkError)code).ToString();
            return "code " + code;
        }

        public void Dispose() => Disconnect();

        private sealed class Scope : IDisposable
        {
            private readonly DeviceConnection _owner;
            public Scope(DeviceConnection owner) { _owner = owner; }
            public void Dispose()
            {
                if (_owner.IsConnected)
                    FkAttend.FK_EnableDevice(_owner.Handle, 1); // 1 = re-enable
            }
        }
    }
}
