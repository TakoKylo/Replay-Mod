using System;
using UnityEngine;

namespace PuckReplayMod
{
    public enum ReplayOverlayPosition
    {
        TopRight,
        TopLeft,
        BottomRight,
        BottomLeft
    }

    public enum ReplayIndicatorVisibility
    {
        Always,
        RecordingAndPlayback,
        RecordingOnly,
        Hidden
    }

    public enum ReplayPlaybackUiInputMode
    {
        Toggle,
        Hold
    }

    public class ReplayModSettings
    {
        private const string Prefix = "PuckReplayMod.";

        public bool AutoRecord = true;
        public bool EnableManualRecordingHotkey = true;
        public int CaptureTickRate = 30;
        public int StorageLimitMb = 2048;
        public int MinimumReplayLengthSeconds = 0;
        public KeyCode ManualRecordingKey = KeyCode.F5;
        public KeyCode MarkerKey = KeyCode.F4;
        public KeyCode ToggleStatusBadgeKey = KeyCode.F7;
        public KeyCode ToggleReplayTimeKey = KeyCode.F8;
        public KeyCode PlaybackUiInputKey = KeyCode.LeftAlt;
        public ReplayPlaybackUiInputMode PlaybackUiInputMode = ReplayPlaybackUiInputMode.Toggle;
        public bool EnableLegacyImport = true;
        public bool EnableDebugProfiling = false;
        public bool ShowStatusIndicator = true;
        public ReplayIndicatorVisibility StatusIndicatorVisibility = ReplayIndicatorVisibility.Always;
        public ReplayOverlayPosition StatusIndicatorPosition = ReplayOverlayPosition.BottomLeft;
        public bool ShowPlaybackTimeline = true;
        public ReplayOverlayPosition PlaybackTimelinePosition = ReplayOverlayPosition.BottomLeft;
        public bool ShowReplayChat = true;
        public bool ClearChatOnPlaybackStart = true;

        public static ReplayModSettings Load()
        {
            ReplayModSettings settings = new ReplayModSettings
            {
                AutoRecord = PlayerPrefs.GetInt(Prefix + "AutoRecord", 1) == 1,
                EnableManualRecordingHotkey = PlayerPrefs.GetInt(Prefix + "EnableManualRecordingHotkey", 1) == 1,
                CaptureTickRate = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "CaptureTickRate", 30), 5, 120),
                StorageLimitMb = Mathf.Max(0, PlayerPrefs.GetInt(Prefix + "StorageLimitMb", 2048)),
                MinimumReplayLengthSeconds = Mathf.Max(0, PlayerPrefs.GetInt(Prefix + "MinimumReplayLengthSeconds", 0)),
                EnableLegacyImport = PlayerPrefs.GetInt(Prefix + "EnableLegacyImport", 1) == 1,
                EnableDebugProfiling = PlayerPrefs.GetInt(Prefix + "EnableDebugProfiling", 0) == 1,
                StatusIndicatorVisibility = LoadEnum(Prefix + "StatusIndicatorVisibility", ReplayIndicatorVisibility.Always),
                StatusIndicatorPosition = LoadEnum(Prefix + "StatusIndicatorPosition", ReplayOverlayPosition.BottomLeft),
                PlaybackUiInputMode = LoadEnum(Prefix + "PlaybackUiInputMode", ReplayPlaybackUiInputMode.Toggle),
                ShowPlaybackTimeline = PlayerPrefs.GetInt(Prefix + "ShowPlaybackTimeline", 1) == 1,
                PlaybackTimelinePosition = LoadEnum(Prefix + "PlaybackTimelinePosition", ReplayOverlayPosition.BottomLeft),
                ShowReplayChat = PlayerPrefs.GetInt(Prefix + "ShowReplayChat", 1) == 1,
                ClearChatOnPlaybackStart = PlayerPrefs.GetInt(Prefix + "ClearChatOnPlaybackStart", 1) == 1
            };

            string manualRecordingKey = PlayerPrefs.GetString(Prefix + "ManualRecordingKey", KeyCode.F5.ToString());
            KeyCode manualParsed;
            if (Enum.TryParse(manualRecordingKey, true, out manualParsed))
            {
                settings.ManualRecordingKey = manualParsed;
            }

