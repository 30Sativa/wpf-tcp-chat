using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using NAudio.Wave;

namespace ChatClient
{
    public partial class MainWindow : Window
    {
        // TcpClient dùng để kết nối tới server
        private TcpClient? _client;

        // Stream dùng để gửi / nhận dữ liệu
        private NetworkStream? _stream;

        // Thread dùng để lắng nghe message từ server (tránh treo UI)
        private Thread? _receiveThread;

        // Username hiện tại của client này
        private string? _currentUsername;

        // Collection để lưu danh sách messages
        private ObservableCollection<MessageModel> _messages;

        // Thư mục để lưu file đã nhận
        private readonly string _downloadDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ChatDownloads");

        // Cancellation token source cho file transfer
        private CancellationTokenSource? _fileTransferCancellation;

        // Voice recording (Facebook style - press and hold)
        private WaveInEvent? _waveIn;
        private WaveFileWriter? _waveWriter;
        private string? _voiceRecordingPath;
        private DateTime _voiceRecordingStartTime;
        private System.Windows.Threading.DispatcherTimer? _voiceTimer;
        private bool _isRecordingVoice = false;

        public MainWindow()
        {
            InitializeComponent(); // Khởi tạo UI
            _messages = new ObservableCollection<MessageModel>();
            MessagesList.ItemsSource = _messages;
            
            // Tạo thư mục download nếu chưa có
            if (!Directory.Exists(_downloadDirectory))
            {
                Directory.CreateDirectory(_downloadDirectory);
            }
            
            System.Diagnostics.Debug.WriteLine("MainWindow initialized, MessagesList bound to collection");
        }

        // Constructor với thông tin kết nối để tự động connect
        public MainWindow(string username, string ip, int port) : this()
        {
            // Tự động kết nối sau khi UI được load
            Loaded += (s, e) =>
            {
                _currentUsername = username;
                ConnectToServer(ip, port);
            };
        }

        // Method để kết nối đến server
        private void ConnectToServer(string ip, int port)
        {
            try
            {
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
                string joinMsg = $"JOIN|{_currentUsername}";
                byte[] joinData = Encoding.UTF8.GetBytes(joinMsg);
                _stream.Write(joinData, 0, joinData.Length);

                // Cập nhật UI sau khi connect thành công
                btnSend.IsEnabled = true;
                txtStatus.Text = $"Connected as {_currentUsername}";
                
                // Hiển thị message kết nối thành công
                AddSystemMessage("✅ Connected to server");
            }
            catch (Exception ex)
            {
                // Hiển thị lỗi nếu connect thất bại
                MessageBox.Show($"Connect failed: {ex.Message}");
                txtStatus.Text = "Connection failed";
            }
        }

        // ================= CONNECT =================
        // Method này không còn được sử dụng vì connect từ ConnectWindow
        // Giữ lại để tương thích nếu có code khác gọi
        private void Connect_Click(object sender, RoutedEventArgs e)
        {
            // Method này không còn được sử dụng
        }

        // ================= RECEIVE =================
        // Hàm chạy trong thread để nhận message từ server
        private void ReceiveMessage()
        {
            try
            {
                // Buffer dùng để đọc dữ liệu
                byte[] buffer = new byte[8192];

                while (true)
                {
                    if (_stream == null) break;

                    // Đọc dữ liệu từ stream (blocking call)
                    int bytesRead = _stream.Read(buffer, 0, buffer.Length);

                    // Nếu bytesRead = 0 nghĩa là server đóng kết nối
                    if (bytesRead == 0) break;

                    // Chuyển byte sang string UTF-8
                    string msg = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    // Kiểm tra nếu là file transfer notification (từ server broadcast)
                    if (msg.StartsWith("FILE|"))
                    {
                        // Parse file header
                        string[] parts = msg.Split('|');
                        if (parts.Length >= 5)
                        {
                            string sender = parts[1];
                            string fileName = parts[2];
                            long fileSize = long.Parse(parts[3]);
                            bool isImage = bool.Parse(parts[4]);

                            // Xử lý file transfer trong background (nhận file data từ stream)
                            _ = Task.Run(async () => await HandleFileTransferAsync(sender, fileName, fileSize, isImage));
                        }
                    }
                    else
                    {
                        // Update UI phải dùng Dispatcher (vì đang ở thread khác)
                        Dispatcher.Invoke(() => ProcessReceivedMessage(msg));
                    }
                }
            }
            catch
            {
                // Nếu có lỗi (server tắt, mất mạng, ...)
                Dispatcher.Invoke(() =>
                {
                    txtStatus.Text = "Disconnected";
                    btnSend.IsEnabled = false;
                    AddSystemMessage("❌ Disconnected from server");
                });
            }
        }

