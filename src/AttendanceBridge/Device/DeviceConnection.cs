using System;
using AttendanceBridge.Config;
using AttendanceBridge.Interop;
using AttendanceBridge.Logging;

namespace AttendanceBridge.Device
{
    /// <summary>
    /// Owns a single network session to the biometric device. The TimeWatch
    /// devices are effectively single-client, so the whole application shares
    /// one connection and one handle.
    ///
    /// All device operations should be wrapped in <see cref="EnableScope"/> so
    /// the device is locked (FK_EnableDevice 0) for the duration of the call
    /// and re-enabled (FK_EnableDevice 1) afterwards, mirroring the vendor
    /// sample (frmLog.cs).
    /// </summary>
    public sealed class DeviceConnection : IDisposable
    {
        private readonly DeviceConfig _cfg;

        public int Handle { get; private set; } = -1;
        public bool IsConnected => Handle > 0;

        public DeviceConnection(DeviceConfig cfg)
        {
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
        }

        /// <summary>
        /// Opens the TCP/IP session. Returns true on success. On failure the
        /// reason is logged and <see cref="Handle"/> stays &lt;= 0.
        /// </summary>
        public bool Connect()
        {
            Log.Info(string.Format(
                "Connecting to device #{0} at {1}:{2} (protocol={3}, timeout={4}ms)...",
                _cfg.machineNo, _cfg.ipAddress, _cfg.netPort,
                (ProtocolType)_cfg.protocolType, _cfg.timeoutMs));

            int result;
            try
            {
                result = FkAttend.FK_ConnectNet(
                    _cfg.machineNo,
                    _cfg.ipAddress,
                    _cfg.netPort,
                    _cfg.timeoutMs,
                    _cfg.protocolType,
                    _cfg.netPassword,
                    _cfg.license);
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
