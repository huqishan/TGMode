using Module.Business.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Module.Business.Services;

/// <summary>
/// 业务配置存储服务，按产品、工步、方案分目录保存 JSON 文件。
/// </summary>
public static class BusinessConfigurationStore
{
    #region 配置路径与序列化字段

    private static readonly string ConfigDirectory =
        Path.Combine(AppContext.BaseDirectory, "Config", "Business");

    private static readonly string ProductDirectory =
        Path.Combine(ConfigDirectory, "Product");

    private static readonly string WorkStepDirectory =
        Path.Combine(ConfigDirectory, "WorkStep");

    private static readonly string SchemeDirectory =
        Path.Combine(ConfigDirectory, "Scheme");

    private const string ProductFileSearchPattern = "*.product.json";
    private const string WorkStepFileSearchPattern = "*.workstep.json";
    private const string SchemeFileSearchPattern = "*.scheme.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    #endregion

    #region 配置读写

    /// <summary>
    /// 加载业务配置，只读取 Config/Business 下的新分目录配置文件。
    /// </summary>
    public static BusinessConfigurationCatalog LoadCatalog()
    {
        BusinessConfigurationCatalog catalog = new()
        {
            Products = LoadProducts(),
            WorkSteps = LoadWorkSteps(),
            Schemes = LoadSchemes()
        };

        return NormalizeCatalog(catalog);
    }

    /// <summary>
    /// 保存业务配置，产品、方案各一个文件，工步按产品各一个文件。
    /// </summary>
    public static void SaveCatalog(BusinessConfigurationCatalog catalog)
    {
        BusinessConfigurationCatalog normalized = NormalizeCatalog(catalog);

        SaveProducts(normalized.Products);
        SaveWorkSteps(normalized.Products, normalized.WorkSteps);
        SaveSchemes(normalized.Schemes);
    }

    #endregion

    #region 分目录读取

    private static ObservableCollection<ProductProfile> LoadProducts()
    {
        ObservableCollection<ProductProfile> products = new();
        foreach (string filePath in EnumerateConfigFiles(ProductDirectory, ProductFileSearchPattern))
        {
            ProductProfile? product = ReadJson<ProductProfile>(filePath);
            if (product is not null)
            {
                products.Add(product);
            }
        }

        return products;
    }

    private static ObservableCollection<WorkStepProfile> LoadWorkSteps()
    {
        ObservableCollection<WorkStepProfile> workSteps = new();
        foreach (string filePath in EnumerateConfigFiles(WorkStepDirectory, WorkStepFileSearchPattern))
        {
            ProductWorkStepConfiguration? productWorkSteps = ReadJson<ProductWorkStepConfiguration>(filePath);
            if (productWorkSteps is null)
            {
                continue;
            }

            foreach (WorkStepProfile workStep in productWorkSteps.WorkSteps.Where(workStep => workStep is not null))
            {
                if (string.IsNullOrWhiteSpace(workStep.ProductName))
                {
                    workStep.ProductName = productWorkSteps.ProductName;
                }

                workSteps.Add(workStep);
            }
        }

        return workSteps;
    }

    private static ObservableCollection<SchemeProfile> LoadSchemes()
    {
        ObservableCollection<SchemeProfile> schemes = new();
        foreach (string filePath in EnumerateConfigFiles(SchemeDirectory, SchemeFileSearchPattern))
        {
            SchemeProfile? scheme = ReadJson<SchemeProfile>(filePath);
            if (scheme is not null)
            {
                schemes.Add(scheme);
            }
        }

        return schemes;
    }

    #endregion

    #region 分目录保存

    private static void SaveProducts(ObservableCollection<ProductProfile> products)
    {
        Directory.CreateDirectory(ProductDirectory);
        HashSet<string> currentFilePaths = new(StringComparer.OrdinalIgnoreCase);

        foreach (ProductProfile product in products)
        {
            string filePath = BuildProductFilePath(product);
            WriteJson(filePath, product);
            currentFilePaths.Add(filePath);
        }

        DeleteStaleFiles(ProductDirectory, ProductFileSearchPattern, currentFilePaths);
    }

