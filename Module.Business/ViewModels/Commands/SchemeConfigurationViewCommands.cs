using ControlLibrary;
using Microsoft.Win32;
using Module.Business.Models;
using Module.Business.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Module.Business.ViewModels;

/// <summary>
/// 方案配置界面命令实现。
/// </summary>
public sealed partial class SchemeConfigurationViewModel
{
    #region 序列化配置

    private static readonly JsonSerializerOptions SchemePackageJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    #endregion

    #region 构造与初始化

    public SchemeConfigurationViewModel()
    {
        Schemes.CollectionChanged += Schemes_CollectionChanged;
        SchemesView = CollectionViewSource.GetDefaultView(Schemes);
        SchemesView.Filter = FilterSchemes;
        InitializeCommands();
        SelectedScheme = Schemes.FirstOrDefault();
        SetPageStatus(
            Schemes.Count == 0 ? "暂无方案配置，请点击新增。" : $"已加载 {Schemes.Count} 个方案。",
            NeutralBrush);
    }

    /// <summary>
    /// 初始化页面命令。
    /// </summary>
    private void InitializeCommands()
    {
        NewSchemeCommand = new RelayCommand(_ => NewScheme());
        DuplicateSchemeCommand = new RelayCommand(_ => DuplicateSelectedScheme(), _ => SelectedScheme is not null);
        DeleteSchemeCommand = new RelayCommand(_ => DeleteSelectedScheme(), _ => SelectedScheme is not null);
        SaveSchemesCommand = new RelayCommand(_ => SaveSchemes());
        ImportSchemeCommand = new RelayCommand(_ => ImportScheme());
        ExportSchemeCommand = new RelayCommand(_ => ExportSelectedScheme(), _ => SelectedScheme is not null);
        AddWorkStepToSchemeCommand = new RelayCommand(_ => AddWorkStepToScheme(), _ => SelectedScheme is not null);
        RemoveWorkStepFromSchemeCommand = new RelayCommand(
            _ => RemoveSelectedSchemeStep(),
            _ => SelectedScheme is not null && SelectedSchemeStep is not null);
        UndoRemoveSchemeStepCommand = new RelayCommand(_ => UndoRemoveSchemeStep(), _ => CanUndoRemoveSchemeStep());
    }

    #endregion

    #region 方案命令

    /// <summary>
    /// 新增方案并切换到新方案。
    /// </summary>
    private void NewScheme()
    {
        if (!CanRunCreateOrCopyCommand())
        {
            return;
        }

        SchemeProfile scheme = CreateScheme(GenerateUniqueSchemeName("方案"));
        Schemes.Add(scheme);
        SelectCreatedScheme(scheme);
        SetPageStatus("已新增方案。", SuccessBrush);
    }

    /// <summary>
    /// 复制当前选中的方案。
    /// </summary>
    private void DuplicateSelectedScheme()
    {
        if (!CanRunCreateOrCopyCommand() || SelectedScheme is null)
        {
            return;
        }

        SchemeProfile scheme = CreateCopyScheme(SelectedScheme);
        Schemes.Add(scheme);
        SelectCreatedScheme(scheme);
        SetPageStatus($"已复制方案：{scheme.SchemeName}。", SuccessBrush);
    }

    /// <summary>
    /// 删除当前选中的方案。
    /// </summary>
    private void DeleteSelectedScheme()
    {
        if (SelectedScheme is null)
        {
            return;
        }

        string deletedSchemeId = SelectedScheme.Id;
        int index = Schemes.IndexOf(SelectedScheme);

        Schemes.Remove(SelectedScheme);
        SelectedScheme = Schemes.Count == 0
            ? null
            : Schemes[Math.Clamp(index, 0, Schemes.Count - 1)];

        ClearRemovedSchemeStepUndo(deletedSchemeId);
        SetPageStatus("已删除方案，点击保存后生效。", WarningBrush);
    }

    /// <summary>
    /// 校验并保存全部方案。
    /// </summary>
    private void SaveSchemes()
    {
        if (!ValidateSchemes(out string message))
        {
            SetPageStatus(message, WarningBrush);
            return;
        }

        BusinessConfigurationStore.SaveCatalog(_catalog);
        SetPageStatus($"已保存 {Schemes.Count} 个方案。", SuccessBrush);
    }

