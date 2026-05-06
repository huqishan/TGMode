using Module.Communication.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Module.Communication.ViewModels;

/// <summary>
/// 协议配置界面的属性、字段和命令声明，供 XAML 绑定使用。
/// </summary>
public sealed partial class ProtocolConfigViewModel
{
    #region 状态颜色与路径字段
    private static readonly string ProtocolConfigDirectory =
        Path.Combine(AppContext.BaseDirectory, "Config", "Protocol");

    private static readonly Brush SuccessBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));

    private static readonly Brush WarningBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EA580C"));

    private static readonly Brush NeutralBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));

    #endregion

    #region 私有状态字段
    private readonly Dictionary<ProtocolConfigProfile, string> _profileStorageFileNames = new();
    private ProtocolConfigProfile? _selectedProfile;
    private string _pageStatusText = "等待初始化";
    private Brush _pageStatusBrush = NeutralBrush;
    private string _requestPreviewStatusText = "等待输入";
    private Brush _requestPreviewStatusBrush = NeutralBrush;
    private string _responsePreviewStatusText = "等待输入";
    private Brush _responsePreviewStatusBrush = NeutralBrush;
    private string _requestPreviewText = "请先选择或创建一个协议配置。";
    private string _responsePreviewText = "请填写示例返回数据后查看解析预览。";
    private string _generatedCommandText = string.Empty;
    private string _parsedResultText = string.Empty;
    private string _searchText = string.Empty;
    private bool _isCommandDrawerOpen;

    #endregion

    #region 集合属性
    public ObservableCollection<ProtocolConfigProfile> Profiles { get; } = new();

    public ICollectionView ProfilesView { get; private set; } = null!;

    public ObservableCollection<ProtocolOption<ProtocolPayloadFormat>> PayloadFormats { get; } = new();

    public ObservableCollection<ProtocolOption<ProtocolCrcMode>> CrcModes { get; } = new();

    #endregion

    #region 当前编辑属性
    public ProtocolConfigProfile? SelectedProfile
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
            UpdatePreviews();
            CloseCommandDrawer();
            ClearGeneratedOutputs();
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

            ProfilesView?.Refresh();
        }
    }

    public bool IsCommandDrawerOpen
    {
        get => _isCommandDrawerOpen;
        private set
        {
            if (SetField(ref _isCommandDrawerOpen, value))
            {
                RaiseCommandStatesChanged();
            }
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

    public string RequestPreviewStatusText
    {
        get => _requestPreviewStatusText;
        private set => SetField(ref _requestPreviewStatusText, value);
    }

    public Brush RequestPreviewStatusBrush
    {
        get => _requestPreviewStatusBrush;
        private set => SetField(ref _requestPreviewStatusBrush, value);
    }

    public string ResponsePreviewStatusText
    {
        get => _responsePreviewStatusText;
        private set => SetField(ref _responsePreviewStatusText, value);
    }

    public Brush ResponsePreviewStatusBrush
    {
        get => _responsePreviewStatusBrush;
        private set => SetField(ref _responsePreviewStatusBrush, value);
    }

    public string RequestPreviewText
    {
        get => _requestPreviewText;
        private set => SetField(ref _requestPreviewText, value);
    }

    public string ResponsePreviewText
    {
        get => _responsePreviewText;
        private set => SetField(ref _responsePreviewText, value);
    }

    public string GeneratedCommandText
    {
        get => _generatedCommandText;
        private set => SetField(ref _generatedCommandText, value);
    }

    public string ParsedResultText
    {
        get => _parsedResultText;
        private set => SetField(ref _parsedResultText, value);
    }

    public string ReplyWaitHelpText =>
        "发送后可强制等待一段时间，把这段时间内收到的数据拼接为同一帧。填 0 表示不额外等待。";

    public string CrcHelpText =>
        "发送帧预览会按当前 CRC 方式自动在末尾追加校验字节，便于直接检查最终报文。";

    public string PlaceholderHelpText =>
        "模板中使用 {{Placeholder}} 形式占位；占位符值会从模板自动提取，可在表格中填写对应值。";

    public string ParseRuleHelpText =>
        "规则格式：Field = Expression。支持 hex、ascii、utf8、text、len、hex(start,length)、ascii(start,length)、utf8(start,length)、u8(index)、u16le(index)、u16be(index)、u32le(index)、u32be(index)，其中 length=-1 表示截取到末尾。";

    public string WaitForResponseHelpText =>
        "选中后显示返回示例与解析规则，并允许对返回数据执行解析。";

    #endregion

    #region 命令属性
    public ICommand NewProfileCommand { get; private set; } = null!;

    public ICommand DuplicateProfileCommand { get; private set; } = null!;

    public ICommand DeleteProfileCommand { get; private set; } = null!;

    public ICommand SaveProfilesCommand { get; private set; } = null!;

    public ICommand NewCommandCommand { get; private set; } = null!;

    public ICommand DuplicateCommandCommand { get; private set; } = null!;

    public ICommand DeleteCommandCommand { get; private set; } = null!;

    public ICommand GenerateCommandCommand { get; private set; } = null!;

    public ICommand ParseResultCommand { get; private set; } = null!;

    public ICommand CloseCommandDrawerCommand { get; private set; } = null!;

    #endregion

    #region 属性联动方法
    private void SelectedProfile_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProtocolConfigProfile.Name) or nameof(ProtocolConfigProfile.Summary))
        {
            ProfilesView.Refresh();
        }

        if (e.PropertyName == nameof(ProtocolConfigProfile.SelectedCommand))
        {
            ClearGeneratedOutputs();

            if (SelectedProfile?.SelectedCommand is null)
            {
                CloseCommandDrawer();
            }
            else
            {
                OpenCommandDrawer();
            }
        }

        UpdatePreviews();
        RaiseCommandStatesChanged();
    }

    #endregion
}
