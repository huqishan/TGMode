using Module.MES.ViewModels.VMs;
using Shared.Infrastructure.Extensions;
using Shared.Infrastructure.PackMethod;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Module.MES.Views
{
    public partial class DataStructureConfigView : UserControl, INotifyPropertyChanged
    {
        private static readonly string MesConfigRootDirectory =
            Path.Combine(AppContext.BaseDirectory, "Config", "MES_Config");
        private static readonly string DataStructureConfigDirectory =
            Path.Combine(MesConfigRootDirectory, "DataStructure");

        private static readonly Brush SuccessBrush =
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));

        private static readonly Brush WarningBrush =
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EA580C"));

        private static readonly Brush NeutralBrush =
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));

        private readonly Dictionary<DataStructureProfile, string> _profileStorageFileNames = new();
        private DataStructureProfile? _selectedProfile;
        private DataStructureLayout? _selectedField;
        private DataStructureLayout? _copiedField;
        private string _searchText = string.Empty;
        private string _pageStatusText = "等待编辑";
        private Brush _pageStatusBrush = NeutralBrush;

        public DataStructureConfigView()
        {
            InitializeComponent();

            ProfilesView = CollectionViewSource.GetDefaultView(Profiles);
            ProfilesView.Filter = FilterProfiles;

            int loadedCount = LoadProfilesFromDisk();
            if (loadedCount == 0)
            {
                DataStructureProfile profile = CreateDefaultProfile(GenerateUniqueName("数据结构"));
                AddProfile(profile);
                SetPageStatus("未发现本地数据结构配置，已创建默认配置。", NeutralBrush);
            }
            else
            {
                SetPageStatus($"已读取 {loadedCount} 个数据结构配置。", SuccessBrush);
            }

            DataContext = this;
            SelectedProfile = Profiles.FirstOrDefault();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<DataStructureProfile> Profiles { get; } = new();

        public ObservableCollection<DataStructureTypeOption> StructureTypes { get; } =
            new ObservableCollection<DataStructureTypeOption>(DataStructureTypes.Options);

        public ICollectionView ProfilesView { get; }

        public DataStructureProfile? SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                if (ReferenceEquals(_selectedProfile, value))
                {
                    return;
                }

                _selectedProfile = value;
                SelectedField = null;
                OnPropertyChanged();
            }
        }

        public DataStructureLayout? SelectedField
        {
            get => _selectedField;
            set
            {
                if (ReferenceEquals(_selectedField, value))
                {
                    return;
                }

                _selectedField = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelectedField));

                if (_selectedField is null && IsStructureDrawerOpen)
                {
                    CloseCommandDrawer();
                }
            }
        }

        public bool HasSelectedField => SelectedField is not null;

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (!SetField(ref _searchText, value ?? string.Empty))
                {
                    return;
                }

                ProfilesView.Refresh();
            }
        }

        public string PageStatusText
        {
            get => _pageStatusText;
            private set => SetField(ref _pageStatusText, value);
        }

        public Brush PageStatusBrush
        {
            get => _pageStatusBrush;
            private set => SetField(ref _pageStatusBrush, value);
        }

        private void AddProfile_Click(object sender, RoutedEventArgs e)
        {
            DataStructureProfile profile = CreateDefaultProfile(GenerateUniqueName("数据结构"));
            AddProfile(profile);
            SelectedProfile = profile;
            SetPageStatus($"已新增数据结构：{profile.Name}", SuccessBrush);
        }

        private void DuplicateProfile_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedProfile is null)
            {
                SetPageStatus("请先选择需要复制的数据结构。", WarningBrush);
                return;
            }

            DataStructureProfile copy = SelectedProfile.Clone(GenerateCopyName(SelectedProfile.Name));
            AddProfile(copy);
            SelectedProfile = copy;
            SetPageStatus($"已复制数据结构：{copy.Name}", SuccessBrush);
        }

        private void DeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedProfile is null)
            {
                SetPageStatus("请先选择需要删除的数据结构。", WarningBrush);
                return;
            }

            int selectedIndex = Profiles.IndexOf(SelectedProfile);
            DataStructureProfile deletedProfile = SelectedProfile;
            deletedProfile.PropertyChanged -= Profile_PropertyChanged;
            Profiles.Remove(deletedProfile);
            DeleteStoredProfileFile(deletedProfile);

            if (Profiles.Count == 0)
            {
                AddProfile(CreateDefaultProfile(GenerateUniqueName("数据结构")));
            }

            SelectedProfile = Profiles[Math.Clamp(selectedIndex, 0, Profiles.Count - 1)];
            SetPageStatus($"已删除数据结构：{deletedProfile.Name}", WarningBrush);
        }

        private void SaveProfiles_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int savedCount = SaveProfilesToDisk();
                SetPageStatus($"已保存 {savedCount} 个数据结构配置。", SuccessBrush);
            }
            catch (Exception ex)
            {
                SetPageStatus($"保存失败：{ex.Message}", WarningBrush);
            }
        }

        private void StructureTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            SelectedField = e.NewValue as DataStructureLayout;
        }

        private void StructureTreeView_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsTreeViewItemSource(e.OriginalSource as DependencyObject))
            {
                SelectedField = null;
            }
        }

        private void DataStructureTreeViewItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not TreeViewItem item)
            {
                return;
            }

            if (!ReferenceEquals(item, GetTreeViewItemFromSource(e.OriginalSource as DependencyObject)))
            {
                return;
            }

            item.IsSelected = true;
            item.Focus();
            e.Handled = true;
        }

        private void DataStructureFieldEditor_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            SelectTreeViewItemFromSource(e.OriginalSource as DependencyObject ?? sender as DependencyObject);
        }

        private void StructureContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            CopyFieldMenuItem.IsEnabled = SelectedField is not null;
            DeleteFieldMenuItem.IsEnabled = SelectedField is not null;
            PasteFieldMenuItem.IsEnabled = SelectedProfile is not null && _copiedField is not null;
        }

        private void AddFieldChild_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedProfile is null)
            {
                SetPageStatus("请先选择一个数据结构。", WarningBrush);
                return;
            }

            ObservableCollection<DataStructureLayout> targetFields =
                SelectedField?.Children ?? SelectedProfile.Structure;
            DataStructureLayout field = CreateDefaultField(targetFields);
            targetFields.Add(field);
            SelectedField = field;
            SetPageStatus($"已新增结构字段：{field.ClientCode}", SuccessBrush);
        }

        private void CopyField_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedField is null)
            {
                SetPageStatus("请先选择需要复制的结构字段。", WarningBrush);
                return;
            }

            _copiedField = SelectedField.Clone();
            PasteFieldMenuItem.IsEnabled = true;
            SetPageStatus($"已复制结构字段：{SelectedField.ClientCode}", SuccessBrush);
        }

        private void PasteField_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedProfile is null || _copiedField is null)
            {
                SetPageStatus("暂无可粘贴的结构字段。", WarningBrush);
                return;
            }

            ObservableCollection<DataStructureLayout> targetFields =
                SelectedField?.Children ?? SelectedProfile.Structure;
            DataStructureLayout pastedField = _copiedField.Clone();
            targetFields.Add(pastedField);
            SelectedField = pastedField;
            SetPageStatus($"已粘贴结构字段：{pastedField.ClientCode}", SuccessBrush);
        }

        private void DeleteField_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedProfile is null || SelectedField is null)
            {
                SetPageStatus("请先选择需要删除的结构字段。", WarningBrush);
                return;
            }

            string fieldName = SelectedField.ClientCode;
            if (!RemoveField(SelectedProfile.Structure, SelectedField))
            {
                SetPageStatus("未找到需要删除的结构字段。", WarningBrush);
                return;
            }

            SelectedField = null;
            SetPageStatus($"已删除结构字段：{fieldName}", WarningBrush);
        }

        private static bool IsTreeViewItemSource(DependencyObject? source)
        {
            return GetTreeViewItemFromSource(source) is not null;
        }

        private void SelectTreeViewItemFromSource(DependencyObject? source)
        {
            TreeViewItem? item = GetTreeViewItemFromSource(source);
            if (item is null)
            {
                return;
            }

            item.IsSelected = true;
            if (item.DataContext is DataStructureLayout field)
            {
                SelectedField = field;
            }
        }

        private static TreeViewItem? GetTreeViewItemFromSource(DependencyObject? source)
        {
            while (source is not null)
            {
                if (source is TreeViewItem item)
                {
                    return item;
                }

                source = VisualTreeHelper.GetParent(source);
            }

            return null;
        }

        private void AddProfile(DataStructureProfile profile)
        {
            profile.PropertyChanged += Profile_PropertyChanged;
            Profiles.Add(profile);
        }

        private void Profile_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not DataStructureProfile profile)
            {
                return;
            }

            if (e.PropertyName is nameof(DataStructureProfile.Name) or
                nameof(DataStructureProfile.StructureType) or
                nameof(DataStructureProfile.Summary))
            {
                ProfilesView.Refresh();
            }

            if (ReferenceEquals(profile, SelectedProfile) &&
                e.PropertyName is nameof(DataStructureProfile.Name) or nameof(DataStructureProfile.StructureType))
            {
                SetPageStatus($"正在编辑：{profile.Name}", NeutralBrush);
            }
        }

        private int LoadProfilesFromDisk()
        {
            if (!Directory.Exists(DataStructureConfigDirectory))
            {
                return 0;
            }

            int loadedCount = 0;
            foreach (string filePath in Directory.EnumerateFiles(DataStructureConfigDirectory, "*.json").OrderBy(Path.GetFileName))
            {
                try
                {
                    string storageText = File.ReadAllText(filePath, Encoding.UTF8);
                    DataStructureProfileDocument? document = DeserializeProfileDocument(storageText);
                    if (document is null)
                    {
                        continue;
                    }

                    string loadedName = string.IsNullOrWhiteSpace(document.Name)
                        ? Path.GetFileNameWithoutExtension(filePath)
                        : document.Name.Trim();
                    string uniqueName = BuildUniqueLoadedName(loadedName);
                    DataStructureProfile profile = document.ToProfile(uniqueName, File.GetLastWriteTime(filePath));
                    AddProfile(profile);
                    _profileStorageFileNames[profile] = Path.GetFileName(filePath);
                    loadedCount++;
                }
                catch (Exception ex)
                {
                    SetPageStatus($"读取数据结构配置失败：{Path.GetFileName(filePath)}，原因：{ex.Message}", WarningBrush);
                }
            }

            return loadedCount;
        }

        private static DataStructureProfileDocument? DeserializeProfileDocument(string storageText)
        {
            try
            {
                return JsonHelper.DeserializeObject<DataStructureProfileDocument>(storageText);
            }
            catch
            {
                return JsonHelper.DeserializeObject<DataStructureProfileDocument>(storageText.DesDecrypt());
            }
        }

        private int SaveProfilesToDisk()
        {
            Directory.CreateDirectory(DataStructureConfigDirectory);

            HashSet<string> usedProfileNames = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> usedFileNames = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<DataStructureProfile, string> targetFileNames = new();
            foreach (DataStructureProfile profile in Profiles)
            {
                ValidateProfileForSave(profile, usedProfileNames);
                targetFileNames[profile] = BuildUniqueStorageFileName(profile.Name, usedFileNames);
            }

            int savedCount = 0;
            foreach (DataStructureProfile profile in Profiles)
            {
                string fileName = targetFileNames[profile];
                string filePath = Path.Combine(DataStructureConfigDirectory, fileName);
                JsonHelper.SaveJson(DataStructureProfileDocument.FromProfile(profile), filePath);
                savedCount++;
            }

            foreach (DataStructureProfile profile in Profiles)
            {
                string fileName = targetFileNames[profile];
                if (_profileStorageFileNames.TryGetValue(profile, out string? oldFileName) &&
                    !string.Equals(oldFileName, fileName, StringComparison.OrdinalIgnoreCase) &&
                    !usedFileNames.Contains(oldFileName))
                {
                    TryDeleteStorageFile(oldFileName);
                }

                _profileStorageFileNames[profile] = fileName;
            }

            return savedCount;
        }

        private static void ValidateProfileForSave(DataStructureProfile profile, HashSet<string> usedProfileNames)
        {
            if (string.IsNullOrWhiteSpace(profile.Name))
            {
                throw new InvalidOperationException("结构名称不能为空。");
            }

            if (!usedProfileNames.Add(profile.Name.Trim()))
            {
                throw new InvalidOperationException($"结构名称重复：{profile.Name}");
            }
        }

        private void DeleteStoredProfileFile(DataStructureProfile profile)
        {
            if (!_profileStorageFileNames.TryGetValue(profile, out string? fileName))
            {
                return;
            }

            TryDeleteStorageFile(fileName);
            _profileStorageFileNames.Remove(profile);
        }

        private static void TryDeleteStorageFile(string fileName)
        {
            try
            {
                string filePath = Path.Combine(DataStructureConfigDirectory, fileName);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch
            {
            }
        }

        private static DataStructureProfile CreateDefaultProfile(string name)
        {
            return new DataStructureProfile
            {
                Name = name,
                StructureType = DataStructureTypes.Json
            };
        }

        private static DataStructureLayout CreateDefaultField(IEnumerable<DataStructureLayout> siblings)
        {
            string code = GenerateUniqueFieldCode(siblings, "Field");
            return new DataStructureLayout
            {
                ClientCode = code,
                MesCode = code,
                DataType = DataStructureFieldDataTypes.String
            };
        }

        private static string GenerateUniqueFieldCode(IEnumerable<DataStructureLayout> siblings, string prefix)
        {
            HashSet<string> usedCodes = siblings
                .Select(field => field.ClientCode)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            for (int index = 1; ; index++)
            {
                string code = $"{prefix}{index}";
                if (!usedCodes.Contains(code))
                {
                    return code;
                }
            }
        }

        private static bool RemoveField(ObservableCollection<DataStructureLayout> fields, DataStructureLayout targetField)
        {
            if (fields.Remove(targetField))
            {
                return true;
            }

            foreach (DataStructureLayout field in fields)
            {
                if (RemoveField(field.Children, targetField))
                {
                    return true;
                }
            }

            return false;
        }

        private static string BuildUniqueStorageFileName(string profileName, HashSet<string> usedFileNames)
        {
            string safeName = BuildSafeFileName(profileName);
            string fileName = $"{safeName}.json";
            for (int index = 2; usedFileNames.Contains(fileName); index++)
            {
                fileName = $"{safeName}_{index}.json";
            }

            usedFileNames.Add(fileName);
            return fileName;
        }

        private static string BuildSafeFileName(string value)
        {
            HashSet<char> invalidChars = new(Path.GetInvalidFileNameChars());
            StringBuilder builder = new(value.Trim().Length);
            foreach (char current in value.Trim())
            {
                builder.Append(invalidChars.Contains(current) || char.IsControl(current)
                    ? '_'
                    : char.IsWhiteSpace(current) ? '_' : current);
            }

            string safeName = builder.ToString().Trim(' ', '.');
            if (string.IsNullOrWhiteSpace(safeName))
            {
                safeName = "DataStructure";
            }

            return safeName.Length <= 80 ? safeName : safeName[..80];
        }

        private string BuildUniqueLoadedName(string loadedName)
        {
            string baseName = string.IsNullOrWhiteSpace(loadedName) ? "数据结构" : loadedName.Trim();
            if (!Profiles.Any(profile => string.Equals(profile.Name, baseName, StringComparison.OrdinalIgnoreCase)))
            {
                return baseName;
            }

            for (int index = 2; ; index++)
            {
                string name = $"{baseName} {index}";
                if (!Profiles.Any(profile => string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    return name;
                }
            }
        }

        private string GenerateUniqueName(string prefix)
        {
            for (int index = 1; ; index++)
            {
                string name = $"{prefix} {index}";
                if (!Profiles.Any(profile => string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    return name;
                }
            }
        }

        private string GenerateCopyName(string baseName)
        {
            string prefix = string.IsNullOrWhiteSpace(baseName) ? "数据结构" : baseName.Trim();
            string firstName = $"{prefix} 副本";
            if (!Profiles.Any(profile => string.Equals(profile.Name, firstName, StringComparison.OrdinalIgnoreCase)))
            {
                return firstName;
            }

            for (int index = 2; ; index++)
            {
                string name = $"{firstName} {index}";
                if (!Profiles.Any(profile => string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    return name;
                }
            }
        }

        private bool FilterProfiles(object item)
        {
            if (item is not DataStructureProfile profile)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(SearchText))
            {
                return true;
            }

            string keyword = SearchText.Trim();
            return Contains(profile.Name, keyword) ||
                   Contains(profile.StructureType, keyword) ||
                   Contains(profile.Summary, keyword);
        }

        private static bool Contains(string? source, string keyword)
        {
            return source?.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void SetPageStatus(string text, Brush brush)
        {
            PageStatusText = text;
            PageStatusBrush = brush;
        }

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

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #region DataInfo弹框
        private bool _isStructureDrawerOpen;
        private const double StructureDrawerClosedOffset = 56d;
        private static readonly Duration StructureDrawerAnimationDuration = new Duration(TimeSpan.FromMilliseconds(220));
        private static readonly IEasingFunction StructureDrawerEasing = new CubicEase { EasingMode = EasingMode.EaseOut };
        public bool IsStructureDrawerOpen
        {
            get => _isStructureDrawerOpen;
            private set
            {
                if (_isStructureDrawerOpen == value)
                {
                    return;
                }

                _isStructureDrawerOpen = value;
                OnPropertyChanged();
            }
        }
        private void CloseInfoDrawerButton_Click(object sender, RoutedEventArgs e)
        {
            CloseCommandDrawer();
        }
        private void DataStructureDrawerBackdrop_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            CloseCommandDrawer();
        }
        private void StructureTreeView_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (!IsTreeViewItemSource(e.OriginalSource as DependencyObject) || SelectedField is null)
            {
                return;
            }

            e.Handled = true;// 阻止双击事件冒泡到 TreeViewItem 导致选中状态改变
            OpenCommandDrawer();
        }
        private void CloseCommandDrawer()
        {
            IsStructureDrawerOpen = false;
            UpdateCommandDrawerVisual(animate: true);
        }
        private void OpenCommandDrawer()
        {
            IsStructureDrawerOpen = true;
            UpdateCommandDrawerVisual(animate: true);
        }
        private void UpdateCommandDrawerVisual(bool animate)
        {
            if (DataStructureDrawerHost is null || DataStructureDrawerTranslateTransform is null)
            {
                return;
            }

            double targetOpacity = IsStructureDrawerOpen ? 1d : 0d;
            double targetOffset = IsStructureDrawerOpen ? 0d : StructureDrawerClosedOffset;

            if (IsStructureDrawerOpen)
            {
                DataStructureDrawerHost.IsHitTestVisible = true;
            }

            if (!animate)
            {
                DataStructureDrawerHost.BeginAnimation(UIElement.OpacityProperty, null);
                DataStructureDrawerTranslateTransform.BeginAnimation(TranslateTransform.YProperty, null);
                DataStructureDrawerHost.Opacity = targetOpacity;
                DataStructureDrawerTranslateTransform.Y = targetOffset;
                DataStructureDrawerHost.IsHitTestVisible = IsStructureDrawerOpen;
                return;
            }

            DoubleAnimation opacityAnimation = new DoubleAnimation
            {
                To = targetOpacity,
                Duration = StructureDrawerAnimationDuration,
                EasingFunction = StructureDrawerEasing
            };

            if (!IsStructureDrawerOpen)
            {
                opacityAnimation.Completed += (_, _) =>
                {
                    if (!IsStructureDrawerOpen)
                    {
                        DataStructureDrawerHost.IsHitTestVisible = false;
                    }
                };
            }

            DoubleAnimation translateAnimation = new DoubleAnimation
            {
                To = targetOffset,
                Duration = StructureDrawerAnimationDuration,
                EasingFunction = StructureDrawerEasing
            };

            DataStructureDrawerHost.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
            DataStructureDrawerTranslateTransform.BeginAnimation(TranslateTransform.YProperty, translateAnimation);
        }
        #endregion

        
    }
}