    /// <summary>
    /// 从本地文件导入方案。
    /// </summary>
    private void ImportScheme()
    {
        OpenFileDialog dialog = new()
        {
            Filter = "方案文件 (*.scheme.json)|*.scheme.json|JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
            DefaultExt = ".scheme.json"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            string json = File.ReadAllText(dialog.FileName);
            SchemeConfigurationPackage? package =
                JsonSerializer.Deserialize<SchemeConfigurationPackage>(json, SchemePackageJsonOptions);

            if (package?.Scheme is null)
            {
                SetPageStatus("导入失败：方案文件为空或格式无效。", WarningBrush);
                return;
            }

            ImportSchemePackage(package);
        }
        catch (Exception ex)
        {
            SetPageStatus($"导入方案失败：{ex.Message}", WarningBrush);
        }
    }

    /// <summary>
    /// 导出当前选中的方案。
    /// </summary>
    private void ExportSelectedScheme()
    {
        if (SelectedScheme is null)
        {
            SetPageStatus("请先选择要导出的方案。", WarningBrush);
            return;
        }

        SaveFileDialog dialog = new()
        {
            Filter = "方案文件 (*.scheme.json)|*.scheme.json|JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
            DefaultExt = ".scheme.json",
            FileName = $"{SanitizeFileName(SelectedScheme.SchemeName)}.scheme.json"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            SchemeConfigurationPackage package = CreateSchemePackage(SelectedScheme);
            string json = JsonSerializer.Serialize(package, SchemePackageJsonOptions);
            File.WriteAllText(dialog.FileName, json);
            SetPageStatus($"已导出方案：{dialog.FileName}", SuccessBrush);
        }
        catch (Exception ex)
        {
            SetPageStatus($"导出方案失败：{ex.Message}", WarningBrush);
        }
    }

    #endregion

    #region 方案工步命令

    /// <summary>
    /// 在当前方案中新增工步。
    /// </summary>
    private void AddWorkStepToScheme()
    {
        if (SelectedScheme is null)
        {
            return;
        }

        SchemeWorkStepItem schemeStep = CreateEmptySchemeStep(GenerateUniqueSchemeStepName("工步"));
        int insertIndex = SelectedSchemeStep is null
            ? SelectedScheme.Steps.Count
            : Math.Clamp(SelectedScheme.Steps.IndexOf(SelectedSchemeStep) + 1, 0, SelectedScheme.Steps.Count);

        SelectedScheme.Steps.Insert(insertIndex, schemeStep);
        SelectedSchemeStep = schemeStep;
        SetPageStatus($"已新增方案工步：{schemeStep.SchemeStepName}。", SuccessBrush);
    }

    /// <summary>
    /// 删除当前选中的方案工步，并写入撤回栈。
    /// </summary>
    private void RemoveSelectedSchemeStep()
    {
        if (SelectedScheme is null || SelectedSchemeStep is null)
        {
            return;
        }

        int index = SelectedScheme.Steps.IndexOf(SelectedSchemeStep);
        RememberRemovedSchemeStep(SelectedSchemeStep, index, SelectedScheme);
        SelectedScheme.Steps.Remove(SelectedSchemeStep);
        SelectedSchemeStep = SelectedScheme.Steps.Count == 0
            ? null
            : SelectedScheme.Steps[Math.Clamp(index, 0, SelectedScheme.Steps.Count - 1)];

        SetPageStatus("已删除方案工步。", WarningBrush);
    }

    /// <summary>
    /// 撤回当前方案最近一次删除的工步，可连续撤回。
    /// </summary>
    private void UndoRemoveSchemeStep()
    {
        if (SelectedScheme is null || !TryPopRemovedSchemeStepUndo(SelectedScheme, out RemovedSchemeStepUndoItem? undoItem))
        {
            return;
        }

        int insertIndex = Math.Clamp(undoItem.StepIndex, 0, SelectedScheme.Steps.Count);
        SchemeWorkStepItem restoredStep = undoItem.SchemeStep.Clone();
        restoredStep.Id = Guid.NewGuid().ToString("N");
        SelectedScheme.Steps.Insert(insertIndex, restoredStep);
        SelectedSchemeStep = restoredStep;

        SetPageStatus($"已撤回删除的工步：{restoredStep.SchemeStepName}。", SuccessBrush);
        RaiseCommandStatesChanged();
    }

