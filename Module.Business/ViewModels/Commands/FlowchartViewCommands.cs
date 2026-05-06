using ControlLibrary;
using ControlLibrary.Controls.FlowchartEditor.Control;
using ControlLibrary.Controls.FlowchartEditor.Models;
using Microsoft.Win32;
using Module.Business.Models;
using Module.Business.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Module.Business.ViewModels;

/// <summary>
/// 流程图页面命令实现，供 XAML Command 绑定调用。
/// </summary>
public sealed partial class FlowchartViewModel
{
    #region 构造与初始化

    public FlowchartViewModel()
    {
        Flowcharts.CollectionChanged += Flowcharts_CollectionChanged;
        FlowchartsView = CollectionViewSource.GetDefaultView(Flowcharts);
        FlowchartsView.Filter = FilterFlowcharts;
        InitializeNodeTemplates();
        InitializeCommands();
        SelectedFlowchart = Flowcharts.FirstOrDefault();
        SetPageStatus(Flowcharts.Count == 0 ? "暂无流程图配置，请点击新增。" : $"已读取 {Flowcharts.Count} 个流程图", NeutralBrush);
    }

    /// <summary>
    /// 初始化左侧可拖拽的节点模板。
    /// </summary>
    private void InitializeNodeTemplates()
    {
        NodeTemplates.Add(new FlowchartNodeTemplate("开始", "开始", FlowchartNodeKind.Start, StartBrush));
        NodeTemplates.Add(new FlowchartNodeTemplate("处理", "处理", FlowchartNodeKind.Process, ProcessBrush));
        NodeTemplates.Add(new FlowchartNodeTemplate("判断", "判断", FlowchartNodeKind.Decision, DecisionBrush));
        NodeTemplates.Add(new FlowchartNodeTemplate("结束", "结束", FlowchartNodeKind.End, EndBrush));
    }

    /// <summary>
    /// 初始化页面全部按钮命令，XAML 不绑定后台 Click 事件。
    /// </summary>
    private void InitializeCommands()
    {
        NewFlowchartCommand = new RelayCommand(NewFlowchart, CanEditFlowchart);
        DuplicateFlowchartCommand = new RelayCommand(DuplicateSelectedFlowchart, parameter => CanEditFlowchart(parameter) && SelectedFlowchart is not null);
        DeleteFlowchartCommand = new RelayCommand(DeleteSelectedFlowchart, _ => CanEdit && SelectedFlowchart is not null);
        SaveFlowchartCommand = new RelayCommand(SaveFlowcharts, CanEditFlowchart);
        OpenFlowchartCommand = new RelayCommand(ImportFlowchart, CanEditFlowchart);
        ExportFlowchartCommand = new RelayCommand(ExportFlowchart, parameter => CanEditFlowchart(parameter) && SelectedFlowchart is not null);
        ExecuteFlowchartCommand = new RelayCommand(async parameter => await ExecuteFlowchartAsync(parameter), CanExecuteFlowchart);
        PauseFlowchartCommand = new RelayCommand(TogglePauseFlowchart, CanPauseFlowchart);
        StopFlowchartCommand = new RelayCommand(StopFlowchart, CanStopFlowchart);
    }

    #endregion

    #region 配置命令方法

    /// <summary>
    /// 新增空白流程图，默认切换到新流程图编辑。
    /// </summary>
    private void NewFlowchart(object? parameter)
    {
        if (!CanRunCreateOrCopyCommand())
        {
            return;
        }

        CaptureCurrentEditorDocument(parameter);

        FlowchartProfile flowchart = new()
        {
            Name = GenerateUniqueFlowchartName("流程图"),
            Document = new FlowchartDocument()
        };

        Flowcharts.Add(flowchart);
        SelectCreatedFlowchart(flowchart);
        SetPageStatus("已新增流程图，编辑后点击保存。", SuccessBrush);
    }

    /// <summary>
    /// 复制当前流程图及其节点连线。
    /// </summary>
    private void DuplicateSelectedFlowchart(object? parameter)
    {
        if (!CanRunCreateOrCopyCommand())
        {
            return;
        }

        if (SelectedFlowchart is null)
        {
            return;
        }

        CaptureCurrentEditorDocument(parameter);

        FlowchartProfile flowchart = SelectedFlowchart.CopyAsNew(GenerateCopyFlowchartName(SelectedFlowchart.Name));
        Flowcharts.Add(flowchart);
        SelectCreatedFlowchart(flowchart);
        SetPageStatus($"已复制流程图：{flowchart.Name}", SuccessBrush);
    }

