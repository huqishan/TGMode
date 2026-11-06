using ControlLibrary;
using ControlLibrary.Controls.FlowchartEditor.Models;
using Newtonsoft.Json;
using System.Windows.Media;

namespace Module.Business.Features.StationConfiguration;


/// <summary>
/// 单个工位配置档案。
/// 包含工位基础信息（名称、编码、启用状态）与该工位的流程图执行流程文档。
/// </summary>
public sealed class StationProfile : ViewModelProperties
{
    #region 私有字段

    /// <summary>工位唯一标识，新建时自动生成。</summary>
    private string _id = Guid.NewGuid().ToString("N");

    /// <summary>工位名称。</summary>
    private string _stationName = "Station-01";

    /// <summary>工位编码。</summary>
    private string _stationCode = "ST-01";

    /// <summary>是否启用，停用工位不参与通信测试。</summary>
    private bool _isEnabled = true;

    /// <summary>最后修改时间。</summary>
    private DateTime _lastModifiedAt = DateTime.Now;

    /// <summary>工位绑定的流程图执行流程文档。</summary>
    private FlowchartDocument _flowchartDocument = new();

    #endregion

    #region 绑定属性

    /// <summary>工位唯一标识，空值时回退为自动生成。</summary>
    public string Id
    {
        get => _id;
        set => SetField(ref _id, string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value.Trim());
    }

    /// <summary>工位名称，变化时同步刷新摘要展示。</summary>
    public string StationName
    {
        get => _stationName;
        set
        {
            if (SetField(ref _stationName, value ?? string.Empty, true))
            {
                RaiseSummaryChanged();
            }
        }
    }

    /// <summary>工位编码，变化时同步刷新摘要展示。</summary>
    public string StationCode
    {
        get => _stationCode;
        set
        {
            if (SetField(ref _stationCode, value ?? string.Empty, true))
            {
                RaiseSummaryChanged();
            }
        }
    }

    /// <summary>是否启用，变化时同步刷新摘要展示。</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetField(ref _isEnabled, value))
            {
                RaiseSummaryChanged();
            }
        }
    }

    /// <summary>最后修改时间，缺省值回退为当前时间。</summary>
    public DateTime LastModifiedAt
    {
        get => _lastModifiedAt;
        set
        {
            if (SetField(ref _lastModifiedAt, value == default ? DateTime.Now : value))
            {
                OnPropertyChanged(nameof(LastModifiedText));
            }
        }
    }

    /// <summary>工位绑定的流程图文档，赋值时做深拷贝防止外部引用篡改。</summary>
    public FlowchartDocument FlowchartDocument
    {
        get => _flowchartDocument;
        set => SetField(ref _flowchartDocument, CloneFlowchartDocument(value));
    }

    /// <summary>启用状态展示文案。</summary>
    [JsonIgnore]
    public string StatusText => IsEnabled ? "启用" : "停用";

    /// <summary>列表摘要：编码 / 状态。</summary>
    [JsonIgnore]
    public string Summary => $"{StationCode} / {StatusText}";

    /// <summary>最后修改时间展示文案。</summary>
    [JsonIgnore]
    public string LastModifiedText => LastModifiedAt.ToString("yyyy-MM-dd HH:mm:ss");

    #endregion

    #region 复制方法

    /// <summary>深拷贝当前工位配置（含流程图文档），用于复制工位。</summary>
    public StationProfile Clone()
    {
        return new StationProfile
        {
            Id = Id,
            StationName = StationName,
            StationCode = StationCode,
            IsEnabled = IsEnabled,
            LastModifiedAt = LastModifiedAt,
            FlowchartDocument = CloneFlowchartDocument(FlowchartDocument)
        };
    }

    /// <summary>以新名称、新编码复制为全新工位：重新生成 Id 与修改时间。</summary>
    /// <param name="name">新工位名称。</param>
    /// <param name="code">新工位编码。</param>
    public StationProfile CopyAsNew(string name, string code)
    {
        StationProfile copy = Clone();
        copy.Id = Guid.NewGuid().ToString("N");
        copy.StationName = name;
        copy.StationCode = code;
        copy.LastModifiedAt = DateTime.Now;
        return copy;
    }

    /// <summary>
    /// 深拷贝流程图文档：逐节点、逐连线复制，避免两份配置共享引用互相影响。
    /// </summary>
    /// <param name="document">源流程图文档，为空时返回空文档。</param>
    public static FlowchartDocument CloneFlowchartDocument(FlowchartDocument? document)
    {
        if (document is null)
        {
            return new FlowchartDocument();
        }

        return new FlowchartDocument
        {
            Version = document.Version,
            Nodes = (document.Nodes ?? new())
                .Select(node => new FlowchartNodeDocument
                {
                    Id = node.Id,
                    Text = node.Text ?? string.Empty,
                    MetadataJson = node.MetadataJson ?? string.Empty,
                    Kind = node.Kind,
                    X = node.X,
                    Y = node.Y,
                    Width = node.Width,
                    Height = node.Height
                })
                .ToList(),
            Connections = (document.Connections ?? new())
                .Select(connection => new FlowchartConnectionDocument
                {
                    Id = connection.Id,
                    SourceNodeId = connection.SourceNodeId,
                    SourceAnchor = connection.SourceAnchor,
                    TargetNodeId = connection.TargetNodeId,
                    TargetAnchor = connection.TargetAnchor
                })
                .ToList()
        };
    }

    /// <summary>通知状态与摘要属性变化，刷新列表展示。</summary>
    private void RaiseSummaryChanged()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(Summary));
    }

    #endregion
}
/// <summary>
/// 流程图节点模板。
/// 仅由界面使用，定义工具面板中可拖拽创建的节点类型、默认文字与主题色。
/// </summary>
public sealed class FlowchartNodeTemplate : ViewModelProperties
{
    /// <summary>构造函数，初始化模板的展示信息。</summary>
    /// <param name="displayName">面板展示名称。</param>
    /// <param name="nodeText">节点默认文字。</param>
    /// <param name="nodeKind">节点种类。</param>
    /// <param name="accentBrush">主题色画刷。</param>
    public FlowchartNodeTemplate(string displayName, string nodeText, FlowchartNodeKind nodeKind, Brush accentBrush)
    {
        DisplayName = displayName;
        NodeText = nodeText;
        NodeKind = nodeKind;
        AccentBrush = accentBrush;
    }