    /// <summary>
    /// 调整方案工步顺序。
    /// </summary>
    public void MoveSchemeStep(SchemeWorkStepItem draggedSchemeStep, SchemeWorkStepItem targetSchemeStep, bool insertAfter)
    {
        if (SelectedScheme is null)
        {
            return;
        }

        ObservableCollection<SchemeWorkStepItem> steps = SelectedScheme.Steps;
        int oldIndex = steps.IndexOf(draggedSchemeStep);
        int targetIndex = steps.IndexOf(targetSchemeStep);
        if (oldIndex < 0 || targetIndex < 0 || oldIndex == targetIndex)
        {
            return;
        }

        int newIndex = targetIndex + (insertAfter ? 1 : 0);
        if (oldIndex < newIndex)
        {
            newIndex--;
        }

        newIndex = Math.Clamp(newIndex, 0, steps.Count - 1);
        if (oldIndex == newIndex)
        {
            return;
        }

        steps.Move(oldIndex, newIndex);
        SelectedSchemeStep = draggedSchemeStep;
        SetPageStatus("已调整工步顺序。", SuccessBrush);
        RaiseCommandStatesChanged();
    }

    #endregion

    #region 校验与搜索

    /// <summary>
    /// 保存前校验方案数据。
    /// </summary>
    private bool ValidateSchemes(out string message)
    {
        if (Schemes.Count == 0)
        {
            message = "请至少保留一个方案。";
            return false;
        }

        HashSet<string> schemeNames = new(StringComparer.OrdinalIgnoreCase);

        foreach (SchemeProfile scheme in Schemes)
        {
            if (string.IsNullOrWhiteSpace(scheme.SchemeName))
            {
                message = "方案名称不能为空。";
                return false;
            }

            if (!schemeNames.Add(scheme.SchemeName.Trim()))
            {
                message = $"方案名称重复：{scheme.SchemeName}";
                return false;
            }

            foreach (SchemeWorkStepItem schemeStep in scheme.Steps)
            {
                if (string.IsNullOrWhiteSpace(schemeStep.SchemeStepName))
                {
                    message = $"方案“{scheme.SchemeName}”存在未命名工步。";
                    return false;
                }
            }
        }

        message = string.Empty;
        return true;
    }

    /// <summary>
    /// 按关键字过滤方案列表。
    /// </summary>
    private bool FilterSchemes(object item)
    {
        if (item is not SchemeProfile scheme)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        string keyword = SearchText.Trim();
        return Contains(scheme.SchemeName, keyword) ||
               scheme.Steps.Any(step =>
                   Contains(step.SchemeStepName, keyword) ||
                   Contains(step.StepName, keyword) ||
                   step.Operations.Any(operation => Contains(operation.DisplayText, keyword)));
    }

