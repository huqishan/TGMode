using ControlLibrary.Controls.Navigation.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace ControlLibrary.Controls.Navigation.Control
{
    /// <summary>
    /// ModernNavigationBar.xaml 的交互逻辑
    /// </summary>
    public partial class ModernNavigationBar : UserControl
    {
        private const double ExpandedPaneWidth = 320;
        private const double CompactPaneWidth = 88;
        private const string NavSelectedItemBrushKey = "NavSelectedItemBrush";
        private const string NavSelectedBorderBrushKey = "NavSelectedBorderBrush";
        private const string NavDescriptionTextBlockStyleKey = "NavDescriptionTextBlockStyle";
        private const string NavIconBrushKey = "NavIconBrush";
        private const string NavMutedIconBrushKey = "NavMutedIconBrush";
        private const string NavSeparatorBrushKey = "NavSeparatorBrush";
        private const string NavChevronExpandedBackgroundBrushKey = "NavChevronExpandedBackgroundBrush";
        private const string NavChevronCollapsedBackgroundBrushKey = "NavChevronCollapsedBackgroundBrush";
        private const string NavChevronExpandedBorderBrushKey = "NavChevronExpandedBorderBrush";
        private const string NavChevronCollapsedBorderBrushKey = "NavChevronCollapsedBorderBrush";
        private static readonly Duration PaneAnimationDuration = new Duration(TimeSpan.FromMilliseconds(180));

        public static readonly DependencyProperty IsPaneOpenProperty =
            DependencyProperty.Register(
                nameof(IsPaneOpen),
                typeof(bool),
                typeof(ModernNavigationBar),
                new PropertyMetadata(true, OnIsPaneOpenChanged));

        private readonly Brush _normalBackgroundBrush = Brushes.Transparent;
        private readonly Brush _normalBorderBrush = Brushes.Transparent;
        // Persist each group expansion state across rebuilds.
        private readonly Dictionary<string, bool> _expandedStates = new Dictionary<string, bool>();

        private IEnumerable<ControlInfoDataItem> _itemsSource = Enumerable.Empty<ControlInfoDataItem>();
        private string _paneTitle = "WpfApp";
        private string _searchText = string.Empty;
        private string? _compactFlyoutItemId;
        private Button? _compactFlyoutPlacementTarget;
        private ControlInfoDataItem? _selectedItem;
        private bool _isSettingsSelected;

        public ModernNavigationBar()
        {
            InitializeComponent();
            InitializeStaticIcons();
            UpdatePaneTitle();
            UpdateSearchPlaceholder();
            ApplyPaneState(animate: false);
            UpdateSettingsVisual();
        }

        public event EventHandler<ModernNavigationSelectionChangedEventArgs>? SelectionChanged;

        public bool IsPaneOpen
        {
            get => (bool)GetValue(IsPaneOpenProperty);
            set => SetValue(IsPaneOpenProperty, value);
        }

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

        private bool HasActiveSearch => IsPaneOpen && !string.IsNullOrWhiteSpace(_searchText);

        private static void OnIsPaneOpenChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
        {
            if (dependencyObject is ModernNavigationBar navigationBar)
            {
                navigationBar.ApplyPaneState(animate: true);
            }
        }

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

        private void InitializeStaticIcons()
        {
            SearchIconHost.Content = CreateThemedIcon(IconFactory.Search, NavMutedIconBrushKey, 14);

            SettingsIconHost.Content = CreateThemedIcon(IconFactory.Settings, NavIconBrushKey, 18);

            UpdatePaneToggleIcon();
        }

        private void UpdatePaneToggleIcon()
        {
            PaneToggleIconHost.Content = CreateThemedIcon(
                IsPaneOpen ? IconFactory.ArrowLeft : IconFactory.Menu,
                NavIconBrushKey,
                18);
        }

        private void PaneToggleButton_Click(object sender, RoutedEventArgs e)
        {
            IsPaneOpen = !IsPaneOpen;
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
            CloseCompactFlyout();
            _selectedItem = null;
            _isSettingsSelected = true;
            UpdateSettingsVisual();
            RebuildNavigation();
            SelectionChanged?.Invoke(this, new ModernNavigationSelectionChangedEventArgs(null, true));
        }

        private void CompactPanePopup_Closed(object sender, EventArgs e)
        {
            ResetCompactFlyoutButtonHighlight();
            _compactFlyoutPlacementTarget = null;
            _compactFlyoutItemId = null;
            CompactFlyoutItemsHost.Children.Clear();
            CompactFlyoutTitleText.Text = string.Empty;
            CompactFlyoutDescriptionText.Text = string.Empty;
            CompactFlyoutDescriptionText.Visibility = Visibility.Collapsed;

            if (!IsPaneOpen)
            {
                RebuildNavigation();
            }
        }

        private void ShowCompactFlyout(ControlInfoDataItem item, IReadOnlyList<ControlInfoDataItem> visibleChildren, Button placementTarget)
        {
            if (CompactPanePopup.IsOpen && _compactFlyoutItemId == item.UniqueId)
            {
                CloseCompactFlyout();
                return;
            }

            _compactFlyoutItemId = item.UniqueId;
            CompactFlyoutTitleText.Text = item.Title;
            CompactFlyoutDescriptionText.Text = item.Description ?? string.Empty;
            CompactFlyoutDescriptionText.Visibility = string.IsNullOrWhiteSpace(item.Description)
                ? Visibility.Collapsed
                : Visibility.Visible;
            CompactFlyoutSeparator.Visibility = visibleChildren.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            CompactFlyoutItemsHost.Children.Clear();
            foreach (ControlInfoDataItem child in visibleChildren)
            {
                CompactFlyoutItemsHost.Children.Add(CreateCompactFlyoutButton(child));
            }

            ResetCompactFlyoutButtonHighlight();
            _compactFlyoutPlacementTarget = placementTarget;
            ApplyCompactFlyoutButtonHighlight(placementTarget);
            CompactPanePopup.PlacementTarget = placementTarget;
            CompactPanePopup.Placement = PlacementMode.Right;
            CompactPanePopup.IsOpen = true;
        }

        private void CloseCompactFlyout()
        {
            _compactFlyoutItemId = null;
            CompactPanePopup.IsOpen = false;
        }

        private void ApplyPaneState(bool animate)
        {
            CloseCompactFlyout();

            double targetWidth = IsPaneOpen ? ExpandedPaneWidth : CompactPaneWidth;
            if (animate && IsLoaded)
            {
                BeginAnimation(
                    WidthProperty,
                    new DoubleAnimation(targetWidth, PaneAnimationDuration)
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    });
            }
            else
            {
                BeginAnimation(WidthProperty, null);
                Width = targetWidth;
            }

            PaneRoot.Padding = IsPaneOpen
                ? new Thickness(22)
                : new Thickness(14, 22, 14, 22);

            HeaderTextStack.Visibility = IsPaneOpen ? Visibility.Visible : Visibility.Collapsed;
            SearchBorder.Visibility = IsPaneOpen ? Visibility.Visible : Visibility.Collapsed;
            PaneToggleButton.ToolTip = IsPaneOpen ? "Collapse navigation pane" : "Expand navigation pane";
            UpdatePaneToggleIcon();

            SettingsLabelText.Visibility = IsPaneOpen ? Visibility.Visible : Visibility.Collapsed;
            SettingsContentGrid.ColumnDefinitions[1].Width = IsPaneOpen
                ? new GridLength(1, GridUnitType.Star)
                : new GridLength(0);
            SettingsButton.Padding = IsPaneOpen ? new Thickness(14, 12, 14, 12) : new Thickness(0);
            SettingsButton.HorizontalContentAlignment = IsPaneOpen
                ? HorizontalAlignment.Stretch
                : HorizontalAlignment.Center;
            SettingsButton.ToolTip = IsPaneOpen ? null : "Settings";

            RebuildNavigation();
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
                    Border separator = new Border
                    {
                        Height = 1,
                        Margin = new Thickness(8, 10, 8, 16)
                    };
                    SetBrushResource(separator, Border.BackgroundProperty, NavSeparatorBrushKey);
                    ItemsHost.Children.Add(separator);
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
            // Keep matches visible during search, but default every group to collapsed otherwise.
            bool isExpanded = HasActiveSearch
                ? true
                : _expandedStates.TryGetValue(group.UniqueId, out bool storedState) && storedState;

            Border container = new Border
            {
                Margin = new Thickness(0, 0, 0, 10),
                Background = Brushes.Transparent
            };

            StackPanel root = new StackPanel();
            Button headerButton = CreateNavigationButton(
                group,
                isSelected: !IsPaneOpen && (IsSelectedGroup(group) || IsCompactFlyoutOpenFor(group)),
                isChild: false,
                showChevron: IsPaneOpen,
                isExpanded: isExpanded);
            headerButton.Click += (_, __) =>
            {
                if (!IsPaneOpen)
                {
                    ShowCompactFlyout(group, visibleChildren, headerButton);
                    return;
                }

                _expandedStates[group.UniqueId] = !isExpanded;
                RebuildNavigation();
            };

            root.Children.Add(headerButton);

            if (IsPaneOpen && isExpanded)
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
                CloseCompactFlyout();
                _selectedItem = item;
                _isSettingsSelected = false;
                UpdateSettingsVisual();
                RebuildNavigation();
                SelectionChanged?.Invoke(this, new ModernNavigationSelectionChangedEventArgs(item, false));
            };

            return button;
        }

        private Button CreateCompactFlyoutButton(ControlInfoDataItem item)
        {
            bool isSelected = _selectedItem?.UniqueId == item.UniqueId;
            Button button = new Button
            {
                Style = (Style)FindResource("NavButtonStyle"),
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(14, 12, 14, 12),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                ToolTip = string.IsNullOrWhiteSpace(item.Description) ? null : item.Description,
                IsEnabled = item.IsEnable
            };
            ApplyButtonSelectionVisual(button, isSelected);

            Grid contentGrid = new Grid();
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition());

            FrameworkElement iconElement = CreateNavigationItemIcon(item);
            Grid.SetColumn(iconElement, 0);
            contentGrid.Children.Add(iconElement);

            StackPanel textStack = new StackPanel
            {
                Margin = new Thickness(14, 0, 0, 0),
                Orientation = Orientation.Vertical
            };

            TextBlock titleText = new TextBlock
            {
                Text = item.Title,
                FontSize = 14,
                FontWeight = isSelected ? FontWeights.Bold : FontWeights.SemiBold
            };
            textStack.Children.Add(titleText);

            Grid.SetColumn(textStack, 1);
            contentGrid.Children.Add(textStack);

            button.Content = contentGrid;
            button.Click += (_, __) =>
            {
                CloseCompactFlyout();
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
                IsEnabled = item.IsEnable
            };
            ApplyButtonSelectionVisual(button, isSelected);
            button.Padding = IsPaneOpen ? new Thickness(14, 12, 14, 12) : new Thickness(0);
            button.HorizontalContentAlignment = IsPaneOpen
                ? HorizontalAlignment.Stretch
                : HorizontalAlignment.Center;
            button.MinHeight = IsPaneOpen ? 0 : 52;
            button.ToolTip = IsPaneOpen
                ? (string.IsNullOrWhiteSpace(item.Description) ? null : item.Description)
                : item.Title;

            Grid contentGrid = new Grid();
            contentGrid.HorizontalAlignment = IsPaneOpen ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (IsPaneOpen)
            {
                contentGrid.ColumnDefinitions.Add(new ColumnDefinition());
            }

            if (showChevron && IsPaneOpen)
            {
                contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            }

            FrameworkElement iconElement = CreateNavigationItemIcon(item);
            iconElement.Margin = isChild && IsPaneOpen ? new Thickness(12, 0, 0, 0) : new Thickness(0);
            Grid.SetColumn(iconElement, 0);
            contentGrid.Children.Add(iconElement);

            if (IsPaneOpen)
            {
                StackPanel textStack = new StackPanel
                {
                    Margin = new Thickness(14, 0, 0, 0),
                    Orientation = Orientation.Vertical
                };

                TextBlock titleText = new TextBlock
                {
                    Text = item.Title,
                    FontSize = isChild ? 14 : 15,
                    FontWeight = isSelected ? FontWeights.Bold : FontWeights.SemiBold
                };
                textStack.Children.Add(titleText);

                if (!isChild && !string.IsNullOrWhiteSpace(item.Description))
                {
                    TextBlock descriptionText = new TextBlock
                    {
                        Margin = new Thickness(0, 3, 0, 0),
                        Style = (Style)FindResource(NavDescriptionTextBlockStyleKey),
                        Text = item.Description
                    };
                    textStack.Children.Add(descriptionText);
                }

                Grid.SetColumn(textStack, 1);
                contentGrid.Children.Add(textStack);
            }

            if (showChevron && IsPaneOpen)
            {
                Border chevronContainer = new Border
                {
                    Margin = new Thickness(12, 0, 0, 0),
                    Padding = new Thickness(6),
                    VerticalAlignment = VerticalAlignment.Center,
                    CornerRadius = new CornerRadius(10),
                    BorderThickness = new Thickness(1),
                    Child = CreateThemedIcon(
                        isExpanded ? IconFactory.ChevronDown : IconFactory.ChevronRight,
                        NavIconBrushKey,
                        14)
                };
                SetBrushResource(
                    chevronContainer,
                    Border.BackgroundProperty,
                    isExpanded ? NavChevronExpandedBackgroundBrushKey : NavChevronCollapsedBackgroundBrushKey);
                SetBrushResource(
                    chevronContainer,
                    Border.BorderBrushProperty,
                    isExpanded ? NavChevronExpandedBorderBrushKey : NavChevronCollapsedBorderBrushKey);

                Grid.SetColumn(chevronContainer, 2);
                contentGrid.Children.Add(chevronContainer);
            }

            button.Content = contentGrid;
            return button;
        }

        private FrameworkElement CreateNavigationItemIcon(ControlInfoDataItem item)
        {
            string? iconKey = item.ImageIconPath;
            if (!string.IsNullOrWhiteSpace(iconKey))
            {
                return CreateThemedIcon(iconKey, NavIconBrushKey, 18);
            }

            return CreateFallbackIconElement(item.ImageIconPath);
        }
        private FrameworkElement CreateFallbackIconElement(string imageIconPath)
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

            TextBlock fallbackIcon = new TextBlock
            {
                Width = 18,
                Height = 18,
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 18,
                VerticalAlignment = VerticalAlignment.Center,
                Text = imageIconPath
            };
            SetBrushResource(fallbackIcon, TextBlock.ForegroundProperty, NavIconBrushKey);
            return fallbackIcon;
        }

        private IEnumerable<ControlInfoDataItem> GetVisibleChildren(ControlInfoDataItem group)
        {
            IEnumerable<ControlInfoDataItem> children = group.Items.Where(child => child.IsVisibility);
            if (!HasActiveSearch)
            {
                return children;
            }

            return children.Where(Matches);
        }

        private bool Matches(ControlInfoDataItem item)
        {
            if (!HasActiveSearch)
            {
                return true;
            }

            // Match both title and description to keep search behavior broad enough.
            return item.Title.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ||
                   (!string.IsNullOrWhiteSpace(item.Description) && item.Description.Contains(_searchText, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsSelectedGroup(ControlInfoDataItem group)
        {
            return _selectedItem is not null &&
                   group.Items.Any(child => child.UniqueId == _selectedItem.UniqueId);
        }

        private bool IsCompactFlyoutOpenFor(ControlInfoDataItem item)
        {
            return CompactPanePopup.IsOpen && _compactFlyoutItemId == item.UniqueId;
        }

        private void ApplyCompactFlyoutButtonHighlight(Button button)
        {
            ApplyButtonSelectionVisual(button, isSelected: true);
        }

        private void ResetCompactFlyoutButtonHighlight()
        {
            if (_compactFlyoutPlacementTarget is null)
            {
                return;
            }

            _compactFlyoutPlacementTarget.Background = _normalBackgroundBrush;
            _compactFlyoutPlacementTarget.BorderBrush = _normalBorderBrush;
        }

        private void UpdateSettingsVisual()
        {
            if (SettingsButton is null)
            {
                return;
            }

            ApplyButtonSelectionVisual(SettingsButton, _isSettingsSelected);
        }

        private void ApplyButtonSelectionVisual(Button button, bool isSelected)
        {
            if (isSelected)
            {
                SetBrushResource(button, BackgroundProperty, NavSelectedItemBrushKey);
                SetBrushResource(button, BorderBrushProperty, NavSelectedBorderBrushKey);
                return;
            }

            button.Background = _normalBackgroundBrush;
            button.BorderBrush = _normalBorderBrush;
        }

        private FrameworkElement CreateThemedIcon(string iconKey, string brushResourceKey, double size)
        {
            FrameworkElement iconElement = IconFactory.Create(iconKey, Brushes.White, size);
            ApplyIconBrushResource(iconElement, brushResourceKey);
            return iconElement;
        }

        private static void ApplyIconBrushResource(DependencyObject element, string brushResourceKey)
        {
            if (element is Shape shape)
            {
                shape.SetResourceReference(Shape.StrokeProperty, brushResourceKey);
            }
            else if (element is TextBlock textBlock)
            {
                textBlock.SetResourceReference(TextBlock.ForegroundProperty, brushResourceKey);
            }

            int childCount = VisualTreeHelper.GetChildrenCount(element);
            for (int i = 0; i < childCount; i++)
            {
                ApplyIconBrushResource(VisualTreeHelper.GetChild(element, i), brushResourceKey);
            }
        }

        private static void SetBrushResource(FrameworkElement element, DependencyProperty property, string resourceKey)
        {
            element.SetResourceReference(property, resourceKey);
        }
    }
}