    private static void SaveWorkSteps(
        ObservableCollection<ProductProfile> products,
        ObservableCollection<WorkStepProfile> workSteps)
    {
        Directory.CreateDirectory(WorkStepDirectory);
        HashSet<string> currentFilePaths = new(StringComparer.OrdinalIgnoreCase);

        IEnumerable<string> productNames = products
            .Select(product => product.ProductName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);

        foreach (string productName in productNames)
        {
            ProductWorkStepConfiguration productWorkSteps = new()
            {
                ProductName = productName,
                WorkSteps = new ObservableCollection<WorkStepProfile>(
                    workSteps
                        .Where(workStep => string.Equals(workStep.ProductName, productName, StringComparison.OrdinalIgnoreCase))
                        .Select(workStep => workStep.Clone()))
            };

            string filePath = BuildWorkStepFilePath(productName);
            WriteJson(filePath, productWorkSteps);
            currentFilePaths.Add(filePath);
        }

        DeleteStaleFiles(WorkStepDirectory, WorkStepFileSearchPattern, currentFilePaths);
    }

    private static void SaveSchemes(ObservableCollection<SchemeProfile> schemes)
    {
        Directory.CreateDirectory(SchemeDirectory);
        HashSet<string> currentFilePaths = new(StringComparer.OrdinalIgnoreCase);

        foreach (SchemeProfile scheme in schemes)
        {
            string filePath = BuildSchemeFilePath(scheme);
            WriteJson(filePath, scheme);
            currentFilePaths.Add(filePath);
        }

        DeleteStaleFiles(SchemeDirectory, SchemeFileSearchPattern, currentFilePaths);
    }

    #endregion

    #region 规范化方法

    private static BusinessConfigurationCatalog NormalizeCatalog(BusinessConfigurationCatalog? catalog)
    {
        BusinessConfigurationCatalog normalized = new()
        {
            Products = new ObservableCollection<ProductProfile>(
                (catalog?.Products ?? new ObservableCollection<ProductProfile>())
                    .Where(product => product is not null)
                    .Select(product => product.Clone())),
            WorkSteps = new ObservableCollection<WorkStepProfile>(
                (catalog?.WorkSteps ?? new ObservableCollection<WorkStepProfile>())
                    .Where(step => step is not null)
                    .Select(step => step.Clone())),
            Schemes = new ObservableCollection<SchemeProfile>(
                (catalog?.Schemes ?? new ObservableCollection<SchemeProfile>())
                    .Where(scheme => scheme is not null)
                    .Select(scheme => scheme.Clone()))
        };

        NormalizeProducts(normalized.Products);
        NormalizeWorkSteps(normalized.WorkSteps, normalized.Products);
        NormalizeSchemes(normalized.Schemes, normalized.WorkSteps, normalized.Products);
        return normalized;
    }

    private static void NormalizeProducts(ObservableCollection<ProductProfile> products)
    {
        HashSet<string> usedIds = new(StringComparer.Ordinal);
        HashSet<string> usedProductNames = new(StringComparer.OrdinalIgnoreCase);
        int index = 1;

        foreach (ProductProfile product in products)
        {
            product.Id = EnsureUniqueId(product.Id, usedIds);
            product.ProductName = BuildUniqueName(
                string.IsNullOrWhiteSpace(product.ProductName) ? $"产品 {index}" : product.ProductName.Trim(),
                usedProductNames);
            product.LastModifiedAt = product.LastModifiedAt == default ? DateTime.Now : product.LastModifiedAt;

            ObservableCollection<ProductKeyValueItem> normalizedKeyValues = new();
            HashSet<string> usedKeys = new(StringComparer.OrdinalIgnoreCase);
            int keyIndex = 1;

            foreach (ProductKeyValueItem item in product.KeyValues.Where(item => item is not null))
            {
                normalizedKeyValues.Add(new ProductKeyValueItem
                {
                    Id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id.Trim(),
                    Key = BuildUniqueName(
                        string.IsNullOrWhiteSpace(item.Key) ? $"参数 {keyIndex}" : item.Key.Trim(),
                        usedKeys),
                    Value = item.Value?.Trim() ?? string.Empty
                });
                keyIndex++;
            }

            product.KeyValues = normalizedKeyValues;
            index++;
        }
    }

