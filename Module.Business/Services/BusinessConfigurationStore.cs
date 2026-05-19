using ControlLibrary.Controls.FlowchartEditor.Models;
using Module.Business.Models;
using Module.Business.ViewModels;
using Module.Business.ViewModels.PropertyVMs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Module.Business.Services;

/// <summary>
/// 业务配置存储服务，按工步、方案分目录保存 JSON 文件。
/// </summary>
public static class BusinessConfigurationStore
{
    private static readonly string RootConfigDirectory =
        Path.Combine(AppContext.BaseDirectory, "Config");

    private static readonly string SchemeDirectory =
        Path.Combine(RootConfigDirectory, "Scheme");

    private static readonly string StationDirectory =
        Path.Combine(RootConfigDirectory, "Station");

    private const string SchemeFileSearchPattern = "*.scheme.json";
    private const string StationFileSearchPattern = "*.station.json";
    private const double DefaultNodeWidth = 150;
    private const double DefaultNodeHeight = 70;

    private static readonly JsonSerializerOptions SchemeJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static SchemeConfigurationCatalog LoadCatalog()
    {
        SchemeConfigurationCatalog catalog = new()
        {
            Schemes = LoadSchemes()
        };

        return NormalizeCatalog(catalog);
    }

    public static void SaveCatalog(SchemeConfigurationCatalog catalog)
    {
        SchemeConfigurationCatalog normalized = NormalizeCatalog(catalog);

        SaveSchemes(normalized.Schemes);
    }

    public static StationConfigurationCatalog LoadStationCatalog()
    {
        StationConfigurationCatalog catalog = new()
        {
            Stations = LoadStations()
        };

        return NormalizeStationCatalog(catalog);
    }

    public static void SaveStationCatalog(StationConfigurationCatalog catalog)
    {
        StationConfigurationCatalog normalized = NormalizeStationCatalog(catalog);
        SaveStations(normalized.Stations);
    }

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

    private static ObservableCollection<StationProfile> LoadStations()
    {
        ObservableCollection<StationProfile> stations = new();
        foreach (string filePath in EnumerateConfigFiles(StationDirectory, StationFileSearchPattern))
        {
            StationProfile? station = ReadJson<StationProfile>(filePath, SchemeJsonOptions);
            if (station is not null)
            {
                stations.Add(station);
            }
        }

        return stations;
    }

    private static void SaveStations(ObservableCollection<StationProfile> stations)
    {
        Directory.CreateDirectory(StationDirectory);
        HashSet<string> currentFilePaths = new(StringComparer.OrdinalIgnoreCase);

        foreach (StationProfile station in stations)
        {
            string filePath = BuildStationFilePath(station);
            WriteJson(filePath, station, SchemeJsonOptions);
            currentFilePaths.Add(filePath);
        }

        DeleteStaleFiles(StationDirectory, StationFileSearchPattern, currentFilePaths);
    }

