using ControlLibrary;
using ControlLibrary.Controls.FlowchartEditor.Models;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;

namespace Module.Business.Models;

/// <summary>
/// 流程图配置根对象，统一保存多个流程图。
/// </summary>
public sealed class FlowchartConfigurationCatalog
{
    public ObservableCollection<FlowchartProfile> Flowcharts { get; set; } = new();
}

/// <summary>
/// 单个流程图配置项。
/// </summary>
public sealed class FlowchartProfile : ViewModelProperties
{
    #region 私有字段

    private string _id = Guid.NewGuid().ToString("N");
    private string _name = "流程图 1";
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
    public int ConnectionCount => Document.Connections?.Count ?? 0;

    [JsonIgnore]
    public string Summary => $"{NodeCount} 个节点 / {ConnectionCount} 条连线";

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
        OnPropertyChanged(nameof(ConnectionCount));
        OnPropertyChanged(nameof(Summary));
    }

    #endregion
}