    private static void NormalizeWorkSteps(ObservableCollection<WorkStepProfile> workSteps, ObservableCollection<ProductProfile> products)
    {
        HashSet<string> usedIds = new(StringComparer.Ordinal);
        HashSet<string> usedProductStepNames = new(StringComparer.OrdinalIgnoreCase);
        int index = 1;

        foreach (WorkStepProfile workStep in workSteps)
        {
            workStep.Id = EnsureUniqueId(workStep.Id, usedIds);
            workStep.ProductName = string.IsNullOrWhiteSpace(workStep.ProductName)
                ? "默认产品"
                : workStep.ProductName.Trim();

            string fallbackStepName = $"工步 {index}";
            workStep.StepName = BuildUniqueName(
                string.IsNullOrWhiteSpace(workStep.StepName) ? fallbackStepName : workStep.StepName.Trim(),
                workStep.ProductName,
                usedProductStepNames);

            ObservableCollection<WorkStepOperation> normalizedOperations = new(
                workStep.Steps
                    .Where(operation => operation is not null)
                    .Select(NormalizeOperation));

            workStep.Steps = normalizedOperations;
            index++;
        }
    }

    private static WorkStepOperation NormalizeOperation(WorkStepOperation operation)
    {
        string operationObject = ResolveOperationObject(operation);
        bool isSystemOperation = IsSystemOperationObject(operationObject);
        string protocolName = isSystemOperation
            ? string.Empty
            : operation.ProtocolName?.Trim() ?? string.Empty;
        string commandName = isSystemOperation
            ? string.Empty
            : (string.IsNullOrWhiteSpace(operation.CommandName)
                ? operation.InvokeMethod?.Trim() ?? string.Empty
                : operation.CommandName.Trim());
        string invokeMethod = isSystemOperation
            ? (string.IsNullOrWhiteSpace(operation.InvokeMethod) ? "等待" : operation.InvokeMethod.Trim())
            : (string.IsNullOrWhiteSpace(commandName) ? "指令" : commandName);

        return new WorkStepOperation
        {
            Id = string.IsNullOrWhiteSpace(operation.Id) ? Guid.NewGuid().ToString("N") : operation.Id.Trim(),
            OperationType = isSystemOperation ? "系统" : "设备",
            OperationObject = operationObject,
            ProtocolName = protocolName,
            CommandName = commandName,
            InvokeMethod = invokeMethod,
            ReturnValue = operation.ReturnValue?.Trim() ?? string.Empty,
            DelayMilliseconds = Math.Max(0, operation.DelayMilliseconds),
            Remark = operation.Remark?.Trim() ?? string.Empty,
            Parameters = new ObservableCollection<WorkStepOperationParameter>(
                operation.Parameters
                    .Where(parameter => parameter is not null)
                    .Select((parameter, index) => NormalizeOperationParameter(parameter, index))
                    .OrderBy(parameter => parameter.Sequence))
        };
    }

    private static string ResolveOperationObject(WorkStepOperation operation)
    {
        if (IsLegacySystemOperationType(operation.OperationType) ||
            IsSystemOperationObject(operation.OperationObject))
        {
            return "System";
        }

        return string.IsNullOrWhiteSpace(operation.OperationObject)
            ? "System"
            : operation.OperationObject.Trim();
    }

    private static WorkStepOperationParameter NormalizeOperationParameter(WorkStepOperationParameter parameter, int index)
    {
        return new WorkStepOperationParameter
        {
            Id = string.IsNullOrWhiteSpace(parameter.Id) ? Guid.NewGuid().ToString("N") : parameter.Id.Trim(),
            Sequence = parameter.Sequence <= 0 ? index + 1 : parameter.Sequence,
            Name = string.IsNullOrWhiteSpace(parameter.Name) ? "设置值" : parameter.Name.Trim(),
            Value = parameter.Value?.Trim() ?? string.Empty,
            Remark = parameter.Remark?.Trim() ?? string.Empty
        };
    }

