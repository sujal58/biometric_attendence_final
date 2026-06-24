using System;
using System.Security.Cryptography;

namespace AttendanceDesktop.Security
{
    /// <summary>PBKDF2 (SHA-256) password hashing for the Settings admin lock.</summary>
    public static class PasswordHasher
    {
        private const int Iterations = 100_000;
        private const int SaltLen = 16;
        private const int HashLen = 32;

        public static (string salt, string hash) Hash(string password)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(SaltLen);
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password ?? "", salt, Iterations, HashAlgorithmName.SHA256, HashLen);
            return (Convert.ToBase64String(salt), Convert.ToBase64String(hash));
        }

        public static bool Verify(string password, string saltB64, string hashB64)
        {
            if (string.IsNullOrEmpty(saltB64) || string.IsNullOrEmpty(hashB64)) return false;
            try
            {
                byte[] salt = Convert.FromBase64String(saltB64);
                byte[] expected = Convert.FromBase64String(hashB64);
                byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password ?? "", salt, Iterations, HashAlgorithmName.SHA256, expected.Length);
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch { return false; }
        }
    }
}
