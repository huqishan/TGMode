using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfApp.Navigation;

namespace WpfApp.Controls
{
    public partial class ModernNavigationBar : UserControl
    {
        // Shared brushes keep the selected and normal states visually consistent.
        private readonly Brush _selectedBackgroundBrush;
        private readonly Brush _selectedBorderBrush;
        private readonly Brush _normalBackgroundBrush = Brushes.Transparent;
        private readonly Brush _normalBorderBrush = Brushes.Transparent;
        private readonly Brush _separatorBrush;
        // Persist each group expansion state across rebuilds.
        private readonly Dictionary<string, bool> _expandedStates = new Dictionary<string, bool>();

        private IEnumerable<ControlInfoDataItem> _itemsSource = Enumerable.Empty<ControlInfoDataItem>();
        private string _paneTitle = "WpfApp";
        private string _searchText = string.Empty;
        private ControlInfoDataItem? _selectedItem;
        private bool _isSettingsSelected;

        public ModernNavigationBar()
        {
            InitializeComponent();
            _selectedBackgroundBrush = (Brush)FindResource("SelectedItemBrush");
            _selectedBorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#58CFF9FF"));
            _separatorBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#18FFFFFF"));
            UpdatePaneTitle();
            UpdateSearchPlaceholder();
            UpdateSettingsVisual();
        }

        public event EventHandler<ModernNavigationSelectionChangedEventArgs>? SelectionChanged;

        public IEnumerable<ControlInfoDataItem> ItemsSource
        {
            get => _itemsSource;
            set
            {
                // Rebuild the navigation tree whenever the caller swaps the source data.
                _itemsSource = value ?? Enumerable.Empty<ControlInfoDataItem>();
                RebuildNavigation();
            }
        }

        public string PaneTitle
        {
            get => _paneTitle;
            set
            {
                _paneTitle = value ?? string.Empty;
                UpdatePaneTitle();
            }
        }

        public ControlInfoDataItem? SelectedItem => _selectedItem;

        public void SelectItem(ControlInfoDataItem? item)
        {
            // Allow the host window to set the initial selection without simulating a click.
            _selectedItem = item;
            _isSettingsSelected = false;
            UpdateSettingsVisual();
            RebuildNavigation();
        }

