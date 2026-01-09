using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows;

namespace ChatServer
{
    public partial class MainWindow : Window
    {
          // TcpListener dùng để lắng nghe kết nối từ client
        private TcpListener _server;

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
        }

        // Hàm khởi động server TCP
        private void StartServer()
        {
            // Lắng nghe kết nối trên tất cả IP của máy, port 5000
            _server = new TcpListener(IPAddress.Any, 5000);
            _server.Start();

            Log(" Server started");

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
            byte[] buffer = new byte[1024];

            try
            {
                while (true)
                {
                    // Đọc dữ liệu từ client (blocking call)
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);

                    // Nếu bytesRead = 0 → client đã ngắt kết nối
                    if (bytesRead == 0) break;

                    // Chuyển byte sang chuỗi UTF-8
                    string raw = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    // Tách message theo protocol (JOIN | MSG)
                    string[] parts = raw.Split('|');

                    // ================= JOIN =================
                    // Client gửi: JOIN|username
                    if (parts[0] == "JOIN")
                    {
                        string username = parts[1];

                        // Lưu username tương ứng với client
                        _clientNames[client] = username;

                        // Thông báo user mới vào cho tất cả client
                        BroadcastSystem($" {username} joined the chat");
                    }
                    // ================= MESSAGE =================
                    // Client gửi: MSG|username|message
                    else if (parts[0] == "MSG")
                    {
                        string username = parts[1];
                        string message = parts[2];

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
    }
}
