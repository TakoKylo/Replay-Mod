using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuckReplayMod
{
    internal static class ReplayHotkeySettingsSection
    {
        public static void Create(ReplayModUiService ui, VisualElement parent)
        {
            parent.Add(ReplayUiTools.CreateSectionTitle("Hotkeys"));
            parent.Add(ReplayUiTools.CreateNote("Choose the keys Replay Mod listens for while you are in game."));

            parent.Add(ReplayUiTools.CreateToggleRow(
                "Manual recording",
                "Lets you start or stop recording while you are in a match. If auto-record is on, stopping manually pauses auto-recording until you leave that match.",
                ui.Settings.EnableManualRecordingHotkey,
                delegate(bool value)
            {
                ui.Settings.EnableManualRecordingHotkey = value;
                ui.SaveSettings();
            }));

            parent.Add(CreateKeybindRow(
                "Start/stop recording",
                "Press this key while in a match to manually start or stop recording.",
                ui.Settings.ManualRecordingKey,
                delegate(KeyCode value)
            {
                ui.Settings.ManualRecordingKey = value;
                ui.SaveSettings();
            }));

            parent.Add(CreateKeybindRow(
                "Add marker",
                "Press this while recording to mark an important moment for later playback/editor tools.",
                ui.Settings.MarkerKey,
                delegate(KeyCode value)
            {
                ui.Settings.MarkerKey = value;
                ui.SaveSettings();
            }));

            parent.Add(ReplayUiTools.CreateSeparator());
            parent.Add(ReplayUiTools.CreateHeader("Playback Controls"));

            parent.Add(CreateKeybindRow(
                "Playback controls focus",
                "While watching a replay, this switches the mouse between camera look and clicking playback controls. Movement keys still move the observer.",
                ui.Settings.PlaybackUiInputKey,
                delegate(KeyCode value)
            {
                ui.Settings.PlaybackUiInputKey = value;
                ui.SaveSettings();
            }));

            parent.Add(ReplayUiTools.CreateDropdownRow(
                "UI input behavior",
                "Toggle keeps controls active until you press the key again. Hold only activates controls while the key is held.",
                FormatPlaybackUiInputMode(ui.Settings.PlaybackUiInputMode),
                GetPlaybackUiInputModeChoices(),
                delegate(string value)
            {
                ui.Settings.PlaybackUiInputMode = ParsePlaybackUiInputMode(value);
                ui.SaveSettings();
            }));

            parent.Add(ReplayUiTools.CreateSeparator());
            parent.Add(ReplayUiTools.CreateHeader("Display"));

            parent.Add(CreateKeybindRow(
                "Toggle status badge",
                "Press this key to quickly show or hide the Replay Mod status badge.",
                ui.Settings.ToggleStatusBadgeKey,
                delegate(KeyCode value)
            {
                ui.Settings.ToggleStatusBadgeKey = value;
                ui.SaveSettings();
            }));

            parent.Add(CreateKeybindRow(
                "Toggle replay time",
                "Press this key to quickly show or hide the replay time display while watching a replay.",
                ui.Settings.ToggleReplayTimeKey,
                delegate(KeyCode value)
            {
                ui.Settings.ToggleReplayTimeKey = value;
                ui.SaveSettings();
            }));
        }

        private static VisualElement CreateKeybindRow(string labelText, string tooltip, KeyCode currentKey, Action<KeyCode> onChanged)
        {
            VisualElement row = ReplayUiTools.CreateConfigurationRow();
            row.tooltip = tooltip ?? string.Empty;
            row.Add(ReplayUiTools.CreateConfigurationLabel(labelText, tooltip));

            Button button = ReplayUiTools.CreateButton(FormatKey(currentKey), null);
            button.tooltip = tooltip ?? string.Empty;
            button.style.width = 210f;
            button.style.minWidth = 210f;
            button.style.maxWidth = 210f;
            button.style.height = 32f;
            button.style.minHeight = 32f;
            button.style.maxHeight = 32f;
            button.userData = currentKey;
            button.clicked += delegate
            {
                BeginKeyCapture(button, onChanged);
            };

            row.Add(button);
            return row;
        }

        private static void BeginKeyCapture(Button button, Action<KeyCode> onChanged)
        {
            button.text = "Press a key...";
            button.Focus();

            EventCallback<KeyDownEvent> callback = null;
            callback = delegate(KeyDownEvent evt)
            {
                if (evt.keyCode == KeyCode.None)
                {
                    return;
                }

                if (evt.keyCode == KeyCode.Escape)
                {
                    button.text = FormatKey((KeyCode)button.userData);
                    button.UnregisterCallback(callback);
                    evt.StopPropagation();
                    return;
                }

                KeyCode keyCode = evt.keyCode;
                button.userData = keyCode;
                button.text = FormatKey(keyCode);
                button.UnregisterCallback(callback);
                onChanged(keyCode);
                evt.StopPropagation();
            };

            button.RegisterCallback(callback);
        }

        private static string FormatKey(KeyCode keyCode)
        {
            switch (keyCode)
            {
                case KeyCode.LeftAlt:
                    return "Left Alt";
                case KeyCode.RightAlt:
                    return "Right Alt";
                case KeyCode.LeftControl:
                    return "Left Ctrl";
                case KeyCode.RightControl:
                    return "Right Ctrl";
                case KeyCode.LeftShift:
                    return "Left Shift";
                case KeyCode.RightShift:
                    return "Right Shift";
                default:
                    return keyCode.ToString();
            }
        }

        private static List<string> GetPlaybackUiInputModeChoices()
        {
            return new List<string>
            {
                "Toggle",
                "Hold"
            };
        }

        private static string FormatPlaybackUiInputMode(ReplayPlaybackUiInputMode mode)
        {
            return mode == ReplayPlaybackUiInputMode.Hold ? "Hold" : "Toggle";
        }

        private static ReplayPlaybackUiInputMode ParsePlaybackUiInputMode(string value)
        {
            return string.Equals(value, "Hold", StringComparison.OrdinalIgnoreCase) ? ReplayPlaybackUiInputMode.Hold : ReplayPlaybackUiInputMode.Toggle;
        }
    }
}
