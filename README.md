# WPF TCP Chat Application

A real-time chat application using TCP/IP built with WPF (Windows Presentation Foundation) and .NET 10.0. The application consists of 2 components: **Chat Server** and **Chat Client**, allowing multiple users to connect and chat with each other over LAN or Internet.

## 📋 Table of Contents

- [Features](#features)
- [System Requirements](#system-requirements)
- [Installation](#installation)
- [Usage Guide](#usage-guide)
- [Project Structure](#project-structure)
- [Communication Protocol](#communication-protocol)
- [Technologies Used](#technologies-used)

## ✨ Features

### Chat Server
- ✅ Listen for connections from multiple clients simultaneously
- ✅ Manage list of connected clients
- ✅ Broadcast messages to all clients
- ✅ Display real-time log of all activities
- ✅ Notify when users join/leave
- ✅ Timestamp for each message

### Chat Client
- ✅ Connect to server via IP and Port
- ✅ Send/receive real-time messages
- ✅ Modern, user-friendly interface
- ✅ Emoji picker with popular emojis
- ✅ Send messages using Enter key
- ✅ Display connection status
- ✅ Auto-scroll to latest messages
- ✅ File transfer with progress tracking
- ✅ Image sharing support
- ✅ Voice message recording (press and hold)

## 💻 System Requirements

- **Operating System**: Windows 10 or higher
- **.NET Runtime**: .NET 10.0 or higher
- **Network Connection**: For chatting over LAN/Internet

## 🚀 Installation

### 1. Clone repository

```bash
git clone <repository-url>
cd wpf-tcp-chat
```

### 2. Open solution in Visual Studio

```bash
# Open solution file
LanChatTCP.slnx
```

Or use Visual Studio Code with C# Dev Kit extension.

### 3. Build project

```bash
# Build ChatServer
cd LanChatTCP/ChatServer
dotnet build

# Build ChatClient
cd ../ChatClient
dotnet build
```

### 4. Run application

**Step 1**: Start Server
```bash
cd LanChatTCP/ChatServer
dotnet run
```
Or run the `ChatServer.exe` file in the `bin/Debug/net10.0-windows/` directory

**Step 2**: Start Client (can run multiple instances)
```bash
cd LanChatTCP/ChatClient
dotnet run
```
Or run the `ChatClient.exe` file in the `bin/Debug/net10.0-windows/` directory

## 📖 Usage Guide

### Starting Server

1. Open the **ChatServer** application
2. Click the **"Start Server"** button
3. Server will start listening on port **5000**
4. View logs in the window to monitor activity

### Connecting Client

1. Open the **ChatClient** application
2. Enter information:
   - **Username**: Your display name
   - **Server IP**: IP address of the machine running the server (e.g., `127.0.0.1` for localhost, or LAN IP like `192.168.1.100`)
   - **Port**: Server port (default: `5000`)
3. Click the **"Connect"** button
4. When connection is successful, status will display "Connected"

### Sending Messages

1. Type your message in the input field
2. Click the **"Send Message"** button or press **Enter**
3. Message will be sent to all connected clients

### Using Emoji

1. Click the **"▲"** (orange) button to open emoji picker
2. Select the emoji you want to use
3. Emoji will be inserted into the input field
4. Send message as usual

### Sending Files

1. Click the **"📎"** (attachment) button
2. Select a file from your computer
3. Progress bar will show upload status
4. File will be sent to all connected clients

### Recording Voice Messages

1. Press and hold the **"🎤"** (microphone) button
2. Record your message while holding the button
3. Release the button to send the voice message
4. Voice file will be sent to all connected clients

## 📁 Project Structure

```
wpf-tcp-chat/
├── LanChatTCP/
│   ├── ChatServer/          # Server Application
│   │   ├── MainWindow.xaml   # Server UI
│   │   ├── MainWindow.xaml.cs # Server logic
│   │   └── ChatServer.csproj  # Project configuration file
│   │
│   ├── ChatClient/           # Client Application
│   │   ├── MainWindow.xaml   # Client UI
│   │   ├── MainWindow.xaml.cs # Client logic
│   │   └── ChatClient.csproj  # Project configuration file
│   │
│   └── LanChatTCP.slnx       # Solution file
│
└── README.md                 # This guide file
```

## 🔌 Communication Protocol

The application uses a simple TCP-based protocol with the following message format:

### Client → Server

**JOIN Message** (When client connects):
```
JOIN|username
```

**MSG Message** (When client sends message):
```
MSG|username|message content
```

### Server → Client

**Regular message**:
```
[HH:mm:ss] username: message content
```

**System message** (join/leave):
```
[HH:mm:ss] [SYSTEM] username joined the chat
[HH:mm:ss] [SYSTEM] username left 
- **NAudio**: Audio recording and playback for voice messagesthe chat
```

### Examples

- Client sends: `JOIN|Alice` → Server broadcasts: `[14:30:15] [SYSTEM] Alice joined the chat`
- Client sends: `MSG|Alice|Hello everyone!` → Server broadcasts: `[14:30:20] Alice: Hello everyone!`

## 🛠️ Technologies Used

- **.NET 10.0**: Main framework
- **WPF (Windows Presentation Foundation)**: Building user interface
- **TCP/IP**: Network protocol for client-server connection
- **C#**: Programming language
- **XAML**: Markup language for UI

## 📝 Notes

- Server runs on port **5000** by default. Make sure this port is not blocked by firewall.
- To chat over LAN, you need to know the IP address of the machine running the server (use `ipconfig` in CMD to view).
- The application supports multiple clients connecting simultaneousl
- Downloaded files are saved to the **Documents/ChatDownloads** folder.
- Voice messages are automatically saved with timestamp naming.
- Images can be previewed directly in the chat interface.y.
- Each client is handled on a separate thread to ensure no blocking.

## 🔧 Future Development

Some features that could be added in the future:

- [ ] Message encryption
- [ ] Login/registration with database
- [ ] Private messaging
- [ ] Video chat
- [ ] Dark mode
- [ ] Message reactions
- [ ] User presence statust
- [ ] Dark mode

## 📄 License

This project is released as open source, free to use and modify.

---

**Author**: 30 Sativa  
**Created**: 2026
