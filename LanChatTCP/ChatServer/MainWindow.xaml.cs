using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Collections.Concurrent;

namespace ChatServer
{
    public partial class MainWindow : Window
    {
        // TcpListener dùng để lắng nghe kết nối từ client
        private TcpListener? _server;

        // Danh sách tất cả client đang kết nối
        private readonly List<TcpClient> _clients = new();

        // Map giữa TcpClient và username tương ứng
        // → dùng để biết client nào là user nào
        private readonly Dictionary<TcpClient, string> _clientNames = new();

        // Khóa ghi theo từng client để tránh interleave bytes khi nhiều tác vụ broadcast/forward cùng lúc
        private readonly ConcurrentDictionary<TcpClient, SemaphoreSlim> _clientSendLocks = new();

        private enum LogLevel
        {
            Debug,
            Info,
            Warning,
            Error
        }

        public MainWindow()
        {
            InitializeComponent(); // Khởi tạo giao diện server
        }

        // ================= START SERVER =================
        // Xử lý khi bấm nút "Start Server"
        private void StartServer_Click(object sender, RoutedEventArgs e)
        {
            // Chạy server bằng Task để đúng async/task pattern
            _ = Task.Run(StartServerAsync);

            // Log trạng thái server
            Log(LogLevel.Info, "Server", "Starting on port 5000...");
            btnStartServer.IsEnabled = false; // Disable nút start
        }

        // Hàm khởi động server TCP (async accept loop)
        private async Task StartServerAsync()
        {
            IPAddress localIP = GetLocalIPv4();


            // Lắng nghe kết nối trên tất cả IP của máy, port 5000
            _server = new TcpListener(localIP, 5000);
            _server.Start();
            Log(LogLevel.Info, "Server", $"Started at {localIP}:5000");

            // Server luôn chạy vòng lặp để chấp nhận client mới
            while (true)
            {
                // Accept async
                TcpClient client = await _server.AcceptTcpClientAsync();

                // Thêm client vào danh sách đang kết nối
                _clients.Add(client);
                _clientSendLocks.TryAdd(client, new SemaphoreSlim(1, 1));

                // Mỗi client được xử lý trên 1 task riêng
                _ = Task.Run(() => HandleClientAsync(client));
            }
        }