    private static bool Contains(string? source, string keyword)
    {
        return source?.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    #endregion

    #region 导入导出辅助

    private SchemeConfigurationPackage CreateSchemePackage(SchemeProfile scheme)
    {
        return new SchemeConfigurationPackage
        {
            Scheme = scheme.Clone(),
            WorkSteps = new ObservableCollection<WorkStepProfile>(
                scheme.Steps.Select(step => step.ToWorkStepProfile()))
        };
    }

    /// <summary>
    /// 导入方案包，并在必要时补齐内嵌工步快照。
    /// </summary>
    private void ImportSchemePackage(SchemeConfigurationPackage package)
    {
        SchemeProfile scheme = package.Scheme!.Clone();
        scheme.Id = Guid.NewGuid().ToString("N");
        scheme.SchemeName = GenerateUniqueImportedSchemeName(scheme.SchemeName);

        foreach (SchemeWorkStepItem schemeStep in scheme.Steps)
        {
            schemeStep.Id = Guid.NewGuid().ToString("N");

            if (schemeStep.Operations.Count == 0)
            {
                WorkStepProfile? sourceWorkStep = FindPackageWorkStep(package, schemeStep);
                if (sourceWorkStep is null)
                {
                    SetPageStatus($"导入失败：工步“{schemeStep.SchemeStepName}”缺少步骤内容。", WarningBrush);
                    return;
                }

                schemeStep.Operations = new ObservableCollection<WorkStepOperation>(
                    sourceWorkStep.Steps.Select(operation => operation.Clone()));

                if (string.IsNullOrWhiteSpace(schemeStep.StepName))
                {
                    schemeStep.StepName = sourceWorkStep.StepName;
                }
            }

            if (string.IsNullOrWhiteSpace(schemeStep.StepName))
            {
                schemeStep.StepName = GenerateUniqueSchemeStepName("工步", scheme);
            }
        }

        Schemes.Add(scheme);
        SelectCreatedScheme(scheme);
        SetPageStatus($"已导入方案：{scheme.SchemeName}。", SuccessBrush);
    }

    private static WorkStepProfile? FindPackageWorkStep(SchemeConfigurationPackage package, SchemeWorkStepItem schemeStep)
    {
        IEnumerable<WorkStepProfile> packageWorkSteps = package.WorkSteps ?? new ObservableCollection<WorkStepProfile>();
        string operationSummary = BuildOperationSummary(schemeStep.Operations);

        return packageWorkSteps.FirstOrDefault(workStep =>
                   string.Equals(workStep.Id, schemeStep.WorkStepId, StringComparison.Ordinal) &&
                   MatchesSchemeStepSnapshot(workStep, schemeStep)) ??
               packageWorkSteps.FirstOrDefault(workStep =>
                   TextEquals(workStep.StepName, schemeStep.StepName) &&
                   TextEquals(workStep.OperationSummary, operationSummary)) ??
               (string.IsNullOrWhiteSpace(operationSummary)
                   ? packageWorkSteps.FirstOrDefault(workStep => TextEquals(workStep.StepName, schemeStep.StepName))
                   : null) ??
               (string.IsNullOrWhiteSpace(schemeStep.StepName)
                   ? packageWorkSteps.FirstOrDefault(workStep => TextEquals(workStep.OperationSummary, operationSummary))
                   : null);
    }

    private static bool MatchesSchemeStepSnapshot(WorkStepProfile workStep, SchemeWorkStepItem schemeStep)
    {
        if (!string.IsNullOrWhiteSpace(schemeStep.StepName) &&
            !string.IsNullOrWhiteSpace(workStep.StepName) &&
            !TextEquals(workStep.StepName, schemeStep.StepName))
        {
            return false;
        }

        string operationSummary = BuildOperationSummary(schemeStep.Operations);
        if (!string.IsNullOrWhiteSpace(operationSummary) &&
            !TextEquals(workStep.OperationSummary, operationSummary))
        {
            return false;
        }

        return true;
    }

    private static string BuildOperationSummary(IEnumerable<WorkStepOperation> operations)
    {
        List<string> items = operations
            .Where(operation => operation is not null)
            .Select(operation => operation.DisplayText)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text.Trim())
            .ToList();

        return items.Count == 0 ? string.Empty : string.Join(" / ", items);
    }

