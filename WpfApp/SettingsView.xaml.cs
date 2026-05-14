using ControlLibrary;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using WpfApp.Models.Settings;
using WpfApp.Services.Settings;

namespace WpfApp
{
    /// <summary>
    /// 应用设置页，包含主题、语言和测试工位配置。
    /// </summary>
    public partial class SettingsView : UserControl, INotifyPropertyChanged
    {
        #region 字段

        private bool _isUpdatingThemeSelection;
        private bool _isUpdatingLanguageSelection;
        private bool _isLanguageEventSubscribed;
        private ObservableCollection<TestStationSetting> _testStations = new();
        private string _testStationStatusText = string.Empty;

        #endregion

        #region 构造函数

        /// <summary>
        /// 初始化设置页实例。
        /// </summary>
        public SettingsView()
        {
            InitializeComponent();
            DataContext = this;
            Loaded += SettingsView_Loaded;
            Unloaded += SettingsView_Unloaded;
        }

        #endregion

        #region 属性

        /// <summary>
        /// 测试工位配置集合。
        /// </summary>
        public ObservableCollection<TestStationSetting> TestStations
        {
            get => _testStations;
            private set
            {
                if (ReferenceEquals(_testStations, value))
                {
                    return;
                }

                UnhookTestStations(_testStations);
                _testStations = value ?? new ObservableCollection<TestStationSetting>();
                HookTestStations(_testStations);

                OnPropertyChanged();
                OnPropertyChanged(nameof(TestStationCount));
                OnPropertyChanged(nameof(TestStationSummaryText));
            }
        }

        /// <summary>
        /// 当前测试工位数量。
        /// </summary>
        public int TestStationCount => TestStations.Count;

        /// <summary>
        /// 测试工位汇总文本。
        /// </summary>
        public string TestStationSummaryText =>
            string.Format(
                AppLanguageManager.GetString("SettingsTestStationsSummary", "已配置 {0} 个测试工位。"),
                TestStationCount);

        /// <summary>
        /// 测试工位设置状态文本。
        /// </summary>
        public string TestStationStatusText
        {
            get => _testStationStatusText;
            private set => SetField(ref _testStationStatusText, value ?? string.Empty);
        }

        #endregion

        #region 事件

        /// <inheritdoc />
        public event PropertyChangedEventHandler? PropertyChanged;

        #endregion

        #region 页面生命周期

        /// <summary>
        /// 页面加载后刷新设置状态。
        /// </summary>
        private void SettingsView_Loaded(object sender, RoutedEventArgs e)
        {
            SubscribeLanguageChanged();
            UpdateSelectedThemeRadioButton();
            RefreshLanguageOptions();
            LoadTestStationSettings();
        }

        /// <summary>
        /// 页面卸载时解除事件订阅。
        /// </summary>
        private void SettingsView_Unloaded(object sender, RoutedEventArgs e)
        {
            UnsubscribeLanguageChanged();
            UnhookTestStations(TestStations);
        }

        #endregion

        #region 主题与语言

        /// <summary>
        /// 语言切换后刷新界面相关显示。
        /// </summary>
        private void AppLanguageManager_LanguageChanged(object? sender, EventArgs e)
        {
            RefreshLanguageOptions();
            OnPropertyChanged(nameof(TestStationSummaryText));
        }

        /// <summary>
        /// 处理主题切换单选框事件。
        /// </summary>
        private void OnThemeRadioButtonChecked(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingThemeSelection)
            {
                return;
            }

            if (sender is RadioButton { Tag: string themeName } &&
                Enum.TryParse(themeName, true, out AppTheme theme))
            {
                AppThemeManager.ApplyTheme(theme);
                UpdateSelectedThemeRadioButton();
            }
        }

        /// <summary>
        /// 根据当前主题刷新单选框选中状态。
        /// </summary>
        private void UpdateSelectedThemeRadioButton()
        {
            _isUpdatingThemeSelection = true;

            foreach (object child in ThemePanel.Children)
            {
                if (child is RadioButton radioButton && radioButton.Tag is string themeName)
                {
                    radioButton.IsChecked = string.Equals(
                        themeName,
                        AppThemeManager.CurrentTheme.ToString(),
                        StringComparison.OrdinalIgnoreCase);
                }
            }

            _isUpdatingThemeSelection = false;
        }

