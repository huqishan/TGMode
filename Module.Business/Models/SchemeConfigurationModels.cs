using ControlLibrary;
using Module.Business.ViewModels.PropertyVMs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Serialization;

namespace Module.Business.Models;

/// <summary>
/// 业务配置根对象，统一保存步骤模板和方案配置。
/// </summary>
public sealed class SchemeConfigurationCatalog
{
    public ObservableCollection<WorkStepProfile> WorkSteps { get; set; } = new();

    public ObservableCollection<SchemeProfile> Schemes { get; set; } = new();
}



/// <summary>
/// 方案导入导出包，包含方案本体和完整工步内容。
/// </summary>
public sealed class SchemeConfigurationPackage
{
    public int Version { get; set; } = 1;

    public SchemeProfile? Scheme { get; set; }

    public ObservableCollection<WorkStepProfile> WorkSteps { get; set; } = new();
}