    private static SchemeConfigurationCatalog NormalizeCatalog(SchemeConfigurationCatalog? catalog)
    {
        SchemeConfigurationCatalog normalized = new()
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

    private static StationConfigurationCatalog NormalizeStationCatalog(StationConfigurationCatalog? catalog)
    {
        StationConfigurationCatalog normalized = new()
        {
            Stations = new ObservableCollection<StationProfile>(
                (catalog?.Stations ?? new ObservableCollection<StationProfile>())
                    .Where(station => station is not null)
                    .Select(station => station.Clone()))
        };

        NormalizeStations(normalized.Stations);
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
                WorkStepProfile? workStep = string.IsNullOrWhiteSpace(step.WorkStepId)
                    ? null
                    : workStepById.TryGetValue(step.WorkStepId, out WorkStepProfile? currentWorkStep)
                        ? currentWorkStep
                        : null;

                if (normalizedStep.Operations.Count == 0 && workStep is not null)
                {
                    normalizedStep.WorkStepId = workStep.Id;
                    normalizedStep.StepName = string.IsNullOrWhiteSpace(normalizedStep.StepName)
                        ? workStep.StepName
                        : normalizedStep.StepName;
                    normalizedStep.Operations = new ObservableCollection<WorkStepOperation>(
                        workStep.Steps.Select(operation => operation.Clone()));
                }

                if (string.IsNullOrWhiteSpace(normalizedStep.StepName))
                {
                    normalizedStep.StepName = $"工步 {normalizedSteps.Count + 1}";
                }

                normalizedStep.Parameters = SchemeWorkStepItem.CreateParametersFromOperations(
                    normalizedStep.Operations,
                    step.Parameters);
                normalizedSteps.Add(normalizedStep);
            }

            scheme.Steps = normalizedSteps;
            scheme.LastModifiedAt = normalizedLastModifiedAt;
            index++;
        }
    }

    private static void NormalizeFlowcharts(ObservableCollection<FlowchartProfile> flowcharts)
    {
        HashSet<string> usedIds = new(StringComparer.Ordinal);
        HashSet<string> usedNames = new(StringComparer.OrdinalIgnoreCase);
        int index = 1;

        foreach (FlowchartProfile flowchart in flowcharts)
        {
            flowchart.Id = EnsureUniqueId(flowchart.Id, usedIds);
            flowchart.Name = BuildUniqueName(
                string.IsNullOrWhiteSpace(flowchart.Name) ? $"流程图{index}" : flowchart.Name.Trim(),
                usedNames);
            flowchart.Document = NormalizeFlowchartDocument(flowchart.Document);
            index++;
        }
    }

    private static void NormalizeStations(ObservableCollection<StationProfile> stations)
    {
        HashSet<string> usedIds = new(StringComparer.Ordinal);
        HashSet<string> usedNames = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> usedCodes = new(StringComparer.OrdinalIgnoreCase);
        int index = 1;

        foreach (StationProfile station in stations)
        {
            station.Id = EnsureUniqueId(station.Id, usedIds);
            station.StationName = BuildUniqueName(
                string.IsNullOrWhiteSpace(station.StationName) ? $"工位 {index}" : station.StationName.Trim(),
                usedNames);
            station.StationCode = BuildUniqueStationCode(station.StationCode, usedCodes, index);
            station.LastModifiedAt = station.LastModifiedAt == default ? DateTime.Now : station.LastModifiedAt;
            station.FlowchartDocument = NormalizeFlowchartDocument(station.FlowchartDocument);
            index++;
        }
    }

    private static FlowchartDocument NormalizeFlowchartDocument(FlowchartDocument? document)
    {
        if (document is null)
        {
            return new FlowchartDocument();
        }

        HashSet<Guid> usedNodeIds = new();
        Dictionary<Guid, Guid> nodeIdMap = new();
        List<FlowchartNodeDocument> nodes = new();

        foreach (FlowchartNodeDocument node in document.Nodes ?? new List<FlowchartNodeDocument>())
        {
            Guid originalId = node.Id;
            Guid nodeId = EnsureUniqueGuid(originalId, usedNodeIds);

            if (originalId != Guid.Empty)
            {
                nodeIdMap[originalId] = nodeId;
            }

            nodes.Add(new FlowchartNodeDocument
            {
                Id = nodeId,
                Text = string.IsNullOrWhiteSpace(node.Text) ? "处理" : node.Text.Trim(),
                MetadataJson = node.MetadataJson ?? string.Empty,
                Kind = Enum.IsDefined(typeof(FlowchartNodeKind), node.Kind) ? node.Kind : FlowchartNodeKind.Process,
                X = NormalizeCoordinate(node.X),
                Y = NormalizeCoordinate(node.Y),
                Width = NormalizeSize(node.Width, DefaultNodeWidth),
                Height = NormalizeSize(node.Height, DefaultNodeHeight)
            });
        }

        HashSet<Guid> usedConnectionIds = new();
        List<FlowchartConnectionDocument> connections = new();
        foreach (FlowchartConnectionDocument connection in document.Connections ?? new List<FlowchartConnectionDocument>())
        {
            if (!nodeIdMap.TryGetValue(connection.SourceNodeId, out Guid sourceNodeId) ||
                !nodeIdMap.TryGetValue(connection.TargetNodeId, out Guid targetNodeId) ||
                sourceNodeId == targetNodeId)
            {
                continue;
            }

            if (!Enum.IsDefined(typeof(FlowchartAnchor), connection.SourceAnchor) ||
                !Enum.IsDefined(typeof(FlowchartAnchor), connection.TargetAnchor))
            {
                continue;
            }

            connections.Add(new FlowchartConnectionDocument
            {
                Id = EnsureUniqueGuid(connection.Id, usedConnectionIds),
                SourceNodeId = sourceNodeId,
                SourceAnchor = connection.SourceAnchor,
                TargetNodeId = targetNodeId,
                TargetAnchor = connection.TargetAnchor
            });
        }

        return new FlowchartDocument
        {
            Version = document.Version <= 0 ? 1 : document.Version,
            Nodes = nodes,
            Connections = connections
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

    private static Guid EnsureUniqueGuid(Guid id, HashSet<Guid> usedIds)
    {
        Guid candidate = id == Guid.Empty ? Guid.NewGuid() : id;
        while (!usedIds.Add(candidate))
        {
            candidate = Guid.NewGuid();
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

    private static string BuildUniqueStationCode(string? code, HashSet<string> usedCodes, int index)
    {
        string baseCode = string.IsNullOrWhiteSpace(code) ? $"ST-{index:00}" : code.Trim().ToUpperInvariant();
        string candidate = baseCode;
        int suffix = 2;

        while (!usedCodes.Add(candidate))
        {
            candidate = $"{baseCode}-{suffix:00}";
            suffix++;
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

    private static double NormalizeCoordinate(double value)
    {
        return double.IsNaN(value) || double.IsInfinity(value) ? 0 : value;
    }

    private static double NormalizeSize(double value, double fallback)
    {
        return double.IsNaN(value) || double.IsInfinity(value) || value <= 0 ? fallback : value;
    }

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

    private static void WriteJson<T>(string filePath, T value, JsonSerializerOptions options)
    {
        string json = JsonSerializer.Serialize(value, options);
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

    
    private static string BuildSchemeFilePath(SchemeProfile scheme)
    {
        return Path.Combine(SchemeDirectory, $"{SanitizeFileName(scheme.SchemeName)}_{SanitizeFileName(scheme.Id)}.scheme.json");
    }

    private static string BuildStationFilePath(StationProfile station)
    {
        return Path.Combine(StationDirectory, $"{SanitizeFileName(station.StationName)}_{SanitizeFileName(station.Id)}.station.json");
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
