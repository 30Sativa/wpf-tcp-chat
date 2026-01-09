using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ChatClient
{
    public partial class MainWindow : Window
    {
        // TcpClient dùng để kết nối tới server
        private TcpClient _client;

        // Stream dùng để gửi / nhận dữ liệu
        private NetworkStream _stream;

        // Thread dùng để lắng nghe message từ server (tránh treo UI)
        private Thread _receiveThread;

        public MainWindow()
        {
            InitializeComponent(); // Khởi tạo UI
        }

        // ================= CONNECT =================
        // Xử lý khi bấm nút Connect
        private void Connect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Lấy IP server từ TextBox
                string ip = txtServerIP.Text.Trim();

                // Lấy username người dùng nhập
                string username = txtName.Text.Trim();

                // Kiểm tra port có hợp lệ không
                if (!int.TryParse(txtPort.Text, out int port))
                {
                    MessageBox.Show("Port không hợp lệ");
                    return;
                }

                // Kiểm tra username rỗng
                if (string.IsNullOrWhiteSpace(username))
                {
                    MessageBox.Show("Vui lòng nhập username");
                    return;
                }

                // Khởi tạo TcpClient và kết nối tới server
                _client = new TcpClient();
                _client.Connect(ip, port);

                // Lấy NetworkStream để gửi / nhận dữ liệu
                _stream = _client.GetStream();

                // Tạo thread để nhận message từ server
                _receiveThread = new Thread(ReceiveMessage)
                {
                    IsBackground = true // Thread chạy nền
                };
                _receiveThread.Start();

                // Gửi message JOIN cho server (báo user vừa vào)
                string joinMsg = $"JOIN|{username}";
                byte[] joinData = Encoding.UTF8.GetBytes(joinMsg);
                _stream.Write(joinData, 0, joinData.Length);

                // Cập nhật UI sau khi connect thành công
                btnSend.IsEnabled = true;
                txtStatus.Text = "Connected";
                AppendMessage("✅ Connected to server");
            }
            catch (Exception ex)
            {
                // Hiển thị lỗi nếu connect thất bại
                MessageBox.Show($"Connect failed: {ex.Message}");
            }
        }

        // ================= RECEIVE =================
        // Hàm chạy trong thread để nhận message từ server
        private void ReceiveMessage()
        {
            try
            {
                // Buffer dùng để đọc dữ liệu
                byte[] buffer = new byte[1024];

                while (true)
                {
                    // Đọc dữ liệu từ stream (blocking call)
                    int bytesRead = _stream.Read(buffer, 0, buffer.Length);

                    // Nếu bytesRead = 0 nghĩa là server đóng kết nối
                    if (bytesRead == 0) break;

                    // Chuyển byte sang string UTF-8
                    string msg = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    // Update UI phải dùng Dispatcher (vì đang ở thread khác)
                    Dispatcher.Invoke(() => AppendMessage(msg));
                }
            }
            catch
            {
                // Nếu có lỗi (server tắt, mất mạng, ...)
                Dispatcher.Invoke(() =>
                {
                    txtStatus.Text = "Disconnected";
                    btnSend.IsEnabled = false;
                    AppendMessage("❌ Disconnected from server");
                });
            }
        }

        // ================= SEND =================
        // Xử lý khi bấm nút Send
        private void Send_Click(object sender, RoutedEventArgs e)
        {
            // Nếu chưa connect thì không gửi
            if (_stream == null) return;

            // Lấy nội dung message
            string message = txtMessage.Text.Trim();
            if (string.IsNullOrEmpty(message)) return;

            // Đóng gói message theo protocol: MSG|username|message
            string fullMessage = $"MSG|{txtName.Text}|{message}";
            byte[] data = Encoding.UTF8.GetBytes(fullMessage);

            // Gửi dữ liệu lên server
            _stream.Write(data, 0, data.Length);

            // Xóa ô nhập sau khi gửi
            txtMessage.Clear();
        }

        // ================= ENTER TO SEND =================
        // Nhấn Enter để gửi message
        private void TxtMessage_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true; // Không cho xuống dòng
                Send_Click(null, null);
            }
        }

        // ================= EMOJI =================
        // Xử lý khi click vào 1 emoji trong popup
        private void Emoji_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                // Lấy emoji từ Tag của button
                string emoji = btn.Tag?.ToString();

                if (!string.IsNullOrEmpty(emoji))
                {
                    // Chèn emoji vào ô nhập
                    txtMessage.Text += emoji;
                    txtMessage.CaretIndex = txtMessage.Text.Length;
                    txtMessage.Focus();
                }

                // Đóng popup emoji sau khi chọn
                EmojiPopup.IsOpen = false;
            }
        }

        // ================= TOGGLE EMOJI POPUP =================
        // Mở / đóng popup emoji
        private void ToggleEmojiPanel(object sender, RoutedEventArgs e)
        {
            EmojiPopup.IsOpen = !EmojiPopup.IsOpen;
        }

        // ================= UI HELPER =================
        // Hàm hỗ trợ hiển thị message lên TextBox chat
        private void AppendMessage(string msg)
        {
            txtMessages.AppendText(msg + "\n");
            txtMessages.ScrollToEnd(); // Tự cuộn xuống cuối
        }
    }
}
