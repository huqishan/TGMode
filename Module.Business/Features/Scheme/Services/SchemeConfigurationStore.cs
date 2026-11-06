using Module.Business.Models;
using Module.Business.Features.Scheme.ViewModels.PresentationModels;
using Module.Business.Features.OperationEditing.ViewModels.PresentationModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Module.Business.Services;

/// <summary>
/// 方案配置持久化工具，负责方案配置的加载、保存与规范化。
/// </summary>
public static class SchemeConfigurationStore
{
    #region 配置目录与默认值
    /// <summary>
    /// 业务配置根目录。
    /// </summary>
    private static readonly string RootConfigDirectory =
        Path.Combine(AppContext.BaseDirectory, "Config");

    /// <summary>
    /// 方案配置文件目录。
    /// </summary>
    private static readonly string SchemeDirectory =
        Path.Combine(RootConfigDirectory, "Scheme");

    /// <summary>
    /// 方案配置文件搜索模式。
    /// </summary>
    private const string SchemeFileSearchPattern = "*.scheme.json";

    #endregion

    #region JSON 序列化选项

    /// <summary>
    /// 方案配置 JSON 序列化选项。
    /// </summary>
    private static readonly JsonSerializerOptions SchemeJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    #endregion

    #region 公开加载保存入口

    /// <summary>
    /// 加载完整方案配置目录。
    /// </summary>
    /// <returns>规范化后的方案配置目录。</returns>
    public static SchemeConfigurationCatalog LoadCatalog()
    {
        SchemeConfigurationCatalog catalog = new()
        {
            Schemes = LoadSchemes()
        };

        return NormalizeCatalog(catalog);
    }

    /// <summary>
    /// 保存完整方案配置目录。
    /// </summary>
    /// <param name="catalog">需要保存的方案配置目录。</param>
    public static void SaveCatalog(SchemeConfigurationCatalog catalog)
    {
        SchemeConfigurationCatalog normalized = NormalizeCatalog(catalog);

        SaveSchemes(normalized.Schemes);
    }

    #endregion

    #region 方案加载保存

    /// <summary>
    /// 从配置目录读取所有方案文件。
    /// </summary>
    /// <returns>读取到的方案集合。</returns>
    private static ObservableCollection<SchemeProfile> LoadSchemes()
    {
        ObservableCollection<SchemeProfile> schemes = new();
        foreach (string filePath in EnumerateConfigFiles(SchemeDirectory, SchemeFileSearchPattern))
        {
            SchemeProfile? scheme = ReadJson<SchemeProfile>(filePath, SchemeJsonOptions);
            if (scheme is not null)
            {
                schemes.Add(scheme);
            }
        }

        return schemes;
    }

    /// <summary>
    /// 将方案集合保存为独立配置文件。
    /// </summary>
    /// <param name="schemes">需要保存的方案集合。</param>
    private static void SaveSchemes(ObservableCollection<SchemeProfile> schemes)
    {
        Directory.CreateDirectory(SchemeDirectory);
        HashSet<string> currentFilePaths = new(StringComparer.OrdinalIgnoreCase);

        foreach (SchemeProfile scheme in schemes)
        {
            string filePath = BuildSchemeFilePath(scheme);
            WriteJson(filePath, scheme, SchemeJsonOptions);
            currentFilePaths.Add(filePath);
        }

        DeleteStaleFiles(SchemeDirectory, SchemeFileSearchPattern, currentFilePaths);
    }

    #endregion

    #region 目录级规范化
    /// <summary>
    /// 克隆并规范化方案配置目录。
    /// </summary>
    /// <param name="catalog">原始方案配置目录。</param>
    /// <returns>可安全使用的方案配置目录。</returns>
    private static SchemeConfigurationCatalog NormalizeCatalog(SchemeConfigurationCatalog? catalog)
    {
        SchemeConfigurationCatalog normalized = new()
        {
            Schemes = new ObservableCollection<SchemeProfile>(
                (catalog?.Schemes ?? new ObservableCollection<SchemeProfile>())
                    .Where(scheme => scheme is not null)
                    .Select(scheme => scheme.Clone()))
        };

        NormalizeSchemes(normalized.Schemes);
        return normalized;
    }

    #endregion

    #region 业务与操作规范化