    /// <summary>面板展示名称。</summary>
    public string DisplayName { get; }

    /// <summary>节点默认文字。</summary>
    public string NodeText { get; }

    /// <summary>节点种类（处理块/判断块等）。</summary>
    public FlowchartNodeKind NodeKind { get; }

    /// <summary>主题色画刷。</summary>
    public Brush AccentBrush { get; }
}
/// <summary>
/// 单个流程图配置项。
/// </summary>
public sealed class FlowchartProfile : ViewModelProperties
{
    #region 私有字段

    /// <summary>流程图唯一标识，新建时自动生成。</summary>
    private string _id = Guid.NewGuid().ToString("N");

    /// <summary>流程图名称。</summary>
    private string _name = "流程图1";

    /// <summary>流程图文档内容。</summary>
    private FlowchartDocument _document = new();

    #endregion

    #region 绑定属性

    /// <summary>流程图唯一标识，空值时回退为自动生成。</summary>
    public string Id
    {
        get => _id;
        set => SetField(ref _id, string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value.Trim());
    }

    /// <summary>流程图名称。</summary>
    public string Name
    {
        get => _name;
        set => SetField(ref _name, value ?? string.Empty, true);
    }

    /// <summary>流程图文档内容，赋值时深拷贝并刷新摘要。</summary>
    public FlowchartDocument Document
    {
        get => _document;
        set
        {
            if (!SetField(ref _document, CloneDocument(value)))
            {
                return;
            }

            RaiseDocumentSummaryChanged();
        }
    }

    /// <summary>节点数量。</summary>
    [JsonIgnore]
    public int NodeCount => Document.Nodes?.Count ?? 0;

    /// <summary>节点数量展示文案。</summary>
    [JsonIgnore]
    public string NodeCountText => $"{NodeCount} 个节点";

    /// <summary>连线数量。</summary>
    [JsonIgnore]
    public int ConnectionCount => Document.Connections?.Count ?? 0;

    /// <summary>列表摘要：节点数 / 连线数。</summary>
    [JsonIgnore]
    public string Summary => $"{NodeCount} 个节点 / {ConnectionCount} 条连线";

    #endregion

    #region 复制方法

    /// <summary>深拷贝当前流程图配置，用于复制流程图。</summary>
    public FlowchartProfile Clone()
    {
        return new FlowchartProfile
        {
            Id = Id,
            Name = Name,
            Document = CloneDocument(Document)
        };
    }

    /// <summary>以新名称复制为全新流程图：重新生成唯一标识。</summary>
    /// <param name="name">新流程图名称。</param>
    public FlowchartProfile CopyAsNew(string name)
    {
        return new FlowchartProfile
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Document = CloneDocument(Document)
        };
    }

    #endregion

    #region 文档工具方法

    /// <summary>
    /// 深拷贝流程图文档：逐节点、逐连线复制，为空时返回空文档。
    /// </summary>
    /// <param name="document">源流程图文档。</param>
    public static FlowchartDocument CloneDocument(FlowchartDocument? document)
    {
        if (document is null)
        {
            return new FlowchartDocument();
        }

        return new FlowchartDocument
        {
            Version = document.Version,
            Nodes = (document.Nodes ?? new())
                .Select(node => new FlowchartNodeDocument
                {
                    Id = node.Id,
                    Text = node.Text ?? string.Empty,
                    MetadataJson = node.MetadataJson ?? string.Empty,
                    Kind = node.Kind,
                    X = node.X,
                    Y = node.Y,
                    Width = node.Width,
                    Height = node.Height
                })
                .ToList(),
            Connections = (document.Connections ?? new())
                .Select(connection => new FlowchartConnectionDocument
                {
                    Id = connection.Id,
                    SourceNodeId = connection.SourceNodeId,
                    SourceAnchor = connection.SourceAnchor,
                    TargetNodeId = connection.TargetNodeId,
                    TargetAnchor = connection.TargetAnchor
                })
                .ToList()
        };
    }

    /// <summary>通知节点数、连线数与摘要属性变化，刷新列表展示。</summary>
    private void RaiseDocumentSummaryChanged()
    {
        OnPropertyChanged(nameof(NodeCount));
        OnPropertyChanged(nameof(NodeCountText));
        OnPropertyChanged(nameof(ConnectionCount));
        OnPropertyChanged(nameof(Summary));
    }

    #endregion
}