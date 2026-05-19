using ControlLibrary;
using ControlLibrary.Controls.FlowchartEditor.Models;
using Module.Business.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;

namespace Module.Business.Models;

/// <summary>
/// 流程图配置根对象，统一保存多个流程图配置。
/// </summary>
public sealed class FlowchartConfigurationCatalog
{
    public ObservableCollection<FlowchartProfile> Flowcharts { get; set; } = new();
}