    /// <summary>
    /// 删除当前选中的流程图。
    /// </summary>
    private void DeleteSelectedFlowchart(object? parameter)
    {
        if (SelectedFlowchart is null)
        {
            return;
        }

        int index = Flowcharts.IndexOf(SelectedFlowchart);
        Flowcharts.Remove(SelectedFlowchart);
        SelectedFlowchart = Flowcharts.Count == 0
            ? null
            : Flowcharts[Math.Clamp(index, 0, Flowcharts.Count - 1)];

        SetPageStatus("已删除流程图，点击保存后生效。", WarningBrush);
    }

    /// <summary>
    /// 保存所有流程图配置。
    /// </summary>
    private void SaveFlowcharts(object? parameter)
    {
        CaptureCurrentEditorDocument(parameter);

        if (!ValidateFlowcharts(out string message))
        {
            SetPageStatus(message, WarningBrush);
            return;
        }

        FlowchartConfigurationStore.SaveCatalog(_catalog);
        SetPageStatus($"已保存 {Flowcharts.Count} 个流程图。", SuccessBrush);
    }

    /// <summary>
    /// 从本地流程图文件导入到当前流程图；未选择时自动新建。
    /// </summary>
    private void ImportFlowchart(object? parameter)
    {
        if (parameter is not FlowchartEditorControl editor)
        {
            SetPageStatus("流程图编辑器未初始化。", WarningBrush);
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
            if (SelectedFlowchart is null)
            {
                FlowchartProfile imported = new()
                {
                    Name = GenerateUniqueFlowchartName(Path.GetFileNameWithoutExtension(dialog.FileName))
                };
                Flowcharts.Add(imported);
                SelectCreatedFlowchart(imported);
            }

            editor.LoadFromFile(dialog.FileName);
            CaptureCurrentEditorDocument(editor);
            ExecutionLogs.Clear();
            SetPageStatus($"已导入流程图：{dialog.FileName}", SuccessBrush);
        }
        catch (Exception ex)
        {
            SetPageStatus($"导入流程图失败：{ex.Message}", WarningBrush);
        }
    }