        // ================= HANDLE CLIENT =================
        // Xử lý dữ liệu gửi từ một client cụ thể
        private async Task HandleClientAsync(TcpClient client)
        {
            // Lấy stream để đọc / ghi dữ liệu
            NetworkStream stream = client.GetStream();

            // Log client mới kết nối
            try
            {
                var remoteEndPoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
                Log(LogLevel.Info, "Connection", $"New client connected: {remoteEndPoint}");
            }
            catch
            {
                Log(LogLevel.Info, "Connection", "New client connected.");
            }

            try
            {
                while (true)
                {
                    // Đọc message text có prefix độ dài 4 byte
                    string? raw = await ReadTextMessageAsync(stream);
                    // Nếu null → client đã ngắt kết nối
                    if (raw == null) break;

                    // Kiểm tra nếu là file transfer
                    if (raw.StartsWith("FILE|"))
                    {
                        // Parse file header
                        string[] parts = raw.Split('|');
                        if (parts.Length >= 5)
                        {
                            string senderName = parts[1];
                            string fileName = parts[2];
                            if (!long.TryParse(parts[3], out long fileSize))
                            {
                                Log(LogLevel.Warning, "File", "Invalid file size received from client.");
                                continue;
                            }
                            bool isImage;
                            try
                            {
                                isImage = bool.Parse(parts[4]);
                            }
                            catch
                            {
                                Log(LogLevel.Warning, "File", "Invalid isImage flag received from client.");
                                continue;
                            }

                            // Lấy danh sách target clients (tất cả clients trừ sender)
                            var targetClients = _clients.Where(c => c != client && _clientNames.ContainsKey(c)).ToList();

                            Log(LogLevel.Info, "File", $"{senderName} is sending file '{fileName}' ({fileSize} bytes, isImage={isImage}) to {targetClients.Count} client(s).");

                            try
                            {
                                if (targetClients.Count > 0)
                                {
                                    var targetStreams = targetClients.Select(c => c.GetStream()).ToArray();
                                    CancellationTokenSource cts = new CancellationTokenSource();

                                    // Forward file async; ghi ra targets song song nhưng có lock per-client
                                    await ForwardFileToTargetsAsync(client, stream, targetClients, senderName, fileName, fileSize, isImage, cts.Token);

                                    Log(LogLevel.Info, "File", $"File '{fileName}' from {senderName} forwarded successfully.");
                                }
                                else
                                {
                                    Log(LogLevel.Info, "File", "No other clients connected, file will not be forwarded.");
                                }
                            }
                            catch (Exception ex)
                            {
                                Log(LogLevel.Error, "File", $"Error forwarding file '{fileName}' from {senderName}: {ex.Message}");
                            }
                        }
                        continue;
                    }

                    // Tách message theo protocol (JOIN | MSG)
                    string[] msgParts = raw.Split('|');

                    // ================= JOIN =================
                    // Client gửi: JOIN|username
                    if (msgParts[0] == "JOIN")
                    {
                        string username = msgParts[1];

                        // Lưu username tương ứng với client
                        _clientNames[client] = username;

                        // Thông báo user mới vào cho tất cả client
                        await BroadcastSystemAsync($" {username} joined the chat");

                        // Log chi tiết
                        Log(LogLevel.Info, "Session", $"{username} joined the chat.");
                    }
                    // ================= MESSAGE =================
                    // Client gửi: MSG|username|message
                    else if (msgParts[0] == "MSG")
                    {
                        string username = msgParts[1];
                        string message = msgParts[2];

                        // Broadcast tin nhắn cho tất cả client
                        await BroadcastAsync($"{username}: {message}");

                        // Log chat
                        Log(LogLevel.Info, "Chat", $"{username}: {message}");
                    }
                }
            }
            catch (Exception ex)
            {
                // Bắt lỗi khi client mất mạng hoặc đóng đột ngột
                Log(LogLevel.Warning, "Connection", $"Client error: {ex.Message}");
            }

            // ================= CLIENT LEAVE =================
            // Khi client thoát hoặc mất kết nối
            if (_clientNames.ContainsKey(client))
            {
                string username = _clientNames[client];

                // Xóa client khỏi danh sách username
                _clientNames.Remove(client);

                // Thông báo user rời khỏi chat
                await BroadcastSystemAsync($" {username} left the chat");
                Log(LogLevel.Info, "Session", $"{username} left the chat.");
            }

            // Xóa client khỏi danh sách kết nối
            _clients.Remove(client);
            _clientSendLocks.TryRemove(client, out _);

            // Đóng kết nối client
            try
            {
                client.Close();
                Log(LogLevel.Info, "Connection", "Client connection closed.");
            }
            catch (Exception ex)
            {
                Log(LogLevel.Warning, "Connection", $"Error while closing client connection: {ex.Message}");
            }
        }

        // ================= BROADCAST =================
        // Gửi message cho tất cả client đang kết nối
        private async Task BroadcastAsync(string message)
        {
            // Lấy timestamp hiện tại
            string time = DateTime.Now.ToString("HH:mm:ss");

            // Gắn timestamp vào message
            string fullMessage = $"[{time}] {message}";

            // Gửi message cho tất cả client (protocol length-prefix)
            var tasks = new List<Task>(_clients.Count);
            foreach (var c in _clients)
            {
                tasks.Add(SendTextMessageToClientAsync(c, fullMessage));
            }
            await Task.WhenAll(tasks);
        }

        // Gửi message hệ thống (join / leave)
        private Task BroadcastSystemAsync(string message)
        {
            return BroadcastAsync($"[SYSTEM] {message}");
        }

        // ================= LOG =================
        // Ghi log ra TextBox trên UI server với định dạng chuẩn
        private void Log(LogLevel level, string category, string message)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string levelText = level.ToString().ToUpper();
            string line = $"{timestamp} [{levelText}] {category} - {message}";

            // Dispatcher dùng để update UI từ thread khác
            Dispatcher.Invoke(() =>
            {
                txtLog.AppendText(line + Environment.NewLine);
                txtLog.ScrollToEnd();
            });
        }

        // Overload đơn giản cho Log cũ (mặc định INFO / General)
        private void Log(string message)
        {
            Log(LogLevel.Info, "General", message);
        }

