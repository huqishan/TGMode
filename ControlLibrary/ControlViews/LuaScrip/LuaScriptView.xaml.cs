using ControlLibrary.ControlViews.LuaScrip.Models;
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
using System.Windows.Media;

namespace ControlLibrary.ControlViews.LuaScrip
{
    /// <summary>
    /// LuaScriptView.xaml 的交互逻辑
    /// </summary>
    public partial class LuaScriptView : UserControl, INotifyPropertyChanged
    {
        private static readonly string LuaScriptConfigDirectory =
            Path.Combine(AppContext.BaseDirectory, "Config", "LuaScript");

        private static readonly Brush SuccessBrush =
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));

        private static readonly Brush WarningBrush =
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EA580C"));

        private static readonly Brush NeutralBrush =
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));

        private readonly Dictionary<LuaScriptProfile, string> _profileStorageFileNames = new Dictionary<LuaScriptProfile, string>();
        private LuaScriptProfile? _selectedProfile;
        private string _pageStatusText = "等待输入";
        private Brush _pageStatusBrush = NeutralBrush;

        public LuaScriptView()
        {
            InitializeComponent();

            int loadedProfileCount = LoadProfilesFromDisk();
            if (loadedProfileCount == 0)
            {
                LuaScriptProfile profile = CreateSampleProfile();
                AddProfile(profile);
                SetPageStatus("未发现本地脚本，已创建默认示例。", NeutralBrush);
            }
            else
            {
                SetPageStatus($"已读取 {loadedProfileCount} 个 Lua 脚本。", SuccessBrush);
            }

            DataContext = this;
            SelectedProfile = Profiles.FirstOrDefault();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<LuaScriptProfile> Profiles { get; } = new ObservableCollection<LuaScriptProfile>();

        public LuaScriptProfile? SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                if (ReferenceEquals(_selectedProfile, value))
                {
                    return;
                }

                if (_selectedProfile is not null)
                {
                    _selectedProfile.PropertyChanged -= SelectedProfile_PropertyChanged;
                }

                _selectedProfile = value;
                if (_selectedProfile is not null)
                {
                    _selectedProfile.PropertyChanged += SelectedProfile_PropertyChanged;
                }

                OnPropertyChanged();
            }
        }

        public string ConfigDirectoryDisplay => $"配置目录：{LuaScriptConfigDirectory}";

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

        private void NewProfileButton_Click(object sender, RoutedEventArgs e)
        {
            LuaScriptProfile profile = CreateNewProfile(GenerateUniqueName("Lua 脚本"));
            AddProfile(profile);
            SelectedProfile = profile;
            SetPageStatus($"已新建脚本：{profile.Name}。", SuccessBrush);
        }

        private void DuplicateProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedProfile is null)
            {
                SetPageStatus("请先选择需要复制的脚本。", WarningBrush);
                return;
            }

            LuaScriptProfile copy = SelectedProfile.Clone(GenerateCopyName(SelectedProfile.Name));
            AddProfile(copy);
            SelectedProfile = copy;
            SetPageStatus($"已复制脚本：{copy.Name}。", SuccessBrush);
        }

        private void DeleteProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedProfile is null)
            {
                SetPageStatus("请先选择需要删除的脚本。", WarningBrush);
                return;
            }

            int selectedIndex = Profiles.IndexOf(SelectedProfile);
            LuaScriptProfile deletedProfile = SelectedProfile;
            deletedProfile.PropertyChanged -= SelectedProfile_PropertyChanged;
            Profiles.Remove(deletedProfile);
            DeleteStoredProfileFile(deletedProfile);

            if (Profiles.Count == 0)
            {
                LuaScriptProfile profile = CreateNewProfile(GenerateUniqueName("Lua 脚本"));
                AddProfile(profile);
            }

            SelectedProfile = Profiles[Math.Clamp(selectedIndex, 0, Profiles.Count - 1)];
            SetPageStatus($"已删除脚本：{deletedProfile.Name}。", NeutralBrush);
        }

        private void SaveProfilesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int savedCount = SaveProfilesToDisk();
                SetPageStatus($"已保存 {savedCount} 个 Lua 脚本。", SuccessBrush);
            }
            catch (Exception ex)
            {
                SetPageStatus($"保存失败：{ex.Message}", WarningBrush);
            }
        }

        private void SelectedProfile_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not LuaScriptProfile profile)
            {
                return;
            }

            if (e.PropertyName is nameof(LuaScriptProfile.Name) or nameof(LuaScriptProfile.ScriptText))
            {
                SetPageStatus($"正在编辑：{profile.Name}。", NeutralBrush);
            }
        }

        private void AddProfile(LuaScriptProfile profile)
        {
            Profiles.Add(profile);
        }

        private static LuaScriptProfile CreateSampleProfile()
        {
            return new LuaScriptProfile
            {
                Name = "Lua 示例脚本",
                ScriptText = string.Join(
                    Environment.NewLine,
                    "-- 点击执行脚本查看返回值",
                    "local message = \"Hello Lua\"",
                    "return message")
            };
        }

        private static LuaScriptProfile CreateNewProfile(string name)
        {
            return new LuaScriptProfile
            {
                Name = name,
                ScriptText = "return \"Hello Lua\""
            };
        }

        private int LoadProfilesFromDisk()
        {
            if (!Directory.Exists(LuaScriptConfigDirectory))
            {
                return 0;
            }

            int loadedCount = 0;
            foreach (string filePath in Directory.EnumerateFiles(LuaScriptConfigDirectory, "*.json").OrderBy(Path.GetFileName))
            {
                try
                {
                    string storageText = File.ReadAllText(filePath, Encoding.UTF8);
                    LuaScriptProfileDocument? document = DeserializeProfileDocument(storageText);
                    if (document is null)
                    {
                        continue;
                    }

                    LuaScriptProfile profile = document.ToProfile();
                    profile.Name = BuildUniqueLoadedName(profile.Name);
                    AddProfile(profile);
                    _profileStorageFileNames[profile] = Path.GetFileName(filePath);
                    loadedCount++;
                }
                catch (Exception ex)
                {
                    SetPageStatus($"读取脚本失败：{Path.GetFileName(filePath)}，原因：{ex.Message}", WarningBrush);
                }
            }

            return loadedCount;
        }

        private static LuaScriptProfileDocument? DeserializeProfileDocument(string storageText)
        {
            try
            {
                return JsonHelper.DeserializeObject<LuaScriptProfileDocument>(storageText);
            }
            catch
            {
                return JsonHelper.DeserializeObject<LuaScriptProfileDocument>(storageText.DesDecrypt());
            }
        }

        private int SaveProfilesToDisk()
        {
            Directory.CreateDirectory(LuaScriptConfigDirectory);

            HashSet<string> usedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<LuaScriptProfile, string> targetFileNames = new Dictionary<LuaScriptProfile, string>();
            foreach (LuaScriptProfile profile in Profiles)
            {
                ValidateProfileForSave(profile);
                targetFileNames[profile] = BuildUniqueStorageFileName(profile.Name, usedFileNames);
            }

            int savedCount = 0;
            foreach (LuaScriptProfile profile in Profiles)
            {
                string fileName = targetFileNames[profile];
                string filePath = Path.Combine(LuaScriptConfigDirectory, fileName);
                string storageText = JsonHelper.SerializeObject(LuaScriptProfileDocument.FromProfile(profile));
                File.WriteAllText(filePath, storageText, Encoding.UTF8);
                savedCount++;
            }

            foreach (LuaScriptProfile profile in Profiles)
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

        private static void ValidateProfileForSave(LuaScriptProfile profile)
        {
            if (string.IsNullOrWhiteSpace(profile.Name))
            {
                throw new InvalidOperationException("脚本名称不能为空。");
            }
        }

        private void DeleteStoredProfileFile(LuaScriptProfile profile)
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
                string filePath = Path.Combine(LuaScriptConfigDirectory, fileName);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch
            {
            }
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
            HashSet<char> invalidChars = new HashSet<char>(Path.GetInvalidFileNameChars());
            StringBuilder builder = new StringBuilder(value.Trim().Length);
            foreach (char current in value.Trim())
            {
                builder.Append(invalidChars.Contains(current) || char.IsControl(current)
                    ? '_'
                    : char.IsWhiteSpace(current) ? '_' : current);
            }

            string safeName = builder.ToString().Trim(' ', '.');
            if (string.IsNullOrWhiteSpace(safeName))
            {
                safeName = "LuaScript";
            }

            return safeName.Length <= 80 ? safeName : safeName[..80];
        }

        private string BuildUniqueLoadedName(string loadedName)
        {
            string baseName = string.IsNullOrWhiteSpace(loadedName) ? "Lua 脚本" : loadedName.Trim();
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
            string prefix = string.IsNullOrWhiteSpace(baseName) ? "Lua 脚本" : baseName.Trim();
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
    }
}
