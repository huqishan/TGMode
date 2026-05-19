using ControlLibrary;
using ControlLibrary.Controls.FlowchartEditor.Models;
using Newtonsoft.Json;
using System.Windows.Media;

namespace Module.Business.ViewModels;


/// <summary>
/// Single station configuration item.
/// </summary>
public sealed class StationProfile : ViewModelProperties
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _stationName = "Station-01";
    private string _stationCode = "ST-01";
    private bool _isEnabled = true;
    private DateTime _lastModifiedAt = DateTime.Now;
    private FlowchartDocument _flowchartDocument = new();

    public string Id
    {
        get => _id;
        set => SetField(ref _id, string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value.Trim());
    }

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

    public FlowchartDocument FlowchartDocument
    {
        get => _flowchartDocument;
        set => SetField(ref _flowchartDocument, CloneFlowchartDocument(value));
    }

    [JsonIgnore]
    public string StatusText => IsEnabled ? "启用" : "停用";

    [JsonIgnore]
    public string Summary => $"{StationCode} / {StatusText}";

    [JsonIgnore]
    public string LastModifiedText => LastModifiedAt.ToString("yyyy-MM-dd HH:mm:ss");

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

    public StationProfile CopyAsNew(string name, string code)
    {
        StationProfile copy = Clone();
        copy.Id = Guid.NewGuid().ToString("N");
        copy.StationName = name;
        copy.StationCode = code;
        copy.LastModifiedAt = DateTime.Now;
        return copy;
    }

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

    private void RaiseSummaryChanged()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(Summary));
    }
}
/// <summary>
/// Flowchart palette item used only by the view for node drag creation.
/// </summary>
public sealed class FlowchartNodeTemplate : ViewModelProperties
{
    public FlowchartNodeTemplate(string displayName, string nodeText, FlowchartNodeKind nodeKind, Brush accentBrush)
    {
        DisplayName = displayName;
        NodeText = nodeText;
        NodeKind = nodeKind;
        AccentBrush = accentBrush;
    }

    public string DisplayName { get; }

    public string NodeText { get; }

    public FlowchartNodeKind NodeKind { get; }

    public Brush AccentBrush { get; }
}
/// <summary>
/// 单个流程图配置项。
/// </summary>
public sealed class FlowchartProfile : ViewModelProperties
{
    #region 私有字段

    private string _id = Guid.NewGuid().ToString("N");
    private string _name = "\u6d41\u7a0b\u56fe1";
    private FlowchartDocument _document = new();

    #endregion

    #region 绑定属性
    public string Id
    {
        get => _id;
        set => SetField(ref _id, string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value.Trim());
    }

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value ?? string.Empty, true);
    }

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

    [JsonIgnore]
    public int NodeCount => Document.Nodes?.Count ?? 0;

    [JsonIgnore]
    public string NodeCountText => $"{NodeCount} \u4e2a\u8282\u70b9";

    [JsonIgnore]
    public int ConnectionCount => Document.Connections?.Count ?? 0;

    [JsonIgnore]
    public string Summary => $"{NodeCount} \u4e2a\u8282\u70b9 / {ConnectionCount} \u6761\u8fde\u7ebf";

    #endregion

    #region 复制方法

    public FlowchartProfile Clone()
    {
        return new FlowchartProfile
        {
            Id = Id,
            Name = Name,
            Document = CloneDocument(Document)
        };
    }

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

    private void RaiseDocumentSummaryChanged()
    {
        OnPropertyChanged(nameof(NodeCount));
        OnPropertyChanged(nameof(NodeCountText));
        OnPropertyChanged(nameof(ConnectionCount));
        OnPropertyChanged(nameof(Summary));
    }

    #endregion
}