        // Xử lý file transfer async
        private async Task HandleFileTransferAsync(string senderName, string fileName, long fileSize, bool isImage)
        {
            try
            {
                if (_stream == null) return;

                var progress = new Progress<(long bytesTransferred, long totalBytes, string fileName)>(p =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        double percent = (double)p.bytesTransferred / p.totalBytes * 100;
                        txtFileProgress.Text = $"Receiving {p.fileName}...";
                        txtFileProgressPercent.Text = $"{percent:F1}%";
                        fileProgressBar.Value = percent;
                        FileProgressContainer.Visibility = Visibility.Visible;
                    });
                });

                CancellationTokenSource cts = new CancellationTokenSource();
                bool success = await FileTransferService.ReceiveFileAsync(_stream, _downloadDirectory, senderName, fileName, fileSize, isImage, progress, cts.Token);

                Dispatcher.Invoke(() =>
                {
                    FileProgressContainer.Visibility = Visibility.Collapsed;
                    
                    if (success)
                    {
                        string filePath = Path.Combine(_downloadDirectory, fileName);
                        
                        // Tìm file thực tế (có thể đã rename nếu trùng)
                        if (!File.Exists(filePath))
                        {
                            // Tìm file với pattern
                            var files = Directory.GetFiles(_downloadDirectory, Path.GetFileNameWithoutExtension(fileName) + "*" + Path.GetExtension(fileName));
                            if (files.Length > 0)
                            {
                                filePath = files[0];
                            }
                        }

                        if (File.Exists(filePath))
                        {
                            // Kiểm tra lại extension để đảm bảo GIF được nhận diện đúng
                            string extension = Path.GetExtension(filePath).ToLower();
                            bool isGifOrImage = isImage || extension == ".gif";
                            
                            // Nếu là ảnh hoặc GIF, dùng file path để hiển thị
                            string displayPath = isGifOrImage ? filePath : string.Empty;
                            AddFileMessage(senderName, fileName, fileSize, displayPath, isGifOrImage);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    FileProgressContainer.Visibility = Visibility.Collapsed;
                    AddSystemMessage($"❌ Error receiving file: {ex.Message}");
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
            string fullMessage = $"MSG|{_currentUsername}|{message}";
            byte[] data = Encoding.UTF8.GetBytes(fullMessage);

            // Gửi dữ liệu lên server
            _stream.Write(data, 0, data.Length);

            // Hiển thị tin nhắn của mình ngay lập tức (bên phải)
            AddOwnMessage(message);

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
                Send_Click(sender, e);
            }
        }

        // ================= EMOJI =================
        // Xử lý khi click vào 1 emoji trong popup
        private void Emoji_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                // Lấy emoji từ Tag của button
                string? emoji = btn.Tag?.ToString();

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

        // ================= ATTACH FILE =================
        // Xử lý khi click nút attach file (tổng quát)
        private void AttachFile_Click(object sender, RoutedEventArgs e)
        {
            if (_stream == null)
            {
                MessageBox.Show("Vui lòng kết nối đến server trước", "Chưa kết nối", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Title = "Chọn file để gửi",
                Filter = "All Files (*.*)|*.*",
                Multiselect = false
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string filePath = openFileDialog.FileName;
                string extension = Path.GetExtension(filePath).ToLower();
                
                // Bỏ qua file ảnh và GIF (dùng nút riêng)
                if (IsImageFile(extension) || extension == ".gif")
                {
                    MessageBox.Show("Vui lòng sử dụng 'Gửi ảnh' hoặc 'Gửi GIF' để gửi ảnh/GIF", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                
                _ = Task.Run(async () => await SendFileAsync(filePath));
            }
        }

        // ================= SEND IMAGE =================
        // Xử lý khi click "Gửi ảnh"
        private void SendImage_Click(object sender, RoutedEventArgs e)
        {
            if (_stream == null)
            {
                MessageBox.Show("Please connect to server first", "Not Connected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Title = "Chọn ảnh để gửi",
                Filter = "Image Files (*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.webp)|*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.webp|All Files (*.*)|*.*",
                Multiselect = false
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string filePath = openFileDialog.FileName;
                string extension = Path.GetExtension(filePath).ToLower();
                
                // Kiểm tra xem có phải là ảnh không
                if (!IsImageFile(extension))
                {
                    MessageBox.Show("Vui lòng chọn file ảnh hợp lệ", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                _ = Task.Run(async () => await SendFileAsync(filePath));
            }
        }

        // ================= SEND FILE =================
        // Xử lý khi click "Gửi file"
        private void SendFile_Click(object sender, RoutedEventArgs e)
        {
            if (_stream == null)
            {
                MessageBox.Show("Please connect to server first", "Not Connected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Title = "Chọn file để gửi",
                Filter = "All Files (*.*)|*.*",
                Multiselect = false
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string filePath = openFileDialog.FileName;
                string extension = Path.GetExtension(filePath).ToLower();
                
                // Bỏ qua file ảnh và GIF (dùng nút riêng)
                if (IsImageFile(extension) || extension == ".gif")
                {
                    MessageBox.Show("Vui lòng sử dụng 'Gửi ảnh' hoặc 'Gửi GIF' để gửi ảnh/GIF", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                
                _ = Task.Run(async () => await SendFileAsync(filePath));
            }
        }

        // Gửi file async
        private async Task SendFileAsync(string filePath)
        {
            try
            {
                if (_stream == null || string.IsNullOrEmpty(_currentUsername)) return;

                FileInfo fileInfo = new FileInfo(filePath);
                string fileName = fileInfo.Name;
                long fileSize = fileInfo.Length;
                string extension = fileInfo.Extension.ToLower();
                // GIF và ảnh đều được xử lý như ảnh để hiển thị
                bool isImage = IsImageFile(extension) || extension == ".gif";

                // Hiển thị progress bar
                Dispatcher.Invoke(() =>
                {
                    FileProgressContainer.Visibility = Visibility.Visible;
                    txtFileProgress.Text = $"Sending {fileName}...";
                    txtFileProgressPercent.Text = "0%";
                    fileProgressBar.Value = 0;
                });

                // Tạo progress reporter
                var progress = new Progress<(long bytesTransferred, long totalBytes, string fileName)>(p =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        double percent = (double)p.bytesTransferred / p.totalBytes * 100;
                        txtFileProgressPercent.Text = $"{percent:F1}%";
                        fileProgressBar.Value = percent;
                    });
                });

                // Gửi file
                _fileTransferCancellation = new CancellationTokenSource();
                bool success = await FileTransferService.SendFileAsync(_stream, filePath, _currentUsername, progress, _fileTransferCancellation.Token);

                Dispatcher.Invoke(() =>
                {
                    FileProgressContainer.Visibility = Visibility.Collapsed;

                    if (success)
                    {
                        // Kiểm tra lại extension để đảm bảo GIF được nhận diện đúng
                        string extension = Path.GetExtension(filePath).ToLower();
                        bool isGifOrImage = isImage || extension == ".gif";
                        
                        // Hiển thị file message trong chat (ảnh/GIF sẽ hiển thị như ảnh)
                        string displayPath = isGifOrImage ? filePath : string.Empty;
                        AddFileMessage(_currentUsername, fileName, fileSize, displayPath, isGifOrImage, true);
                    }
                    else
                    {
                        AddSystemMessage($"❌ Failed to send file: {fileName}");
                    }
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    FileProgressContainer.Visibility = Visibility.Collapsed;
                    AddSystemMessage($"❌ Error sending file: {ex.Message}");
                });
            }
        }

        // ================= GIF POPUP =================
        // Toggle GIF popup
        private void ToggleGifPanel(object sender, RoutedEventArgs e)
        {
            if (_stream == null)
            {
                MessageBox.Show("Vui lòng kết nối đến server trước", "Chưa kết nối", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Load GIF mẫu nếu chưa load
            if (GifList.ItemsSource == null)
            {
                LoadSampleGifs();
            }

            GifPopup.IsOpen = !GifPopup.IsOpen;
        }

        // Load các GIF mẫu
        private void LoadSampleGifs()
        {
            var gifSources = new List<BitmapImage>();
            
            // Danh sách GIF mẫu từ GIPHY
            var sampleGifUrls = new List<string>
            {
                "https://media.giphy.com/media/3o7aCTPPm4OHfRLSH6/giphy.gif", // Thumbs up
                "https://media.giphy.com/media/l0MYC0LajbaPoEADu/giphy.gif", // Laughing
                "https://media.giphy.com/media/3o7abldb0xbmxM3Tfa/giphy.gif", // Dancing
                "https://media.giphy.com/media/l0HlNQ03J5JxX6lva/giphy.gif", // Heart
                "https://media.giphy.com/media/3o7aD2sa0pbkOq7hva/giphy.gif", // Wave
                "https://media.giphy.com/media/3o7aCT8jYqP3X3q3va/giphy.gif", // Clapping
                "https://media.giphy.com/media/26BRuo6sLetdllPAQ/giphy.gif", // Fire
                "https://media.giphy.com/media/3o7aD2sa0pbkOq7hva/giphy.gif", // Party
                "https://media.giphy.com/media/3o7abldb0xbmxM3Tfa/giphy.gif", // Celebration
                "https://media.giphy.com/media/l0HlNQ03J5JxX6lva/giphy.gif", // Love
                "https://media.giphy.com/media/3o7aCTPPm4OHfRLSH6/giphy.gif", // OK
                "https://media.giphy.com/media/l0MYC0LajbaPoEADu/giphy.gif", // Happy
            };

            foreach (var gifUrl in sampleGifUrls)
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(gifUrl, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    gifSources.Add(bitmap);
                }
                catch
                {
                    // Bỏ qua nếu không load được
                }
            }

            GifList.ItemsSource = gifSources;
        }

        // Xử lý khi click vào GIF item
        private void GifItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is BitmapImage bitmap)
            {
                GifPopup.IsOpen = false;
                
                // Lấy URL để download và gửi
                string? gifUrl = bitmap.UriSource?.ToString();
                if (!string.IsNullOrEmpty(gifUrl))
                {
                    _ = Task.Run(async () => await DownloadAndSendGifAsync(gifUrl));
                }
            }
        }

        // Chọn file GIF từ máy
        private void BrowseGifFile_Click(object sender, RoutedEventArgs e)
        {
            GifPopup.IsOpen = false;

            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Title = "Chọn file GIF",
                Filter = "GIF Files (*.gif)|*.gif|All Files (*.*)|*.*",
                Multiselect = false
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string filePath = openFileDialog.FileName;
                string extension = Path.GetExtension(filePath).ToLower();
                
                if (extension == ".gif")
                {
                    _ = Task.Run(async () => await SendFileAsync(filePath));
                }
                else
                {
                    MessageBox.Show("Vui lòng chọn file GIF hợp lệ", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        // Download GIF từ URL và gửi
        private async Task DownloadAndSendGifAsync(string gifUrl)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    FileProgressContainer.Visibility = Visibility.Visible;
                    txtFileProgress.Text = "Đang tải GIF...";
                    txtFileProgressPercent.Text = "0%";
                    fileProgressBar.Value = 0;
                });

                using (var client = new System.Net.Http.HttpClient())
                {
                    var response = await client.GetAsync(gifUrl);
                    response.EnsureSuccessStatusCode();
                    
                    string tempPath = Path.Combine(Path.GetTempPath(), $"gif_{Guid.NewGuid()}.gif");
                    byte[] gifData = await response.Content.ReadAsByteArrayAsync();
                    await File.WriteAllBytesAsync(tempPath, gifData);
                    
                    Dispatcher.Invoke(() =>
                    {
                        FileProgressContainer.Visibility = Visibility.Collapsed;
                    });

                    // Gửi file đã download
                    await SendFileAsync(tempPath);
                    
                    // Xóa file tạm sau khi gửi
                    try { File.Delete(tempPath); } catch { }
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    FileProgressContainer.Visibility = Visibility.Collapsed;
                    AddSystemMessage($"❌ Lỗi khi tải GIF: {ex.Message}");
                });
            }
        }

        // ================= VOICE RECORDING (Facebook Style) =================
        // Nhấn giữ để bắt đầu ghi âm
        private void VoiceButton_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_stream == null)
            {
                MessageBox.Show("Vui lòng kết nối đến server trước", "Chưa kết nối", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Capture mouse để đảm bảo MouseUp được gọi
            if (sender is Button btn)
            {
                btn.CaptureMouse();
            }

            e.Handled = true;
            StartVoiceRecording();
        }

        // Thả nút để dừng và gửi
        private void VoiceButton_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Button btn)
            {
                btn.ReleaseMouseCapture();
            }

            if (_isRecordingVoice)
            {
                StopVoiceRecording();
            }

            e.Handled = true;
        }

        // Rời chuột khỏi nút cũng dừng ghi
        private void VoiceButton_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_isRecordingVoice)
            {
                StopVoiceRecording();
            }
        }

        private void StartVoiceRecording()
        {
            try
            {
                if (_isRecordingVoice) return;

                _isRecordingVoice = true;
                _voiceRecordingStartTime = DateTime.Now;
                _voiceRecordingPath = Path.Combine(Path.GetTempPath(), $"voice_{Guid.NewGuid()}.wav");

                // Khởi tạo WaveInEvent để ghi âm
                _waveIn = new NAudio.Wave.WaveInEvent
                {
                    WaveFormat = new NAudio.Wave.WaveFormat(44100, 1) // 44.1kHz, Mono
                };

                // Tạo WaveFileWriter để ghi vào file
                _waveWriter = new NAudio.Wave.WaveFileWriter(_voiceRecordingPath, _waveIn.WaveFormat);

                // Xử lý dữ liệu audio
                _waveIn.DataAvailable += WaveIn_DataAvailable;
                _waveIn.RecordingStopped += WaveIn_RecordingStopped;

                // Bắt đầu ghi âm
                _waveIn.StartRecording();

                // Cập nhật UI
                btnSendVoice.Content = "⏹";
                btnSendVoice.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 53, 69));
                btnSendVoice.ToolTip = "Đang ghi âm... Thả ra để gửi";
                
                // Hiển thị progress bar với thông báo
                FileProgressContainer.Visibility = Visibility.Visible;
                txtFileProgress.Text = "Đang ghi âm...";
                txtFileProgressPercent.Text = "00:00";
                fileProgressBar.Value = 0;

                // Bắt đầu timer để hiển thị thời gian
                _voiceTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _voiceTimer.Tick += VoiceTimer_Tick;
                _voiceTimer.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khi bắt đầu ghi âm: {ex.Message}\n\nĐảm bảo microphone đã được kết nối và cho phép.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                _isRecordingVoice = false;
            }
        }

        private void StopVoiceRecording()
        {
            try
            {
                if (!_isRecordingVoice) return;

                _isRecordingVoice = false;
                _voiceTimer?.Stop();

                // Dừng ghi âm
                _waveIn?.StopRecording();
                
                // Đợi một chút để đảm bảo dữ liệu được ghi xong
                System.Threading.Thread.Sleep(100);

                _waveIn?.Dispose();
                _waveIn = null;

                _waveWriter?.Dispose();
                _waveWriter = null;

                // Cập nhật UI
                btnSendVoice.Content = "🎤";
                btnSendVoice.Background = System.Windows.Media.Brushes.Transparent;
                btnSendVoice.ToolTip = "Nhấn giữ để ghi âm";

                // Kiểm tra thời gian ghi âm (tối thiểu 0.5 giây)
                TimeSpan duration = DateTime.Now - _voiceRecordingStartTime;
                if (duration.TotalSeconds < 0.5)
                {
                    // Quá ngắn, hủy
                    FileProgressContainer.Visibility = Visibility.Collapsed;
                    if (!string.IsNullOrEmpty(_voiceRecordingPath) && File.Exists(_voiceRecordingPath))
                    {
                        try { File.Delete(_voiceRecordingPath); } catch { }
                    }
                    _voiceRecordingPath = null;
                    return;
                }

                // Ẩn progress bar
                FileProgressContainer.Visibility = Visibility.Collapsed;

                // Gửi file voice
                if (!string.IsNullOrEmpty(_voiceRecordingPath) && File.Exists(_voiceRecordingPath))
                {
                    _ = Task.Run(async () => await SendFileAsync(_voiceRecordingPath));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khi dừng ghi âm: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void WaveIn_DataAvailable(object? sender, NAudio.Wave.WaveInEventArgs e)
        {
            // Ghi dữ liệu audio vào file
            _waveWriter?.Write(e.Buffer, 0, e.BytesRecorded);
        }

        private void WaveIn_RecordingStopped(object? sender, NAudio.Wave.StoppedEventArgs e)
        {
            // Đảm bảo file được đóng đúng cách
            _waveWriter?.Dispose();
            _waveWriter = null;
            _waveIn?.Dispose();
            _waveIn = null;
        }

        private void VoiceTimer_Tick(object? sender, EventArgs e)
        {
            if (_isRecordingVoice)
            {
                TimeSpan duration = DateTime.Now - _voiceRecordingStartTime;
                txtFileProgressPercent.Text = $"{duration.Minutes:D2}:{duration.Seconds:D2}";
                
                // Cập nhật progress bar (giả sử tối đa 60 giây)
                double progress = Math.Min((duration.TotalSeconds / 60.0) * 100, 100);
                fileProgressBar.Value = progress;
            }
        }

        private bool IsImageFile(string extension)
        {
            // Không bao gồm .gif vì GIF có nút riêng
            string[] imageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".webp", ".ico" };
            return Array.Exists(imageExtensions, ext => ext.Equals(extension, StringComparison.OrdinalIgnoreCase));
        }

        // Thêm file message vào chat
        private void AddFileMessage(string sender, string fileName, long fileSize, string filePath, bool isImage, bool isOwn = false)
        {
            string time = DateTime.Now.ToString("HH:mm");
            
            // Xác định message text dựa trên loại file
            string messageText;
            string extension = Path.GetExtension(fileName).ToLower();
            if (extension == ".gif")
            {
                messageText = "🎬 GIF"; // GIF có icon riêng
            }
            else if (isImage)
            {
                messageText = "📷 Image";
            }
            else
            {
                messageText = $"📎 {fileName}";
            }
            
            var msgModel = new MessageModel
            {
                SenderName = sender,
                Message = messageText,
                Time = time,
                IsOwnMessage = isOwn,
                IsSystemMessage = false,
                IsFileMessage = true,
                FileName = fileName,
                FileSize = fileSize,
                FilePath = filePath,
                IsImage = isImage // GIF cũng được đánh dấu là image để hiển thị
            };

            _messages.Add(msgModel);
            ScrollToBottom();
        }

        // Xử lý khi click vào ảnh để mở
        private void Image_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Controls.Image image && image.DataContext is MessageModel msg)
            {
                if (!string.IsNullOrEmpty(msg.FilePath) && File.Exists(msg.FilePath))
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = msg.FilePath,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Cannot open file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        // ================= MESSAGE PROCESSING =================
        // Xử lý message nhận được từ server
        private void ProcessReceivedMessage(string rawMessage)
        {
            // Parse message từ server: [HH:mm:ss] username: message hoặc [HH:mm:ss] [SYSTEM] message
            // Ví dụ: [14:30:15] [SYSTEM] Alice joined the chat
            // Ví dụ: [14:30:20] Alice: Hello everyone!

            if (rawMessage.Contains("[SYSTEM]"))
            {
                // System message
                string systemMsg = rawMessage.Substring(rawMessage.IndexOf("[SYSTEM]") + 9).Trim();
                
                // Bỏ qua system message về file transfer (file sẽ hiển thị trực tiếp)
                if (systemMsg.Contains("sent a file:"))
                {
                    return; // Không hiển thị system message này
                }
                
                AddSystemMessage(systemMsg);
            }
            else
            {
                // Regular message - parse format: [HH:mm:ss] username: message
                var match = Regex.Match(rawMessage, @"\[(\d{2}:\d{2}:\d{2})\]\s*(.+?):\s*(.+)");
                if (match.Success)
                {
                    string time = match.Groups[1].Value;
                    string sender = match.Groups[2].Value;
                    string message = match.Groups[3].Value;

                    // Kiểm tra xem có phải tin nhắn của mình không
                    bool isOwn = sender.Equals(_currentUsername, StringComparison.OrdinalIgnoreCase);
                    
                    if (isOwn)
                    {
                        // Tin nhắn của mình (đã hiển thị khi gửi, nhưng có thể update lại với timestamp từ server)
                        // Hoặc có thể bỏ qua vì đã hiển thị rồi
                    }
                    else
                    {
                        // Tin nhắn của người khác (hiển thị bên trái)
                        AddOtherMessage(sender, message, time);
                    }
                }
                else
                {
                    // Fallback: hiển thị như system message nếu không parse được
                    AddSystemMessage(rawMessage);
                }
            }
        }

        // Thêm tin nhắn của mình (bên phải)
        private void AddOwnMessage(string message)
        {
            if (string.IsNullOrEmpty(_currentUsername)) return;
            
            string time = DateTime.Now.ToString("HH:mm");
            var msgModel = new MessageModel
            {
                SenderName = _currentUsername ?? "",
                Message = message,
                Time = time,
                IsOwnMessage = true,
                IsSystemMessage = false
            };
            
            _messages.Add(msgModel);
            System.Diagnostics.Debug.WriteLine($"Added own message: {message}, Total messages: {_messages.Count}");
            ScrollToBottom();
        }

        // Thêm tin nhắn của người khác (bên trái)
        private void AddOtherMessage(string sender, string message, string time)
        {
            // Chuyển time từ HH:mm:ss sang HH:mm
            if (time.Length > 5)
            {
                time = time.Substring(0, 5);
            }

            var msgModel = new MessageModel
            {
                SenderName = sender,
                Message = message,
                Time = time,
                IsOwnMessage = false,
                IsSystemMessage = false
            };
            
            _messages.Add(msgModel);
            ScrollToBottom();
        }

        // Thêm system message
        private void AddSystemMessage(string message)
        {
            var msgModel = new MessageModel
            {
                SenderName = "",
                Message = message,
                Time = "",
                IsOwnMessage = false,
                IsSystemMessage = true
            };
            
            _messages.Add(msgModel);
            ScrollToBottom();
        }

        // Tự động scroll xuống cuối
        private void ScrollToBottom()
        {
            // Scroll ScrollViewer xuống cuối
            MessagesScrollViewer.ScrollToEnd();
            
            // Đảm bảo UI được update
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                MessagesScrollViewer.ScrollToEnd();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }
}
