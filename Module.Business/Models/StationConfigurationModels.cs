using ControlLibrary;
using ControlLibrary.Controls.FlowchartEditor.Models;
using Module.Business.ViewModels;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Module.Business.Models;

/// <summary>
/// Station configuration root object.
/// </summary>
public sealed class StationConfigurationCatalog
{
    public ObservableCollection<StationProfile> Stations { get; set; } = new();
}


