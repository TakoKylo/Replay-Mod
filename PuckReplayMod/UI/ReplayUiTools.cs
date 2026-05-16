using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuckReplayMod
{
    internal static class ReplayUiTools
    {
        public static readonly Color PanelColor = new Color(0.196f, 0.196f, 0.196f, 0.98f);
        public static readonly Color HeaderColor = new Color(0.14f, 0.14f, 0.14f, 1f);
        public static readonly Color ControlColor = new Color(0.25f, 0.25f, 0.25f, 1f);
        public static readonly Color FieldColor = new Color(0.15f, 0.15f, 0.15f, 1f);
        public static readonly Color BodyTextColor = new Color(0.82f, 0.86f, 0.9f, 1f);
        public static readonly Color MutedTextColor = new Color(0.68f, 0.72f, 0.76f, 1f);

        public static VisualElement CreateConfigurationRow()
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.marginTop = 5f;
            row.style.marginBottom = 5f;
            row.style.minHeight = 32f;
            return row;
        }

        public static Label CreateConfigurationLabel(string text)
        {
            return CreateConfigurationLabel(text, null);
        }

        public static Label CreateConfigurationLabel(string text, string tooltip)
        {
            Label label = new Label(text);
            label.style.fontSize = 15f;
            label.style.color = BodyTextColor;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.flexGrow = 1f;
            if (!string.IsNullOrEmpty(tooltip))
            {
                label.tooltip = tooltip;
            }

            return label;
        }

        public static Label CreateSectionTitle(string text)
        {
            Label label = new Label(text);
            label.style.fontSize = 24f;
            label.style.color = Color.white;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginBottom = 8f;
            return label;
        }

        public static Label CreateHeader(string text)
        {
            Label label = new Label("<b>" + text + "</b>");
            label.style.fontSize = 18f;
            label.style.color = Color.white;
            label.style.marginTop = 10f;
            label.style.marginBottom = 6f;
            return label;
        }

        public static Label CreateNote(string text)
        {
            Label label = CreateConfigurationLabel(text);
            label.style.fontSize = 13f;
            label.style.color = MutedTextColor;
            label.style.marginBottom = 8f;
            return label;
        }

        public static VisualElement CreateSeparator()
        {
            VisualElement separator = new VisualElement();
            separator.style.height = 1f;
            separator.style.backgroundColor = new Color(0.38f, 0.38f, 0.38f, 1f);
            separator.style.marginTop = 14f;
            separator.style.marginBottom = 14f;
            return separator;
        }

        public static Button CreateButton(string text, Action onClick)
        {
            Button button = new Button(onClick)
            {
                text = text
            };
            StyleConfigButton(button);
            return button;
        }

        public static Button CreateSidebarButton(string text, Action onClick)
        {
            Button button = new Button(onClick)
            {
                text = text
            };
            button.style.height = 48f;
            button.style.minHeight = 48f;
            button.style.maxHeight = 48f;
            button.style.width = new StyleLength(new Length(100f, LengthUnit.Percent));
            button.style.unityTextAlign = TextAnchor.MiddleLeft;
            button.style.paddingLeft = 15f;
            StyleConfigButton(button);
            button.style.fontSize = 17f;
            return button;
        }

        public static void SetSidebarButtonSelected(Button button, bool selected)
        {
            if (button == null)
            {
                return;
            }

            button.userData = selected;
            button.style.backgroundColor = selected ? new StyleColor(new Color(0.7f, 0.7f, 0.7f, 1f)) : new StyleColor(ControlColor);
            button.style.color = selected ? Color.black : Color.white;
        }

        public static void StyleConfigButton(Button button)
        {
            button.style.backgroundColor = new StyleColor(ControlColor);
            button.style.color = Color.white;
            button.style.unityTextAlign = TextAnchor.MiddleCenter;
            button.style.fontSize = 15f;
            button.style.paddingTop = 7f;
            button.style.paddingBottom = 7f;
            button.style.paddingLeft = 14f;
            button.style.paddingRight = 14f;
            button.style.borderTopWidth = 0f;
            button.style.borderBottomWidth = 0f;
            button.style.borderLeftWidth = 0f;
            button.style.borderRightWidth = 0f;
            AddHoverEffectsForButton(button);
        }

        public static void AddHoverEffectsForButton(Button button)
        {
            button.RegisterCallback<MouseEnterEvent>(delegate
            {
                button.style.backgroundColor = Color.white;
                button.style.color = Color.black;
            });
            button.RegisterCallback<MouseLeaveEvent>(delegate
            {
                bool selected = button.userData is bool && (bool)button.userData;
                button.style.backgroundColor = selected ? new StyleColor(new Color(0.7f, 0.7f, 0.7f, 1f)) : new StyleColor(ControlColor);
                button.style.color = selected ? Color.black : Color.white;
            });
        }

        public static Toggle CreateToggle(bool value, Action<bool> onChanged)
        {
            Toggle toggle = new Toggle();
            toggle.value = value;
            StyleToggle(toggle);
            toggle.RegisterValueChangedCallback(delegate(ChangeEvent<bool> evt)
            {
                onChanged(evt.newValue);
            });
            return toggle;
        }

        public static PopupField<string> CreateDropdown(List<string> choices, string value, Action<string> onChanged)
        {
            PopupField<string> dropdown = new PopupField<string>(choices, choices.Contains(value) ? value : choices[0], null, null);
            dropdown.style.width = 210f;
            dropdown.style.minWidth = 210f;
            dropdown.style.maxWidth = 210f;
            dropdown.style.fontSize = 14f;
            dropdown.style.height = 32f;
            dropdown.style.minHeight = 32f;
            dropdown.style.maxHeight = 32f;
            dropdown.style.overflow = Overflow.Hidden;
            dropdown.style.backgroundColor = new StyleColor(FieldColor);
            dropdown.style.color = Color.white;
            dropdown.style.paddingLeft = 8f;
            dropdown.RegisterValueChangedCallback(delegate(ChangeEvent<string> evt)
            {
                onChanged(evt.newValue);
            });
            StyleDropdown(dropdown);
            return dropdown;
        }

        public static IntegerField CreateIntegerField(int value, Action<int> onChanged)
        {
            IntegerField field = new IntegerField();
            field.value = value;
            field.style.width = 210f;
            field.style.minWidth = 210f;
            field.style.maxWidth = 210f;
            field.RegisterValueChangedCallback(delegate(ChangeEvent<int> evt)
            {
                onChanged(evt.newValue);
            });
            StyleTextField(field);
            return field;
        }

        public static VisualElement CreateToggleRow(string labelText, bool value, Action<bool> onChanged)
        {
            return CreateToggleRow(labelText, null, value, onChanged);
        }

        public static VisualElement CreateToggleRow(string labelText, string tooltip, bool value, Action<bool> onChanged)
        {
            VisualElement row = CreateConfigurationRow();
            row.tooltip = tooltip ?? string.Empty;
            row.Add(CreateConfigurationLabel(labelText, tooltip));
            row.Add(CreateToggle(value, onChanged));
            return row;
        }

        public static VisualElement CreateDropdownRow(string labelText, string value, List<string> choices, Action<string> onChanged)
        {
            return CreateDropdownRow(labelText, null, value, choices, onChanged);
        }

        public static VisualElement CreateDropdownRow(string labelText, string tooltip, string value, List<string> choices, Action<string> onChanged)
        {
            VisualElement row = CreateConfigurationRow();
            row.tooltip = tooltip ?? string.Empty;
            row.Add(CreateConfigurationLabel(labelText, tooltip));
            PopupField<string> dropdown = CreateDropdown(choices, value, onChanged);
            dropdown.tooltip = tooltip ?? string.Empty;
            row.Add(dropdown);
            return row;
        }

        public static VisualElement CreateIntegerRow(string labelText, int value, Action<int> onChanged)
        {
            return CreateIntegerRow(labelText, null, value, onChanged);
        }

        public static VisualElement CreateIntegerRow(string labelText, string tooltip, int value, Action<int> onChanged)
        {
            VisualElement row = CreateConfigurationRow();
            row.tooltip = tooltip ?? string.Empty;
            row.Add(CreateConfigurationLabel(labelText, tooltip));
            IntegerField field = CreateIntegerField(value, onChanged);
            field.tooltip = tooltip ?? string.Empty;
            row.Add(field);
            return row;
        }

        public static void StyleMenuAccessButton(Button referenceButton, Button button)
        {
            if (referenceButton == null || button == null)
            {
                return;
            }

            foreach (string className in referenceButton.GetClasses())
            {
                button.AddToClassList(className);
            }

            if (!float.IsNaN(referenceButton.resolvedStyle.width) && referenceButton.resolvedStyle.width > 0f)
            {
                button.style.width = referenceButton.resolvedStyle.width;
            }

            if (!float.IsNaN(referenceButton.resolvedStyle.height) && referenceButton.resolvedStyle.height > 0f)
            {
                button.style.height = referenceButton.resolvedStyle.height;
                button.style.marginLeft = referenceButton.resolvedStyle.marginLeft;
                button.style.marginRight = referenceButton.resolvedStyle.marginRight;
                button.style.marginTop = referenceButton.resolvedStyle.marginTop;
                button.style.marginBottom = referenceButton.resolvedStyle.marginBottom;
                button.style.paddingLeft = referenceButton.resolvedStyle.paddingLeft;
                button.style.paddingRight = referenceButton.resolvedStyle.paddingRight;
                button.style.paddingTop = referenceButton.resolvedStyle.paddingTop;
                button.style.paddingBottom = referenceButton.resolvedStyle.paddingBottom;
            }

            button.style.fontSize = referenceButton.resolvedStyle.fontSize;
            button.style.unityTextAlign = referenceButton.resolvedStyle.unityTextAlign;
            button.style.unityFontStyleAndWeight = referenceButton.resolvedStyle.unityFontStyleAndWeight;
            AddHoverEffectsForButton(button);
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes >= 1024L * 1024L * 1024L)
            {
                return (bytes / (1024f * 1024f * 1024f)).ToString("0.00") + " GB";
            }

            if (bytes >= 1024L * 1024L)
            {
                return (bytes / (1024f * 1024f)).ToString("0.0") + " MB";
            }

            return (bytes / 1024f).ToString("0.0") + " KB";
        }

        private static void StyleToggle(Toggle toggle)
        {
            toggle.RegisterCallback<AttachToPanelEvent>(delegate
            {
                VisualElement input = toggle.Q<VisualElement>(null, "unity-toggle__input");
                if (input == null)
                {
                    return;
                }

                input.style.backgroundColor = new StyleColor(FieldColor);
                input.style.borderTopColor = new StyleColor(new Color(0.4f, 0.4f, 0.4f, 1f));
                input.style.borderBottomColor = new StyleColor(new Color(0.4f, 0.4f, 0.4f, 1f));
                input.style.borderLeftColor = new StyleColor(new Color(0.4f, 0.4f, 0.4f, 1f));
                input.style.borderRightColor = new StyleColor(new Color(0.4f, 0.4f, 0.4f, 1f));
            });
        }

        private static void StyleTextField(IntegerField field)
        {
            field.RegisterCallback<AttachToPanelEvent>(delegate
            {
                VisualElement input = field.Q<VisualElement>(null, "unity-base-text-field__input");
                if (input == null)
                {
                    return;
                }

                input.style.backgroundColor = new StyleColor(FieldColor);
                input.style.color = Color.white;
                input.style.paddingLeft = 8f;
                input.style.paddingRight = 8f;
                input.style.paddingTop = 4f;
                input.style.paddingBottom = 4f;
            });
        }

        private static void StyleDropdown(VisualElement dropdown)
        {
            dropdown.RegisterCallback<AttachToPanelEvent>(delegate
            {
                VisualElement input = dropdown.Q<VisualElement>(null, "unity-base-popup-field__input");
                if (input != null)
                {
                    input.style.backgroundColor = new StyleColor(FieldColor);
                    input.style.color = Color.white;
                    input.style.flexDirection = FlexDirection.Row;
                    input.style.justifyContent = Justify.SpaceBetween;
                    input.style.alignItems = Align.Center;
                    input.style.paddingLeft = 0f;
                    input.style.paddingRight = 8f;
                }
            });

            dropdown.RegisterCallback<MouseDownEvent>(delegate
            {
                dropdown.schedule.Execute((Action)delegate
                {
                    StyleDropdownPopover(dropdown);
                }).ExecuteLater(2L);
            });
        }

        private static void StyleDropdownPopover(VisualElement dropdown)
        {
            IPanel panel = dropdown.panel;
            VisualElement visualTree = panel != null ? panel.visualTree : null;
            if (visualTree == null)
            {
                return;
            }

            VisualElement popup = visualTree.Q<VisualElement>(null, "unity-base-dropdown");
            if (popup == null)
            {
                return;
            }

            VisualElement inner = popup.Q<VisualElement>(null, "unity-base-dropdown__container-inner");
            if (inner != null)
            {
                inner.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f, 0.96f);
                inner.style.borderTopWidth = 1f;
                inner.style.borderBottomWidth = 1f;
                inner.style.borderLeftWidth = 1f;
                inner.style.borderRightWidth = 1f;
                inner.style.borderTopColor = new Color(0.7f, 0.7f, 0.7f, 1f);
                inner.style.borderBottomColor = new Color(0.7f, 0.7f, 0.7f, 1f);
                inner.style.borderLeftColor = new Color(0.7f, 0.7f, 0.7f, 1f);
                inner.style.borderRightColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            }

            foreach (VisualElement item in popup.Query<VisualElement>(null, "unity-base-dropdown__item").Build())
            {
                item.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.92f);
                item.style.alignItems = Align.Center;
                item.style.justifyContent = Justify.Center;
                item.style.minHeight = 32f;
                item.style.paddingTop = 0f;
                item.style.paddingBottom = 0f;
                item.style.paddingLeft = 12f;
                item.style.paddingRight = 12f;

                VisualElement capturedItem = item;
                capturedItem.RegisterCallback<MouseEnterEvent>(delegate
                {
                    capturedItem.style.backgroundColor = Color.white;
                    Label itemLabel = capturedItem.Q<Label>(null, "unity-base-dropdown__label");
                    if (itemLabel != null)
                    {
                        itemLabel.style.color = Color.black;
                    }
                });
                capturedItem.RegisterCallback<MouseLeaveEvent>(delegate
                {
                    capturedItem.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.92f);
                    Label itemLabel = capturedItem.Q<Label>(null, "unity-base-dropdown__label");
                    if (itemLabel != null)
                    {
                        itemLabel.style.color = Color.white;
                    }
                });

                Label label = item.Q<Label>(null, "unity-base-dropdown__label");
                if (label != null)
                {
                    label.style.color = Color.white;
                    label.style.fontSize = 14f;
                    label.style.flexGrow = 1f;
                    label.style.unityTextAlign = TextAnchor.MiddleLeft;
                }
            }
        }

    }
}