        /// <summary>
        /// 处理语言下拉框变更事件。
        /// </summary>
        private void OnLanguageSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingLanguageSelection)
            {
                return;
            }

            if (LanguageComboBox.SelectedValue is string languageKey)
            {
                AppLanguageManager.ApplyLanguage(languageKey);
                RefreshLanguageOptions();
            }
        }

        /// <summary>
        /// 刷新语言列表和当前选中项。
        /// </summary>
        private void RefreshLanguageOptions()
        {
            _isUpdatingLanguageSelection = true;

            LanguageComboBox.ItemsSource = AppLanguageManager.SupportedLanguages
                .Select(language => new LanguageSelectionItem(
                    language.Key,
                    AppLanguageManager.GetString(language.DisplayNameResourceKey, language.Key)))
                .ToList();

            LanguageComboBox.SelectedValue = AppLanguageManager.CurrentLanguage;
            _isUpdatingLanguageSelection = false;
        }

        #endregion

        #region 测试工位设置

        /// <summary>
        /// 加载测试工位设置。
        /// </summary>
        private void LoadTestStationSettings()
        {
            TestStationSettingsCatalog catalog = TestStationSettingsStore.Load();
            TestStations = new ObservableCollection<TestStationSetting>(
                catalog.Stations.Select(station => station.Clone()));
            TestStationStatusText = string.Empty;
        }

        /// <summary>
        /// 新增一个测试工位配置。
        /// </summary>
        private void AddStationButton_Click(object sender, RoutedEventArgs e)
        {
            TestStations.Add(new TestStationSetting
            {
                StationName = GenerateNextStationName(),
                IsSchemeMatchingEnabled = false
            });

            SetTestStationStatus("SettingsTestStationsStatusAdded", "已新增测试工位，请继续编辑后保存。");
        }

        /// <summary>
        /// 删除指定测试工位配置。
        /// </summary>
        private void DeleteStationButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: TestStationSetting station })
            {
                return;
            }

            if (TestStations.Count <= 1)
            {
                SetTestStationStatus("SettingsTestStationsStatusKeepOne", "至少保留一个测试工位。");
                return;
            }

            TestStations.Remove(station);
            SetTestStationStatus("SettingsTestStationsStatusDeleted", "已删除测试工位，请保存设置。");
        }

        /// <summary>
        /// 保存当前测试工位设置。
        /// </summary>
        private void SaveStationsButton_Click(object sender, RoutedEventArgs e)
        {
            TestStationSettingsCatalog catalog = new()
            {
                Stations = new ObservableCollection<TestStationSetting>(
                    TestStations.Select(station => station.Clone()))
            };

            TestStationSettingsStore.Save(catalog);
            LoadTestStationSettings();
            SetTestStationStatus("SettingsTestStationsStatusSaved", "测试工位设置已保存。");
        }

        /// <summary>
        /// 生成下一个默认工位名称。
        /// </summary>
        /// <returns>默认工位名称。</returns>
        private string GenerateNextStationName()
        {
            int index = TestStations.Count + 1;
            return $"Station-{index:00}";
        }

        /// <summary>
        /// 订阅测试工位集合和项变化。
        /// </summary>
        /// <param name="stations">测试工位集合。</param>
        private void HookTestStations(ObservableCollection<TestStationSetting> stations)
        {
            stations.CollectionChanged += TestStations_CollectionChanged;

            foreach (TestStationSetting station in stations)
            {
                station.PropertyChanged += TestStation_PropertyChanged;
            }
        }

        /// <summary>
        /// 解除测试工位集合和项变化订阅。
        /// </summary>
        /// <param name="stations">测试工位集合。</param>
        private void UnhookTestStations(ObservableCollection<TestStationSetting> stations)
        {
            stations.CollectionChanged -= TestStations_CollectionChanged;

            foreach (TestStationSetting station in stations)
            {
                station.PropertyChanged -= TestStation_PropertyChanged;
            }
        }

        /// <summary>
        /// 处理测试工位集合变化。
        /// </summary>
        private void TestStations_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems is not null)
            {
                foreach (TestStationSetting station in e.OldItems.OfType<TestStationSetting>())
                {
                    station.PropertyChanged -= TestStation_PropertyChanged;
                }
            }

            if (e.NewItems is not null)
            {
                foreach (TestStationSetting station in e.NewItems.OfType<TestStationSetting>())
                {
                    station.PropertyChanged += TestStation_PropertyChanged;
                }
            }

            OnPropertyChanged(nameof(TestStationCount));
            OnPropertyChanged(nameof(TestStationSummaryText));
        }

        /// <summary>
        /// 处理测试工位项属性变化。
        /// </summary>
        private void TestStation_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            SetTestStationStatus("SettingsTestStationsStatusModified", "测试工位设置已修改，记得保存。");
        }

        /// <summary>
        /// 设置测试工位状态提示文本。
        /// </summary>
        /// <param name="resourceKey">资源键。</param>
        /// <param name="fallback">默认文本。</param>
        private void SetTestStationStatus(string resourceKey, string fallback)
        {
            TestStationStatusText = AppLanguageManager.GetString(resourceKey, fallback);
        }

        #endregion

        #region 语言订阅

        /// <summary>
        /// 订阅语言切换事件。
        /// </summary>
        private void SubscribeLanguageChanged()
        {
            if (_isLanguageEventSubscribed)
            {
                return;
            }

            AppLanguageManager.LanguageChanged += AppLanguageManager_LanguageChanged;
            _isLanguageEventSubscribed = true;
        }

        /// <summary>
        /// 解除语言切换事件订阅。
        /// </summary>
        private void UnsubscribeLanguageChanged()
        {
            if (!_isLanguageEventSubscribed)
            {
                return;
            }

            AppLanguageManager.LanguageChanged -= AppLanguageManager_LanguageChanged;
            _isLanguageEventSubscribed = false;
        }

        #endregion

        #region 通知辅助

        /// <summary>
        /// 触发属性变更通知。
        /// </summary>
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// 设置字段值并触发属性通知。
        /// </summary>
        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        #endregion

        #region 内部类型

        /// <summary>
        /// 语言选择下拉项。
        /// </summary>
        private sealed record LanguageSelectionItem(string Key, string DisplayName);

        #endregion
    }
}
