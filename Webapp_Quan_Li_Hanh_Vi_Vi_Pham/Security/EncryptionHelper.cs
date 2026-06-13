using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Security;

public static class EncryptionHelper
{
    private static readonly byte[] Salt = Encoding.UTF8.GetBytes("ViolationSystemSalt2026");

    public static string Encrypt(string plainText, string key)
    {
        if (string.IsNullOrEmpty(plainText))
            return plainText;

        try
        {
            using var aes = Aes.Create();
            var keyBytes = GenerateKeyBytes(key);
            aes.Key = keyBytes;
            aes.GenerateIV(); // Tạo IV ngẫu nhiên

            using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
            using var ms = new MemoryStream();
            
            // Ghi IV vào đầu stream để dùng cho việc giải mã
            ms.Write(aes.IV, 0, aes.IV.Length);

            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            using (var sw = new StreamWriter(cs))
            {
                sw.Write(plainText);
            }

            return Convert.ToBase64String(ms.ToArray());
        }
        catch (Exception)
        {
            return plainText;
        }
    }

    public static string Decrypt(string cipherText, string key)
    {
        if (string.IsNullOrEmpty(cipherText))
            return cipherText;

        try
        {
            var fullCipher = Convert.FromBase64String(cipherText);

            using var aes = Aes.Create();
            var keyBytes = GenerateKeyBytes(key);
            aes.Key = keyBytes;

            // Đọc IV từ 16 byte đầu tiên
            var iv = new byte[aes.BlockSize / 8];
            if (fullCipher.Length < iv.Length)
            {
                // Chuỗi Base64 hợp lệ nhưng quá ngắn để chứa IV => Không phải định dạng mã hóa của chúng ta
                return cipherText;
            }

            Array.Copy(fullCipher, 0, iv, 0, iv.Length);
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
            using var ms = new MemoryStream(fullCipher, iv.Length, fullCipher.Length - iv.Length);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var sr = new StreamReader(cs);
            
            return sr.ReadToEnd();
        }
        catch (FormatException)
        {
            // Nếu không phải định dạng Base64 hợp lệ (tức là văn bản chưa mã hóa từ trước)
            return cipherText;
        }
        catch (CryptographicException)
        {
            // Lỗi giải mã (sai key, sai định dạng)
            return cipherText;
        }
        catch (Exception)
        {
            return cipherText;
        }
    }

    private static byte[] GenerateKeyBytes(string key)
    {
        using var rfc2898 = new Rfc2898DeriveBytes(key, Salt, 10000, HashAlgorithmName.SHA256);
        return rfc2898.GetBytes(32); // 256 bit key for AES
    }
}
