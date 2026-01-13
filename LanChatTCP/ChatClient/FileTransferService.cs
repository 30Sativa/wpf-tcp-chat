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
        private const int HeaderSize = 256; // Header size for file info

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

                // Gửi file header như text message: FILE|sender|filename|filesize|isImage
                string header = $"FILE|{senderName}|{fileName}|{fileSize}|{isImage}";
                byte[] headerBytes = Encoding.UTF8.GetBytes(header);
                
                // Gửi header như text message (không có length prefix)
                await stream.WriteAsync(headerBytes, 0, headerBytes.Length, cancellationToken);
                await stream.FlushAsync(cancellationToken);

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

                    while (totalBytesReceived < fileSize)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        // Đọc chunk size (4 bytes)
                        byte[] chunkSizeBytes = new byte[4];
                        int bytesRead = await stream.ReadAsync(chunkSizeBytes, 0, 4, cancellationToken);
                        if (bytesRead != 4) return false;

                        int chunkSize = BitConverter.ToInt32(chunkSizeBytes, 0);
                        if (chunkSize == 0) break; // End marker

                        // Đọc chunk data
                        byte[] buffer = new byte[chunkSize];
                        int remaining = chunkSize;
                        int offset = 0;

                        while (remaining > 0)
                        {
                            bytesRead = await stream.ReadAsync(buffer, offset, remaining, cancellationToken);
                            if (bytesRead == 0) return false;
                            remaining -= bytesRead;
                            offset += bytesRead;
                        }

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