        // Đọc 1 message text có prefix độ dài 4 byte từ client
        private static async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = await stream.ReadAsync(buffer, offset + total, count - total);
                if (read == 0) return false;
                total += read;
            }
            return true;
        }

        private static async Task<string?> ReadTextMessageAsync(NetworkStream stream)
        {
            byte[] lengthBytes = new byte[4];
            bool ok = await ReadExactAsync(stream, lengthBytes, 0, 4);
            if (!ok) return null;

            int length = BitConverter.ToInt32(lengthBytes, 0);
            if (length <= 0 || length > 1024 * 1024)
            {
                throw new IOException($"Invalid message length received from client: {length}");
            }

            byte[] data = new byte[length];
            ok = await ReadExactAsync(stream, data, 0, length);
            if (!ok) return null;

            return Encoding.UTF8.GetString(data, 0, length);
        }

        private async Task SendTextMessageToClientAsync(TcpClient client, string message)
        {
            try
            {
                if (!_clientSendLocks.TryGetValue(client, out var sendLock))
                {
                    return;
                }

                await sendLock.WaitAsync();
                try
                {
                    NetworkStream stream = client.GetStream();
                    byte[] data = Encoding.UTF8.GetBytes(message);
                    byte[] lengthBytes = BitConverter.GetBytes(data.Length);
                    await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length);
                    await stream.WriteAsync(data, 0, data.Length);
                    await stream.FlushAsync();
                }
                finally
                {
                    sendLock.Release();
                }
            }
            catch
            {
                // ignore send failures
            }
        }

        private async Task ForwardFileToTargetsAsync(
            TcpClient sourceClient,
            NetworkStream sourceStream,
            List<TcpClient> targets,
            string senderName,
            string fileName,
            long fileSize,
            bool isImage,
            CancellationToken cancellationToken)
        {
            // Header -> gửi song song (nhưng mỗi client có lock)
            string header = $"FILE|{senderName}|{fileName}|{fileSize}|{isImage}";
            var headerTasks = new List<Task>(targets.Count);
            foreach (var t in targets)
            {
                headerTasks.Add(SendTextMessageToClientAsync(t, header));
            }
            await Task.WhenAll(headerTasks);

            // Data -> đọc tuần tự từ source (bắt buộc), ghi ra targets song song theo chunk
            byte[] chunkSizeBytes = new byte[4];
            long total = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                bool ok = await ReadExactAsync(sourceStream, chunkSizeBytes, 0, 4);
                if (!ok) throw new IOException("Disconnected while reading file chunk size from source client.");

                int chunkSize = BitConverter.ToInt32(chunkSizeBytes, 0);
                if (chunkSize == 0)
                {
                    // End marker từ client gửi file
                    break;
                }
                if (chunkSize < 0 || chunkSize > 10 * 1024 * 1024)
                {
                    throw new IOException($"Invalid chunk size: {chunkSize}");
                }

                byte[] chunkData = new byte[chunkSize];
                ok = await ReadExactAsync(sourceStream, chunkData, 0, chunkSize);
                if (!ok) throw new IOException("Disconnected while reading file chunk data from source client.");

                var writeTasks = new List<Task>(targets.Count);
                foreach (var t in targets)
                {
                    writeTasks.Add(WriteChunkToClientAsync(t, chunkSizeBytes, chunkData, cancellationToken));
                }
                await Task.WhenAll(writeTasks);

                total += chunkSize;
                // Sanity check: không cho vượt quá fileSize quá nhiều (nhưng không dừng sớm để không bỏ sót end marker)
                if (total > fileSize + 1024 * 1024)
                {
                    throw new IOException("Forwarded more data than expected file size.");
                }
            }

            // End marker -> gửi song song
            byte[] endMarker = BitConverter.GetBytes(0);
            var endTasks = new List<Task>(targets.Count);
            foreach (var t in targets)
            {
                endTasks.Add(WriteRawToClientAsync(t, endMarker, cancellationToken));
            }
            await Task.WhenAll(endTasks);
        }

        private async Task WriteChunkToClientAsync(TcpClient client, byte[] chunkSizeBytes, byte[] chunkData, CancellationToken cancellationToken)
        {
            if (!_clientSendLocks.TryGetValue(client, out var sendLock))
            {
                return;
            }

            await sendLock.WaitAsync(cancellationToken);
            try
            {
                NetworkStream s = client.GetStream();
                await s.WriteAsync(chunkSizeBytes, 0, 4, cancellationToken);
                await s.WriteAsync(chunkData, 0, chunkData.Length, cancellationToken);
                await s.FlushAsync(cancellationToken);
            }
            catch
            {
                // ignore
            }
            finally
            {
                sendLock.Release();
            }
        }

        private async Task WriteRawToClientAsync(TcpClient client, byte[] data, CancellationToken cancellationToken)
        {
            if (!_clientSendLocks.TryGetValue(client, out var sendLock))
            {
                return;
            }

            await sendLock.WaitAsync(cancellationToken);
            try
            {
                NetworkStream s = client.GetStream();
                await s.WriteAsync(data, 0, data.Length, cancellationToken);
                await s.FlushAsync(cancellationToken);
            }
            catch
            {
                // ignore
            }
            finally
            {
                sendLock.Release();
            }
        }
    


    private IPAddress GetLocalIPv4()
        {
            foreach (var ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip; // IPv4
                }
            }
            return IPAddress.Loopback;
        }
    }

}
