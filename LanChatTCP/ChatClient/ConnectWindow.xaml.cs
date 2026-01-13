using System;
using System.Windows;
using System.Windows.Input;

namespace ChatClient
{
    /// <summary>
    /// Interaction logic for ConnectWindow.xaml
    /// </summary>
    public partial class ConnectWindow : Window
    {
        public ConnectWindow()
        {
            InitializeComponent();
            txtName.Focus();
        }

        private void Connect_Click(object sender, RoutedEventArgs e)
        {
            Connect();
        }

        private void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Connect();
            }
        }

        private void Connect()
        {
            string username = txtName.Text.Trim();
            string ip = txtIP.Text.Trim();

            if (!int.TryParse(txtPort.Text.Trim(), out int port))
            {
                MessageBox.Show("Invalid port", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                txtPort.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(username))
            {
                MessageBox.Show("Please enter username", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                txtName.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(ip))
            {
                MessageBox.Show("Please enter server IP", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                txtIP.Focus();
                return;
            }

            // Mở cửa sổ chat và tự động connect
            MainWindow chat = new MainWindow(username, ip, port);
            chat.Show();

            // Đóng cửa sổ connect
            Close();
        }
    }
}
