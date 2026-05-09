using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace WpfApp;

public static class TabOverflowMenuBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(TabOverflowMenuBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty HiddenTabItemsProperty =
        DependencyProperty.RegisterAttached(
            "HiddenTabItems",
            typeof(ObservableCollection<TabOverflowMenuItem>),
            typeof(TabOverflowMenuBehavior),
            new PropertyMetadata(null));

    public static readonly DependencyProperty HasHiddenTabsProperty =
        DependencyProperty.RegisterAttached(
            "HasHiddenTabs",
            typeof(bool),
            typeof(TabOverflowMenuBehavior),
            new PropertyMetadata(false));

    public static readonly DependencyProperty SelectTabCommandProperty =
        DependencyProperty.RegisterAttached(
            "SelectTabCommand",
            typeof(ICommand),
            typeof(TabOverflowMenuBehavior),
            new PropertyMetadata(null));

    public static readonly DependencyProperty OpenContextMenuOnClickProperty =
        DependencyProperty.RegisterAttached(
            "OpenContextMenuOnClick",
            typeof(bool),
            typeof(TabOverflowMenuBehavior),
            new PropertyMetadata(false, OnOpenContextMenuOnClickChanged));

    private static readonly DependencyProperty StateProperty =
        DependencyProperty.RegisterAttached(
            "State",
            typeof(TabOverflowMenuState),
            typeof(TabOverflowMenuBehavior),
            new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject element)
    {
        return (bool)element.GetValue(IsEnabledProperty);
    }

    public static void SetIsEnabled(DependencyObject element, bool value)
    {
        element.SetValue(IsEnabledProperty, value);
    }

    public static ObservableCollection<TabOverflowMenuItem> GetHiddenTabItems(DependencyObject element)
    {
        return (ObservableCollection<TabOverflowMenuItem>)element.GetValue(HiddenTabItemsProperty);
    }

    public static bool GetHasHiddenTabs(DependencyObject element)
    {
        return (bool)element.GetValue(HasHiddenTabsProperty);
    }

    public static ICommand GetSelectTabCommand(DependencyObject element)
    {
        return (ICommand)element.GetValue(SelectTabCommandProperty);
    }

    public static bool GetOpenContextMenuOnClick(DependencyObject element)
    {
        return (bool)element.GetValue(OpenContextMenuOnClickProperty);
    }

    public static void SetOpenContextMenuOnClick(DependencyObject element, bool value)
    {
        element.SetValue(OpenContextMenuOnClickProperty, value);
    }

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TabControl tabControl)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            Attach(tabControl);
        }
        else
        {
            Detach(tabControl);
        }
    }

    private static void Attach(TabControl tabControl)
    {
        Detach(tabControl);

        TabOverflowMenuState state = new(tabControl);
        tabControl.SetValue(StateProperty, state);
        tabControl.SetValue(HiddenTabItemsProperty, state.HiddenItems);
        tabControl.SetValue(SelectTabCommandProperty, new SelectHiddenTabCommand(state));

        tabControl.Loaded += state.TabControl_Loaded;
        tabControl.Unloaded += state.TabControl_Unloaded;
        tabControl.SelectionChanged += state.TabControl_SelectionChanged;

        if (tabControl.Items is INotifyCollectionChanged notifyCollection)
        {
            notifyCollection.CollectionChanged += state.Items_CollectionChanged;
        }

        state.Initialize();
    }

    private static void OnOpenContextMenuOnClickChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Button button)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            button.Click += OverflowButton_Click;
        }
        else
        {
            button.Click -= OverflowButton_Click;
        }
    }

    private static void OverflowButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu is null)
        {
            return;
        }

        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.IsOpen = true;
    }

    private static void Detach(TabControl tabControl)
    {
        if (tabControl.GetValue(StateProperty) is not TabOverflowMenuState state)
        {
            return;
        }

        tabControl.Loaded -= state.TabControl_Loaded;
        tabControl.Unloaded -= state.TabControl_Unloaded;
        tabControl.SelectionChanged -= state.TabControl_SelectionChanged;

        if (tabControl.Items is INotifyCollectionChanged notifyCollection)
        {
            notifyCollection.CollectionChanged -= state.Items_CollectionChanged;
        }

        state.DetachScrollViewer();
        tabControl.ClearValue(StateProperty);
        tabControl.ClearValue(SelectTabCommandProperty);
        tabControl.SetValue(HasHiddenTabsProperty, false);
        state.HiddenItems.Clear();
    }

    public sealed class TabOverflowMenuItem
    {
        public TabOverflowMenuItem(object item, string title)
        {
            Item = item;
            Title = title;
        }

        public object Item { get; }

        public string Title { get; }
    }

    private sealed class TabOverflowMenuState
    {
        private readonly TabControl _tabControl;
        private ScrollViewer? _scrollViewer;
        private bool _isUpdateQueued;

        public TabOverflowMenuState(TabControl tabControl)
        {
            _tabControl = tabControl;
        }

        public ObservableCollection<TabOverflowMenuItem> HiddenItems { get; } = new();

        public void Initialize()
        {
            _tabControl.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                HookScrollViewer();
                QueueUpdate();
            }));
        }

        public void TabControl_Loaded(object sender, RoutedEventArgs e)
        {
            Initialize();
        }

        public void TabControl_Unloaded(object sender, RoutedEventArgs e)
        {
            DetachScrollViewer();
        }

        public void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            QueueUpdate();
        }

        public void Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            QueueUpdate();
        }

        public void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            QueueUpdate();
        }

        public void ScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            QueueUpdate();
        }

        public void DetachScrollViewer()
        {
            if (_scrollViewer is null)
            {
                return;
            }

            _scrollViewer.ScrollChanged -= ScrollViewer_ScrollChanged;
            _scrollViewer.SizeChanged -= ScrollViewer_SizeChanged;
            _scrollViewer = null;
        }

        public void Select(TabOverflowMenuItem menuItem)
        {
            _tabControl.SelectedItem = menuItem.Item;
            _tabControl.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                BringItemIntoHeaderView(menuItem.Item);
                QueueUpdate();
            }));
        }

        private void HookScrollViewer()
        {
            _tabControl.ApplyTemplate();
            ScrollViewer? scrollViewer = _tabControl.Template?.FindName("TabHeaderScrollViewer", _tabControl) as ScrollViewer;
            if (ReferenceEquals(_scrollViewer, scrollViewer))
            {
                return;
            }

            DetachScrollViewer();
            _scrollViewer = scrollViewer;
            if (_scrollViewer is null)
            {
                return;
            }

            _scrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;
            _scrollViewer.SizeChanged += ScrollViewer_SizeChanged;
        }

        private void QueueUpdate()
        {
            if (_isUpdateQueued)
            {
                return;
            }

            _isUpdateQueued = true;
            _tabControl.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                _isUpdateQueued = false;
                HookScrollViewer();
                UpdateHiddenItems();
            }));
        }

        private void UpdateHiddenItems()
        {
            HiddenItems.Clear();

            if (_scrollViewer is null || _scrollViewer.ViewportWidth <= 0d)
            {
                _tabControl.SetValue(HasHiddenTabsProperty, false);
                return;
            }

            foreach (object item in _tabControl.Items)
            {
                if (GetTabItem(item) is not TabItem tabItem || tabItem.ActualWidth <= 0d)
                {
                    continue;
                }

                Point position = tabItem.TransformToAncestor(_scrollViewer).Transform(new Point(0, 0));
                double left = position.X;
                double right = left + tabItem.ActualWidth;

                if (right <= 0.5d || left >= _scrollViewer.ViewportWidth - 0.5d)
                {
                    HiddenItems.Add(new TabOverflowMenuItem(item, ResolveTitle(tabItem)));
                }
            }

            _tabControl.SetValue(HasHiddenTabsProperty, HiddenItems.Count > 0);
        }

        private void BringItemIntoHeaderView(object item)
        {
            if (_scrollViewer is null || GetTabItem(item) is not TabItem tabItem)
            {
                return;
            }

            tabItem.UpdateLayout();
            Point position = tabItem.TransformToAncestor(_scrollViewer).Transform(new Point(0, 0));
            double left = position.X;
            double right = left + tabItem.ActualWidth;

            if (left < 0d)
            {
                _scrollViewer.ScrollToHorizontalOffset(_scrollViewer.HorizontalOffset + left);
            }
            else if (right > _scrollViewer.ViewportWidth)
            {
                _scrollViewer.ScrollToHorizontalOffset(_scrollViewer.HorizontalOffset + right - _scrollViewer.ViewportWidth);
            }
        }

        private TabItem? GetTabItem(object item)
        {
            return item as TabItem
                ?? _tabControl.ItemContainerGenerator.ContainerFromItem(item) as TabItem;
        }

        private static string ResolveTitle(TabItem tabItem)
        {
            return tabItem.Header?.ToString() ?? string.Empty;
        }
    }

    private sealed class SelectHiddenTabCommand : ICommand
    {
        private readonly TabOverflowMenuState _state;

        public SelectHiddenTabCommand(TabOverflowMenuState state)
        {
            _state = state;
        }

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter)
        {
            return parameter is TabOverflowMenuItem;
        }

        public void Execute(object? parameter)
        {
            if (parameter is TabOverflowMenuItem menuItem)
            {
                _state.Select(menuItem);
            }
        }
    }
}
