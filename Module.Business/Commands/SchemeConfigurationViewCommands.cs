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
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Media;

namespace Module.Business.ViewModels;

/// <summary>
/// 方案配置界面命令实现。
/// </summary>
public sealed partial class SchemeConfigurationViewModel
{
    private static readonly JsonSerializerOptions SchemePackageJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    #region 构造与初始化

    public SchemeConfigurationViewModel()
    {
        Schemes.CollectionChanged += Schemes_CollectionChanged;
        SchemesView = CollectionViewSource.GetDefaultView(Schemes);
        SchemesView.Filter = FilterSchemes;
        InitializeCommands();
        RefreshProductOptions();
        SelectedScheme = Schemes.FirstOrDefault();
        SetPageStatus(Schemes.Count == 0 ? "暂无方案配置，请点击新增。" : $"已读取 {Schemes.Count} 个方案", NeutralBrush);
    }

    /// <summary>
    /// 初始化页面命令，所有按钮通过 Command 绑定。
    /// </summary>
    private void InitializeCommands()
    {
        NewSchemeCommand = new RelayCommand(_ => NewScheme());
        DuplicateSchemeCommand = new RelayCommand(_ => DuplicateSelectedScheme(), _ => SelectedScheme is not null);
        DeleteSchemeCommand = new RelayCommand(_ => DeleteSelectedScheme(), _ => SelectedScheme is not null);
        SaveSchemesCommand = new RelayCommand(_ => SaveSchemes());
        ImportSchemeCommand = new RelayCommand(_ => ImportScheme());
        ExportSchemeCommand = new RelayCommand(_ => ExportSelectedScheme(), _ => SelectedScheme is not null);
        RefreshWorkStepsCommand = new RelayCommand(_ => RefreshProductAndAvailableWorkSteps());
        AddWorkStepToSchemeCommand = new RelayCommand(_ => AddWorkStepToScheme(), _ => SelectedScheme is not null && SelectedAvailableWorkStep is not null);
        RemoveWorkStepFromSchemeCommand = new RelayCommand(_ => RemoveSelectedSchemeStep(), _ => SelectedScheme is not null && SelectedSchemeStep is not null);
        MoveSchemeStepUpCommand = new RelayCommand(_ => MoveSelectedSchemeStep(-1), _ => CanMoveSelectedSchemeStep(-1));
        MoveSchemeStepDownCommand = new RelayCommand(_ => MoveSelectedSchemeStep(1), _ => CanMoveSelectedSchemeStep(1));
    }

    #endregion

    #region 方案命令方法

    /// <summary>
    /// 新增方案，默认使用当前方案或首个工步产品。
    /// </summary>
    private void NewScheme()
    {
        if (!CanRunCreateOrCopyCommand())
        {
            return;
        }

        string productName = string.IsNullOrWhiteSpace(SelectedScheme?.ProductName)
            ? ProductOptions.FirstOrDefault() ?? WorkSteps.FirstOrDefault()?.ProductName ?? "默认产品"
            : SelectedScheme.ProductName;

        SchemeProfile scheme = CreateScheme(productName, GenerateUniqueSchemeName("方案"));
        Schemes.Add(scheme);
        SelectCreatedScheme(scheme);
        SetPageStatus("已新增方案，选择产品后添加工步。", SuccessBrush);
    }

    /// <summary>
    /// 复制当前选中方案及其工步列表。
    /// </summary>
    private void DuplicateSelectedScheme()
    {
        if (!CanRunCreateOrCopyCommand())
        {
            return;
        }

        if (SelectedScheme is null)
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

        int index = Schemes.IndexOf(SelectedScheme);
        Schemes.Remove(SelectedScheme);
        SelectedScheme = Schemes.Count == 0
            ? null
            : Schemes[Math.Clamp(index, 0, Schemes.Count - 1)];

        SetPageStatus("已删除方案，点击保存后生效。", WarningBrush);
    }

