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
/// 业务配置存储服务，按工步、方案分目录保存 JSON 文件。
/// </summary>
public static class BusinessConfigurationStore
{
    private static readonly string ConfigDirectory =
        Path.Combine(AppContext.BaseDirectory, "Config", "Business");

    private static readonly string WorkStepDirectory =
        Path.Combine(ConfigDirectory, "WorkStep");

    private static readonly string SchemeDirectory =
        Path.Combine(ConfigDirectory, "Scheme");

    private const string WorkStepFileSearchPattern = "*.workstep.json";
    private const string SchemeFileSearchPattern = "*.scheme.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static BusinessConfigurationCatalog LoadCatalog()
    {
        BusinessConfigurationCatalog catalog = new()
        {
            WorkSteps = LoadWorkSteps(),
            Schemes = LoadSchemes()
        };

        return NormalizeCatalog(catalog);
    }

    public static SchemeProfile? LoadSchemeByName(string schemeName)
    {
        if (string.IsNullOrWhiteSpace(schemeName))
        {
            return null;
        }

        string normalizedSchemeName = schemeName.Trim();
        return LoadCatalog().Schemes
            .FirstOrDefault(scheme => string.Equals(
                scheme.SchemeName?.Trim(),
                normalizedSchemeName,
                StringComparison.OrdinalIgnoreCase))
            ?.Clone();
    }

    public static void SaveCatalog(BusinessConfigurationCatalog catalog)
    {
        BusinessConfigurationCatalog normalized = NormalizeCatalog(catalog);

        SaveWorkSteps(normalized.WorkSteps);
        SaveSchemes(normalized.Schemes);
    }

