using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ChatClient
{
    public class FileTransferService
    {
        private const int ChunkSize = 64 * 1024; // 64KB chunks
        private static async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int total = 0;
            while (total < count)
            {
                int read = await stream.ReadAsync(buffer, offset + total, count - total, cancellationToken);
                if (read == 0) return false; // disconnected
                total += read;
            }
            return true;
        }


        // Gửi 1 message text có prefix độ dài 4 byte
        public static async Task SendTextMessageAsync(NetworkStream stream, string message, CancellationToken cancellationToken)
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            byte[] lengthBytes = BitConverter.GetBytes(data.Length);

            await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length, cancellationToken);
            await stream.WriteAsync(data, 0, data.Length, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        // Gửi file với async/await
        public static async Task<bool> SendFileAsync(NetworkStream stream, string filePath, string senderName, 
            IProgress<(long bytesTransferred, long totalBytes, string fileName)> progress, 
            CancellationToken cancellationToken)
        {
            try
            {
                FileInfo fileInfo = new FileInfo(filePath);
                string fileName = fileInfo.Name;
                long fileSize = fileInfo.Length;
                string fileExtension = fileInfo.Extension.ToLower();
                bool isImage = IsImageFile(fileExtension);

                // Gửi file header như text message có length prefix: FILE|sender|filename|filesize|isImage
                string header = $"FILE|{senderName}|{fileName}|{fileSize}|{isImage}";
                await SendTextMessageAsync(stream, header, cancellationToken);

                // Gửi file data theo chunks
                using (FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    byte[] buffer = new byte[ChunkSize];
                    long totalBytesRead = 0;

                    while (totalBytesRead < fileSize)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        int bytesRead = await fileStream.ReadAsync(buffer, 0, ChunkSize, cancellationToken);
                        if (bytesRead == 0) break;

                        // Gửi chunk size (4 bytes)
                        byte[] chunkSizeBytes = BitConverter.GetBytes(bytesRead);
                        await stream.WriteAsync(chunkSizeBytes, 0, 4, cancellationToken);

                        // Gửi chunk data
                        await stream.WriteAsync(buffer, 0, bytesRead, cancellationToken);

                        totalBytesRead += bytesRead;
                        progress?.Report((totalBytesRead, fileSize, fileName));
                    }
                }

                // Gửi end marker (0 bytes)
                byte[] endMarker = BitConverter.GetBytes(0);
                await stream.WriteAsync(endMarker, 0, 4, cancellationToken);

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error sending file: {ex.Message}");
                return false;
            }
        }

        // Nhận file với async/await (header đã được parse trước đó)
        public static async Task<bool> ReceiveFileAsync(NetworkStream stream, string saveDirectory,
            string senderName, string fileName, long fileSize, bool isImage,
            IProgress<(long bytesTransferred, long totalBytes, string fileName)> progress,
            CancellationToken cancellationToken)
        {
            try
            {
                // Header đã được parse, bắt đầu nhận file data
                // Tạo thư mục nếu chưa có
                if (!Directory.Exists(saveDirectory))
                {
                    Directory.CreateDirectory(saveDirectory);
                }

                string filePath = Path.Combine(saveDirectory, fileName);
                
                // Đảm bảo file name unique nếu đã tồn tại
                int counter = 1;
                string originalPath = filePath;
                while (File.Exists(filePath))
                {
                    string nameWithoutExt = Path.GetFileNameWithoutExtension(originalPath);
                    string extension = Path.GetExtension(originalPath);
                    filePath = Path.Combine(saveDirectory, $"{nameWithoutExt}_{counter}{extension}");
                    counter++;
                }

                // Nhận file data theo chunks
                using (FileStream fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                {
                    long totalBytesReceived = 0;

                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        // Đọc chunk size (4 bytes)
                        byte[] chunkSizeBytes = new byte[4];
                        bool ok = await ReadExactAsync(stream, chunkSizeBytes, 0, 4, cancellationToken);
                        if (!ok) return false;

                        int chunkSize = BitConverter.ToInt32(chunkSizeBytes, 0);
                        if (chunkSize == 0)
                        {
                            // End marker từ server
                            break;
                        }

                        // Validate chunk size
                        if (chunkSize < 0 || chunkSize > 10 * 1024 * 1024)
                        {
                            System.Diagnostics.Debug.WriteLine($"Invalid chunk size received: {chunkSize}");
                            return false;
                        }

                        // Đọc chunk data
                        byte[] buffer = new byte[chunkSize];
                        ok = await ReadExactAsync(stream, buffer, 0, chunkSize, cancellationToken);
                        if (!ok) return false;

                        // Ghi chunk vào file
                        await fileStream.WriteAsync(buffer, 0, chunkSize, cancellationToken);
                        totalBytesReceived += chunkSize;
                        progress?.Report((totalBytesReceived, fileSize, fileName));
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error receiving file: {ex.Message}");
                return false;
            }
        }

        private static bool IsImageFile(string extension)
        {
            string[] imageExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".ico" };
            return Array.Exists(imageExtensions, ext => ext.Equals(extension, StringComparison.OrdinalIgnoreCase));
        }
    }
}