    private static bool TextEquals(string? left, string? right)
    {
        return string.Equals(NormalizeText(left), NormalizeText(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeText(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    private static string SanitizeFileName(string fileName)
    {
        string safeName = string.IsNullOrWhiteSpace(fileName) ? "方案" : fileName.Trim();
        foreach (char invalidChar in Path.GetInvalidFileNameChars())
        {
            safeName = safeName.Replace(invalidChar, '_');
        }

        return safeName;
    }

    #endregion

    #region 工厂与命名

    private SchemeProfile CreateScheme(string schemeName)
    {
        return new SchemeProfile
        {
            SchemeName = schemeName
        };
    }

    private SchemeWorkStepItem CreateEmptySchemeStep(string schemeStepName)
    {
        return new SchemeWorkStepItem
        {
            StepName = schemeStepName,
            IsStartupEnabled = true,
            LastModifiedAt = DateTime.Now
        };
    }

    private SchemeProfile CreateCopyScheme(SchemeProfile source)
    {
        return new SchemeProfile
        {
            Id = Guid.NewGuid().ToString("N"),
            SchemeName = GenerateCopySchemeName(source.SchemeName),
            Steps = new ObservableCollection<SchemeWorkStepItem>(
                source.Steps.Select(step =>
                {
                    SchemeWorkStepItem clone = step.Clone();
                    clone.Id = Guid.NewGuid().ToString("N");
                    return clone;
                }))
        };
    }

    private void SelectCreatedScheme(SchemeProfile scheme)
    {
        SearchText = string.Empty;
        SchemesView.Refresh();
        SelectedScheme = scheme;
        SchemesView.MoveCurrentTo(scheme);
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

    private string GenerateUniqueSchemeName(string prefix)
    {
        HashSet<string> existingNames = new(Schemes.Select(scheme => scheme.SchemeName), StringComparer.OrdinalIgnoreCase);
        int index = existingNames.Count + 1;
        string candidate;

        do
        {
            candidate = $"{prefix} {index}";
            index++;
        }
        while (existingNames.Contains(candidate));

        return candidate;
    }

    private string GenerateUniqueImportedSchemeName(string schemeName)
    {
        HashSet<string> existingNames = new(Schemes.Select(scheme => scheme.SchemeName), StringComparer.OrdinalIgnoreCase);
        string baseName = string.IsNullOrWhiteSpace(schemeName) ? "方案" : schemeName.Trim();
        string candidate = baseName;
        int index = 2;

        while (existingNames.Contains(candidate))
        {
            candidate = $"{baseName} {index}";
            index++;
        }

        return candidate;
    }

    private string GenerateCopySchemeName(string baseName)
    {
        HashSet<string> existingNames = new(Schemes.Select(scheme => scheme.SchemeName), StringComparer.OrdinalIgnoreCase);
        string normalizedName = string.IsNullOrWhiteSpace(baseName) ? "方案" : baseName.Trim();
        string copyName = $"{normalizedName} 副本";
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

    private string GenerateUniqueSchemeStepName(string prefix, SchemeProfile? targetScheme = null)
    {
        SchemeProfile? scheme = targetScheme ?? SelectedScheme;
        HashSet<string> existingNames = new(
            scheme?.Steps.Select(step => step.SchemeStepName) ?? Enumerable.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);

        string baseName = string.IsNullOrWhiteSpace(prefix) ? "工步" : prefix.Trim();
        string candidate = baseName;
        int index = 1;

        while (existingNames.Contains(candidate))
        {
            index++;
            candidate = $"{baseName} {index}";
        }

        return candidate;
    }

    #endregion

    #region 删除撤回

    private bool CanUndoRemoveSchemeStep()
    {
        return SelectedScheme is not null &&
               _removedSchemeStepUndoItems.Any(item =>
                   string.Equals(item.SchemeId, SelectedScheme.Id, StringComparison.Ordinal));
    }

    private void RememberRemovedSchemeStep(SchemeWorkStepItem schemeStep, int index, SchemeProfile scheme)
    {
        _removedSchemeStepUndoItems.Add(new RemovedSchemeStepUndoItem
        {
            SchemeId = scheme.Id,
            StepIndex = Math.Max(0, index),
            SchemeStep = schemeStep.Clone()
        });

        RaiseCommandStatesChanged();
    }

    private bool TryPopRemovedSchemeStepUndo(SchemeProfile scheme, out RemovedSchemeStepUndoItem? undoItem)
    {
        for (int index = _removedSchemeStepUndoItems.Count - 1; index >= 0; index--)
        {
            RemovedSchemeStepUndoItem currentItem = _removedSchemeStepUndoItems[index];
            if (!string.Equals(currentItem.SchemeId, scheme.Id, StringComparison.Ordinal))
            {
                continue;
            }

            _removedSchemeStepUndoItems.RemoveAt(index);
            undoItem = currentItem;
            return true;
        }

        undoItem = null;
        return false;
    }

    private void ClearRemovedSchemeStepUndo(string? schemeId = null)
    {
        if (string.IsNullOrWhiteSpace(schemeId))
        {
            _removedSchemeStepUndoItems.Clear();
        }
        else
        {
            _removedSchemeStepUndoItems.RemoveAll(item =>
                string.Equals(item.SchemeId, schemeId, StringComparison.Ordinal));
        }

        RaiseCommandStatesChanged();
    }

    #endregion

    #region 页面状态与命令刷新

    private void Schemes_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RaisePageSummaryChanged();
        SchemesView.Refresh();
        RaiseCommandStatesChanged();
    }

    private void SetPageStatus(string text, Brush brush)
    {
        PageStatusText = text;
        PageStatusBrush = brush;
    }

    private void RaiseCommandStatesChanged()
    {
        RaiseCommandState(DuplicateSchemeCommand);
        RaiseCommandState(DeleteSchemeCommand);
        RaiseCommandState(ImportSchemeCommand);
        RaiseCommandState(ExportSchemeCommand);
        RaiseCommandState(AddWorkStepToSchemeCommand);
        RaiseCommandState(RemoveWorkStepFromSchemeCommand);
        RaiseCommandState(UndoRemoveSchemeStepCommand);
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