    /// <summary>
    /// 规范化单个业务操作。
    /// </summary>
    /// <param name="operation">原始业务操作。</param>
    /// <returns>规范化后的业务操作。</returns>
    private static WorkStepOperation NormalizeOperation(WorkStepOperation operation)
    {
        string operationObject = ResolveOperationObject(operation);
        bool isLuaOperation = IsLuaOperationObject(operationObject);

        string pCommandName = isLuaOperation
            ? operation.PCommandName?.Trim() ?? string.Empty
            : string.IsNullOrWhiteSpace(operation.PCommandName)
                ? operationObject
                : operation.PCommandName.Trim();

        ObservableCollection<InputParameter> parameters = isLuaOperation
            ? new ObservableCollection<InputParameter>()
            : new ObservableCollection<InputParameter>(
                operation.Parameters
                    .Where(parameter => parameter is not null)
                    .Select((parameter, index) => NormalizeInputParameter(parameter, index))
                    .OrderBy(parameter => parameter.Num));

        ObservableCollection<ReturnValue> returnValues = isLuaOperation
            ? new ObservableCollection<ReturnValue>()
            : new ObservableCollection<ReturnValue>(
                operation.ReturnValues
                    .Where(rv => rv is not null)
                    .Select(NormalizeReturnValue));

        return new WorkStepOperation
        {
            Id = string.IsNullOrWhiteSpace(operation.Id) ? Guid.NewGuid().ToString("N") : operation.Id.Trim(),
            OperationObjectName = operationObject,
            PCommandName = pCommandName,
            ReturnValue = operation.ReturnValue?.Trim() ?? string.Empty,
            LuaScript = isLuaOperation ? operation.LuaScript ?? string.Empty : string.Empty,
            Summary = operation.Summary?.Trim() ?? string.Empty,
            DelayMilliseconds = Math.Max(0, operation.DelayMilliseconds),
            IsEditParameter = operation.IsEditParameter,
            Parameters = parameters,
            ReturnValues = returnValues,
            ConditionExecution = operation.ConditionExecution?.Clone() ?? new ConditionExecution()
        };
    }

    /// <summary>
    /// 根据旧版和新版字段解析操作对象。
    /// </summary>
    /// <param name="operation">需要解析的业务操作。</param>
    /// <returns>标准化操作对象名称。</returns>
    private static string ResolveOperationObject(WorkStepOperation operation)
    {
        if (IsLuaOperationObject(operation.OperationObjectName))
        {
            return "Lua";
        }

        if (IsNormalizedSystemOperationObject(operation.OperationObjectName))
        {
            return "System";
        }

        return string.IsNullOrWhiteSpace(operation.OperationObjectName)
            ? "System"
            : operation.OperationObjectName.Trim();
    }

    /// <summary>
    /// 规范化输入参数并补齐序号和名称。
    /// </summary>
    /// <param name="parameter">原始输入参数。</param>
    /// <param name="index">参数所在索引。</param>
    /// <returns>规范化后的输入参数。</returns>
    private static InputParameter NormalizeInputParameter(InputParameter parameter, int index)
    {
        return new InputParameter
        {
            Id = string.IsNullOrWhiteSpace(parameter.Id) ? Guid.NewGuid().ToString("N") : parameter.Id.Trim(),
            Num = parameter.Num <= 0 ? index + 1 : parameter.Num,
            ParameterName = string.IsNullOrWhiteSpace(parameter.ParameterName)
                ? parameter.ParameterType?.Trim() ?? string.Empty
                : parameter.ParameterName.Trim(),
            ParameterType = string.IsNullOrWhiteSpace(parameter.ParameterType) ? "设置值" : parameter.ParameterType.Trim(),
            Value = parameter.Value?.Trim() ?? string.Empty,
            Description = parameter.Description?.Trim() ?? string.Empty
        };
    }

    /// <summary>
    /// 规范化返回值并补齐标识。
    /// </summary>
    /// <param name="rv">原始返回值。</param>
    /// <returns>规范化后的返回值。</returns>
    private static ReturnValue NormalizeReturnValue(ReturnValue rv)
    {
        return new ReturnValue
        {
            Id = string.IsNullOrWhiteSpace(rv.Id) ? Guid.NewGuid().ToString("N") : rv.Id.Trim(),
            ReturnParameterName = rv.ReturnParameterName?.Trim() ?? string.Empty,
            IsShowView = rv.IsShowView,
            ViewDataName = rv.ViewDataName?.Trim() ?? string.Empty
        };
    }

    #endregion

    #region 方案内容规范化
    /// <summary>
    /// 规范化方案集合。
    /// </summary>
    /// <param name="schemes">需要规范化的方案集合。</param>
    private static void NormalizeSchemes(
        ObservableCollection<SchemeProfile> schemes)
    {
        HashSet<string> usedIds = new(StringComparer.Ordinal);
        HashSet<string> usedSchemeNames = new(StringComparer.OrdinalIgnoreCase);
        int index = 1;

        foreach (SchemeProfile scheme in schemes)
        {
            DateTime normalizedLastModifiedAt = scheme.LastModifiedAt == default ? DateTime.Now : scheme.LastModifiedAt;
            scheme.Id = EnsureUniqueId(scheme.Id, usedIds);
            scheme.SchemeName = BuildUniqueName(
                string.IsNullOrWhiteSpace(scheme.SchemeName) ? $"方案 {index}" : scheme.SchemeName.Trim(),
                usedSchemeNames);

            ObservableCollection<SchemeWorkStepItem> normalizedSteps = new();
            foreach (SchemeWorkStepItem step in scheme.Steps.Where(step => step is not null))
            {
                SchemeWorkStepItem normalizedStep = step.Clone();
                normalizedStep.Id = string.IsNullOrWhiteSpace(step.Id) ? Guid.NewGuid().ToString("N") : step.Id.Trim();

                if (string.IsNullOrWhiteSpace(normalizedStep.StepName))
                {
                    normalizedStep.StepName = $"业务 {normalizedSteps.Count + 1}";
                }

                normalizedSteps.Add(normalizedStep);
            }

            scheme.Steps = normalizedSteps;
            scheme.LastModifiedAt = normalizedLastModifiedAt;
            // 加载与规范化过程会经过属性 Setter，但不属于用户修改，完成后恢复为已保存状态。
            scheme.AcceptChanges();
            index++;
        }
    }