    /// <summary>
    /// 保存所有方案配置。
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
    /// 导入方案文件，产品名称相同则复用产品，工步内容相同则复用工步。
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
            SchemeConfigurationPackage? package = JsonSerializer.Deserialize<SchemeConfigurationPackage>(json, SchemePackageJsonOptions);
            if (package?.Scheme is null)
            {
                SetPageStatus("导入失败：方案文件内容为空或格式不正确。", WarningBrush);
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
    /// 导出当前选中方案，以及方案引用的产品和完整工步内容。
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

    /// <summary>
    /// 重新读取产品名称和可添加工步列表，保留当前正在编辑的方案。
    /// </summary>
    private void RefreshProductAndAvailableWorkSteps()
    {
        BusinessConfigurationCatalog latestCatalog = BusinessConfigurationStore.LoadCatalog();
        _catalog.Products = latestCatalog.Products;
        _catalog.WorkSteps = latestCatalog.WorkSteps;

        OnPropertyChanged(nameof(WorkSteps));
        RefreshProductOptions();
        RefreshAvailableWorkSteps();
        SetPageStatus("已刷新产品名称和可添加工步列表。", SuccessBrush);
    }

    #endregion

    #region 方案工步命令方法

    /// <summary>
    /// 将当前产品下的可选工步添加到方案工步列表。
    /// </summary>
    private void AddWorkStepToScheme()
    {
        if (SelectedScheme is null || SelectedAvailableWorkStep is null)
        {
            return;
        }

        SchemeWorkStepItem schemeStep = SchemeWorkStepItem.FromWorkStep(SelectedAvailableWorkStep);
        SelectedScheme.Steps.Add(schemeStep);
        SelectedSchemeStep = schemeStep;
        SetPageStatus($"已添加工步：{schemeStep.StepName}", SuccessBrush);
    }

    /// <summary>
    /// 从方案中移除当前选中的工步。
    /// </summary>
    private void RemoveSelectedSchemeStep()
    {
        if (SelectedScheme is null || SelectedSchemeStep is null)
        {
            return;
        }

        int index = SelectedScheme.Steps.IndexOf(SelectedSchemeStep);
        SelectedScheme.Steps.Remove(SelectedSchemeStep);
        SelectedSchemeStep = SelectedScheme.Steps.Count == 0
            ? null
            : SelectedScheme.Steps[Math.Clamp(index, 0, SelectedScheme.Steps.Count - 1)];

        SetPageStatus("已移除方案工步。", WarningBrush);
    }

    /// <summary>
    /// 调整方案工步顺序。
    /// </summary>
    private void MoveSelectedSchemeStep(int offset)
    {
        if (!CanMoveSelectedSchemeStep(offset) || SelectedScheme is null || SelectedSchemeStep is null)
        {
            return;
        }

        int oldIndex = SelectedScheme.Steps.IndexOf(SelectedSchemeStep);
        int newIndex = oldIndex + offset;
        SelectedScheme.Steps.Move(oldIndex, newIndex);
        SetPageStatus("已调整方案工步顺序。", SuccessBrush);
        RaiseCommandStatesChanged();
    }

    #endregion

    #region 筛选与校验方法

    private void RefreshProductOptions()
    {
        string? selectedProductName = SelectedScheme?.ProductName;
        IEnumerable<string?> productNames = _catalog.Products
            .Select(product => product.ProductName)
            .Concat(WorkSteps.Select(step => step.ProductName));

        if (!string.IsNullOrWhiteSpace(selectedProductName))
        {
            productNames = productNames.Append(selectedProductName);
        }

        List<string> distinctProductNames = productNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ProductOptions.SequenceEqual(distinctProductNames, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        ProductOptions.Clear();
        foreach (string productName in distinctProductNames)
        {
            ProductOptions.Add(productName);
        }
    }

    private void RefreshAvailableWorkSteps()
    {
        AvailableWorkSteps.Clear();
        SelectedAvailableWorkStep = null;

        if (SelectedScheme is null || string.IsNullOrWhiteSpace(SelectedScheme.ProductName))
        {
            RaisePageSummaryChanged();
            return;
        }

        foreach (WorkStepProfile workStep in WorkSteps
                     .Where(step => string.Equals(step.ProductName, SelectedScheme.ProductName, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(step => step.StepName))
        {
            AvailableWorkSteps.Add(workStep);
        }

        SelectedAvailableWorkStep = AvailableWorkSteps.FirstOrDefault();
        RaisePageSummaryChanged();
    }

    private bool ValidateSchemes(out string message)
    {
        if (Schemes.Count == 0)
        {
            message = "请至少新增一个方案。";
            return false;
        }

        HashSet<string> schemeNames = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, WorkStepProfile> workStepById = WorkSteps.ToDictionary(step => step.Id, StringComparer.Ordinal);

        foreach (SchemeProfile scheme in Schemes)
        {
            if (string.IsNullOrWhiteSpace(scheme.SchemeName))
            {
                message = "方案名称不能为空。";
                return false;
            }

            if (!schemeNames.Add(scheme.SchemeName.Trim()))
            {
                message = $"方案名称不能重复：{scheme.SchemeName}";
                return false;
            }

            if (string.IsNullOrWhiteSpace(scheme.ProductName))
            {
                message = $"方案“{scheme.SchemeName}”的产品名称不能为空。";
                return false;
            }

            foreach (SchemeWorkStepItem schemeStep in scheme.Steps)
            {
                if (!workStepById.TryGetValue(schemeStep.WorkStepId, out WorkStepProfile? workStep))
                {
                    message = $"方案“{scheme.SchemeName}”包含已不存在的工步，请移除后保存。";
                    return false;
                }

                if (!string.Equals(workStep.ProductName, scheme.ProductName, StringComparison.OrdinalIgnoreCase))
                {
                    message = $"方案“{scheme.SchemeName}”只能添加产品“{scheme.ProductName}”下的工步。";
                    return false;
                }
            }
        }

        message = string.Empty;
        return true;
    }

    #endregion

    #region 工具方法

    private SchemeConfigurationPackage CreateSchemePackage(SchemeProfile scheme)
    {
        SchemeConfigurationPackage package = new()
        {
            Scheme = scheme.Clone(),
            Product = _catalog.Products
                .FirstOrDefault(product => TextEquals(product.ProductName, scheme.ProductName))
                ?.Clone()
        };

        if (package.Product is null && !string.IsNullOrWhiteSpace(scheme.ProductName))
        {
            package.Product = new ProductProfile
            {
                Id = Guid.NewGuid().ToString("N"),
                ProductName = scheme.ProductName.Trim()
            };
        }

        HashSet<string> exportedWorkStepIds = new(StringComparer.Ordinal);
        foreach (SchemeWorkStepItem schemeStep in scheme.Steps)
        {
            WorkStepProfile? workStep = FindCatalogWorkStep(schemeStep);
            if (workStep is not null && exportedWorkStepIds.Add(workStep.Id))
            {
                package.WorkSteps.Add(workStep.Clone());
            }
        }

        return package;
    }

    private void ImportSchemePackage(SchemeConfigurationPackage package)
    {
        SchemeProfile sourceScheme = package.Scheme!;
        List<(SchemeWorkStepItem SchemeStep, WorkStepProfile WorkStep)> sourceWorkSteps = new();

        foreach (SchemeWorkStepItem sourceSchemeStep in sourceScheme.Steps)
        {
            WorkStepProfile? sourceWorkStep = FindPackageWorkStep(package, sourceSchemeStep);
            if (sourceWorkStep is null)
            {
                SetPageStatus($"导入失败：方案文件缺少工步“{sourceSchemeStep.StepName}”的完整内容。", WarningBrush);
                return;
            }

            sourceWorkSteps.Add((sourceSchemeStep, sourceWorkStep));
        }

        RefreshProductsAndWorkStepsFromLocalFiles();
        string importedSchemeName = GenerateUniqueImportedSchemeName(sourceScheme.SchemeName);
        string productName = ResolveImportedProductName(package, importedSchemeName, out bool createdProduct);
        SchemeProfile scheme = CreateScheme(productName, importedSchemeName);

        int reusedWorkStepCount = 0;
        int createdWorkStepCount = 0;
        foreach ((SchemeWorkStepItem sourceSchemeStep, WorkStepProfile sourceWorkStep) in sourceWorkSteps)
        {
            WorkStepProfile workStep = ResolveImportedWorkStep(sourceWorkStep, sourceSchemeStep.StepName, productName, importedSchemeName, out bool createdWorkStep);
            if (createdWorkStep)
            {
                createdWorkStepCount++;
            }
            else
            {
                reusedWorkStepCount++;
            }

            scheme.Steps.Add(SchemeWorkStepItem.FromWorkStep(workStep));
        }

        Schemes.Add(scheme);
        SelectCreatedScheme(scheme);
        RefreshProductOptions();
        RefreshAvailableWorkSteps();
        string productStatus = createdProduct ? "新建产品" : "复用产品";
        SetPageStatus($"已导入方案，{productStatus}，复用 {reusedWorkStepCount} 个工步，新建 {createdWorkStepCount} 个工步，点击保存后生效。", SuccessBrush);
    }

    private void RefreshProductsAndWorkStepsFromLocalFiles()
    {
        BusinessConfigurationCatalog latestCatalog = BusinessConfigurationStore.LoadCatalog();
        ObservableCollection<ProductProfile> currentProducts = _catalog.Products;
        ObservableCollection<WorkStepProfile> currentWorkSteps = _catalog.WorkSteps;

        _catalog.Products = latestCatalog.Products;
        foreach (ProductProfile product in currentProducts)
        {
            if (!_catalog.Products.Any(localProduct => TextEquals(localProduct.ProductName, product.ProductName)))
            {
                _catalog.Products.Add(product);
            }
        }

        _catalog.WorkSteps = latestCatalog.WorkSteps;
        foreach (WorkStepProfile workStep in currentWorkSteps)
        {
            if (!_catalog.WorkSteps.Any(localWorkStep => IsSameWorkStepIdentity(localWorkStep, workStep)))
            {
                _catalog.WorkSteps.Add(workStep);
            }
        }

        OnPropertyChanged(nameof(WorkSteps));
    }

    private string ResolveImportedProductName(SchemeConfigurationPackage package, string schemeName, out bool createdProduct)
    {
        createdProduct = false;
        string? productName = package.Product?.ProductName;
        if (string.IsNullOrWhiteSpace(productName))
        {
            productName = package.Scheme?.ProductName;
        }

        productName = string.IsNullOrWhiteSpace(productName) ? "默认产品" : productName.Trim();

        ProductProfile? existingProduct = _catalog.Products
            .FirstOrDefault(product => TextEquals(product.ProductName, productName));
        if (existingProduct is not null)
        {
            return existingProduct.ProductName;
        }

        ProductProfile createdProductProfile = package.Product?.Clone() ?? new ProductProfile();
        createdProductProfile.Id = Guid.NewGuid().ToString("N");
        createdProductProfile.ProductName = GenerateUniqueProductName(AppendSchemeNameSuffix(productName, schemeName));
        createdProductProfile.MarkModified();
        _catalog.Products.Add(createdProductProfile);
        createdProduct = true;
        return createdProductProfile.ProductName;
    }

    private WorkStepProfile ResolveImportedWorkStep(WorkStepProfile source, string sourceStepName, string productName, string schemeName, out bool createdWorkStep)
    {
        string normalizedSourceStepName = string.IsNullOrWhiteSpace(sourceStepName)
            ? source.StepName
            : sourceStepName;
        WorkStepProfile? existingWorkStep = WorkSteps
            .FirstOrDefault(workStep =>
                TextEquals(workStep.ProductName, productName) &&
                TextEquals(workStep.StepName, normalizedSourceStepName) &&
                HasSameOperationContent(workStep, source));

        if (existingWorkStep is not null)
        {
            createdWorkStep = false;
            return existingWorkStep;
        }

        WorkStepProfile created = CreateImportedWorkStep(source, sourceStepName, productName, schemeName);
        WorkSteps.Add(created);
        createdWorkStep = true;
        return created;
    }

    private WorkStepProfile CreateImportedWorkStep(WorkStepProfile source, string sourceStepName, string productName, string schemeName)
    {
        string stepName = !string.IsNullOrWhiteSpace(sourceStepName)
            ? sourceStepName.Trim()
            : string.IsNullOrWhiteSpace(source.StepName) ? "工步" : source.StepName.Trim();
        return new WorkStepProfile
        {
            Id = Guid.NewGuid().ToString("N"),
            ProductName = productName,
            StepName = GenerateUniqueWorkStepName(productName, AppendSchemeNameSuffix(stepName, schemeName)),
            LastModifiedAt = DateTime.Now,
            Steps = new ObservableCollection<WorkStepOperation>(
                source.Steps.Select(operation => new WorkStepOperation
                {
                    Id = Guid.NewGuid().ToString("N"),
                    OperationType = operation.OperationType,
                    OperationObject = operation.OperationObject,
                    ProtocolName = operation.ProtocolName,
                    CommandName = operation.CommandName,
                    InvokeMethod = operation.InvokeMethod,
                    ReturnValue = operation.ReturnValue,
                    LuaScript = operation.LuaScript,
                    DelayMilliseconds = operation.DelayMilliseconds,
                    Remark = operation.Remark,
                    Parameters = new ObservableCollection<WorkStepOperationParameter>(
                        operation.Parameters.Select(parameter => parameter.Clone()))
                }))
        };
    }

    private WorkStepProfile? FindCatalogWorkStep(SchemeWorkStepItem schemeStep)
    {
        return WorkSteps.FirstOrDefault(workStep => string.Equals(workStep.Id, schemeStep.WorkStepId, StringComparison.Ordinal)) ??
               WorkSteps.FirstOrDefault(workStep =>
                   TextEquals(workStep.ProductName, schemeStep.ProductName) &&
                   TextEquals(workStep.StepName, schemeStep.StepName) &&
                   TextEquals(workStep.OperationSummary, schemeStep.OperationSummary)) ??
               (string.IsNullOrWhiteSpace(schemeStep.OperationSummary)
                   ? WorkSteps.FirstOrDefault(workStep =>
                       TextEquals(workStep.ProductName, schemeStep.ProductName) &&
                       TextEquals(workStep.StepName, schemeStep.StepName))
                   : null) ??
               (string.IsNullOrWhiteSpace(schemeStep.StepName)
                   ? WorkSteps.FirstOrDefault(workStep =>
                       TextEquals(workStep.ProductName, schemeStep.ProductName) &&
                       TextEquals(workStep.OperationSummary, schemeStep.OperationSummary))
                   : null);
    }

    private static WorkStepProfile? FindPackageWorkStep(SchemeConfigurationPackage package, SchemeWorkStepItem schemeStep)
    {
        IEnumerable<WorkStepProfile> packageWorkSteps = package.WorkSteps ?? new ObservableCollection<WorkStepProfile>();
        return packageWorkSteps.FirstOrDefault(workStep =>
                   string.Equals(workStep.Id, schemeStep.WorkStepId, StringComparison.Ordinal) &&
                   MatchesSchemeStepSnapshot(workStep, schemeStep)) ??
               packageWorkSteps.FirstOrDefault(workStep =>
                   TextEquals(workStep.ProductName, schemeStep.ProductName) &&
                   TextEquals(workStep.StepName, schemeStep.StepName) &&
                   TextEquals(workStep.OperationSummary, schemeStep.OperationSummary)) ??
               (string.IsNullOrWhiteSpace(schemeStep.OperationSummary)
                   ? packageWorkSteps.FirstOrDefault(workStep =>
                       TextEquals(workStep.ProductName, schemeStep.ProductName) &&
                       TextEquals(workStep.StepName, schemeStep.StepName))
                   : null) ??
               (string.IsNullOrWhiteSpace(schemeStep.StepName)
                   ? packageWorkSteps.FirstOrDefault(workStep =>
                       TextEquals(workStep.ProductName, schemeStep.ProductName) &&
                       TextEquals(workStep.OperationSummary, schemeStep.OperationSummary))
                   : null);
    }

    private static bool MatchesSchemeStepSnapshot(WorkStepProfile workStep, SchemeWorkStepItem schemeStep)
    {
        if (!string.IsNullOrWhiteSpace(schemeStep.ProductName) &&
            !string.IsNullOrWhiteSpace(workStep.ProductName) &&
            !TextEquals(workStep.ProductName, schemeStep.ProductName))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(schemeStep.StepName) &&
            !string.IsNullOrWhiteSpace(workStep.StepName) &&
            !TextEquals(workStep.StepName, schemeStep.StepName))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(schemeStep.OperationSummary) &&
            !TextEquals(workStep.OperationSummary, schemeStep.OperationSummary))
        {
            return false;
        }

        return true;
    }

    private string GenerateUniqueProductName(string productName)
    {
        HashSet<string> existingNames = new(_catalog.Products.Select(product => product.ProductName), StringComparer.OrdinalIgnoreCase);
        string baseName = string.IsNullOrWhiteSpace(productName) ? "产品" : productName.Trim();
        string candidate = baseName;
        int index = 2;

        while (existingNames.Contains(candidate))
        {
            candidate = $"{baseName} {index}";
            index++;
        }

        return candidate;
    }

    private string GenerateUniqueWorkStepName(string productName, string stepName)
    {
        HashSet<string> existingNames = new(
            WorkSteps
                .Where(workStep => TextEquals(workStep.ProductName, productName))
                .Select(workStep => workStep.StepName),
            StringComparer.OrdinalIgnoreCase);

        string baseName = string.IsNullOrWhiteSpace(stepName) ? "工步" : stepName.Trim();
        string candidate = baseName;
        int index = 2;

        while (existingNames.Contains(candidate))
        {
            candidate = $"{baseName} {index}";
            index++;
        }

        return candidate;
    }

    private static bool HasSameOperationContent(WorkStepProfile left, WorkStepProfile right)
    {
        if (left.Steps.Count != right.Steps.Count)
        {
            return false;
        }

        for (int index = 0; index < left.Steps.Count; index++)
        {
            WorkStepOperation leftOperation = left.Steps[index];
            WorkStepOperation rightOperation = right.Steps[index];
            if (!TextEquals(leftOperation.OperationObject, rightOperation.OperationObject) ||
                !TextEquals(leftOperation.ProtocolName, rightOperation.ProtocolName) ||
                !TextEquals(GetComparableCommandName(leftOperation), GetComparableCommandName(rightOperation)) ||
                !TextEquals(leftOperation.InvokeMethod, rightOperation.InvokeMethod) ||
                !TextEquals(leftOperation.LuaScript, rightOperation.LuaScript))
            {
                return false;
            }
        }

        return true;
    }

    private static string GetComparableCommandName(WorkStepOperation operation)
    {
        return string.IsNullOrWhiteSpace(operation.CommandName)
            ? operation.InvokeMethod
            : operation.CommandName;
    }

    private static bool IsSameWorkStepIdentity(WorkStepProfile left, WorkStepProfile right)
    {
        return string.Equals(left.Id, right.Id, StringComparison.Ordinal) ||
               (TextEquals(left.ProductName, right.ProductName) &&
                TextEquals(left.StepName, right.StepName));
    }

    private static string AppendSchemeNameSuffix(string baseName, string schemeName)
    {
        string normalizedBaseName = string.IsNullOrWhiteSpace(baseName) ? "名称" : baseName.Trim();
        string normalizedSchemeName = NormalizeText(schemeName);
        if (string.IsNullOrWhiteSpace(normalizedSchemeName))
        {
            return normalizedBaseName;
        }

        string suffix = $"_{normalizedSchemeName}";
        return normalizedBaseName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? normalizedBaseName
            : $"{normalizedBaseName}{suffix}";
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
        string safeName = string.IsNullOrWhiteSpace(fileName) ? "scheme" : fileName.Trim();
        foreach (char invalidChar in Path.GetInvalidFileNameChars())
        {
            safeName = safeName.Replace(invalidChar, '_');
        }

        return safeName;
    }

    private SchemeProfile CreateScheme(string productName, string schemeName)
    {
        return new SchemeProfile
        {
            ProductName = productName,
            SchemeName = schemeName
        };
    }

    private SchemeProfile CreateCopyScheme(SchemeProfile source)
    {
        return new SchemeProfile
        {
            Id = Guid.NewGuid().ToString("N"),
            ProductName = source.ProductName,
            SchemeName = GenerateCopySchemeName(source.SchemeName),
            Steps = new ObservableCollection<SchemeWorkStepItem>(
                source.Steps.Select(step => new SchemeWorkStepItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    WorkStepId = step.WorkStepId,
                    ProductName = step.ProductName,
                    StepName = step.StepName,
                    OperationSummary = step.OperationSummary
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
               Contains(scheme.ProductName, keyword) ||
               scheme.Steps.Any(step => Contains(step.StepName, keyword) || Contains(step.OperationSummary, keyword));
    }

    private static bool Contains(string? source, string keyword)
    {
        return source?.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private bool CanMoveSelectedSchemeStep(int offset)
    {
        if (SelectedScheme is null || SelectedSchemeStep is null)
        {
            return false;
        }

        int index = SelectedScheme.Steps.IndexOf(SelectedSchemeStep);
        int newIndex = index + offset;
        return index >= 0 && newIndex >= 0 && newIndex < SelectedScheme.Steps.Count;
    }

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
        RaiseCommandState(MoveSchemeStepUpCommand);
        RaiseCommandState(MoveSchemeStepDownCommand);
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