    private static bool IsLegacySystemOperationType(string? operationType)
    {
        return string.Equals(operationType?.Trim(), "系统", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSystemOperationObject(string? operationObject)
    {
        return string.Equals(operationObject?.Trim(), "System", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(operationObject?.Trim(), "系统", StringComparison.OrdinalIgnoreCase);
    }

    private static void NormalizeSchemes(
        ObservableCollection<SchemeProfile> schemes,
        ObservableCollection<WorkStepProfile> workSteps,
        ObservableCollection<ProductProfile> products)
    {
        HashSet<string> usedIds = new(StringComparer.Ordinal);
        HashSet<string> usedSchemeNames = new(StringComparer.OrdinalIgnoreCase);
        _ = products;
        Dictionary<string, WorkStepProfile> workStepById = workSteps.ToDictionary(step => step.Id, StringComparer.Ordinal);
        string fallbackProductName = workSteps.FirstOrDefault()?.ProductName ?? "默认产品";
        int index = 1;

        foreach (SchemeProfile scheme in schemes)
        {
            scheme.Id = EnsureUniqueId(scheme.Id, usedIds);
            scheme.SchemeName = BuildUniqueName(
                string.IsNullOrWhiteSpace(scheme.SchemeName) ? $"方案 {index}" : scheme.SchemeName.Trim(),
                usedSchemeNames);
            scheme.ProductName = string.IsNullOrWhiteSpace(scheme.ProductName)
                ? fallbackProductName
                : scheme.ProductName.Trim();

            ObservableCollection<SchemeWorkStepItem> normalizedSteps = new();
            foreach (SchemeWorkStepItem step in scheme.Steps.Where(step => step is not null))
            {
                if (!workStepById.TryGetValue(step.WorkStepId, out WorkStepProfile? workStep))
                {
                    continue;
                }

                if (!string.Equals(workStep.ProductName, scheme.ProductName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                SchemeWorkStepItem normalizedStep = SchemeWorkStepItem.FromWorkStep(workStep);
                normalizedStep.Id = string.IsNullOrWhiteSpace(step.Id) ? Guid.NewGuid().ToString("N") : step.Id.Trim();
                normalizedSteps.Add(normalizedStep);
            }

            scheme.Steps = normalizedSteps;
            index++;
        }
    }

    private static string EnsureUniqueId(string id, HashSet<string> usedIds)
    {
        string candidate = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id.Trim();
        while (!usedIds.Add(candidate))
        {
            candidate = Guid.NewGuid().ToString("N");
        }

        return candidate;
    }

    private static string BuildUniqueName(string name, HashSet<string> usedNames)
    {
        string baseName = string.IsNullOrWhiteSpace(name) ? "名称" : name.Trim();
        string candidate = baseName;
        int index = 2;

        while (!usedNames.Add(candidate))
        {
            candidate = $"{baseName} {index}";
            index++;
        }

        return candidate;
    }

    private static string BuildUniqueName(string name, string productName, HashSet<string> usedProductStepNames)
    {
        string baseName = string.IsNullOrWhiteSpace(name) ? "工步" : name.Trim();
        string candidate = baseName;
        int index = 2;

        while (!usedProductStepNames.Add($"{productName.Trim()}::{candidate}"))
        {
            candidate = $"{baseName} {index}";
            index++;
        }

        return candidate;
    }

    #endregion

    #region 文件工具

    private static IEnumerable<string> EnumerateConfigFiles(string directory, string searchPattern)
    {
        if (!Directory.Exists(directory))
        {
            return Enumerable.Empty<string>();
        }

        return Directory
            .EnumerateFiles(directory, searchPattern, SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
    }

    private static T? ReadJson<T>(string filePath)
    {
        try
        {
            string json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch
        {
            return default;
        }
    }

    private static void WriteJson<T>(string filePath, T value)
    {
        string json = JsonSerializer.Serialize(value, JsonOptions);
        File.WriteAllText(filePath, json);
    }

    private static void DeleteStaleFiles(string directory, string searchPattern, HashSet<string> currentFilePaths)
    {
        foreach (string filePath in Directory.EnumerateFiles(directory, searchPattern, SearchOption.TopDirectoryOnly))
        {
            if (!currentFilePaths.Contains(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    private static string BuildProductFilePath(ProductProfile product)
    {
        return Path.Combine(ProductDirectory, $"{SanitizeFileName(product.ProductName)}_{SanitizeFileName(product.Id)}.product.json");
    }

    private static string BuildWorkStepFilePath(string productName)
    {
        return Path.Combine(WorkStepDirectory, $"{SanitizeFileName(productName)}.workstep.json");
    }

    private static string BuildSchemeFilePath(SchemeProfile scheme)
    {
        return Path.Combine(SchemeDirectory, $"{SanitizeFileName(scheme.SchemeName)}_{SanitizeFileName(scheme.Id)}.scheme.json");
    }

    private static string SanitizeFileName(string fileName)
    {
        string safeName = string.IsNullOrWhiteSpace(fileName) ? "config" : fileName.Trim();
        foreach (char invalidChar in Path.GetInvalidFileNameChars())
        {
            safeName = safeName.Replace(invalidChar, '_');
        }

        return safeName;
    }

    #endregion
}
