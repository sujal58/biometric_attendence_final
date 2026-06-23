using System;
using System.Collections.Generic;

namespace AttendanceBridge.Interop
{
    /// <summary>
    /// Decodes the bit-packed verify / in-out fields returned by the general
    /// log. Ports the vendor sample logic (frmMain.GetStringVerifyMode and
    /// GetIoModeAndDoorMode):
    ///
    ///  - Verify: values &lt; 256 are the legacy small codes (see VerifyMode);
    ///    larger values are nibble-packed verification "kinds" (FP/FACE/CARD...),
    ///    e.g. 0x10000000 = FP.
    ///  - In/out: the low byte is the in/out mode; the upper bytes are the door
    ///    mode, e.g. 0x0911 = io 0x11, door 0x09.
    /// </summary>
    public static class FkLogDecoder
    {
        public static string VerifyLabel(int raw)
        {
            if (raw >= 0 && raw < 256)
            {
                return Enum.IsDefined(typeof(VerifyMode), raw)
                    ? ((VerifyMode)raw).ToString()
                    : raw.ToString();
            }

            // Nibble-packed: walk the 4 bytes high->low, each nibble is a kind.
            byte[] bytes = BitConverter.GetBytes(raw);
            var parts = new List<string>();
            for (int i = bytes.Length - 1; i >= 0; i--)
            {
                int first = (bytes[i] & 0xF0) >> 4;
                int second = bytes[i] & 0x0F;
                if (first == 0) break;
                parts.Add(KindName(first));
                if (second == 0) break;
                parts.Add(KindName(second));
            }
            return parts.Count > 0 ? string.Join("+", parts) : raw.ToString();
        }

        public static void DecodeIo(int raw, out int ioMode, out int doorMode)
        {
            ioMode = raw & 0xFF;
            doorMode = (raw >> 8) & 0xFFFFFF;
        }

        private static string KindName(int kind)
        {
            switch (kind)
            {
                case (int)VerifyKind.Fp: return "FP";
                case (int)VerifyKind.Pass: return "PASS";
                case (int)VerifyKind.Card: return "CARD";
                case (int)VerifyKind.Face: return "FACE";
                case (int)VerifyKind.FingerVein: return "FINGERVEIN";
                case (int)VerifyKind.Iris: return "IRIS";
                case (int)VerifyKind.PalmVein: return "PALMVEIN";
                case (int)VerifyKind.Voice: return "VOICE";
                case (int)VerifyKind.VFace: return "VFACE";
                default: return "K" + kind;
            }
        }
    }

    /// <summary>Verification kinds used by the nibble-packed verify code (clasFunction.cs enumVerifyKind).</summary>
    public enum VerifyKind
    {
        None = 0,
        Fp = 1,
        Pass = 2,
        Card = 3,
        Face = 4,
        FingerVein = 5,
        Iris = 6,
        PalmVein = 7,
        Voice = 8,
        VFace = 9,
    }
}
