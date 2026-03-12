using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace BaoMiHuaPatch
{
    internal static class SettingsPageUiPatch
    {
        private const string SettingKey = "Values/ExternalPlayerPath";
        private const string SectionTag = "BaoMiHua.ExternalPlayer.Section";
        private const string TextBoxTag = "BaoMiHua.ExternalPlayer.PathTextBox";
        private const string StatusTag = "BaoMiHua.ExternalPlayer.StatusText";

        internal static void EnsureExternalPlayerSection(object settingsPage)
        {
            FrameworkElement page = settingsPage as FrameworkElement;
            if (page == null)
            {
                return;
            }

            page.Loaded -= new RoutedEventHandler(OnSettingsPageLoaded);
            page.Loaded += new RoutedEventHandler(OnSettingsPageLoaded);
        }

        private static void OnSettingsPageLoaded(object sender, RoutedEventArgs e)
        {
            FrameworkElement page = sender as FrameworkElement;
            if (page == null)
            {
                return;
            }

            page.Loaded -= new RoutedEventHandler(OnSettingsPageLoaded);
            FrameworkElement anchor = ResolveAnchorElement(page);
            Panel container = ResolveVisibleContainer(page, anchor);
            if (container == null || FindTaggedDescendant(container, SectionTag) != null)
            {
                return;
            }

            FrameworkElement section = BuildSection();
            InsertSection(container, section, anchor);
        }

        private static FrameworkElement BuildSection()
        {
            StackPanel section = new StackPanel
            {
                Tag = SectionTag,
                Spacing = 8,
                Margin = new Thickness(0, 24, 0, 0)
            };

            TextBlock title = new TextBlock
            {
                Text = "外部播放器",
                FontSize = 18
            };

            TextBlock description = new TextBlock
            {
                Text = "配置 PotPlayer 或其他播放器的 exe 路径。留空时继续使用内置播放器。",
                TextWrapping = TextWrapping.WrapWholeWords,
                Opacity = 0.78
            };

            Border card = new Border
            {
                Padding = new Thickness(16, 14, 16, 14),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1)
            };

            Grid cardGrid = new Grid
            {
                RowSpacing = 10
            };
            cardGrid.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto
            });
            cardGrid.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto
            });

            Grid row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1.0, GridUnitType.Star)
            });
            row.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto
            });
            row.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto
            });

            TextBox pathTextBox = new TextBox
            {
                Tag = TextBoxTag,
                PlaceholderText = "例如：C:\\Program Files\\DAUM\\PotPlayer\\PotPlayerMini64.exe",
                Text = GetConfiguredPath() ?? string.Empty,
                MinWidth = 360
            };
            Grid.SetColumn(pathTextBox, 0);

            Button saveButton = new Button
            {
                Content = "保存",
                Margin = new Thickness(12, 0, 0, 0)
            };
            saveButton.Click += new RoutedEventHandler(OnSaveClicked);
            Grid.SetColumn(saveButton, 1);

            Button clearButton = new Button
            {
                Content = "清空",
                Margin = new Thickness(8, 0, 0, 0)
            };
            clearButton.Click += new RoutedEventHandler(OnClearClicked);
            Grid.SetColumn(clearButton, 2);

            TextBlock statusTextBlock = new TextBlock
            {
                Tag = StatusTag,
                Text = BuildInitialStatus(pathTextBox.Text),
                TextWrapping = TextWrapping.WrapWholeWords,
                Opacity = 0.72
            };
            Grid.SetRow(statusTextBlock, 1);

            row.Children.Add(pathTextBox);
            row.Children.Add(saveButton);
            row.Children.Add(clearButton);

            cardGrid.Children.Add(row);
            cardGrid.Children.Add(statusTextBlock);
            card.Child = cardGrid;

            section.Children.Add(title);
            section.Children.Add(description);
            section.Children.Add(card);

            return section;
        }

        private static string BuildInitialStatus(string configuredPath)
        {
            string normalizedPath = NormalizeStoredString(configuredPath);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return "当前未配置外部播放器，播放时会继续使用内置播放器。";
            }

            string expandedPath = Environment.ExpandEnvironmentVariables(normalizedPath);
            if (File.Exists(expandedPath))
            {
                return "当前已配置外部播放器，下一次点播放会优先走该播放器。";
            }

            return "当前保存的播放器路径不存在，播放时会自动回退到内置播放器。";
        }

        private static void OnSaveClicked(object sender, RoutedEventArgs e)
        {
            FrameworkElement section = FindParentSection(sender as DependencyObject);
            TextBox pathTextBox = FindTaggedDescendant(section, TextBoxTag) as TextBox;
            TextBlock statusTextBlock = FindTaggedDescendant(section, StatusTag) as TextBlock;
            if (pathTextBox == null || statusTextBlock == null)
            {
                return;
            }

            string candidatePath = NormalizeStoredString(pathTextBox.Text);
            if (!string.IsNullOrWhiteSpace(candidatePath))
            {
                candidatePath = Environment.ExpandEnvironmentVariables(candidatePath);
                if (!File.Exists(candidatePath))
                {
                    statusTextBlock.Text = "保存失败：播放器路径不存在，请检查 exe 路径。";
                    return;
                }
            }

            if (SaveConfiguredPath(candidatePath))
            {
                pathTextBox.Text = candidatePath ?? string.Empty;
                statusTextBlock.Text = string.IsNullOrWhiteSpace(candidatePath)
                    ? "已清空外部播放器配置，播放时会使用内置播放器。"
                    : "已保存外部播放器路径，重新点播放即可生效。";
            }
            else
            {
                statusTextBlock.Text = "保存失败：无法写入本地配置文件。";
            }
        }

        private static void OnClearClicked(object sender, RoutedEventArgs e)
        {
            FrameworkElement section = FindParentSection(sender as DependencyObject);
            TextBox pathTextBox = FindTaggedDescendant(section, TextBoxTag) as TextBox;
            TextBlock statusTextBlock = FindTaggedDescendant(section, StatusTag) as TextBlock;
            if (pathTextBox == null || statusTextBlock == null)
            {
                return;
            }

            if (SaveConfiguredPath(string.Empty))
            {
                pathTextBox.Text = string.Empty;
                statusTextBlock.Text = "已清空外部播放器配置，播放时会使用内置播放器。";
            }
            else
            {
                statusTextBlock.Text = "清空失败：无法写入本地配置文件。";
            }
        }

        private static FrameworkElement FindParentSection(DependencyObject dependencyObject)
        {
            while (dependencyObject != null)
            {
                FrameworkElement element = dependencyObject as FrameworkElement;
                if (element != null && Equals(element.Tag, SectionTag))
                {
                    return element;
                }

                dependencyObject = VisualTreeHelper.GetParent(dependencyObject);
            }

            return null;
        }

        private static FrameworkElement ResolveAnchorElement(FrameworkElement page)
        {
            FrameworkElement anchor = FindElementByText(page, "硬件加速");
            if (anchor != null)
            {
                return anchor;
            }

            return FindFirstSettingsAnchor(page);
        }

        private static Panel ResolveVisibleContainer(FrameworkElement page, FrameworkElement anchor)
        {
            StackPanel anchorStackPanel = FindTopmostStackPanelAncestor(anchor, page);
            if (IsLikelySettingsContainer(anchorStackPanel))
            {
                return anchorStackPanel;
            }

            StackPanel reflectedStackPanel = FindPrivateStackPanelFieldValue(page);
            if (reflectedStackPanel != null)
            {
                return reflectedStackPanel;
            }

            ScrollViewer scrollViewer = FindPrivateScrollViewerFieldValue(page);
            if (scrollViewer == null)
            {
                scrollViewer = FindFirstScrollViewer(page);
            }

            if (scrollViewer != null)
            {
                StackPanel contentStackPanel = UnwrapPanel(scrollViewer.Content) as StackPanel;
                if (IsLikelySettingsContainer(contentStackPanel))
                {
                    return contentStackPanel;
                }

                StackPanel descendantPanel = FindFirstStackPanel(scrollViewer);
                if (IsLikelySettingsContainer(descendantPanel))
                {
                    return descendantPanel;
                }
            }

            StackPanel pageStackPanel = FindFirstStackPanel(page);
            if (IsLikelySettingsContainer(pageStackPanel))
            {
                return pageStackPanel;
            }

            return null;
        }

        private static void InsertSection(Panel container, FrameworkElement section, FrameworkElement anchor)
        {
            FrameworkElement anchorChild = FindDirectChild(container, anchor);
            if (anchorChild != null)
            {
                int insertIndex = container.Children.IndexOf(anchorChild) + 1;
                if (insertIndex > 0)
                {
                    container.Children.Insert(insertIndex, section);
                    return;
                }
            }

            container.Children.Add(section);
        }

        private static Panel UnwrapPanel(object content)
        {
            Panel panel = content as Panel;
            if (panel != null)
            {
                return panel;
            }

            ContentControl contentControl = content as ContentControl;
            if (contentControl != null)
            {
                return UnwrapPanel(contentControl.Content);
            }

            return null;
        }

        private static FrameworkElement FindDirectChild(Panel container, FrameworkElement anchor)
        {
            if (container == null || anchor == null)
            {
                return null;
            }

            DependencyObject current = anchor;
            while (current != null)
            {
                DependencyObject parent = VisualTreeHelper.GetParent(current);
                if (parent == container)
                {
                    return current as FrameworkElement;
                }

                current = parent;
            }

            return null;
        }

        private static StackPanel FindTopmostStackPanelAncestor(DependencyObject start, FrameworkElement page)
        {
            if (start == null)
            {
                return null;
            }

            StackPanel match = null;
            DependencyObject current = start;
            while (current != null && current != page)
            {
                StackPanel panel = current as StackPanel;
                if (panel != null)
                {
                    match = panel;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return match;
        }

        private static StackPanel FindFirstStackPanel(DependencyObject root)
        {
            if (root == null)
            {
                return null;
            }

            StackPanel stackPanel = root as StackPanel;
            if (stackPanel != null)
            {
                return stackPanel;
            }

            int childrenCount = VisualTreeHelper.GetChildrenCount(root);
            for (int index = 0; index < childrenCount; index++)
            {
                StackPanel match = FindFirstStackPanel(VisualTreeHelper.GetChild(root, index));
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        private static ScrollViewer FindFirstScrollViewer(DependencyObject root)
        {
            if (root == null)
            {
                return null;
            }

            ScrollViewer scrollViewer = root as ScrollViewer;
            if (scrollViewer != null)
            {
                return scrollViewer;
            }

            int childrenCount = VisualTreeHelper.GetChildrenCount(root);
            for (int index = 0; index < childrenCount; index++)
            {
                ScrollViewer match = FindFirstScrollViewer(VisualTreeHelper.GetChild(root, index));
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        private static FrameworkElement FindFirstSettingsAnchor(DependencyObject root)
        {
            if (root == null)
            {
                return null;
            }

            // 优先找设置项自身的交互控件，避免把补丁插进根 Grid 导致更新后叠层。
            FrameworkElement element = root as FrameworkElement;
            if (element != null &&
                (element is CheckBox || element is ToggleSwitch))
            {
                return element;
            }

            int childrenCount = VisualTreeHelper.GetChildrenCount(root);
            for (int index = 0; index < childrenCount; index++)
            {
                FrameworkElement match = FindFirstSettingsAnchor(VisualTreeHelper.GetChild(root, index));
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        private static StackPanel FindPrivateStackPanelFieldValue(object instance)
        {
            if (instance == null)
            {
                return null;
            }

            FieldInfo[] fields = instance.GetType().GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            for (int index = 0; index < fields.Length; index++)
            {
                StackPanel value = fields[index].GetValue(instance) as StackPanel;
                if (IsLikelySettingsContainer(value))
                {
                    return value;
                }
            }

            return null;
        }

        private static ScrollViewer FindPrivateScrollViewerFieldValue(object instance)
        {
            if (instance == null)
            {
                return null;
            }

            FieldInfo[] fields = instance.GetType().GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            for (int index = 0; index < fields.Length; index++)
            {
                ScrollViewer value = fields[index].GetValue(instance) as ScrollViewer;
                if (value != null)
                {
                    return value;
                }
            }

            return null;
        }

        private static bool IsLikelySettingsContainer(StackPanel panel)
        {
            return panel != null &&
                panel.Children.Count > 0 &&
                FindFirstSettingsAnchor(panel) != null;
        }

        private static FrameworkElement FindElementByText(DependencyObject root, string text)
        {
            if (root == null)
            {
                return null;
            }

            TextBlock textBlock = root as TextBlock;
            if (textBlock != null && string.Equals(textBlock.Text, text, StringComparison.Ordinal))
            {
                return textBlock;
            }

            int childrenCount = VisualTreeHelper.GetChildrenCount(root);
            for (int index = 0; index < childrenCount; index++)
            {
                FrameworkElement match = FindElementByText(VisualTreeHelper.GetChild(root, index), text);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        private static FrameworkElement FindTaggedDescendant(DependencyObject root, string tag)
        {
            if (root == null)
            {
                return null;
            }

            FrameworkElement element = root as FrameworkElement;
            if (element != null && Equals(element.Tag, tag))
            {
                return element;
            }

            int childrenCount = VisualTreeHelper.GetChildrenCount(root);
            for (int index = 0; index < childrenCount; index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, index);
                FrameworkElement match = FindTaggedDescendant(child, tag);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        private static object GetPrivateFieldValue(object instance, string fieldName)
        {
            if (instance == null)
            {
                return null;
            }

            FieldInfo fieldInfo = instance.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return fieldInfo != null ? fieldInfo.GetValue(instance) : null;
        }

        private static string GetConfiguredPath()
        {
            string storedValue = ReadSettingValue();
            return NormalizeStoredString(storedValue);
        }

        private static bool SaveConfiguredPath(string value)
        {
            string settingsPath = ResolveLocalSettingsPath();
            if (string.IsNullOrWhiteSpace(settingsPath))
            {
                return false;
            }

            try
            {
                string directoryPath = Path.GetDirectoryName(settingsPath);
                if (!string.IsNullOrWhiteSpace(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                Dictionary<string, string> settings = new Dictionary<string, string>(StringComparer.Ordinal);
                if (File.Exists(settingsPath))
                {
                    string currentJson = File.ReadAllText(settingsPath);
                    Dictionary<string, string> currentSettings = JsonSerializer.Deserialize<Dictionary<string, string>>(currentJson);
                    if (currentSettings != null)
                    {
                        settings = currentSettings;
                    }
                }

                settings[SettingKey] = EncodeStoredString(value ?? string.Empty);
                string outputJson = JsonSerializer.Serialize(settings);
                File.WriteAllText(settingsPath, outputJson);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string ReadSettingValue()
        {
            string settingsPath = ResolveLocalSettingsPath();
            if (string.IsNullOrWhiteSpace(settingsPath) || !File.Exists(settingsPath))
            {
                return null;
            }

            try
            {
                using (FileStream stream = File.OpenRead(settingsPath))
                using (JsonDocument document = JsonDocument.Parse(stream))
                {
                    JsonElement root = document.RootElement;
                    JsonElement valueElement;
                    if (root.ValueKind == JsonValueKind.Object &&
                        root.TryGetProperty(SettingKey, out valueElement) &&
                        valueElement.ValueKind == JsonValueKind.String)
                    {
                        return valueElement.GetString();
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static string ResolveLocalSettingsPath()
        {
            string applicationDataFolder = "BaoMiHua/ApplicationData";
            string localSettingsFile = "LocalSettings.json";

            try
            {
                string appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
                if (File.Exists(appSettingsPath))
                {
                    using (FileStream stream = File.OpenRead(appSettingsPath))
                    using (JsonDocument document = JsonDocument.Parse(stream))
                    {
                        JsonElement root = document.RootElement;
                        JsonElement optionsElement;
                        if (root.ValueKind == JsonValueKind.Object &&
                            root.TryGetProperty("LocalSettingsOptions", out optionsElement) &&
                            optionsElement.ValueKind == JsonValueKind.Object)
                        {
                            JsonElement folderElement;
                            if (optionsElement.TryGetProperty("ApplicationDataFolder", out folderElement) &&
                                folderElement.ValueKind == JsonValueKind.String &&
                                !string.IsNullOrWhiteSpace(folderElement.GetString()))
                            {
                                applicationDataFolder = folderElement.GetString();
                            }

                            JsonElement fileElement;
                            if (optionsElement.TryGetProperty("LocalSettingsFile", out fileElement) &&
                                fileElement.ValueKind == JsonValueKind.String &&
                                !string.IsNullOrWhiteSpace(fileElement.GetString()))
                            {
                                localSettingsFile = fileElement.GetString();
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, applicationDataFolder, localSettingsFile);
        }

        private static string NormalizeStoredString(string value)
        {
            string trimmed = value == null ? null : value.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return string.Empty;
            }

            if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"')
            {
                return trimmed.Substring(1, trimmed.Length - 2);
            }

            return trimmed;
        }

        private static string EncodeStoredString(string value)
        {
            return "\"" + (value ?? string.Empty) + "\"";
        }
    }
}