    #endregion

    #region 操作类型判断

    /// <summary>
    /// 判断操作对象是否表示系统操作。
    /// </summary>
    /// <param name="operationObject">操作对象文本。</param>
    /// <returns>是系统操作对象时返回 true。</returns>
    private static bool IsSystemOperationObject(string? operationObject)
    {
        return string.Equals(operationObject?.Trim(), "System", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(operationObject?.Trim(), "系统", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 判断操作对象是否表示已规范化的系统操作。
    /// </summary>
    /// <param name="operationObject">操作对象文本。</param>
    /// <returns>是系统操作对象时返回 true。</returns>
    private static bool IsNormalizedSystemOperationObject(string? operationObject)
    {
        return IsSystemOperationObject(operationObject);
    }

    /// <summary>
    /// 判断操作对象是否为 Lua 操作。
    /// </summary>
    /// <param name="operationObject">操作对象文本。</param>
    /// <returns>是 Lua 操作时返回 true。</returns>
    private static bool IsLuaOperationObject(string? operationObject)
    {
        return string.Equals(operationObject?.Trim(), "Lua", StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region 唯一性与名称工具

    /// <summary>
    /// 确保字符串标识在当前集合中唯一。
    /// </summary>
    /// <param name="id">候选标识。</param>
    /// <param name="usedIds">已使用标识集合。</param>
    /// <returns>唯一字符串标识。</returns>
    private static string EnsureUniqueId(string id, HashSet<string> usedIds)
    {
        string candidate = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id.Trim();
        while (!usedIds.Add(candidate))
        {
            candidate = Guid.NewGuid().ToString("N");
        }

        return candidate;
    }

    /// <summary>
    /// 基于名称生成不重复的显示名称。
    /// </summary>
    /// <param name="name">候选名称。</param>
    /// <param name="usedNames">已使用名称集合。</param>
    /// <returns>唯一显示名称。</returns>
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

    #endregion

    #region 文件与数值工具
    /// <summary>
    /// 枚举指定目录下的配置文件。
    /// </summary>
    /// <param name="directory">配置目录。</param>
    /// <param name="searchPattern">文件搜索模式。</param>
    /// <returns>按文件名排序后的配置文件路径。</returns>
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

    /// <summary>
    /// 从指定文件反序列化 JSON。
    /// </summary>
    /// <typeparam name="T">目标对象类型。</typeparam>
    /// <param name="filePath">JSON 文件路径。</param>
    /// <param name="options">JSON 序列化选项。</param>
    /// <returns>读取到的对象，失败时返回默认值。</returns>
    private static T? ReadJson<T>(string filePath, JsonSerializerOptions options)
    {
        try
        {
            string json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<T>(json, options);
        }
        catch
        {
            return default;
        }
    }

    /// <summary>
    /// 将对象序列化并写入 JSON 文件。
    /// </summary>
    /// <typeparam name="T">待写入对象类型。</typeparam>
    /// <param name="filePath">JSON 文件路径。</param>
    /// <param name="value">待写入对象。</param>
    /// <param name="options">JSON 序列化选项。</param>
    private static void WriteJson<T>(string filePath, T value, JsonSerializerOptions options)
    {
        string json = JsonSerializer.Serialize(value, options);
        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// 删除本次保存未生成的历史配置文件。
    /// </summary>
    /// <param name="directory">配置目录。</param>
    /// <param name="searchPattern">文件搜索模式。</param>
    /// <param name="currentFilePaths">本次保存生成的文件路径集合。</param>
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

    /// <summary>
    /// 构建方案配置文件路径。
    /// </summary>
    /// <param name="scheme">方案配置。</param>
    /// <returns>方案配置文件路径。</returns>
    private static string BuildSchemeFilePath(SchemeProfile scheme)
    {
        return Path.Combine(SchemeDirectory, $"{SanitizeFileName(scheme.SchemeName)}_{SanitizeFileName(scheme.Id)}.scheme.json");
    }

    /// <summary>
    /// 清理文件名中的非法字符。
    /// </summary>
    /// <param name="fileName">原始文件名。</param>
    /// <returns>可用于文件系统的安全文件名。</returns>
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
