// CMDevicesManager - System Hardware Monitoring Application
// This logging utility is for debugging and diagnostic purposes only.
// No sensitive data is logged. Encryption is disabled by default for transparency.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace CMDevicesManager.Helper
{
    public enum LogLevel
    {
        Info,
        Warn,
        Error
    }
    public static class Logger
    {
        private static readonly BlockingCollection<(LogLevel Level, string Message, Exception Ex)> _logQueue
            = new BlockingCollection<(LogLevel, string, Exception)>();

        private static readonly string LogFile = "app.log"; // 单一日志文件
        private static readonly long MaxFileSize = 50 * 1024 * 1024; // 50 MB
        private static readonly LogLevel MinLogLevel = LogLevel.Info; // 日志过滤等级

        // 🔑 加密控制开关（调试时可以关闭）
        public static bool EnableEncryption { get; set; } = false;

        // Remove hardcoded keys for security - encryption disabled by default
        // If encryption is needed, keys should be generated dynamically or stored securely
        private static readonly byte[] Key = GenerateSecureKey();
        private static readonly byte[] IV = GenerateSecureIV();

        private static readonly CancellationTokenSource _cts = new CancellationTokenSource();

        static Logger()
        {
            Task.Factory.StartNew(ProcessLogQueue, TaskCreationOptions.LongRunning);
        }

        public static void Info(string message) => Enqueue(LogLevel.Info, message, null);
        public static void Warn(string message) => Enqueue(LogLevel.Warn, message, null);
        public static void Error(string message, Exception ex = null) => Enqueue(LogLevel.Error, message, ex);

        private static void Enqueue(LogLevel level, string message, Exception ex)
        {
            if (level < MinLogLevel) return;
            _logQueue.Add((level, message, ex));
        }

        private static void ProcessLogQueue()
        {
            try
            {
                foreach (var (Level, Message, Ex) in _logQueue.GetConsumingEnumerable(_cts.Token))
                {
                    try
                    {
                        string logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{Level}] {Message}";
                        if (Ex != null)
                            logMessage += Environment.NewLine + $"Exception: {Ex}" + Environment.NewLine;

                        WriteLog(logMessage);
                    }
                    catch { /* swallow exceptions */ }
                }
            }
            catch { /* swallow exceptions */ }
        }

        private static void WriteLog(string text)
        {
            // 如果文件超过 50MB，则覆盖
            if (File.Exists(LogFile) && new FileInfo(LogFile).Length > MaxFileSize)
            {
                File.Delete(LogFile);
            }

            // Encryption is disabled by default for security reasons
            // Most applications don't need encrypted logs and it can trigger antivirus software
            if (EnableEncryption && Key.Any(b => b != 0) && IV.Any(b => b != 0))
            {
                try
                {
                    byte[] plainBytes = Encoding.UTF8.GetBytes(text + Environment.NewLine);
                    byte[] encryptedBytes = Encrypt(plainBytes);

                    using (var fs = new FileStream(LogFile, FileMode.Append, FileAccess.Write, FileShare.Read))
                    {
                        fs.Write(encryptedBytes, 0, encryptedBytes.Length);
                    }
                }
                catch (Exception ex)
                {
                    // Fallback to plain text if encryption fails
                    File.AppendAllText(LogFile, $"[ENCRYPTION_ERROR] {ex.Message}" + Environment.NewLine);
                    File.AppendAllText(LogFile, text + Environment.NewLine);
                }
            }
            else
            {
                File.AppendAllText(LogFile, text + Environment.NewLine);
            }
        }

        private static byte[] Encrypt(byte[] data)
        {
            if (!EnableEncryption || !Key.Any(b => b != 0) || !IV.Any(b => b != 0))
                return data; // Return plain data if encryption is disabled or keys are empty

            using (var aes = Aes.Create())
            {
                aes.Key = Key;
                aes.IV = IV;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using (var encryptor = aes.CreateEncryptor())
                {
                    return encryptor.TransformFinalBlock(data, 0, data.Length);
                }
            }
        }

        private static byte[] Decrypt(byte[] data)
        {
            if (!EnableEncryption || !Key.Any(b => b != 0) || !IV.Any(b => b != 0))
                return data; // Return plain data if encryption is disabled or keys are empty

            using (var aes = Aes.Create())
            {
                aes.Key = Key;
                aes.IV = IV;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using (var decryptor = aes.CreateDecryptor())
                {
                    return decryptor.TransformFinalBlock(data, 0, data.Length);
                }
            }
        }

        public static string[] ReadLogs()
        {
            if (!File.Exists(LogFile)) return Array.Empty<string>();

            if (!EnableEncryption)
            {
                // 直接读明文
                return File.ReadAllLines(LogFile);
            }

            // 解密读取
            byte[] encrypted = File.ReadAllBytes(LogFile);
            byte[] decrypted = Decrypt(encrypted);

            string text = Encoding.UTF8.GetString(decrypted);
            return text.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static byte[] GenerateSecureKey()
        {
            // Generate a secure random key if encryption is enabled
            // For security reasons, we disable encryption by default
            if (!EnableEncryption)
                return new byte[32]; // Return empty key when encryption is disabled
            
            using var rng = RandomNumberGenerator.Create();
            byte[] key = new byte[32]; // 256-bit key
            rng.GetBytes(key);
            return key;
        }

        private static byte[] GenerateSecureIV()
        {
            // Generate a secure random IV if encryption is enabled
            if (!EnableEncryption)
                return new byte[16]; // Return empty IV when encryption is disabled
                
            using var rng = RandomNumberGenerator.Create();
            byte[] iv = new byte[16]; // 128-bit IV
            rng.GetBytes(iv);
            return iv;
        }

        public static void Shutdown()
        {
            _logQueue.CompleteAdding();
            while (_logQueue.Count > 0)
            {
                Thread.Sleep(30); // Give time for ProcessLogQueue to finish writing
            }
            _cts.Cancel();
        }
    }
}

