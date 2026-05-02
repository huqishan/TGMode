using ControlLibrary;
using Shared.Infrastructure.Extensions;
using Shared.Infrastructure.PackMethod;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Module.MES.ViewModels
{
    /// <summary>
    /// 数据结构配置页 ViewModel，负责配置读写、命令和页面业务状态。
    /// </summary>
    public sealed partial class DataStructureConfigViewModel
    {
        #region 构造与初始化

        public DataStructureConfigViewModel()
        {
            InitializeCommands();

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

            SelectedProfile = Profiles.FirstOrDefault();
        }

        /// <summary>
        /// 初始化界面可命令化操作。
        /// </summary>
        private void InitializeCommands()
        {
            AddProfileCommand = new RelayCommand(_ => AddProfile());
            DuplicateProfileCommand = new RelayCommand(_ => DuplicateProfile(), _ => HasSelectedProfile());
            DeleteProfileCommand = new RelayCommand(_ => DeleteProfile(), _ => HasSelectedProfile());
            SaveProfilesCommand = new RelayCommand(_ => SaveProfiles());

            AddFieldChildCommand = new RelayCommand(_ => AddFieldChild(), _ => HasSelectedProfile());
            CopyFieldCommand = new RelayCommand(_ => CopyField(), _ => HasSelectedField);
            PasteFieldCommand = new RelayCommand(_ => PasteField(), _ => HasSelectedProfile() && _copiedField is not null);
            DeleteFieldCommand = new RelayCommand(_ => DeleteField(), _ => HasSelectedProfile() && HasSelectedField);

            OpenStructureDrawerCommand = new RelayCommand(_ => OpenStructureDrawer(), _ => HasSelectedField);
            CloseStructureDrawerCommand = new RelayCommand(_ => CloseStructureDrawer());
        }

        #endregion

        #region 配置命令方法

        /// <summary>
        /// 新增默认数据结构配置。
        /// </summary>
        private void AddProfile()
        {
            DataStructureProfile profile = CreateDefaultProfile(GenerateUniqueName("数据结构"));
            AddProfile(profile);
            SelectedProfile = profile;
            SetPageStatus($"已新增数据结构：{profile.Name}", SuccessBrush);
        }

        /// <summary>
        /// 复制当前数据结构配置。
        /// </summary>
        private void DuplicateProfile()
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

        /// <summary>
        /// 删除当前数据结构配置和本地存储文件。
        /// </summary>
        private void DeleteProfile()
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

        /// <summary>
        /// 保存全部数据结构配置。
        /// </summary>
        private void SaveProfiles()
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

        #endregion

        #region 字段命令方法

        /// <summary>
        /// 在当前节点下新增子字段；未选中字段时添加到根级。
        /// </summary>
        private void AddFieldChild()
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

        /// <summary>
        /// 复制当前选中的结构字段。
        /// </summary>
        private void CopyField()
        {
            if (SelectedField is null)
            {
                SetPageStatus("请先选择需要复制的结构字段。", WarningBrush);
                return;
            }

            _copiedField = SelectedField.Clone();
            SetPageStatus($"已复制结构字段：{SelectedField.ClientCode}", SuccessBrush);
            RaiseCommandStatesChanged();
        }

        /// <summary>
        /// 将已复制字段粘贴到当前节点下；未选中字段时粘贴到根级。
        /// </summary>
        private void PasteField()
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

        /// <summary>
        /// 删除当前选中的结构字段。
        /// </summary>
        private void DeleteField()
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

        #endregion

        #region 抽屉命令方法

        /// <summary>
        /// 请求打开节点详细信息抽屉。
        /// </summary>
        private void OpenStructureDrawer()
        {
            if (SelectedField is null)
            {
                SetPageStatus("请先选择结构字段。", WarningBrush);
                return;
            }

            IsStructureDrawerOpen = true;
        }

        /// <summary>
        /// 请求关闭节点详细信息抽屉。
        /// </summary>
        private void CloseStructureDrawer()
        {
            IsStructureDrawerOpen = false;
        }

        #endregion

        #region 配置持久化方法

        /// <summary>
        /// 将配置加入列表并监听变化。
        /// </summary>
        private void AddProfile(DataStructureProfile profile)
        {
            profile.PropertyChanged += Profile_PropertyChanged;
            Profiles.Add(profile);
        }

        /// <summary>
        /// 从本地数据结构目录读取配置。
        /// </summary>
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

        /// <summary>
        /// 保存当前列表中的全部数据结构配置。
        /// </summary>
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

        /// <summary>
        /// 删除已保存的数据结构配置文件。
        /// </summary>
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

        #endregion

        #region 配置工厂方法

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
            string sourceValue = string.IsNullOrWhiteSpace(value) ? "DataStructure" : value.Trim();
            HashSet<char> invalidChars = new(Path.GetInvalidFileNameChars());
            StringBuilder builder = new(sourceValue.Length);
            foreach (char current in sourceValue)
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

        #endregion

        #region 列表过滤与状态刷新方法

        /// <summary>
        /// 配置内容变化后刷新列表摘要。
        /// </summary>
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

        private bool HasSelectedProfile()
        {
            return SelectedProfile is not null;
        }

        private void RaiseCommandStatesChanged()
        {
            RaiseCommandState(DuplicateProfileCommand);
            RaiseCommandState(DeleteProfileCommand);
            RaiseCommandState(AddFieldChildCommand);
            RaiseCommandState(CopyFieldCommand);
            RaiseCommandState(PasteFieldCommand);
            RaiseCommandState(DeleteFieldCommand);
            RaiseCommandState(OpenStructureDrawerCommand);
        }

        private static void RaiseCommandState(ICommand? command)
        {
            if (command is RelayCommand relayCommand)
            {
                relayCommand.RaiseCanExecuteChanged();
            }
        }

        #endregion
    }
}
