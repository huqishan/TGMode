using ControlLibrary;
using Module.MES.ViewModels;
using Newtonsoft.Json;
using Shared.Models.MES;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Module.MES.ViewModels
{
    /// <summary>
    /// 接口配置页属性集中声明，便于 XAML 绑定和后续维护。
    /// </summary>
    public sealed partial class ApiConfigViewModel
    {
        #region 配置路径字段

        private static readonly string MesConfigRootDirectory =
            Path.Combine(AppContext.BaseDirectory, "Config", "MES_Config");

        private static readonly string ApiConfigDirectory =
            Path.Combine(MesConfigRootDirectory, "ApiConfig");

        private static readonly string MesSystemConfigFilePath =
            Path.Combine(MesConfigRootDirectory, "MesSystemConfig", "MesSystemConfig.json");

        private static readonly string DataStructureConfigDirectory =
            Path.Combine(MesConfigRootDirectory, "DataStructure");

        #endregion

        #region 状态颜色字段

        private static readonly Brush SuccessBrush =
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));

        private static readonly Brush WarningBrush =
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EA580C"));

        private static readonly Brush NeutralBrush =
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));

        #endregion

        #region 抽屉布局字段

        private const double HeaderDrawerClosedOffset = 40d;

        #endregion

        #region 私有状态字段

        private readonly Dictionary<ApiInterfaceProfile, string> _profileStorageFileNames = new();
        private ApiInterfaceProfile? _selectedProfile;
        private string _searchText = string.Empty;
        private string _pageStatusText = "等待编辑";
        private Brush _pageStatusBrush = NeutralBrush;
        private bool _isBusy;
        private bool _isHeaderDrawerOpen;

        #endregion

        #region 集合属性

        public ObservableCollection<ApiInterfaceProfile> Profiles { get; } = new();

        public ObservableCollection<ApiOptionItem> MethodTypes { get; } = new();

        public ObservableCollection<ApiOptionItem> WebApiMethods { get; } = new();

        public ObservableCollection<string> DataStructureOptions { get; } = new();

        public ICollectionView ProfilesView { get; private set; } = null!;

        #endregion

        #region 当前编辑属性

        public ApiInterfaceProfile? SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                if (ReferenceEquals(_selectedProfile, value))
                {
                    return;
                }

                _selectedProfile = value;
                CloseHeaderDrawer();
                OnPropertyChanged();
                RaiseCommandStatesChanged();
            }
        }

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

        #endregion

        #region 页面状态属性

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

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetField(ref _isBusy, value))
                {
                    RaiseCommandStatesChanged();
                }
            }
        }

        #endregion

        #region 请求头抽屉属性

        public bool IsHeaderDrawerOpen
        {
            get => _isHeaderDrawerOpen;
            private set
            {
                if (!SetField(ref _isHeaderDrawerOpen, value))
                {
                    return;
                }

                OnPropertyChanged(nameof(HeaderDrawerOpacity));
                OnPropertyChanged(nameof(HeaderDrawerOffset));
            }
        }

        public double HeaderDrawerOpacity => IsHeaderDrawerOpen ? 1d : 0d;

        public double HeaderDrawerOffset => IsHeaderDrawerOpen ? 0d : HeaderDrawerClosedOffset;

        #endregion

        #region 接口配置命令

        public ICommand NewProfileCommand { get; private set; } = null!;

        public ICommand DuplicateProfileCommand { get; private set; } = null!;

        public ICommand DeleteProfileCommand { get; private set; } = null!;

        public ICommand SaveProfilesCommand { get; private set; } = null!;

        public ICommand RefreshStructuresCommand { get; private set; } = null!;

        public ICommand GeneratePayloadCommand { get; private set; } = null!;

        public ICommand TestInterfaceCommand { get; private set; } = null!;

        #endregion

        #region 请求头命令

        public ICommand OpenHeaderDrawerCommand { get; private set; } = null!;

        public ICommand CloseHeaderDrawerCommand { get; private set; } = null!;

        public ICommand AddHeaderCommand { get; private set; } = null!;

        public ICommand DeleteHeaderCommand { get; private set; } = null!;

        public ICommand SaveHeaderCommand { get; private set; } = null!;

        #endregion

    }
    
}
