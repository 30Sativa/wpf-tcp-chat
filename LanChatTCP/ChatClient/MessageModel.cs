using System;

namespace ChatClient
{
    public class MessageModel
    {
        public string SenderName { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Time { get; set; } = string.Empty;
        public bool IsOwnMessage { get; set; }
        public bool IsSystemMessage { get; set; }
        public bool IsFileMessage { get; set; }
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public bool IsImage { get; set; }
        public bool IsAudio { get; set; }
        public bool IsAudioPlaying { get; set; }
        public string AudioDuration { get; set; } = string.Empty;
        public double? FileProgress { get; set; } // 0.0 to 1.0

        // Convenience flag to hide the generic file bubble for image/audio
        public bool ShowFileInfo => IsFileMessage && !IsImage && !IsAudio;
    }
}