    private static ObservableCollection<WorkStepProfile> LoadWorkSteps()
    {
        ObservableCollection<WorkStepProfile> workSteps = new();
        foreach (string filePath in EnumerateConfigFiles(WorkStepDirectory, WorkStepFileSearchPattern))
        {
            WorkStepProfile? workStep = ReadJson<WorkStepProfile>(filePath);
            if (workStep is not null)
            {
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

    private static void SaveWorkSteps(ObservableCollection<WorkStepProfile> workSteps)
    {
        Directory.CreateDirectory(WorkStepDirectory);
        HashSet<string> currentFilePaths = new(StringComparer.OrdinalIgnoreCase);

        foreach (WorkStepProfile workStep in workSteps)
        {
            string filePath = BuildWorkStepFilePath(workStep);
            WriteJson(filePath, workStep);
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

    private static BusinessConfigurationCatalog NormalizeCatalog(BusinessConfigurationCatalog? catalog)
    {
        BusinessConfigurationCatalog normalized = new()
        {
            WorkSteps = new ObservableCollection<WorkStepProfile>(
                (catalog?.WorkSteps ?? new ObservableCollection<WorkStepProfile>())
                    .Where(step => step is not null)
                    .Select(step => step.Clone())),
            Schemes = new ObservableCollection<SchemeProfile>(
                (catalog?.Schemes ?? new ObservableCollection<SchemeProfile>())
                    .Where(scheme => scheme is not null)
                    .Select(scheme => scheme.Clone()))
        };

        NormalizeWorkSteps(normalized.WorkSteps);
        NormalizeSchemes(normalized.Schemes, normalized.WorkSteps);
        return normalized;
    }

    private static void NormalizeWorkSteps(ObservableCollection<WorkStepProfile> workSteps)
    {
        HashSet<string> usedIds = new(StringComparer.Ordinal);
        HashSet<string> usedStepNames = new(StringComparer.OrdinalIgnoreCase);
        int index = 1;

        foreach (WorkStepProfile workStep in workSteps)
        {
            workStep.Id = EnsureUniqueId(workStep.Id, usedIds);
            string fallbackStepName = $"工步 {index}";
            workStep.StepName = BuildUniqueName(
                string.IsNullOrWhiteSpace(workStep.StepName) ? fallbackStepName : workStep.StepName.Trim(),
                usedStepNames);
            workStep.LastModifiedAt = workStep.LastModifiedAt == default ? DateTime.Now : workStep.LastModifiedAt;
            workStep.Steps = new ObservableCollection<WorkStepOperation>(
                workStep.Steps
                    .Where(operation => operation is not null)
                    .Select(NormalizeOperation));
            index++;
        }
    }

    private static WorkStepOperation NormalizeOperation(WorkStepOperation operation)
    {
        string operationObject = ResolveOperationObject(operation);
        bool isLuaOperation = IsLuaOperationObject(operationObject);
        bool isJudgeOperation = !isLuaOperation && IsJudgeOperationObject(operationObject);
        bool isSystemOperation = !isLuaOperation && !isJudgeOperation && IsNormalizedSystemOperationObject(operationObject);
        string protocolName = isSystemOperation || isJudgeOperation || isLuaOperation
            ? string.Empty
            : operation.ProtocolName?.Trim() ?? string.Empty;
        string commandName = isSystemOperation || isJudgeOperation || isLuaOperation
            ? string.Empty
            : (string.IsNullOrWhiteSpace(operation.CommandName)
                ? operation.InvokeMethod?.Trim() ?? string.Empty
                : operation.CommandName.Trim());
        string invokeMethod = isLuaOperation
            ? "Lua"
            : isJudgeOperation
                ? operation.InvokeMethod?.Trim() ?? string.Empty
                : isSystemOperation
                    ? (string.IsNullOrWhiteSpace(operation.InvokeMethod) ? "等待" : operation.InvokeMethod.Trim())
                    : (string.IsNullOrWhiteSpace(commandName) ? "指令" : commandName);
        ObservableCollection<WorkStepOperationParameter> parameters = isLuaOperation
            ? new ObservableCollection<WorkStepOperationParameter>()
            : new ObservableCollection<WorkStepOperationParameter>(
                operation.Parameters
                    .Where(parameter => parameter is not null)
                    .Select((parameter, index) => NormalizeOperationParameter(parameter, index))
                    .OrderBy(parameter => parameter.Sequence));

        return new WorkStepOperation
        {
            Id = string.IsNullOrWhiteSpace(operation.Id) ? Guid.NewGuid().ToString("N") : operation.Id.Trim(),
            OperationType = isLuaOperation ? "Lua" : isJudgeOperation ? "判断" : isSystemOperation ? "系统" : "设备",
            OperationObject = operationObject,
            ProtocolName = protocolName,
            CommandName = commandName,
            InvokeMethod = invokeMethod,
            ReturnValue = isLuaOperation ? string.Empty : operation.ReturnValue?.Trim() ?? string.Empty,
            ShowDataToView = !isLuaOperation && operation.ShowDataToView,
            ViewDataName = isLuaOperation ? string.Empty : operation.ViewDataName?.Trim() ?? string.Empty,
            ViewJudgeType = isLuaOperation ? string.Empty : operation.ViewJudgeType?.Trim() ?? string.Empty,
            ViewJudgeCondition = isLuaOperation ? string.Empty : operation.ViewJudgeCondition?.Trim() ?? string.Empty,
            LuaScript = isLuaOperation ? operation.LuaScript ?? string.Empty : string.Empty,
            DelayMilliseconds = Math.Max(0, operation.DelayMilliseconds),
            Remark = operation.Remark?.Trim() ?? string.Empty,
            Parameters = parameters
        };
    }

    private static string ResolveOperationObject(WorkStepOperation operation)
    {
        if (IsLuaOperationObject(operation.OperationType) ||
            IsLuaOperationObject(operation.OperationObject))
        {
            return "Lua";
        }

        if (IsJudgeOperationObject(operation.OperationType) ||
            IsJudgeOperationObject(operation.OperationObject))
        {
            return "判断";
        }

        if (IsNormalizedSystemOperationType(operation.OperationType) ||
            IsNormalizedSystemOperationObject(operation.OperationObject))
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
            ParameterName = string.IsNullOrWhiteSpace(parameter.ParameterName)
                ? parameter.Description?.Trim() ?? string.Empty
                : parameter.ParameterName.Trim(),
            Value = parameter.Value?.Trim() ?? string.Empty,
            Remark = parameter.Remark?.Trim() ?? string.Empty
        };
    }

    private static void NormalizeSchemes(
        ObservableCollection<SchemeProfile> schemes,
        ObservableCollection<WorkStepProfile> workSteps)
    {
        HashSet<string> usedIds = new(StringComparer.Ordinal);
        HashSet<string> usedSchemeNames = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, WorkStepProfile> workStepById = workSteps.ToDictionary(step => step.Id, StringComparer.Ordinal);
        int index = 1;

        foreach (SchemeProfile scheme in schemes)
        {
            scheme.Id = EnsureUniqueId(scheme.Id, usedIds);
            scheme.SchemeName = BuildUniqueName(
                string.IsNullOrWhiteSpace(scheme.SchemeName) ? $"方案 {index}" : scheme.SchemeName.Trim(),
                usedSchemeNames);

            ObservableCollection<SchemeWorkStepItem> normalizedSteps = new();
            foreach (SchemeWorkStepItem step in scheme.Steps.Where(step => step is not null))
            {
                if (!workStepById.TryGetValue(step.WorkStepId, out WorkStepProfile? workStep))
                {
                    continue;
                }

                SchemeWorkStepItem normalizedStep = step.Clone();
                normalizedStep.Id = string.IsNullOrWhiteSpace(step.Id) ? Guid.NewGuid().ToString("N") : step.Id.Trim();
                normalizedStep.WorkStepId = workStep.Id;
                normalizedStep.StepName = workStep.StepName;
                normalizedStep.OperationSummary = workStep.OperationSummary;
                normalizedStep.Parameters = SchemeWorkStepItem.CreateParametersFromWorkStep(workStep, step.Parameters);
                normalizedSteps.Add(normalizedStep);
            }

            scheme.Steps = normalizedSteps;
            index++;
        }
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

    private static bool IsNormalizedSystemOperationType(string? operationType)
    {
        return string.Equals(operationType?.Trim(), "System", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(operationType?.Trim(), "系统", StringComparison.OrdinalIgnoreCase) ||
               IsLegacySystemOperationType(operationType);
    }

    private static bool IsNormalizedSystemOperationObject(string? operationObject)
    {
        return string.Equals(operationObject?.Trim(), "System", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(operationObject?.Trim(), "系统", StringComparison.OrdinalIgnoreCase) ||
               IsSystemOperationObject(operationObject);
    }

    private static bool IsJudgeOperationObject(string? operationObject)
    {
        return string.Equals(operationObject?.Trim(), "判断", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLuaOperationObject(string? operationObject)
    {
        return string.Equals(operationObject?.Trim(), "Lua", StringComparison.OrdinalIgnoreCase);
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

    private static string BuildWorkStepFilePath(WorkStepProfile workStep)
    {
        return Path.Combine(
            WorkStepDirectory,
            $"{SanitizeFileName(workStep.StepName)}_{SanitizeFileName(workStep.Id)}.workstep.json");
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
}
