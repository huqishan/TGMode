using ControlLibrary.Controls.FlowchartEditor.Models;
using Module.Business.Features.StationConfiguration;
using Module.Business.Features.OperationEditing.ViewModels.PresentationModels;
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
/// 工位配置持久化工具，负责工位配置的加载、保存与规范化。
/// </summary>
public static class StationConfigurationStore
{
    #region 配置目录与默认值

    /// <summary>
    /// 业务配置根目录。
    /// </summary>
    private static readonly string RootConfigDirectory =
        Path.Combine(AppContext.BaseDirectory, "Config");

    /// <summary>
    /// 工位配置文件目录。
    /// </summary>
    private static readonly string StationDirectory =
        Path.Combine(RootConfigDirectory, "Station");

    /// <summary>
    /// 工位配置文件搜索模式。
    /// </summary>
    private const string StationFileSearchPattern = "*.station.json";

    /// <summary>
    /// 流程图节点默认宽度。
    /// </summary>
    private const double DefaultNodeWidth = 150;

    /// <summary>
    /// 流程图节点默认高度。
    /// </summary>
    private const double DefaultNodeHeight = 70;

    #endregion

    #region JSON 序列化选项

    /// <summary>
    /// 工位配置 JSON 序列化选项。
    /// </summary>
    private static readonly JsonSerializerOptions StationJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    #endregion

    #region 公开加载保存入口

    /// <summary>
    /// 加载完整工位配置目录。
    /// </summary>
    /// <returns>规范化后的工位配置目录。</returns>
    public static StationConfigurationCatalog LoadCatalog()
    {
        StationConfigurationCatalog catalog = new()
        {
            Stations = LoadStations()
        };

        return NormalizeCatalog(catalog);
    }

    /// <summary>
    /// 保存完整工位配置目录。
    /// </summary>
    /// <param name="catalog">需要保存的工位配置目录。</param>
    public static void SaveCatalog(StationConfigurationCatalog catalog)
    {
        StationConfigurationCatalog normalized = NormalizeCatalog(catalog);
        SaveStations(normalized.Stations);
    }

    #endregion

    #region 工位加载保存

    /// <summary>
    /// 从配置目录读取所有工位文件。
    /// </summary>
    /// <returns>读取到的工位集合。</returns>
    private static ObservableCollection<StationProfile> LoadStations()
    {
        ObservableCollection<StationProfile> stations = new();
        foreach (string filePath in EnumerateConfigFiles(StationDirectory, StationFileSearchPattern))
        {
            StationProfile? station = ReadJson<StationProfile>(filePath, StationJsonOptions);
            if (station is not null)
            {
                stations.Add(station);
            }
        }

        return stations;
    }

    /// <summary>
    /// 将工位集合保存为独立配置文件。
    /// </summary>
    /// <param name="stations">需要保存的工位集合。</param>
    private static void SaveStations(ObservableCollection<StationProfile> stations)
    {
        Directory.CreateDirectory(StationDirectory);
        HashSet<string> currentFilePaths = new(StringComparer.OrdinalIgnoreCase);

        foreach (StationProfile station in stations)
        {
            string filePath = BuildStationFilePath(station);
            WriteJson(filePath, station, StationJsonOptions);
            currentFilePaths.Add(filePath);
        }

        DeleteStaleFiles(StationDirectory, StationFileSearchPattern, currentFilePaths);
    }

    #endregion

    #region 目录级规范化

    /// <summary>
    /// 克隆并规范化工位配置目录。
    /// </summary>
    /// <param name="catalog">原始工位配置目录。</param>
    /// <returns>可安全使用的工位配置目录。</returns>
    private static StationConfigurationCatalog NormalizeCatalog(StationConfigurationCatalog? catalog)
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

    /// <summary>
    /// 规范化工位集合，补齐工位名称、编码和流程图文档。
    /// </summary>
    /// <param name="stations">需要规范化的工位集合。</param>
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

    #endregion

    #region 流程图规范化

    /// <summary>
    /// 规范化流程图文档，过滤无效节点和连线。
    /// </summary>
    /// <param name="document">原始流程图文档。</param>
    /// <returns>规范化后的流程图文档。</returns>
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
                MetadataJson = NormalizeNodeOperationMetadata(node),
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

    /// <summary>
    /// 确保所有流程图节点都使用统一的步骤实体作为业务载荷。
    /// </summary>
    private static string NormalizeNodeOperationMetadata(FlowchartNodeDocument node)
    {
        if (!string.IsNullOrWhiteSpace(node.MetadataJson))
        {
            try
            {
                if (JsonSerializer.Deserialize<WorkStepOperation>(node.MetadataJson) is not null)
                {
                    return node.MetadataJson;
                }
            }
            catch (JsonException)
            {
                // 无效或旧格式载荷由下方统一步骤实体替换。
            }
        }

        string defaultText = node.Kind switch
        {
            FlowchartNodeKind.Start => "开始",
            FlowchartNodeKind.Decision => "判断",
            FlowchartNodeKind.End => "结束",
            _ => "处理"
        };
        WorkStepOperation operation = new()
        {
            OperationObjectName = node.Kind switch
            {
                FlowchartNodeKind.Decision => "判断",
                FlowchartNodeKind.Start => "开始",
                FlowchartNodeKind.End => "结束",
                _ => "System"
            },
            Summary = string.IsNullOrWhiteSpace(node.Text) ? defaultText : node.Text.Trim()
        };
        operation.ConditionExecution.IsEnabled = node.Kind == FlowchartNodeKind.Decision;
        return JsonSerializer.Serialize(operation);
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
    /// 确保 Guid 标识在当前集合中唯一。
    /// </summary>
    /// <param name="id">候选 Guid。</param>
    /// <param name="usedIds">已使用 Guid 集合。</param>
    /// <returns>唯一 Guid。</returns>
    private static Guid EnsureUniqueGuid(Guid id, HashSet<Guid> usedIds)
    {
        Guid candidate = id == Guid.Empty ? Guid.NewGuid() : id;
        while (!usedIds.Add(candidate))
        {
            candidate = Guid.NewGuid();
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

    /// <summary>
    /// 基于工位编码生成不重复的编码。
    /// </summary>
    /// <param name="code">候选工位编码。</param>
    /// <param name="usedCodes">已使用编码集合。</param>
    /// <param name="index">默认编码序号。</param>
    /// <returns>唯一工位编码。</returns>
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
    /// 规范化流程图坐标值。
    /// </summary>
    /// <param name="value">原始坐标值。</param>
    /// <returns>可用坐标值。</returns>
    private static double NormalizeCoordinate(double value)
    {
        return double.IsNaN(value) || double.IsInfinity(value) ? 0 : value;
    }

    /// <summary>
    /// 规范化流程图节点尺寸。
    /// </summary>
    /// <param name="value">原始尺寸值。</param>
    /// <param name="fallback">无效时使用的默认尺寸。</param>
    /// <returns>可用尺寸值。</returns>
    private static double NormalizeSize(double value, double fallback)
    {
        return double.IsNaN(value) || double.IsInfinity(value) || value <= 0 ? fallback : value;
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
    /// 构建工位配置文件路径。
    /// </summary>
    /// <param name="station">工位配置。</param>
    /// <returns>工位配置文件路径。</returns>
    private static string BuildStationFilePath(StationProfile station)
    {
        return Path.Combine(StationDirectory, $"{SanitizeFileName(station.StationName)}_{SanitizeFileName(station.Id)}.station.json");
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
