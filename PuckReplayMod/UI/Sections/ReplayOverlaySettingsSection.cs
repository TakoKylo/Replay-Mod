using System.Collections.Generic;
using UnityEngine.UIElements;

namespace PuckReplayMod
{
    internal static class ReplayOverlaySettingsSection
    {
        public static void Create(ReplayModUiService ui, VisualElement parent)
        {
            parent.Add(ReplayUiTools.CreateSectionTitle("On-screen Display"));
            parent.Add(ReplayUiTools.CreateNote("Choose when Replay Mod status appears and where it sits on your screen."));

            parent.Add(ReplayUiTools.CreateToggleRow("Show status badge", ui.Settings.ShowStatusIndicator, delegate(bool value)
            {
                ui.Settings.ShowStatusIndicator = value;
                ui.SaveSettings();
                ui.RefreshStatusIndicator();
            }));

            parent.Add(ReplayUiTools.CreateToggleRow("Show replay time", ui.Settings.ShowPlaybackTimeline, delegate(bool value)
            {
                ui.Settings.ShowPlaybackTimeline = value;
                ui.SaveSettings();
                ui.RefreshTimelineIndicator();
            }));

            parent.Add(ReplayUiTools.CreateToggleRow("Show recorded chat", ui.Settings.ShowReplayChat, delegate(bool value)
            {
                ui.Settings.ShowReplayChat = value;
                ui.SaveSettings();
            }));

            parent.Add(ReplayUiTools.CreateToggleRow("Clear chat before playback", ui.Settings.ClearChatOnPlaybackStart, delegate(bool value)
            {
                ui.Settings.ClearChatOnPlaybackStart = value;
                ui.SaveSettings();
            }));

            parent.Add(ReplayUiTools.CreateSeparator());

            parent.Add(ReplayUiTools.CreateDropdownRow("Status badge", FormatIndicatorVisibility(ui.Settings.StatusIndicatorVisibility), GetIndicatorVisibilityChoices(), delegate(string value)
            {
                ui.Settings.StatusIndicatorVisibility = ParseIndicatorVisibility(value);
                ui.SaveSettings();
                ui.RefreshStatusIndicator();
            }));

            parent.Add(ReplayUiTools.CreateDropdownRow("Status badge position", FormatOverlayPosition(ui.Settings.StatusIndicatorPosition), GetOverlayPositionChoices(), delegate(string value)
            {
                ReplayOverlayPosition parsed = ParseOverlayPosition(value);
                ui.Settings.StatusIndicatorPosition = parsed;
                ui.SaveSettings();
                ui.ApplyOverlayPosition(ui.StatusLabel, parsed, 76f);
            }));

            parent.Add(ReplayUiTools.CreateDropdownRow("Replay time position", FormatOverlayPosition(ui.Settings.PlaybackTimelinePosition), GetOverlayPositionChoices(), delegate(string value)
            {
                ReplayOverlayPosition parsed = ParseOverlayPosition(value);
                ui.Settings.PlaybackTimelinePosition = parsed;
                ui.SaveSettings();
                ui.ApplyOverlayPosition(ui.TimelineLabel, parsed, 110f);
            }));
        }

        private static List<string> GetIndicatorVisibilityChoices()
        {
            return new List<string>
            {
                "Always",
                "Recording and playback",
                "Recording only",
                "Hidden"
            };
        }

        private static List<string> GetOverlayPositionChoices()
        {
            return new List<string>
            {
                "Top right",
                "Top left",
                "Bottom right",
                "Bottom left"
            };
        }

        private static string FormatIndicatorVisibility(ReplayIndicatorVisibility visibility)
        {
            switch (visibility)
            {
                case ReplayIndicatorVisibility.RecordingAndPlayback:
                    return "Recording and playback";
                case ReplayIndicatorVisibility.RecordingOnly:
                    return "Recording only";
                case ReplayIndicatorVisibility.Hidden:
                    return "Hidden";
                default:
                    return "Always";
            }
        }

        private static ReplayIndicatorVisibility ParseIndicatorVisibility(string value)
        {
            switch (value)
            {
                case "Recording and playback":
                    return ReplayIndicatorVisibility.RecordingAndPlayback;
                case "Recording only":
                    return ReplayIndicatorVisibility.RecordingOnly;
                case "Hidden":
                    return ReplayIndicatorVisibility.Hidden;
                default:
                    return ReplayIndicatorVisibility.Always;
            }
        }

        private static string FormatOverlayPosition(ReplayOverlayPosition position)
        {
            switch (position)
            {
                case ReplayOverlayPosition.TopLeft:
                    return "Top left";
                case ReplayOverlayPosition.BottomRight:
                    return "Bottom right";
                case ReplayOverlayPosition.BottomLeft:
                    return "Bottom left";
                default:
                    return "Top right";
            }
        }

        private static ReplayOverlayPosition ParseOverlayPosition(string value)
        {
            switch (value)
            {
                case "Top left":
                    return ReplayOverlayPosition.TopLeft;
                case "Bottom right":
                    return ReplayOverlayPosition.BottomRight;
                case "Bottom left":
                    return ReplayOverlayPosition.BottomLeft;
                default:
                    return ReplayOverlayPosition.TopRight;
            }
        }
    }
}
