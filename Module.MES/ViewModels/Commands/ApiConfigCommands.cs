using ControlLibrary;
using Shared.Infrastructure.Extensions;
using Shared.Infrastructure.PackMethod;
using Shared.Models.MES;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Module.MES.ViewModels
{
    /// <summary>
    /// 接口配置页 ViewModel，承载页面状态、命令和配置读写逻辑。
    /// </summary>
    public sealed partial class ApiConfigViewModel
    {

        #region 构造与初始化

        public ApiConfigViewModel()
        {
            InitializeOptionItems();
            InitializeCommands();

            ProfilesView = CollectionViewSource.GetDefaultView(Profiles);
            ProfilesView.Filter = FilterProfiles;

            RefreshDataStructureOptions();
            int loadedCount = LoadProfilesFromDisk();
            if (loadedCount == 0)
            {
                ApiInterfaceProfile sampleProfile = CreateSampleProfile();
                AddProfile(sampleProfile);
                SetPageStatus("未发现本地接口配置，已创建默认示例。", NeutralBrush);
            }
            else
            {
                SetPageStatus($"已读取 {loadedCount} 个接口配置。", SuccessBrush);
            }

            SelectedProfile = Profiles.FirstOrDefault();
            CloseHeaderDrawer();
        }

        /// <summary>
        /// 初始化固定选项，供调用方式和 WebApi 方法下拉绑定。
        /// </summary>
        private void InitializeOptionItems()
        {
            MethodTypes.Add(new ApiOptionItem("TCP CLIENT", "TCP Client", "通过 TCP Client 调用接口。"));
            MethodTypes.Add(new ApiOptionItem("WEBAPI", "WebApi", "通过 HTTP 接口调用 MES。"));
            MethodTypes.Add(new ApiOptionItem("WEBSERVICE", "WebService", "通过 SOAP/XML 调用 MES。"));
            MethodTypes.Add(new ApiOptionItem("FTP", "FTP", "通过 FTP 上传或下载文件。"));
            MethodTypes.Add(new ApiOptionItem("P-INVOKE", "P-Invoke", "保留扩展调用入口。"));

            WebApiMethods.Add(new ApiOptionItem("POST", "POST"));
            WebApiMethods.Add(new ApiOptionItem("GET", "GET"));
        }

        /// <summary>
        /// 初始化页面所有可命令化操作，XAML 不再绑定后台 Click 事件。
        /// </summary>
        private void InitializeCommands()
        {
            NewProfileCommand = new RelayCommand(_ => NewProfile());
            DuplicateProfileCommand = new RelayCommand(_ => DuplicateProfile(), _ => HasSelectedProfile());
            DeleteProfileCommand = new RelayCommand(_ => DeleteProfile(), _ => HasSelectedProfile());
            SaveProfilesCommand = new RelayCommand(_ => SaveProfiles());
            RefreshStructuresCommand = new RelayCommand(_ => RefreshStructures());
            GeneratePayloadCommand = new RelayCommand(_ => GeneratePayload(), _ => HasSelectedProfile());
            TestInterfaceCommand = new AsyncRelayCommand(_ => TestInterfaceAsync(), _ => HasSelectedProfile() && !IsBusy);

            OpenHeaderDrawerCommand = new RelayCommand(_ => OpenHeaderDrawer(), _ => HasSelectedProfile());
            CloseHeaderDrawerCommand = new RelayCommand(_ => CloseHeaderDrawer());
            AddHeaderCommand = new RelayCommand(_ => AddHeader(), _ => HasSelectedProfile());
            DeleteHeaderCommand = new RelayCommand(_ => DeleteHeader(), _ => SelectedProfile?.SelectedHeader is not null);
            SaveHeaderCommand = new RelayCommand(_ => SaveHeader(), _ => HasSelectedProfile());

        }

        #endregion

        #region 接口配置命令方法

        /// <summary>
        /// 新增一个默认接口配置并立即选中。
        /// </summary>
        private void NewProfile()
        {
            ApiInterfaceProfile profile = CreateDefaultProfile(GenerateUniqueName("MES 接口"));
            AddProfile(profile);
            SelectedProfile = profile;
            SetPageStatus("已新增接口配置。", SuccessBrush);
        }

        /// <summary>
        /// 复制当前接口配置，保留请求头和连接参数。
        /// </summary>
        private void DuplicateProfile()
        {
            if (SelectedProfile is null)
            {
                SetPageStatus("请先选择需要复制的接口配置。", WarningBrush);
                return;
            }

            ApiInterfaceProfile copy = SelectedProfile.Clone(GenerateCopyName(SelectedProfile.ApiName));
            AddProfile(copy);
            SelectedProfile = copy;
            SetPageStatus("已复制当前接口配置。", SuccessBrush);
        }

        /// <summary>
        /// 删除当前接口配置和对应的本地存储文件。
        /// </summary>
        private void DeleteProfile()
        {
            if (SelectedProfile is null)
            {
                SetPageStatus("请先选择需要删除的接口配置。", WarningBrush);
                return;
            }

            int selectedIndex = Profiles.IndexOf(SelectedProfile);
            ApiInterfaceProfile profile = SelectedProfile;
            DeleteStoredProfileFile(profile);
            profile.PropertyChanged -= Profile_PropertyChanged;
            Profiles.Remove(profile);

            if (Profiles.Count == 0)
            {
                AddProfile(CreateDefaultProfile(GenerateUniqueName("MES 接口")));
            }

            SelectedProfile = Profiles[Math.Clamp(selectedIndex, 0, Profiles.Count - 1)];
            SetPageStatus("已删除接口配置。", WarningBrush);
        }

        /// <summary>
        /// 保存当前页面所有接口配置。
        /// </summary>
        private void SaveProfiles()
        {
            try
            {
                int savedCount = SaveProfilesToDisk();
                SetPageStatus($"已保存 {savedCount} 个接口配置。", SuccessBrush);
            }
            catch (Exception ex)
            {
                SetPageStatus($"保存失败：{ex.Message}", WarningBrush);
            }
        }

        /// <summary>
        /// 重新读取数据结构配置目录中的可选结构。
        /// </summary>
        private void RefreshStructures()
        {
            RefreshDataStructureOptions();
            SetPageStatus($"已刷新数据结构选项，共 {DataStructureOptions.Count} 个。", SuccessBrush);
        }

        /// <summary>
        /// 根据选中的数据结构生成上传报文预览。
        /// </summary>
        private void GeneratePayload()
        {
            if (SelectedProfile is null)
            {
                SetPageStatus("请先选择接口配置。", WarningBrush);
                return;
            }

            if (string.IsNullOrWhiteSpace(SelectedProfile.DataStructName))
            {
                SetPageStatus("请先选择数据结构，再生成上传报文。", WarningBrush);
                return;
            }

            try
            {
                string? payload = MesDataConvert.Convert(new MesDataInfoTree(), SelectedProfile.DataStructName);
                if (string.IsNullOrWhiteSpace(payload))
                {
                    SetPageStatus($"数据结构 {SelectedProfile.DataStructName} 未生成有效报文，请检查结构内容。", WarningBrush);
                    return;
                }

                SelectedProfile.SampleRequestBody = FormatResponseText(payload);
                SetPageStatus($"已生成上传报文：{SelectedProfile.DataStructName}", SuccessBrush);
            }
            catch (Exception ex)
            {
                SetPageStatus($"生成上传报文失败：{ex.Message}", WarningBrush);
            }
        }

        /// <summary>
        /// 保存配置后调用 MES 测试接口，并把请求和返回结果回填到预览区。
        /// </summary>
        private async Task TestInterfaceAsync()
        {
            if (SelectedProfile is null || IsBusy)
            {
                return;
            }

            ApiInterfaceProfile profile = SelectedProfile;

            try
            {
                ValidateProfileForSave(profile);
                EnsureMesSystemConfigExists();
                SaveProfilesToDisk(profile);

                string requestPayload = profile.SampleRequestBody ?? string.Empty;
                IsBusy = true;
                SetPageStatus($"正在测试接口：{profile.ApiName}", NeutralBrush);

                (MesResult Result, string Payload) testResult = await Task.Run(() =>
                {
                    string payloadCopy = requestPayload;
                    MesResult result = MesDataConvert.SendMES(profile.ApiName, ref payloadCopy, null);
                    return (result, payloadCopy);
                });

                profile.SampleRequestBody = testResult.Payload;
                profile.SampleResponseBody = FormatResponseText(testResult.Result.Message);

                if (testResult.Result.State == MesStatus.ResultOK)
                {
                    SetPageStatus($"接口测试成功：{profile.ApiName}", SuccessBrush);
                    return;
                }

                SetPageStatus($"接口测试完成，但返回状态为 {testResult.Result.State}。", WarningBrush);
            }
            catch (Exception ex)
            {
                profile.SampleResponseBody = ex.Message;
                SetPageStatus($"测试失败：{ex.Message}", WarningBrush);
            }
            finally
            {
                IsBusy = false;
            }
        }

        #endregion

        #region 请求头抽屉方法

        /// <summary>
        /// 打开请求头编辑抽屉。
        /// </summary>
        private void OpenHeaderDrawer()
        {
            if (SelectedProfile is null)
            {
                SetPageStatus("请先选择接口配置。", WarningBrush);
                return;
            }

            IsHeaderDrawerOpen = true;
        }

        /// <summary>
        /// 关闭请求头编辑抽屉。
        /// </summary>
        private void CloseHeaderDrawer()
        {
            IsHeaderDrawerOpen = false;
        }

        /// <summary>
        /// 新增一条默认请求头。
        /// </summary>
        private void AddHeader()
        {
            if (SelectedProfile is null)
            {
                SetPageStatus("请先选择接口配置。", WarningBrush);
                return;
            }

            ApiHeaderItem header = new()
            {
                Key = "Content-Type",
                Value = "application/json"
            };

            SelectedProfile.Heads.Add(header);
            SelectedProfile.SelectedHeader = header;
            SetPageStatus("已新增请求头。", SuccessBrush);
            RaiseCommandStatesChanged();
        }

        /// <summary>
        /// 删除当前选中的请求头。
        /// </summary>
        private void DeleteHeader()
        {
            if (SelectedProfile?.SelectedHeader is null)
            {
                SetPageStatus("请先选择需要删除的请求头。", WarningBrush);
                return;
            }

            ApiHeaderItem header = SelectedProfile.SelectedHeader;
            SelectedProfile.Heads.Remove(header);
            SelectedProfile.SelectedHeader = SelectedProfile.Heads.FirstOrDefault();
            SetPageStatus("已删除请求头。", WarningBrush);
            RaiseCommandStatesChanged();
        }

        /// <summary>
        /// 保存当前接口的请求头配置。
        /// </summary>
        private void SaveHeader()
        {
            if (SelectedProfile is null)
            {
                SetPageStatus("请先选择接口配置。", WarningBrush);
                return;
            }

            try
            {
                SaveProfilesToDisk(SelectedProfile);
                SetPageStatus("请求头配置已保存。", SuccessBrush);
                CloseHeaderDrawer();
            }
            catch (Exception ex)
            {
                SetPageStatus($"请求头保存失败：{ex.Message}", WarningBrush);
            }
        }

        #endregion

        #region 配置持久化方法

        /// <summary>
        /// 将配置加入列表并监听属性变化，保证列表摘要能实时刷新。
        /// </summary>
        private void AddProfile(ApiInterfaceProfile profile)
        {
            profile.PropertyChanged += Profile_PropertyChanged;
            Profiles.Add(profile);
        }

        /// <summary>
        /// 从接口配置目录加载本地 JSON 文件。
        /// </summary>
        private int LoadProfilesFromDisk()
        {
            if (!Directory.Exists(ApiConfigDirectory))
            {
                return 0;
            }

            int loadedCount = 0;
            foreach (string filePath in Directory.EnumerateFiles(ApiConfigDirectory, "*.json").OrderBy(Path.GetFileName))
            {
                try
                {
                    APIConfig? config = JsonHelper.ReadJson<APIConfig>(filePath);
                    if (config is null)
                    {
                        continue;
                    }

                    string fallbackName = Path.GetFileNameWithoutExtension(filePath);
                    ApiInterfaceProfile profile = ApiInterfaceProfile.FromApiConfig(config, fallbackName);
                    AddProfile(profile);
                    _profileStorageFileNames[profile] = Path.GetFileName(filePath);
                    loadedCount++;
                }
                catch (Exception ex)
                {
                    SetPageStatus($"读取接口配置失败：{Path.GetFileName(filePath)}，原因：{ex.Message}", WarningBrush);
                }
            }

            return loadedCount;
        }

        /// <summary>
        /// 保存全部接口配置，或只保存指定接口配置。
        /// </summary>
        private int SaveProfilesToDisk(ApiInterfaceProfile? apiProfile = null)
        {
            Directory.CreateDirectory(ApiConfigDirectory);
            IEnumerable<ApiInterfaceProfile> targetProfiles = apiProfile is null
                ? Profiles
                : new[] { apiProfile };
            HashSet<string> usedFileNames = new(StringComparer.OrdinalIgnoreCase);
            int savedCount = 0;

            foreach (ApiInterfaceProfile profile in targetProfiles)
            {
                ValidateProfileForSave(profile);

                string fileName = BuildUniqueStorageFileName(profile.ApiName, usedFileNames);
                string filePath = Path.Combine(ApiConfigDirectory, fileName);
                JsonHelper.SaveJson(profile.ToApiConfig(), filePath);
                if (_profileStorageFileNames.TryGetValue(profile, out string? oldFileName) &&
                    !string.Equals(oldFileName, fileName, StringComparison.OrdinalIgnoreCase))
                {
                    TryDeleteStorageFile(oldFileName);
                }

                _profileStorageFileNames[profile] = fileName;
                savedCount++;
            }

            return savedCount;
        }

        /// <summary>
        /// 校验保存所需的关键字段。
        /// </summary>
        private static void ValidateProfileForSave(ApiInterfaceProfile profile)
        {
            if (string.IsNullOrWhiteSpace(profile.ApiName))
            {
                throw new InvalidOperationException("方法名称不能为空。");
            }

            ValidatePort(profile.TCPLocalPort, "本地端口");
            ValidatePort(profile.TCPRemotePort, "远程端口");
        }

        private static void ValidatePort(string value, string displayName)
        {
            if (!ushort.TryParse(value?.Trim(), out _))
            {
                throw new InvalidOperationException($"{displayName} 必须是 0-65535 之间的整数。");
            }
        }

        /// <summary>
        /// 删除接口配置对应的旧文件。
        /// </summary>
        private void DeleteStoredProfileFile(ApiInterfaceProfile profile)
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
                string filePath = Path.Combine(ApiConfigDirectory, fileName);
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
            string sourceValue = string.IsNullOrWhiteSpace(value) ? "ApiConfig" : value.Trim();
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
                safeName = "ApiConfig";
            }

            return safeName.Length <= 80 ? safeName : safeName[..80];
        }

        #endregion

        #region 数据结构与报文方法

        /// <summary>
        /// 刷新接口可绑定的数据结构名称。
        /// </summary>
        private void RefreshDataStructureOptions()
        {
            DataStructureOptions.Clear();

            if (!Directory.Exists(DataStructureConfigDirectory))
            {
                return;
            }

            foreach (string filePath in Directory.EnumerateFiles(DataStructureConfigDirectory, "*.json").OrderBy(Path.GetFileName))
            {
                DataStructureOptions.Add(Path.GetFileName(filePath).Replace(".json", ""));
            }
        }

        private void EnsureMesSystemConfigExists()
        {
            if (File.Exists(MesSystemConfigFilePath))
            {
                return;
            }

            JsonHelper.SaveJson(new MesSystemConfig(), MesSystemConfigFilePath);
        }

        private static string FormatResponseText(string? responseText)
        {
            if (string.IsNullOrWhiteSpace(responseText))
            {
                return string.Empty;
            }

            string trimmed = responseText.Trim();
            if (trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                return trimmed.ToJsonFormat();
            }

            if (trimmed.StartsWith("<", StringComparison.Ordinal))
            {
                return trimmed.ToXMLFormat();
            }

            return responseText;
        }

        #endregion

        #region 配置工厂方法

        private ApiInterfaceProfile CreateSampleProfile()
        {
            ApiInterfaceProfile profile = CreateDefaultProfile("工站状态上报");
            profile.Remarks = "默认示例接口，用于快速补齐 WebApi 调用参数。";
            profile.ResultCheck = "\"code\":200";
            profile.Url = "https://mes.example.com/api/report";
            profile.TokenUrl = "https://mes.example.com/api/token";
            profile.TokenName = "accessToken";
            profile.Heads.Clear();
            profile.Heads.Add(new ApiHeaderItem { Key = "Content-Type", Value = "application/json" });
            profile.Heads.Add(new ApiHeaderItem { Key = "Authorization", Value = "ACCESS_TOKEN" });
            profile.SelectedHeader = profile.Heads.FirstOrDefault();
            return profile;
        }

        private static ApiInterfaceProfile CreateDefaultProfile(string name)
        {
            ApiInterfaceProfile profile = new()
            {
                ApiName = name,
                SelectMESType = "WEBAPI",
                IsEnabledAPI = true,
                IsCommunicationQueryVisible = true,
                WebApiType = "POST",
                Lua = $"return SendMES(\"{name}\")"
            };

            profile.Heads.Add(new ApiHeaderItem { Key = "Content-Type", Value = "application/json" });
            profile.SelectedHeader = profile.Heads.FirstOrDefault();
            return profile;
        }

        private string GenerateUniqueName(string prefix)
        {
            for (int index = 1; ; index++)
            {
                string name = $"{prefix} {index}";
                if (!Profiles.Any(profile => string.Equals(profile.ApiName, name, StringComparison.OrdinalIgnoreCase)))
                {
                    return name;
                }
            }
        }

        private string GenerateCopyName(string baseName)
        {
            string prefix = string.IsNullOrWhiteSpace(baseName) ? "MES 接口" : baseName.Trim();
            string firstName = $"{prefix} 副本";
            if (!Profiles.Any(profile => string.Equals(profile.ApiName, firstName, StringComparison.OrdinalIgnoreCase)))
            {
                return firstName;
            }

            for (int index = 2; ; index++)
            {
                string name = $"{firstName} {index}";
                if (!Profiles.Any(profile => string.Equals(profile.ApiName, name, StringComparison.OrdinalIgnoreCase)))
                {
                    return name;
                }
            }
        }

        #endregion

        #region 列表过滤与状态刷新方法

        /// <summary>
        /// 当配置内容变化时，刷新列表摘要和命令可用状态。
        /// </summary>
        private void Profile_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(ApiInterfaceProfile.ApiName) or
                nameof(ApiInterfaceProfile.Remarks) or
                nameof(ApiInterfaceProfile.DataStructName) or
                nameof(ApiInterfaceProfile.SelectMESType) or
                nameof(ApiInterfaceProfile.IsEnabledAPI) or
                nameof(ApiInterfaceProfile.Summary))
            {
                ProfilesView.Refresh();
            }

            if (e.PropertyName == nameof(ApiInterfaceProfile.SelectMESType))
            {
                CloseHeaderDrawer();
            }

            if (e.PropertyName is nameof(ApiInterfaceProfile.SelectedHeader) or
                nameof(ApiInterfaceProfile.HasSelectedHeader))
            {
                RaiseCommandStatesChanged();
            }
        }

        private bool FilterProfiles(object item)
        {
            if (item is not ApiInterfaceProfile profile)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(SearchText))
            {
                return true;
            }

            string keyword = SearchText.Trim();
            return Contains(profile.ApiName, keyword) ||
                   Contains(profile.Remarks, keyword) ||
                   Contains(profile.DataStructName, keyword) ||
                   Contains(profile.TypeDisplayName, keyword) ||
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
            RaiseCommandState(GeneratePayloadCommand);
            RaiseCommandState(TestInterfaceCommand);
            RaiseCommandState(OpenHeaderDrawerCommand);
            RaiseCommandState(AddHeaderCommand);
            RaiseCommandState(DeleteHeaderCommand);
            RaiseCommandState(SaveHeaderCommand);
        }

        private static void RaiseCommandState(ICommand? command)
        {
            switch (command)
            {
                case RelayCommand relayCommand:
                    relayCommand.RaiseCanExecuteChanged();
                    break;
                case AsyncRelayCommand asyncRelayCommand:
                    asyncRelayCommand.RaiseCanExecuteChanged();
                    break;
            }
        }

        #endregion
    }
}
