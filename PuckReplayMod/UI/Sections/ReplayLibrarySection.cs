using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuckReplayMod
{
    internal static class ReplayLibrarySection
    {
        public static void Create(ReplayModUiService ui, VisualElement parent)
        {
            parent.style.flexGrow = 1f;
            parent.style.paddingTop = 12f;
            parent.style.paddingBottom = 10f;

            VisualElement header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.marginBottom = 8f;
            parent.Add(header);

            Label title = ReplayUiTools.CreateSectionTitle("Replay Library");
            title.style.marginBottom = 0f;
            header.Add(title);

            Button refreshButton = ReplayUiTools.CreateButton("REFRESH", delegate
            {
                ui.RefreshLibraryText();
                ui.RefreshReplayList();
                ui.RefreshPlaybackStatus();
            });
            refreshButton.style.width = 104f;
            refreshButton.style.minWidth = 104f;
            refreshButton.style.height = 34f;
            refreshButton.style.minHeight = 34f;
            header.Add(refreshButton);

            VisualElement libraryBody = new VisualElement();
            libraryBody.style.flexDirection = FlexDirection.Row;
            libraryBody.style.flexGrow = 1f;
            parent.Add(libraryBody);

            VisualElement listFrame = new VisualElement
            {
                name = "PuckReplayModReplayListFrame"
            };
            listFrame.style.flexGrow = 1f;
            listFrame.style.minHeight = 360f;
            listFrame.style.backgroundColor = new Color(0.11f, 0.11f, 0.11f, 0.95f);
            listFrame.style.borderTopWidth = 1f;
            listFrame.style.borderBottomWidth = 1f;
            listFrame.style.borderLeftWidth = 1f;
            listFrame.style.borderRightWidth = 1f;
            listFrame.style.borderTopColor = new Color(0.32f, 0.32f, 0.32f, 1f);
            listFrame.style.borderBottomColor = new Color(0.32f, 0.32f, 0.32f, 1f);
            listFrame.style.borderLeftColor = new Color(0.32f, 0.32f, 0.32f, 1f);
            listFrame.style.borderRightColor = new Color(0.32f, 0.32f, 0.32f, 1f);
            libraryBody.Add(listFrame);

            ScrollView replayScrollView = new ScrollView(ScrollViewMode.Vertical)
            {
                name = "PuckReplayModReplayListScroll"
            };
            replayScrollView.style.flexGrow = 1f;
            replayScrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            listFrame.Add(replayScrollView);

            ui.ReplayList = new VisualElement
            {
                name = "PuckReplayModReplayList"
            };
            ui.ReplayList.style.paddingLeft = 4f;
            ui.ReplayList.style.paddingRight = 4f;
            ui.ReplayList.style.paddingTop = 4f;
            ui.ReplayList.style.paddingBottom = 4f;
            replayScrollView.Add(ui.ReplayList);

            VisualElement miniPanel = CreateLibraryMiniPanel(ui);
            libraryBody.Add(miniPanel);

            ui.RefreshLibraryText();
            ui.RefreshPlaybackStatus();
            ui.RefreshReplayList();
        }

        private static VisualElement CreateLibraryMiniPanel(ReplayModUiService ui)
        {
            VisualElement miniPanel = new VisualElement
            {
                name = "PuckReplayModLibraryMiniPanel"
            };
            miniPanel.style.width = 214f;
            miniPanel.style.minWidth = 214f;
            miniPanel.style.marginLeft = 10f;
            miniPanel.style.paddingLeft = 10f;
            miniPanel.style.paddingRight = 10f;
            miniPanel.style.paddingTop = 10f;
            miniPanel.style.paddingBottom = 10f;
            miniPanel.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f, 0.98f);
            miniPanel.style.borderTopWidth = 1f;
            miniPanel.style.borderBottomWidth = 1f;
            miniPanel.style.borderLeftWidth = 1f;
            miniPanel.style.borderRightWidth = 1f;
            miniPanel.style.borderTopColor = new Color(0.32f, 0.32f, 0.32f, 1f);
            miniPanel.style.borderBottomColor = new Color(0.32f, 0.32f, 0.32f, 1f);
            miniPanel.style.borderLeftColor = new Color(0.32f, 0.32f, 0.32f, 1f);
            miniPanel.style.borderRightColor = new Color(0.32f, 0.32f, 0.32f, 1f);

            Label nowPlayingTitle = ReplayUiTools.CreateHeader("Now Playing");
            nowPlayingTitle.style.marginTop = 0f;
            nowPlayingTitle.style.marginBottom = 4f;
            miniPanel.Add(nowPlayingTitle);

            ui.PlaybackLabel = ReplayUiTools.CreateConfigurationLabel(string.Empty);
            ui.PlaybackLabel.style.fontSize = 13f;
            ui.PlaybackLabel.style.color = Color.white;
            ui.PlaybackLabel.style.marginBottom = 12f;
            miniPanel.Add(ui.PlaybackLabel);

            VisualElement separator = ReplayUiTools.CreateSeparator();
            separator.style.marginTop = 4f;
            separator.style.marginBottom = 10f;
            miniPanel.Add(separator);

            Label libraryTitle = ReplayUiTools.CreateHeader("Library");
            libraryTitle.style.marginTop = 0f;
            libraryTitle.style.marginBottom = 4f;
            miniPanel.Add(libraryTitle);

            ui.StorageLabel = ReplayUiTools.CreateConfigurationLabel(string.Empty);
            ui.StorageLabel.style.fontSize = 13f;
            ui.StorageLabel.style.color = ReplayUiTools.BodyTextColor;
            miniPanel.Add(ui.StorageLabel);

            return miniPanel;
        }

        public static void RefreshReplayList(ReplayModUiService ui)
        {
            if (ui.ReplayList == null)
            {
                return;
            }

            ui.ReplayList.Clear();
            List<ReplayFileSummary> replays = ui.Reader.GetRecentReplays(ui.Storage.ReplaysDirectory, ui.Storage.SummariesDirectory, 40);
            if (replays.Count == 0)
            {
                Label emptyLabel = ReplayUiTools.CreateConfigurationLabel("No saved replays yet. Join a server with recording enabled, then leave to save your first replay.");
                emptyLabel.style.color = ReplayUiTools.MutedTextColor;
                ui.ReplayList.Add(emptyLabel);
                return;
            }

            foreach (ReplayFileSummary replay in replays)
            {
                ui.ReplayList.Add(CreateReplayRow(ui, replay));
            }
        }

        private static VisualElement CreateReplayRow(ReplayModUiService ui, ReplayFileSummary replay)
        {
            VisualElement container = new VisualElement();
            container.style.marginTop = 2f;
            container.style.backgroundColor = new Color(0.16f, 0.16f, 0.16f, 0.95f);

            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.minHeight = 42f;
            row.style.paddingLeft = 8f;
            row.style.paddingRight = 8f;
            row.style.paddingTop = 3f;
            row.style.paddingBottom = 3f;
            container.Add(row);

            VisualElement details = new VisualElement();
            details.style.flexDirection = FlexDirection.Column;
            details.style.flexGrow = 1f;
            details.style.marginRight = 8f;

            Label title = ReplayUiTools.CreateConfigurationLabel(GetDisplayTitle(replay));
            title.style.color = Color.white;
            title.style.fontSize = 14f;
            title.style.marginBottom = 0f;
            title.style.whiteSpace = WhiteSpace.NoWrap;
            details.Add(title);

            string date = replay.LastWriteUtc.ToLocalTime().ToString("MM/dd/yyyy HH:mm");
            string duration = replay.IsMetadataComplete ? FormatDuration(replay.DurationSeconds) : "Indexing...";
            string sourceName = string.IsNullOrEmpty(replay.DisplayName) ? string.Empty : "    " + GetServerTitle(replay);
            string versionInfo = string.IsNullOrEmpty(replay.GameVersion) ? string.Empty : "    Puck " + replay.GameVersion;
            Label meta = ReplayUiTools.CreateConfigurationLabel(date + "    " + duration + "    " + ReplayUiTools.FormatBytes(replay.SizeBytes) + versionInfo + sourceName);
            meta.style.fontSize = 11f;
            meta.style.color = ReplayUiTools.MutedTextColor;
            meta.style.whiteSpace = WhiteSpace.NoWrap;
            details.Add(meta);
            row.Add(details);

            ReplayFileSummary selectedReplay = replay;
            Button playButton = ReplayUiTools.CreateButton("PLAY", delegate
            {
                ui.PlayReplay(selectedReplay.FilePath);
            });
            playButton.style.width = 74f;
            playButton.style.minWidth = 74f;
            playButton.style.height = 32f;
            playButton.style.minHeight = 32f;
            playButton.style.marginRight = 4f;
            row.Add(playButton);

            VisualElement actionPanel = CreateActionPanel(ui, selectedReplay);
            actionPanel.style.display = DisplayStyle.None;
            container.Add(actionPanel);

            Button actionButton = ReplayUiTools.CreateButton("...", delegate
            {
                actionPanel.style.display = actionPanel.style.display == DisplayStyle.None ? DisplayStyle.Flex : DisplayStyle.None;
            });
            actionButton.style.width = 34f;
            actionButton.style.minWidth = 34f;
            actionButton.style.height = 32f;
            actionButton.style.minHeight = 32f;
            actionButton.style.marginRight = 4f;
            actionButton.style.paddingLeft = 0f;
            actionButton.style.paddingRight = 0f;
            row.Add(actionButton);

            row.Add(CreateFavoriteButton(ui, selectedReplay));

            return container;
        }

        private static Button CreateFavoriteButton(ReplayModUiService ui, ReplayFileSummary replay)
        {
            Button favoriteButton = new Button(delegate
            {
                ui.SetReplayFavorite(replay, !replay.IsFavorite);
            });
            ReplayUiTools.StyleConfigButton(favoriteButton);
            favoriteButton.tooltip = replay.IsFavorite ? "Remove from favorites" : "Add to favorites";
            favoriteButton.text = string.Empty;
            favoriteButton.style.width = 34f;
            favoriteButton.style.minWidth = 34f;
            favoriteButton.style.height = 32f;
            favoriteButton.style.minHeight = 32f;
            favoriteButton.style.alignItems = Align.Center;
            favoriteButton.style.justifyContent = Justify.Center;
            favoriteButton.style.paddingLeft = 5f;
            favoriteButton.style.paddingRight = 5f;
            favoriteButton.style.paddingTop = 5f;
            favoriteButton.style.paddingBottom = 5f;
            favoriteButton.Add(new FavoriteStarIcon(replay.IsFavorite, 20f));
            return favoriteButton;
        }

        private sealed class FavoriteStarIcon : VisualElement
        {
            private readonly bool filled;

            public FavoriteStarIcon(bool filled, float size)
            {
                this.filled = filled;
                base.pickingMode = PickingMode.Ignore;
                base.style.width = size;
                base.style.height = size;
                base.generateVisualContent += this.OnGenerateVisualContent;
            }

            private void OnGenerateVisualContent(MeshGenerationContext context)
            {
                Rect rect = base.contentRect;
                float size = Mathf.Min(rect.width, rect.height);
                if (size <= 0f)
                {
                    return;
                }

                Vector2 center = new Vector2(rect.x + (rect.width * 0.5f), rect.y + (rect.height * 0.5f));
                Vector2[] points = this.CreateStarPoints(center, size * 0.43f, size * 0.2f);
                Painter2D painter = context.painter2D;
                painter.lineJoin = LineJoin.Round;
                painter.lineCap = LineCap.Round;
                painter.lineWidth = Mathf.Max(1.8f, size * 0.075f);
                painter.strokeColor = new Color(1f, 0.78f, 0.16f, 1f);
                painter.fillColor = this.filled ? new Color(1f, 0.67f, 0.08f, 1f) : new Color(0f, 0f, 0f, 0f);

                painter.BeginPath();
                painter.MoveTo(points[0]);
                for (int i = 1; i < points.Length; i++)
                {
                    painter.LineTo(points[i]);
                }

                painter.ClosePath();
                if (this.filled)
                {
                    painter.Fill();
                }

                painter.Stroke();
            }

            private Vector2[] CreateStarPoints(Vector2 center, float outerRadius, float innerRadius)
            {
                Vector2[] points = new Vector2[10];
                for (int i = 0; i < points.Length; i++)
                {
                    float radius = i % 2 == 0 ? outerRadius : innerRadius;
                    float angle = (-90f + (36f * i)) * Mathf.Deg2Rad;
                    points[i] = new Vector2(center.x + (Mathf.Cos(angle) * radius), center.y + (Mathf.Sin(angle) * radius));
                }

                return points;
            }
        }

        private static VisualElement CreateActionPanel(ReplayModUiService ui, ReplayFileSummary replay)
        {
            VisualElement panel = new VisualElement();
            panel.style.flexDirection = FlexDirection.Row;
            panel.style.flexWrap = Wrap.Wrap;
            panel.style.alignItems = Align.Center;
            panel.style.justifyContent = Justify.FlexEnd;
            panel.style.paddingLeft = 10f;
            panel.style.paddingRight = 10f;
            panel.style.paddingTop = 8f;
            panel.style.paddingBottom = 8f;
            panel.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f, 0.98f);

            ShowDefaultActions(ui, replay, panel);
            return panel;
        }

        private static void ShowDefaultActions(ReplayModUiService ui, ReplayFileSummary replay, VisualElement panel)
        {
            panel.Clear();
            panel.style.justifyContent = Justify.FlexEnd;

            Button renameButton = CreateActionButton("RENAME", delegate
            {
                ShowRenameForm(ui, replay, panel);
            });
            panel.Add(renameButton);

            Button openButton = CreateActionButton("OPEN LOCATION", delegate
            {
                ui.OpenReplayLocation(replay);
            });
            openButton.style.width = 140f;
            openButton.style.minWidth = 140f;
            panel.Add(openButton);

            Button copyPathButton = CreateActionButton("COPY PATH", delegate
            {
                ui.CopyReplayPath(replay);
            });
            panel.Add(copyPathButton);

            Button deleteButton = ReplayUiTools.CreateButton("DELETE", delegate
            {
                ShowDeleteConfirmation(ui, replay, panel);
            });
            deleteButton.style.width = 110f;
            deleteButton.style.minWidth = 110f;
            deleteButton.style.marginLeft = 8f;
            panel.Add(deleteButton);
        }

        private static Button CreateActionButton(string text, Action action)
        {
            Button button = ReplayUiTools.CreateButton(text, action);
            button.style.width = 110f;
            button.style.minWidth = 110f;
            button.style.marginLeft = 8f;
            return button;
        }

        private static void ShowRenameForm(ReplayModUiService ui, ReplayFileSummary replay, VisualElement panel)
        {
            panel.Clear();
            panel.style.justifyContent = Justify.SpaceBetween;

            TextField nameField = new TextField();
            nameField.value = string.IsNullOrEmpty(replay.DisplayName) ? GetServerTitle(replay) : replay.DisplayName;
            nameField.style.flexGrow = 1f;
            nameField.style.marginRight = 8f;
            nameField.style.height = 32f;
            nameField.style.color = Color.white;
            Action saveRename = delegate
            {
                ui.RenameReplay(replay, nameField.value);
            };
            nameField.RegisterCallback<KeyDownEvent>(delegate(KeyDownEvent evt)
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    saveRename();
                    evt.StopPropagation();
                }
            });
            nameField.RegisterCallback<AttachToPanelEvent>(delegate
            {
                VisualElement input = nameField.Q<VisualElement>(null, "unity-base-text-field__input");
                if (input != null)
                {
                    input.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f);
                    input.style.color = Color.white;
                    input.style.paddingLeft = 8f;
                    input.style.paddingRight = 8f;
                }
            });
            panel.Add(nameField);
            nameField.schedule.Execute((Action)delegate
            {
                nameField.Focus();
                nameField.SelectAll();
            });

            VisualElement buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.alignItems = Align.Center;
            panel.Add(buttons);

            Button clearButton = ReplayUiTools.CreateButton("CLEAR", delegate
            {
                ui.RenameReplay(replay, string.Empty);
            });
            clearButton.style.width = 84f;
            clearButton.style.minWidth = 84f;
            clearButton.style.marginRight = 8f;
            buttons.Add(clearButton);

            Button cancelButton = ReplayUiTools.CreateButton("CANCEL", delegate
            {
                ShowDefaultActions(ui, replay, panel);
            });
            cancelButton.style.width = 96f;
            cancelButton.style.minWidth = 96f;
            cancelButton.style.marginRight = 8f;
            buttons.Add(cancelButton);

            Button saveButton = ReplayUiTools.CreateButton("SAVE", saveRename);
            saveButton.style.width = 84f;
            saveButton.style.minWidth = 84f;
            buttons.Add(saveButton);
        }

        private static void ShowDeleteConfirmation(ReplayModUiService ui, ReplayFileSummary replay, VisualElement panel)
        {
            panel.Clear();
            panel.style.justifyContent = Justify.SpaceBetween;

            Label confirmation = ReplayUiTools.CreateConfigurationLabel("Delete this replay?");
            confirmation.style.color = Color.white;
            confirmation.style.fontSize = 13f;
            panel.Add(confirmation);

            VisualElement buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.alignItems = Align.Center;
            panel.Add(buttons);

            Button cancelButton = ReplayUiTools.CreateButton("CANCEL", delegate
            {
                ShowDefaultActions(ui, replay, panel);
            });
            cancelButton.style.width = 100f;
            cancelButton.style.minWidth = 100f;
            cancelButton.style.marginRight = 8f;
            buttons.Add(cancelButton);

            Button confirmButton = ReplayUiTools.CreateButton("DELETE REPLAY", delegate
            {
                ui.DeleteReplay(replay);
            });
            confirmButton.style.width = 150f;
            confirmButton.style.minWidth = 150f;
            confirmButton.style.backgroundColor = new Color(0.55f, 0.05f, 0.06f, 1f);
            buttons.Add(confirmButton);
        }

        private static string GetDisplayTitle(ReplayFileSummary replay)
        {
            string title = string.IsNullOrEmpty(replay.DisplayName) ? GetServerTitle(replay) : replay.DisplayName;
            return title;
        }

        private static string GetServerTitle(ReplayFileSummary replay)
        {
            return string.IsNullOrEmpty(replay.ServerName) ? "Unknown Server" : replay.ServerName;
        }

        private static string FormatDuration(float seconds)
        {
            TimeSpan timeSpan = TimeSpan.FromSeconds(Math.Max(0f, seconds));
            if (timeSpan.TotalHours >= 1.0)
            {
                return string.Format("{0:D2}:{1:D2}:{2:D2}", (int)timeSpan.TotalHours, timeSpan.Minutes, timeSpan.Seconds);
            }

            return string.Format("{0:D2}:{1:D2}", timeSpan.Minutes, timeSpan.Seconds);
        }
    }
}
