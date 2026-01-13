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

        public MainWindow()
        {
            InitializeComponent(); // Khởi tạo giao diện server
        }

        // ================= START SERVER =================
        // Xử lý khi bấm nút "Start Server"
        private void StartServer_Click(object sender, RoutedEventArgs e)
        {
            // Chạy server trên thread riêng để không treo UI
            Thread serverThread = new Thread(StartServer)
            {
                IsBackground = true // Thread chạy nền
            };
            serverThread.Start();

            // Log trạng thái server
            Log(" Server starting on port 5000...");
            btnStartServer.IsEnabled = false; // Disable nút start
        }

        // Hàm khởi động server TCP
        private void StartServer()
        {
            IPAddress localIP = GetLocalIPv4();


            // Lắng nghe kết nối trên tất cả IP của máy, port 5000
            _server = new TcpListener(localIP, 5000);
            _server.Start();
            Log($" Server started at {localIP}:5000");

            // Server luôn chạy vòng lặp để chấp nhận client mới
            while (true)
            {
                // Chờ client kết nối (blocking call)
                TcpClient client = _server.AcceptTcpClient();

                // Thêm client vào danh sách đang kết nối
                _clients.Add(client);

                // Mỗi client được xử lý trên 1 thread riêng
                Thread t = new Thread(() => HandleClient(client))
                {
                    IsBackground = true
                };
                t.Start();
            }
        }

        // ================= HANDLE CLIENT =================
        // Xử lý dữ liệu gửi từ một client cụ thể
        private void HandleClient(TcpClient client)
        {
            // Lấy stream để đọc / ghi dữ liệu
            NetworkStream stream = client.GetStream();

            // Buffer để đọc dữ liệu từ client
            byte[] buffer = new byte[8192];

            try
            {
                while (true)
                {
                    // Đọc dữ liệu từ client (blocking call)
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);

                    // Nếu bytesRead = 0 → client đã ngắt kết nối
                    if (bytesRead == 0) break;

                    // Chuyển byte sang chuỗi UTF-8 để kiểm tra
                    string raw = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    // Kiểm tra nếu là file transfer
                    if (raw.StartsWith("FILE|"))
                    {
                        // Parse file header
                        string[] parts = raw.Split('|');
                        if (parts.Length >= 5)
                        {
                            string senderName = parts[1];
                            string fileName = parts[2];
                            long fileSize = long.Parse(parts[3]);
                            bool isImage = bool.Parse(parts[4]);

                            // Lấy danh sách target clients (tất cả clients trừ sender)
                            var targetClients = _clients.Where(c => c != client && _clientNames.ContainsKey(c)).ToList();

                            // Forward file đến tất cả clients khác (async, không block)
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    if (targetClients.Count > 0)
                                    {
                                        var targetStreams = targetClients.Select(c => c.GetStream()).ToArray();
                                        CancellationTokenSource cts = new CancellationTokenSource();
                                        await FileTransferService.ForwardFileAsync(stream, targetStreams, senderName, fileName, fileSize, isImage, cts.Token);
                                    }

                                    // Không broadcast system message nữa - file sẽ hiển thị trực tiếp trong chat
                                }
                                catch (Exception ex)
                                {
                                    Dispatcher.Invoke(() => Log($"Error forwarding file: {ex.Message}"));
                                }
                            });
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
                        BroadcastSystem($" {username} joined the chat");
                    }
                    // ================= MESSAGE =================
                    // Client gửi: MSG|username|message
                    else if (msgParts[0] == "MSG")
                    {
                        string username = msgParts[1];
                        string message = msgParts[2];

                        // Broadcast tin nhắn cho tất cả client
                        Broadcast($"{username}: {message}");
                    }
                }
            }
            catch
            {
                // Bắt lỗi khi client mất mạng hoặc đóng đột ngột
            }

            // ================= CLIENT LEAVE =================
            // Khi client thoát hoặc mất kết nối
            if (_clientNames.ContainsKey(client))
            {
                string username = _clientNames[client];

                // Xóa client khỏi danh sách username
                _clientNames.Remove(client);

                // Thông báo user rời khỏi chat
                BroadcastSystem($" {username} left the chat");
            }

            // Xóa client khỏi danh sách kết nối
            _clients.Remove(client);

            // Đóng kết nối client
            client.Close();
        }

        // ================= BROADCAST =================
        // Gửi message cho tất cả client đang kết nối
        private void Broadcast(string message)
        {
            // Lấy timestamp hiện tại
            string time = DateTime.Now.ToString("HH:mm:ss");

            // Gắn timestamp vào message
            string fullMessage = $"[{time}] {message}";

            // Chuyển message sang byte
            byte[] data = Encoding.UTF8.GetBytes(fullMessage);

            // Gửi message cho tất cả client
            foreach (var c in _clients)
            {
                try
                {
                    c.GetStream().Write(data, 0, data.Length);
                }
                catch
                {
                    // Bỏ qua nếu client lỗi
                }
            }

            // Log message lên giao diện server
            Log(fullMessage);
        }

        // Gửi message hệ thống (join / leave)
        private void BroadcastSystem(string message)
        {
            Broadcast($"[SYSTEM] {message}");
        }

        // ================= LOG =================
        // Ghi log ra TextBox trên UI server
        private void Log(string message)
        {
            // Dispatcher dùng để update UI từ thread khác
            Dispatcher.Invoke(() =>
            {
                txtLog.AppendText(message + "\n");
                txtLog.ScrollToEnd();
            });
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
