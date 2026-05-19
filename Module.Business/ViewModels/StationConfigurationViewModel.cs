using ControlLibrary;
using ControlLibrary.Controls.FlowchartEditor.Control;
using ControlLibrary.Controls.FlowchartEditor.Models;
using Microsoft.Win32;
using Module.Business.Models;
using Module.Business.Services;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Module.Business.ViewModels;

public sealed class StationConfigurationViewModel : ViewModelProperties
{
    private static readonly Brush SuccessBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));

    private static readonly Brush WarningBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EA580C"));

    private static readonly Brush NeutralBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));

    private static readonly Brush StartBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB"));

    private static readonly Brush ProcessBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F766E"));

    private static readonly Brush DecisionBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A16207"));

    private static readonly Brush EndBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));

    private readonly StationConfigurationCatalog _catalog = BusinessConfigurationStore.LoadStationCatalog();
    private StationProfile? _selectedStation;
    private string _searchText = string.Empty;
    private string _pageStatusText = "等待编辑";
    private Brush _pageStatusBrush = NeutralBrush;
    private string _executionStatusText = "状态：等待操作";
    private Brush _executionStatusBrush = NeutralBrush;
    private bool _isExecuting;
    private bool _isPaused;
    private DateTime _lastCreateOrCopyCommandAt = DateTime.MinValue;

    public StationConfigurationViewModel()
    {
        Stations.CollectionChanged += Stations_CollectionChanged;
        HookStations(Stations);

        StationsView = CollectionViewSource.GetDefaultView(Stations);
        StationsView.Filter = FilterStations;

        InitializeNodeTemplates();

        SelectedStation = Stations.FirstOrDefault();
        SetPageStatus(
            Stations.Count == 0 ? "暂无工位配置，请点击新建。" : $"已加载 {Stations.Count} 个工位。",
            NeutralBrush);

        NewStationCommand = new RelayCommand(_ => NewStation());
        DuplicateStationCommand = new RelayCommand(_ => DuplicateSelectedStation(), _ => SelectedStation is not null);
        DeleteStationCommand = new RelayCommand(_ => DeleteSelectedStation(), _ => SelectedStation is not null);
        SaveStationCommand = new RelayCommand(_ => SaveStations());
        OpenFlowchartCommand = new RelayCommand(ImportFlowchart, _ => SelectedStation is not null);
        ExportFlowchartCommand = new RelayCommand(ExportFlowchart, _ => SelectedStation is not null);
        ExecuteFlowchartCommand = new RelayCommand(
            async parameter => await ExecuteFlowchartAsync(parameter),
            _ => SelectedStation is not null && !IsExecuting);
        PauseFlowchartCommand = new RelayCommand(
            TogglePauseFlowchart,
            _ => SelectedStation is not null && IsExecuting);
        StopFlowchartCommand = new RelayCommand(
            StopFlowchart,
            _ => SelectedStation is not null && IsExecuting);
    }

    public ObservableCollection<StationProfile> Stations => _catalog.Stations;

    public ICollectionView StationsView { get; }

    public ObservableCollection<FlowchartNodeTemplate> NodeTemplates { get; } = new();

    public ObservableCollection<string> ExecutionLogs { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetField(ref _searchText, value ?? string.Empty))
            {
                return;
            }

            StationsView.Refresh();
        }
    }

    public StationProfile? SelectedStation
    {
        get => _selectedStation;
        set
        {
            if (ReferenceEquals(_selectedStation, value))
            {
                return;
            }

            _selectedStation = value;
            ExecutionLogs.Clear();
            SetExecutionStatus("状态：等待操作", NeutralBrush);
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedStation));
            OnPropertyChanged(nameof(CurrentStationSummary));
            RaiseCommandStatesChanged();
        }
    }

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

    public string ExecutionStatusText
    {
        get => _executionStatusText;
        private set => SetField(ref _executionStatusText, value);
    }

    public Brush ExecutionStatusBrush
    {
        get => _executionStatusBrush;
        private set => SetField(ref _executionStatusBrush, value);
    }

    public bool IsExecuting
    {
        get => _isExecuting;
        private set
        {
            if (SetField(ref _isExecuting, value))
            {
                OnPropertyChanged(nameof(CanEdit));
                RaiseCommandStatesChanged();
            }
        }
    }

    public bool IsPaused
    {
        get => _isPaused;
        private set
        {
            if (SetField(ref _isPaused, value))
            {
                RaiseCommandStatesChanged();
            }
        }
    }

    public bool CanEdit => !IsExecuting;

    public string StationCountText => $"{Stations.Count} 个工位";

    public bool HasSelectedStation => SelectedStation is not null;

    public string CurrentStationSummary => SelectedStation?.Summary ?? "未选择工位";

    public ICommand NewStationCommand { get; }

    public ICommand DuplicateStationCommand { get; }

    public ICommand DeleteStationCommand { get; }

    public ICommand SaveStationCommand { get; }

    public ICommand OpenFlowchartCommand { get; }

    public ICommand ExportFlowchartCommand { get; }

    public ICommand ExecuteFlowchartCommand { get; }

    public ICommand PauseFlowchartCommand { get; }

    public ICommand StopFlowchartCommand { get; }

    private void InitializeNodeTemplates()
    {
        NodeTemplates.Add(new FlowchartNodeTemplate("开始", "开始", FlowchartNodeKind.Start, StartBrush));
        NodeTemplates.Add(new FlowchartNodeTemplate("处理", "处理", FlowchartNodeKind.Process, ProcessBrush));
        NodeTemplates.Add(new FlowchartNodeTemplate("判断", "判断", FlowchartNodeKind.Decision, DecisionBrush));
        NodeTemplates.Add(new FlowchartNodeTemplate("结束", "结束", FlowchartNodeKind.End, EndBrush));
    }

    private void NewStation()
    {
        if (!CanRunCreateOrCopyCommand())
        {
            return;
        }

        StationProfile station = new()
        {
            StationName = GenerateUniqueStationName(),
            StationCode = GenerateUniqueStationCode(),
            LastModifiedAt = DateTime.Now
        };

        Stations.Add(station);
        SelectCreatedStation(station);
        SetPageStatus("已新增工位，请继续编辑后保存。", SuccessBrush);
    }

    private void DuplicateSelectedStation()
    {
        if (!CanRunCreateOrCopyCommand() || SelectedStation is null)
        {
            return;
        }

        StationProfile station = SelectedStation.CopyAsNew(
            GenerateCopyStationName(SelectedStation.StationName),
            GenerateUniqueStationCode(SelectedStation.StationCode));
        station.LastModifiedAt = DateTime.Now;

        Stations.Add(station);
        SelectCreatedStation(station);
        SetPageStatus($"已复制工位：{station.StationName}", SuccessBrush);
    }

    private void DeleteSelectedStation()
    {
        if (SelectedStation is null)
        {
            return;
        }

        int index = Stations.IndexOf(SelectedStation);
        Stations.Remove(SelectedStation);
        SelectedStation = Stations.Count == 0
            ? null
            : Stations[Math.Clamp(index, 0, Stations.Count - 1)];

        SetPageStatus("已删除工位，点击保存后生效。", WarningBrush);
    }

    private void SaveStations()
    {
        if (!ValidateStations(out string message))
        {
            SetPageStatus(message, WarningBrush);
            return;
        }

        BusinessConfigurationStore.SaveStationCatalog(_catalog);
        StationsView.Refresh();
        SetPageStatus($"已保存 {Stations.Count} 个工位。", SuccessBrush);
    }

    private void ImportFlowchart(object? parameter)
    {
        if (parameter is not FlowchartEditorControl editor || SelectedStation is null)
        {
            SetPageStatus("请先选择工位。", WarningBrush);
            return;
        }

        OpenFileDialog dialog = new()
        {
            Filter = "流程图文件 (*.flowchart.json)|*.flowchart.json|JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
            DefaultExt = ".flowchart.json"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            editor.LoadFromFile(dialog.FileName);
            SelectedStation.FlowchartDocument = editor.CreateDocumentSnapshot();
            SelectedStation.LastModifiedAt = DateTime.Now;
            SetPageStatus($"已导入流程图：{dialog.FileName}", SuccessBrush);
        }
        catch (Exception ex)
        {
            SetPageStatus($"导入流程图失败：{ex.Message}", WarningBrush);
        }
    }

    private void ExportFlowchart(object? parameter)
    {
        if (parameter is not FlowchartEditorControl editor || SelectedStation is null)
        {
            SetPageStatus("请先选择工位。", WarningBrush);
            return;
        }

        CaptureCurrentEditorDocument(editor);

        SaveFileDialog dialog = new()
        {
            Filter = "流程图文件 (*.flowchart.json)|*.flowchart.json|JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
            DefaultExt = ".flowchart.json",
            FileName = $"{SanitizeFileName(SelectedStation.StationName)}.flowchart.json"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            editor.SaveToFile(dialog.FileName);
            SetPageStatus($"已导出流程图：{dialog.FileName}", SuccessBrush);
        }
        catch (Exception ex)
        {
            SetPageStatus($"导出流程图失败：{ex.Message}", WarningBrush);
        }
    }

    private async Task ExecuteFlowchartAsync(object? parameter)
    {
        if (parameter is not FlowchartEditorControl editor || SelectedStation is null)
        {
            SetExecutionStatus("状态：请先选择工位。", WarningBrush);
            return;
        }

        CaptureCurrentEditorDocument(editor);

        IsExecuting = true;
        IsPaused = false;
        ExecutionLogs.Clear();
        SetExecutionStatus("状态：开始预览流程图", NeutralBrush);

        try
        {
            void HandleExecutionStepChanged(object? sender, FlowchartExecutionStepEventArgs e)
            {
                ExecutionLogs.Add(e.Message);
                SetExecutionStatus($"状态：{e.Message}", NeutralBrush);
            }

            editor.ExecutionStepChanged += HandleExecutionStepChanged;
            FlowchartExecutionResult result;
            try
            {
                result = await editor.ExecuteFlowAsync();
            }
            finally
            {
                editor.ExecutionStepChanged -= HandleExecutionStepChanged;
            }

            foreach (string step in result.Steps)
            {
                if (!ExecutionLogs.Contains(step))
                {
                    ExecutionLogs.Add(step);
                }
            }

            SetExecutionStatus(
                $"状态：{result.Message}",
                result.IsSuccess ? SuccessBrush : WarningBrush);
        }
        catch (Exception ex)
        {
            SetExecutionStatus($"状态：预览流程图失败：{ex.Message}", WarningBrush);
        }
        finally
        {
            IsPaused = false;
            IsExecuting = false;
        }
    }

    private void TogglePauseFlowchart(object? parameter)
    {
        if (parameter is not FlowchartEditorControl editor || SelectedStation is null)
        {
            SetExecutionStatus("状态：未选择工位。", WarningBrush);
            return;
        }

        bool isSuccess = IsPaused
            ? editor.ResumeExecution()
            : editor.PauseExecution();

        if (isSuccess)
        {
            IsPaused = !IsPaused;
            SetExecutionStatus(IsPaused ? "状态：流程图预览已暂停。" : "状态：流程图预览已继续。", NeutralBrush);
            return;
        }

        SetExecutionStatus(IsPaused ? "状态：流程图预览未处于暂停状态。" : "状态：没有正在预览的流程图。", WarningBrush);
    }

    private void StopFlowchart(object? parameter)
    {
        if (parameter is not FlowchartEditorControl editor || SelectedStation is null)
        {
            SetExecutionStatus("状态：未选择工位。", WarningBrush);
            return;
        }

        bool isSuccess = editor.StopExecution();
        if (isSuccess)
        {
            IsPaused = false;
            SetExecutionStatus("状态：已发送停止预览请求。", WarningBrush);
            return;
        }

        SetExecutionStatus("状态：没有正在预览的流程图。", WarningBrush);
    }

    public void CaptureCurrentEditorDocument(object? parameter)
    {
        if (parameter is FlowchartEditorControl editor && SelectedStation is not null)
        {
            SelectedStation.FlowchartDocument = editor.CreateDocumentSnapshot();
        }
    }

    private void Stations_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (StationProfile station in e.OldItems.OfType<StationProfile>())
            {
                UnhookStation(station);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (StationProfile station in e.NewItems.OfType<StationProfile>())
            {
                HookStation(station);
            }
        }

        OnPropertyChanged(nameof(StationCountText));
        StationsView.Refresh();
        RaiseCommandStatesChanged();
    }

    private void HookStations(IEnumerable<StationProfile> stations)
    {
        foreach (StationProfile station in stations)
        {
            HookStation(station);
        }
    }

    private void HookStation(StationProfile station)
    {
        station.PropertyChanged += Station_PropertyChanged;
    }

    private void UnhookStation(StationProfile station)
    {
        station.PropertyChanged -= Station_PropertyChanged;
    }

    private void Station_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not StationProfile station)
        {
            return;
        }

        if (ShouldRefreshLastModified(e.PropertyName))
        {
            station.LastModifiedAt = DateTime.Now;
        }

        if (ReferenceEquals(station, SelectedStation))
        {
            OnPropertyChanged(nameof(CurrentStationSummary));
        }

        StationsView.Refresh();
        SetPageStatus("工位配置已修改，记得保存。", NeutralBrush);
    }

    private static bool ShouldRefreshLastModified(string? propertyName)
    {
        return propertyName is nameof(StationProfile.StationName)
            or nameof(StationProfile.StationCode)
            or nameof(StationProfile.IsEnabled)
            or nameof(StationProfile.FlowchartDocument)
            or nameof(StationProfile.Summary);
    }

    private bool FilterStations(object item)
    {
        if (item is not StationProfile station)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        string keyword = SearchText.Trim();
        return Contains(station.StationName, keyword) ||
               Contains(station.StationCode, keyword);
    }

    private static bool Contains(string? source, string keyword)
    {
        return source?.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private bool ValidateStations(out string message)
    {
        HashSet<string> stationNames = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> stationCodes = new(StringComparer.OrdinalIgnoreCase);

        foreach (StationProfile station in Stations)
        {
            if (string.IsNullOrWhiteSpace(station.StationName))
            {
                message = "工位名称不能为空。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(station.StationCode))
            {
                message = $"工位“{station.StationName}”的工位编码不能为空。";
                return false;
            }

            if (!stationNames.Add(station.StationName.Trim()))
            {
                message = $"工位名称不能重复：{station.StationName}";
                return false;
            }

            if (!stationCodes.Add(station.StationCode.Trim()))
            {
                message = $"工位编码不能重复：{station.StationCode}";
                return false;
            }
        }

        message = string.Empty;
        return true;
    }

    private void SelectCreatedStation(StationProfile station)
    {
        SearchText = string.Empty;
        StationsView.Refresh();
        SelectedStation = station;
        StationsView.MoveCurrentTo(station);
    }

    private bool CanRunCreateOrCopyCommand()
    {
        DateTime now = DateTime.UtcNow;
        if (now - _lastCreateOrCopyCommandAt < TimeSpan.FromMilliseconds(300))
        {
            return false;
        }

        _lastCreateOrCopyCommandAt = now;
        return true;
    }

    private string GenerateUniqueStationName()
    {
        HashSet<string> existingNames = new(Stations.Select(station => station.StationName), StringComparer.OrdinalIgnoreCase);
        for (int index = 1; ; index++)
        {
            string candidate = $"工位 {index}";
            if (!existingNames.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private string GenerateCopyStationName(string baseName)
    {
        HashSet<string> existingNames = new(Stations.Select(station => station.StationName), StringComparer.OrdinalIgnoreCase);
        string copyName = $"{baseName.Trim()} 副本";
        if (!existingNames.Contains(copyName))
        {
            return copyName;
        }

        for (int index = 2; ; index++)
        {
            string candidate = $"{copyName} {index}";
            if (!existingNames.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private string GenerateUniqueStationCode(string? baseCode = null)
    {
        HashSet<string> existingCodes = new(
            Stations.Select(station => station.StationCode?.Trim() ?? string.Empty),
            StringComparer.OrdinalIgnoreCase);

        string root = string.IsNullOrWhiteSpace(baseCode) ? "ST" : baseCode.Trim().ToUpperInvariant();
        for (int index = 1; ; index++)
        {
            string candidate = string.IsNullOrWhiteSpace(baseCode)
                ? $"ST-{index:00}"
                : $"{root}-{index:00}";
            if (!existingCodes.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private void SetPageStatus(string text, Brush brush)
    {
        PageStatusText = text;
        PageStatusBrush = brush;
    }

    private void SetExecutionStatus(string text, Brush brush)
    {
        SetPageStatus(text, brush);
    }

    private void RaiseCommandStatesChanged()
    {
        RaiseCommandState(DuplicateStationCommand);
        RaiseCommandState(DeleteStationCommand);
        RaiseCommandState(OpenFlowchartCommand);
        RaiseCommandState(ExportFlowchartCommand);
        RaiseCommandState(ExecuteFlowchartCommand);
        RaiseCommandState(PauseFlowchartCommand);
        RaiseCommandState(StopFlowchartCommand);
    }

    private static void RaiseCommandState(ICommand? command)
    {
        if (command is RelayCommand relayCommand)
        {
            relayCommand.RaiseCanExecuteChanged();
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        string safeName = string.IsNullOrWhiteSpace(fileName) ? "flowchart" : fileName.Trim();
        foreach (char invalidChar in Path.GetInvalidFileNameChars())
        {
            safeName = safeName.Replace(invalidChar, '_');
        }

        return safeName;
    }
}
