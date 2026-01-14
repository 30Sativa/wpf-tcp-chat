using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ChatServer
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

        // Forward file từ một client đến tất cả clients khác
        public static async Task<bool> ForwardFileAsync(NetworkStream sourceStream, NetworkStream[] targetStreams, 
            string senderName, string fileName, long fileSize, bool isImage,
            CancellationToken cancellationToken)
        {
            try
            {
                // Gửi file header đến tất cả target streams (text message có length prefix)
                string header = $"FILE|{senderName}|{fileName}|{fileSize}|{isImage}";
                foreach (var targetStream in targetStreams)
                {
                    try
                    {
                        await SendTextMessageAsync(targetStream, header, cancellationToken);
                    }
                    catch
                    {
                        // Bỏ qua nếu client lỗi
                    }
                }

                // Forward file data theo chunks
                long totalBytesRead = 0;

                while (totalBytesRead < fileSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Đọc chunk size từ source
                    byte[] chunkSizeBytes = new byte[4];
                    bool ok = await ReadExactAsync(sourceStream, chunkSizeBytes, 0, 4, cancellationToken);
                    if (!ok) return false;

                    int chunkSize = BitConverter.ToInt32(chunkSizeBytes, 0);
                    if (chunkSize == 0) break; // End marker
                    
                    // Validate chunk size để tránh overflow và lỗi
                    if (chunkSize < 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"Invalid negative chunk size: {chunkSize}");
                        return false;
                    }
                    
                    // Giới hạn chunk size hợp lý (tối đa 10MB mỗi chunk)
                    const int MaxChunkSize = 10 * 1024 * 1024;
                    if (chunkSize > MaxChunkSize)
                    {
                        System.Diagnostics.Debug.WriteLine($"Chunk size too large: {chunkSize}");
                        return false;
                    }
                    
                    // Kiểm tra để tránh overflow khi cộng
                    if (totalBytesRead > long.MaxValue - (long)chunkSize)
                    {
                        System.Diagnostics.Debug.WriteLine("File too large, overflow risk");
                        return false;
                    }

                    // Đọc chunk data từ source
                    byte[] chunkData = new byte[chunkSize];
                    int bytesRead = 0;
                    while (bytesRead < chunkSize)
                    {
                        int read = await sourceStream.ReadAsync(chunkData, bytesRead, chunkSize - bytesRead, cancellationToken);
                        if (read == 0) return false;
                        bytesRead += read;
                    }

                    // Forward chunk đến tất cả targets
                    foreach (var targetStream in targetStreams)
                    {
                        try
                        {
                            await targetStream.WriteAsync(chunkSizeBytes, 0, 4, cancellationToken);
                            await targetStream.WriteAsync(chunkData, 0, chunkSize, cancellationToken);
                        }
                        catch
                        {
                            // Bỏ qua nếu client lỗi
                        }
                    }

                    // Cộng an toàn với ép kiểu explicit
                    totalBytesRead = totalBytesRead + (long)chunkSize;
                }

                // Forward end marker
                byte[] endMarker = BitConverter.GetBytes(0);
                foreach (var targetStream in targetStreams)
                {
                    try
                    {
                        await targetStream.WriteAsync(endMarker, 0, 4, cancellationToken);
                    }
                    catch
                    {
                        // Bỏ qua nếu client lỗi
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error forwarding file: {ex.Message}");
                return false;
            }
        }
    }
}
