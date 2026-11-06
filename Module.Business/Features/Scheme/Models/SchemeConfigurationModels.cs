using ControlLibrary;
using Module.Business.Features.Scheme.ViewModels.PresentationModels;
using Module.Business.Features.OperationEditing.ViewModels.PresentationModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace Module.Business.Models;

/// <summary>
/// 业务配置根对象，统一保存业务模板和方案配置。
/// </summary>
public sealed class SchemeConfigurationCatalog
{
    /// <summary>
    /// 业务方案集合。
    /// </summary>
    public ObservableCollection<SchemeProfile> Schemes { get; set; } = new();
}