            string markerKey = PlayerPrefs.GetString(Prefix + "MarkerKey", KeyCode.F4.ToString());
            KeyCode parsed;
            if (Enum.TryParse(markerKey, true, out parsed))
            {
                settings.MarkerKey = parsed;
            }

            string toggleStatusBadgeKey = PlayerPrefs.GetString(Prefix + "ToggleStatusBadgeKey", KeyCode.F7.ToString());
            if (Enum.TryParse(toggleStatusBadgeKey, true, out parsed))
            {
                settings.ToggleStatusBadgeKey = parsed;
            }

            string toggleReplayTimeKey = PlayerPrefs.GetString(Prefix + "ToggleReplayTimeKey", KeyCode.F8.ToString());
            if (Enum.TryParse(toggleReplayTimeKey, true, out parsed))
            {
                settings.ToggleReplayTimeKey = parsed;
            }

            string playbackUiInputKey = PlayerPrefs.GetString(Prefix + "PlaybackUiInputKey", KeyCode.LeftAlt.ToString());
            if (Enum.TryParse(playbackUiInputKey, true, out parsed))
            {
                settings.PlaybackUiInputKey = parsed;
            }

            settings.ShowStatusIndicator = PlayerPrefs.GetInt(Prefix + "ShowStatusIndicator", 1) == 1;

            return settings;
        }

        public void Save()
        {
            PlayerPrefs.SetInt(Prefix + "AutoRecord", this.AutoRecord ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "EnableManualRecordingHotkey", this.EnableManualRecordingHotkey ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "CaptureTickRate", Mathf.Clamp(this.CaptureTickRate, 5, 120));
            PlayerPrefs.SetInt(Prefix + "StorageLimitMb", Mathf.Max(0, this.StorageLimitMb));
            PlayerPrefs.SetInt(Prefix + "MinimumReplayLengthSeconds", Mathf.Max(0, this.MinimumReplayLengthSeconds));
            PlayerPrefs.SetString(Prefix + "ManualRecordingKey", this.ManualRecordingKey.ToString());
            PlayerPrefs.SetString(Prefix + "MarkerKey", this.MarkerKey.ToString());
            PlayerPrefs.SetString(Prefix + "ToggleStatusBadgeKey", this.ToggleStatusBadgeKey.ToString());
            PlayerPrefs.SetString(Prefix + "ToggleReplayTimeKey", this.ToggleReplayTimeKey.ToString());
            PlayerPrefs.SetString(Prefix + "PlaybackUiInputKey", this.PlaybackUiInputKey.ToString());
            PlayerPrefs.SetString(Prefix + "PlaybackUiInputMode", this.PlaybackUiInputMode.ToString());
            PlayerPrefs.SetInt(Prefix + "EnableLegacyImport", this.EnableLegacyImport ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "EnableDebugProfiling", this.EnableDebugProfiling ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "ShowStatusIndicator", this.ShowStatusIndicator ? 1 : 0);
            PlayerPrefs.SetString(Prefix + "StatusIndicatorVisibility", this.StatusIndicatorVisibility.ToString());
            PlayerPrefs.SetString(Prefix + "StatusIndicatorPosition", this.StatusIndicatorPosition.ToString());
            PlayerPrefs.SetInt(Prefix + "ShowPlaybackTimeline", this.ShowPlaybackTimeline ? 1 : 0);
            PlayerPrefs.SetString(Prefix + "PlaybackTimelinePosition", this.PlaybackTimelinePosition.ToString());
            PlayerPrefs.SetInt(Prefix + "ShowReplayChat", this.ShowReplayChat ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "ClearChatOnPlaybackStart", this.ClearChatOnPlaybackStart ? 1 : 0);
            PlayerPrefs.Save();
        }

        private static T LoadEnum<T>(string key, T fallback) where T : struct
        {
            string value = PlayerPrefs.GetString(key, fallback.ToString());
            T parsed;
            if (Enum.TryParse(value, true, out parsed))
            {
                return parsed;
            }

            return fallback;
        }
    }
}
