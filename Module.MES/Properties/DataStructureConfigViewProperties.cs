using ControlLibrary;
using Module.MES.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Module.MES.ViewModels
{
    /// <summary>
    /// 数据结构配置页属性集中声明，供 XAML 绑定。
    /// </summary>
    public sealed partial class DataStructureConfigViewModel
    {
        #region 配置路径字段

        private static readonly string MesConfigRootDirectory =
            Path.Combine(AppContext.BaseDirectory, "Config", "MES_Config");

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

        #region 私有状态字段

        private readonly Dictionary<DataStructureProfile, string> _profileStorageFileNames = new();
        private DataStructureProfile? _selectedProfile;
        private DataStructureLayout? _selectedField;
        private DataStructureLayout? _copiedField;
        private string _searchText = string.Empty;
        private string _pageStatusText = "等待编辑";
        private Brush _pageStatusBrush = NeutralBrush;
        private bool _isStructureDrawerOpen;

        #endregion

        #region 集合属性

        public ObservableCollection<DataStructureProfile> Profiles { get; } = new();

        public ObservableCollection<DataStructureTypeOption> StructureTypes { get; } =
            new(DataStructureTypes.Options);

        public ICollectionView ProfilesView { get; private set; } = null!;

        #endregion

        #region 当前编辑属性

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
                RaiseCommandStatesChanged();
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
                if (_selectedField is null)
                {
                    CloseStructureDrawer();
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelectedField));
                RaiseCommandStatesChanged();
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

        #endregion

        #region 抽屉状态属性

        public bool IsStructureDrawerOpen
        {
            get => _isStructureDrawerOpen;
            private set => SetField(ref _isStructureDrawerOpen, value);
        }

        #endregion

        #region 配置命令

        public ICommand AddProfileCommand { get; private set; } = null!;

        public ICommand DuplicateProfileCommand { get; private set; } = null!;

        public ICommand DeleteProfileCommand { get; private set; } = null!;

        public ICommand SaveProfilesCommand { get; private set; } = null!;

        #endregion

        #region 字段命令

        public ICommand AddFieldChildCommand { get; private set; } = null!;

        public ICommand CopyFieldCommand { get; private set; } = null!;

        public ICommand PasteFieldCommand { get; private set; } = null!;

        public ICommand DeleteFieldCommand { get; private set; } = null!;

        #endregion

        #region 抽屉命令

        public ICommand OpenStructureDrawerCommand { get; private set; } = null!;

        public ICommand CloseStructureDrawerCommand { get; private set; } = null!;

        #endregion

    }
}
