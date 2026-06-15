using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Helpers
{
    public static class EncryptionHelper
    {
        // Must be exactly 32 bytes for AES-256. 
        private static string Key = "CH_Encryption_Key_2026_Secure123"; 
        private static readonly string Prefix = "ENC:";

        public static void Initialize(string configKey)
        {
            if (!string.IsNullOrWhiteSpace(configKey) && configKey.Length == 32)
            {
                Key = configKey;
            }
        }

        public static string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return plainText;
            if (plainText.StartsWith(Prefix)) return plainText; // Already encrypted

            using var aes = Aes.Create();
            aes.Key = Encoding.UTF8.GetBytes(Key);
            aes.GenerateIV();

            using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
            using var ms = new MemoryStream();
            ms.Write(aes.IV, 0, aes.IV.Length); // Prepend IV
            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            using (var sw = new StreamWriter(cs))
            {
                sw.Write(plainText);
            }

            return Prefix + Convert.ToBase64String(ms.ToArray());
        }

        public static string Decrypt(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText)) return cipherText;
            if (!cipherText.StartsWith(Prefix)) return cipherText; // Not encrypted, return as is (for backwards compatibility)

            try
            {
                var fullCipher = Convert.FromBase64String(cipherText.Substring(Prefix.Length));
                using var aes = Aes.Create();
                aes.Key = Encoding.UTF8.GetBytes(Key);

                var iv = new byte[aes.BlockSize / 8];
                Array.Copy(fullCipher, 0, iv, 0, iv.Length);
                aes.IV = iv;

                using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
                using var ms = new MemoryStream(fullCipher, iv.Length, fullCipher.Length - iv.Length);
                using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
                using var sr = new StreamReader(cs);

                return sr.ReadToEnd();
            }
            catch
            {
                return "[Lỗi giải mã tin nhắn/Message Decryption Error]";
            }
        }
    }
}
