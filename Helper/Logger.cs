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

        private static readonly byte[] Key = Encoding.UTF8.GetBytes("1234567890abcdef1234567890abcdef"); // 32字节 AES Key
        private static readonly byte[] IV = Encoding.UTF8.GetBytes("abcdef1234567890"); // 16字节 IV

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

            if (EnableEncryption)
            {
                byte[] plainBytes = Encoding.UTF8.GetBytes(text + Environment.NewLine);
                byte[] encryptedBytes = Encrypt(plainBytes);

                using (var fs = new FileStream(LogFile, FileMode.Append, FileAccess.Write, FileShare.Read))
                {
                    fs.Write(encryptedBytes, 0, encryptedBytes.Length);
                }
            }
            else
            {
                File.AppendAllText(LogFile, text + Environment.NewLine);
            }
        }

        private static byte[] Encrypt(byte[] data)
        {
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

