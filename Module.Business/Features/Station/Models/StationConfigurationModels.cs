using ControlLibrary;
using ControlLibrary.Controls.FlowchartEditor.Models;
using Module.Business.Features.StationConfiguration;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Module.Business.Models;

/// <summary>
/// 工位配置根对象。
/// 工位配置在磁盘上的序列化根节点，统一保存全部工位档案。
/// </summary>
public sealed class StationConfigurationCatalog
{
    /// <summary>工位档案集合，加载后整体还原到界面。</summary>
    public ObservableCollection<StationProfile> Stations { get; set; } = new();
}