        private void UpdatePaneTitle()
        {
            if (PaneTitleText is not null)
            {
                PaneTitleText.Text = _paneTitle;
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Search updates the visible item list immediately while typing.
            _searchText = SearchTextBox.Text.Trim();
            UpdateSearchPlaceholder();
            RebuildNavigation();
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            _selectedItem = null;
            _isSettingsSelected = true;
            UpdateSettingsVisual();
            RebuildNavigation();
            SelectionChanged?.Invoke(this, new ModernNavigationSelectionChangedEventArgs(null, true));
        }

        private void UpdateSearchPlaceholder()
        {
            if (SearchPlaceholderText is not null)
            {
                SearchPlaceholderText.Visibility = string.IsNullOrWhiteSpace(SearchTextBox.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        private void RebuildNavigation()
        {
            if (ItemsHost is null)
            {
                return;
            }

            // Rebuild the visual tree directly; the menu is small enough that this stays simple.
            ItemsHost.Children.Clear();

            bool separatorInserted = false;
            bool hasRenderedLeaf = false;

            foreach (ControlInfoDataItem item in _itemsSource.Where(item => item.IsVisibility))
            {
                if (item.Items.Count == 0)
                {
                    if (!Matches(item))
                    {
                        continue;
                    }

                    ItemsHost.Children.Add(CreateLeafButton(item, isChild: false));
                    hasRenderedLeaf = true;
                    continue;
                }

                List<ControlInfoDataItem> visibleChildren = GetVisibleChildren(item).ToList();
                bool showGroup = visibleChildren.Count > 0 || Matches(item);
                if (!showGroup)
                {
                    continue;
                }

                if (!separatorInserted && hasRenderedLeaf)
                {
                    ItemsHost.Children.Add(new Border
                    {
                        Height = 1,
                        Margin = new Thickness(8, 10, 8, 16),
                        Background = _separatorBrush
                    });
                    separatorInserted = true;
                }

                if (visibleChildren.Count == 0)
                {
                    visibleChildren = item.Items.Where(child => child.IsVisibility).ToList();
                }

                ItemsHost.Children.Add(CreateGroupSection(item, visibleChildren));
            }
        }

        private FrameworkElement CreateGroupSection(ControlInfoDataItem group, IReadOnlyList<ControlInfoDataItem> visibleChildren)
        {
            // Search mode forces groups open so matching children do not stay hidden.
            bool isExpanded = string.IsNullOrWhiteSpace(_searchText)
                ? !_expandedStates.TryGetValue(group.UniqueId, out bool storedState) || storedState
                : true;

            Border container = new Border
            {
                Margin = new Thickness(0, 0, 0, 10),
                Background = Brushes.Transparent
            };

            StackPanel root = new StackPanel();
            Button headerButton = CreateNavigationButton(group, isSelected: false, isChild: false, showChevron: true, isExpanded: isExpanded);
            headerButton.Click += (_, __) =>
            {
                _expandedStates[group.UniqueId] = !isExpanded;
                RebuildNavigation();
            };

            root.Children.Add(headerButton);

            if (isExpanded)
            {
                StackPanel childHost = new StackPanel
                {
                    Margin = new Thickness(14, 8, 0, 0)
                };

                foreach (ControlInfoDataItem child in visibleChildren)
                {
                    childHost.Children.Add(CreateLeafButton(child, isChild: true));
                }

                root.Children.Add(childHost);
            }

            container.Child = root;
            return container;
        }

        private Button CreateLeafButton(ControlInfoDataItem item, bool isChild)
        {
            bool isSelected = _selectedItem?.UniqueId == item.UniqueId;
            Button button = CreateNavigationButton(item, isSelected, isChild, showChevron: false, isExpanded: false);
            button.Click += (_, __) =>
            {
                // Only leaf items trigger page activation; groups only toggle expansion.
                _selectedItem = item;
                _isSettingsSelected = false;
                UpdateSettingsVisual();
                RebuildNavigation();
                SelectionChanged?.Invoke(this, new ModernNavigationSelectionChangedEventArgs(item, false));
            };

            return button;
        }

        private Button CreateNavigationButton(
            ControlInfoDataItem item,
            bool isSelected,
            bool isChild,
            bool showChevron,
            bool isExpanded)
        {
            // Build all buttons through one method so group items and leaf items stay aligned.
            Button button = new Button
            {
                Style = (Style)FindResource("NavButtonStyle"),
                Margin = new Thickness(0, 0, 0, 6),
                Background = isSelected ? _selectedBackgroundBrush : _normalBackgroundBrush,
                BorderBrush = isSelected ? _selectedBorderBrush : _normalBorderBrush,
                ToolTip = string.IsNullOrWhiteSpace(item.Description) ? null : item.Description,
                IsEnabled = item.IsEnable
            };

            Grid contentGrid = new Grid();
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition());
            if (showChevron)
            {
                contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            }

            FrameworkElement iconElement = CreateIconElement(item.ImageIconPath);
            iconElement.Margin = isChild ? new Thickness(12, 0, 0, 0) : new Thickness(0);
            Grid.SetColumn(iconElement, 0);
            contentGrid.Children.Add(iconElement);

            StackPanel textStack = new StackPanel
            {
                Margin = new Thickness(14, 0, 0, 0),
                Orientation = Orientation.Vertical
            };

            textStack.Children.Add(new TextBlock
            {
                Text = item.Title,
                FontSize = isChild ? 14 : 15,
                FontWeight = isSelected ? FontWeights.Bold : FontWeights.SemiBold,
                Foreground = Brushes.White
            });

            if (!isChild && !string.IsNullOrWhiteSpace(item.Description))
            {
                textStack.Children.Add(new TextBlock
                {
                    Margin = new Thickness(0, 3, 0, 0),
                    Text = item.Description,
                    FontSize = 11,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#86A8C4"))
                });
            }

            Grid.SetColumn(textStack, 1);
            contentGrid.Children.Add(textStack);

            if (showChevron)
            {
                TextBlock chevron = new TextBlock
                {
                    Margin = new Thickness(12, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 14,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9BC4E4")),
                    Text = isExpanded ? "\u25BE" : "\u25B8"
                };
                Grid.SetColumn(chevron, 2);
                contentGrid.Children.Add(chevron);
            }

            button.Content = contentGrid;
            return button;
        }

        private FrameworkElement CreateIconElement(string imageIconPath)
        {
            if (imageIconPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                // Keep support for bitmap icons as the menu evolves.
                return new Image
                {
                    Width = 18,
                    Height = 18,
                    Stretch = Stretch.Uniform,
                    Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(imageIconPath, UriKind.RelativeOrAbsolute))
                };
            }

            return new TextBlock
            {
                Width = 18,
                Height = 18,
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 18,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                Text = imageIconPath
            };
        }

        private IEnumerable<ControlInfoDataItem> GetVisibleChildren(ControlInfoDataItem group)
        {
            IEnumerable<ControlInfoDataItem> children = group.Items.Where(child => child.IsVisibility);
            if (string.IsNullOrWhiteSpace(_searchText))
            {
                return children;
            }

            return children.Where(Matches);
        }

        private bool Matches(ControlInfoDataItem item)
        {
            if (string.IsNullOrWhiteSpace(_searchText))
            {
                return true;
            }

            // Match both title and description to keep search behavior broad enough.
            return item.Title.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ||
                   (!string.IsNullOrWhiteSpace(item.Description) && item.Description.Contains(_searchText, StringComparison.OrdinalIgnoreCase));
        }

        private void UpdateSettingsVisual()
        {
            if (SettingsButton is null)
            {
                return;
            }

            SettingsButton.Background = _isSettingsSelected ? _selectedBackgroundBrush : _normalBackgroundBrush;
            SettingsButton.BorderBrush = _isSettingsSelected ? _selectedBorderBrush : _normalBorderBrush;
        }
    }
}
