// EncryptionService.cs
using System;
using System.Security.Cryptography;
using System.Text;

namespace Protes
{
    public static class EncryptionService
    {
        private const int SALT_SIZE = 16;    // 128 bits
        private const int KEY_SIZE = 32;     // 256 bits
        private const int IV_SIZE = 16;      // 128 bits for AES-CBC
        private const int ITERATIONS = 100_000;

        // ─── KEY DERIVATION ───────────────────────────────────────
        public static byte[] DeriveKey(string password, byte[] salt)
        {
            if (string.IsNullOrEmpty(password) || salt == null || salt.Length == 0)
                throw new ArgumentException("Password and salt must not be null or empty.");
            using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, ITERATIONS))
            {
                return pbkdf2.GetBytes(KEY_SIZE);
            }
        }

        // ─── ENCRYPTION (with pre-derived key) ─────────────────────
        public static (byte[] iv, byte[] encryptedContent, byte[] hmac) EncryptWithKey(string plainText, byte[] key)
        {
            if (string.IsNullOrEmpty(plainText) || key == null || key.Length != KEY_SIZE)
                throw new ArgumentException("Invalid input.");

            var iv = new byte[IV_SIZE];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(iv);

            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            byte[] encrypted;
            using (var aes = Aes.Create())
            {
                aes.Key = key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                using (var encryptor = aes.CreateEncryptor())
                {
                    encrypted = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
                }
            }

            // HMAC over IV + ciphertext
            var data = new byte[iv.Length + encrypted.Length];
            Buffer.BlockCopy(iv, 0, data, 0, iv.Length);
            Buffer.BlockCopy(encrypted, 0, data, iv.Length, encrypted.Length);

            byte[] hmac;
            using (var hmacSha256 = new HMACSHA256(key))
            {
                hmac = hmacSha256.ComputeHash(data);
            }

            return (iv, encrypted, hmac);
        }

        // ─── DECRYPTION (with pre-derived key) ─────────────────────
        public static string DecryptWithKey(byte[] iv, byte[] encryptedContent, byte[] hmac, byte[] key)
        {
            if (iv == null || encryptedContent == null || hmac == null || key == null || key.Length != KEY_SIZE)
                throw new ArgumentException("Invalid input.");

            // Reconstruct data for HMAC
            var data = new byte[iv.Length + encryptedContent.Length];
            Buffer.BlockCopy(iv, 0, data, 0, iv.Length);
            Buffer.BlockCopy(encryptedContent, 0, data, iv.Length, encryptedContent.Length);

            byte[] computedHmac;
            using (var hmacSha256 = new HMACSHA256(key))
            {
                computedHmac = hmacSha256.ComputeHash(data);
            }

            if (!AreEqual(hmac, computedHmac))
                throw new CryptographicException("HMAC verification failed. Data may be corrupted or password is incorrect.");

            using (var aes = Aes.Create())
            {
                aes.Key = key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                using (var decryptor = aes.CreateDecryptor())
                {
                    var plainBytes = decryptor.TransformFinalBlock(encryptedContent, 0, encryptedContent.Length);
                    return Encoding.UTF8.GetString(plainBytes);
                }
            }
        }

        // ─── SAFE BYTE ARRAY COMPARISON (constant-time-ish) ───────
        private static bool AreEqual(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                    return false;
            }
            return true;
        }
    }
}