    /// <summary>
    /// 将当前流程图导出为独立文件。
    /// </summary>
    private void ExportFlowchart(object? parameter)
    {
        if (parameter is not FlowchartEditorControl editor)
        {
            SetPageStatus("流程图编辑器未初始化。", WarningBrush);
            return;
        }

        if (SelectedFlowchart is null)
        {
            SetPageStatus("请先选择流程图。", WarningBrush);
            return;
        }

        CaptureCurrentEditorDocument(editor);

        SaveFileDialog dialog = new()
        {
            Filter = "流程图文件 (*.flowchart.json)|*.flowchart.json|JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
            DefaultExt = ".flowchart.json",
            FileName = $"{SanitizeFileName(SelectedFlowchart.Name)}.flowchart.json"
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

    #endregion

    #region 执行命令方法

    /// <summary>
    /// 执行当前流程图，并把执行步骤同步到绑定属性。
    /// </summary>
    private async Task ExecuteFlowchartAsync(object? parameter)
    {
        if (parameter is not FlowchartEditorControl editor)
        {
            SetExecutionStatus("状态：流程图编辑器未初始化。", WarningBrush);
            return;
        }

        CaptureCurrentEditorDocument(editor);
        IsExecuting = true;
        IsPaused = false;
        ExecutionLogs.Clear();
        SetExecutionStatus("状态：开始执行流程图", NeutralBrush);

        try
        {
            void OnExecutionStepChanged(object? _, FlowchartExecutionStepEventArgs e)
            {
                ExecutionLogs.Add(e.Message);
                SetExecutionStatus($"状态：{e.Message}", NeutralBrush);
            }

            editor.ExecutionStepChanged += OnExecutionStepChanged;
            FlowchartExecutionResult result;
            try
            {
                result = await editor.ExecuteFlowAsync().ConfigureAwait(true);
            }
            finally
            {
                editor.ExecutionStepChanged -= OnExecutionStepChanged;
            }

            if (!ExecutionLogs.Any())
            {
                foreach (string step in result.Steps)
                {
                    ExecutionLogs.Add(step);
                }
            }

            SetExecutionStatus($"状态：{result.Message}", result.IsSuccess ? SuccessBrush : WarningBrush);
        }
        catch (Exception ex)
        {
            SetExecutionStatus($"状态：执行流程图失败：{ex.Message}", WarningBrush);
        }
        finally
        {
            IsPaused = false;
            IsExecuting = false;
        }
    }

    /// <summary>
    /// 暂停或继续当前执行中的流程图。
    /// </summary>
    private void TogglePauseFlowchart(object? parameter)
    {
        if (IsPaused)
        {
            if (parameter is not FlowchartEditorControl editor)
            {
                SetExecutionStatus("状态：流程图编辑器未初始化。", WarningBrush);
                return;
            }

            if (editor.ResumeExecution())
            {
                IsPaused = false;
                SetExecutionStatus("状态：继续执行流程图。", NeutralBrush);
            }
            else
            {
                SetExecutionStatus("状态：当前流程图未处于暂停状态。", WarningBrush);
            }

            return;
        }

        if (parameter is not FlowchartEditorControl currentEditor)
        {
            SetExecutionStatus("状态：流程图编辑器未初始化。", WarningBrush);
            return;
        }

        if (currentEditor.PauseExecution())
        {
            IsPaused = true;
            SetExecutionStatus("状态：流程图已暂停。", NeutralBrush);
        }
        else
        {
            SetExecutionStatus("状态：当前没有可暂停的流程图执行。", WarningBrush);
        }
    }

    /// <summary>
    /// 请求结束当前流程图执行。
    /// </summary>
    private void StopFlowchart(object? parameter)
    {
        if (parameter is not FlowchartEditorControl editor)
        {
            SetExecutionStatus("状态：流程图编辑器未初始化。", WarningBrush);
            return;
        }

        if (editor.StopExecution())
        {
            IsPaused = false;
            SetExecutionStatus("状态：正在结束执行。", WarningBrush);
        }
        else
        {
            SetExecutionStatus("状态：当前没有正在执行的流程图。", WarningBrush);
        }
    }

    #endregion

    #region 工具方法

    private void CaptureCurrentEditorDocument(object? parameter)
    {
        if (parameter is FlowchartEditorControl editor && SelectedFlowchart is not null)
        {
            SelectedFlowchart.Document = editor.CreateDocumentSnapshot();
        }
    }

    private void SelectCreatedFlowchart(FlowchartProfile flowchart)
    {
        SearchText = string.Empty;
        FlowchartsView.Refresh();
        SelectedFlowchart = flowchart;
        FlowchartsView.MoveCurrentTo(flowchart);
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

    private string GenerateUniqueFlowchartName(string prefix)
    {
        string normalizedPrefix = string.IsNullOrWhiteSpace(prefix) ? "流程图" : prefix.Trim();
        HashSet<string> existingNames = new(Flowcharts.Select(flowchart => flowchart.Name), StringComparer.OrdinalIgnoreCase);
        int index = existingNames.Count + 1;
        string candidate;

        do
        {
            candidate = $"{normalizedPrefix} {index}";
            index++;
        }
        while (existingNames.Contains(candidate));

        return candidate;
    }

    private string GenerateCopyFlowchartName(string baseName)
    {
        HashSet<string> existingNames = new(Flowcharts.Select(flowchart => flowchart.Name), StringComparer.OrdinalIgnoreCase);
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

    private bool FilterFlowcharts(object item)
    {
        if (item is not FlowchartProfile flowchart)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        string keyword = SearchText.Trim();
        return Contains(flowchart.Name, keyword) || Contains(flowchart.Summary, keyword);
    }

    private static bool Contains(string? source, string keyword)
    {
        return source?.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private bool ValidateFlowcharts(out string message)
    {
        if (Flowcharts.Count == 0)
        {
            message = "请至少新增一个流程图。";
            return false;
        }

        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (FlowchartProfile flowchart in Flowcharts)
        {
            if (string.IsNullOrWhiteSpace(flowchart.Name))
            {
                message = "流程图名称不能为空。";
                return false;
            }

            if (!names.Add(flowchart.Name.Trim()))
            {
                message = $"流程图名称不能重复：{flowchart.Name}";
                return false;
            }
        }

        message = string.Empty;
        return true;
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

    private void Flowcharts_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RaisePageSummaryChanged();
        FlowchartsView.Refresh();
        RaiseCommandStatesChanged();
    }

    private void SetPageStatus(string text, Brush brush)
    {
        PageStatusText = text;
        PageStatusBrush = brush;
    }

    private void SetExecutionStatus(string text, Brush brush)
    {
        ExecutionStatusText = text;
        ExecutionStatusBrush = brush;
    }

    #endregion

    #region 命令状态方法

    private bool CanUseEditor(object? parameter)
    {
        return parameter is FlowchartEditorControl;
    }

    private bool CanEditFlowchart(object? parameter)
    {
        return CanUseEditor(parameter) && CanEdit;
    }

    private bool CanExecuteFlowchart(object? parameter)
    {
        return CanUseEditor(parameter) && SelectedFlowchart is not null && !IsExecuting;
    }

    private bool CanPauseFlowchart(object? parameter)
    {
        return CanUseEditor(parameter) && IsExecuting;
    }

    private bool CanStopFlowchart(object? parameter)
    {
        return CanUseEditor(parameter) && IsExecuting;
    }

    private void RaiseCommandStatesChanged()
    {
        RaiseCommandState(NewFlowchartCommand);
        RaiseCommandState(DuplicateFlowchartCommand);
        RaiseCommandState(DeleteFlowchartCommand);
        RaiseCommandState(SaveFlowchartCommand);
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

    #endregion
}
