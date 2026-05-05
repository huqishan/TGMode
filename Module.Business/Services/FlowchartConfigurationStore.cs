using ControlLibrary.Controls.FlowchartEditor.Models;
using Module.Business.Models;
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
/// 流程图配置存储服务，负责多个流程图配置文件的读取与保存。
/// </summary>
public static class FlowchartConfigurationStore
{
    #region 配置路径与序列化字段

    private const double DefaultNodeWidth = 150;
    private const double DefaultNodeHeight = 70;

    private static readonly string ConfigDirectory =
        Path.Combine(AppContext.BaseDirectory, "Config", "Flowchart");

    private const string FlowchartConfigFileSearchPattern = "*.flowchart.config.json";

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    #endregion

    #region 配置读写

    /// <summary>
    /// 加载流程图配置；当配置文件不存在或格式异常时返回空配置。
    /// </summary>
    public static FlowchartConfigurationCatalog LoadCatalog()
    {
        if (!Directory.Exists(ConfigDirectory))
        {
            return NormalizeCatalog(new FlowchartConfigurationCatalog());
        }

        ObservableCollection<FlowchartProfile> flowcharts = new();
        foreach (string filePath in Directory
                     .EnumerateFiles(ConfigDirectory, FlowchartConfigFileSearchPattern, SearchOption.TopDirectoryOnly)
                     .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                string json = File.ReadAllText(filePath);
                FlowchartProfile? flowchart = JsonSerializer.Deserialize<FlowchartProfile>(json, JsonOptions);
                if (flowchart is not null)
                {
                    flowcharts.Add(flowchart);
                }
            }
            catch
            {
                // 忽略单个损坏的配置文件，保证其余流程图仍可继续加载。
            }
        }

        return NormalizeCatalog(new FlowchartConfigurationCatalog
        {
            Flowcharts = flowcharts
        });
    }

    /// <summary>
    /// 保存流程图配置；保存前会清理空值、重复名称和无效连线。
    /// </summary>
    public static void SaveCatalog(FlowchartConfigurationCatalog catalog)
    {
        FlowchartConfigurationCatalog normalized = NormalizeCatalog(catalog);
        Directory.CreateDirectory(ConfigDirectory);

        HashSet<string> currentFilePaths = new(StringComparer.OrdinalIgnoreCase);
        foreach (FlowchartProfile flowchart in normalized.Flowcharts)
        {
            string filePath = BuildFlowchartFilePath(flowchart);
            string json = JsonSerializer.Serialize(flowchart, JsonOptions);
            File.WriteAllText(filePath, json);
            currentFilePaths.Add(filePath);
        }

        DeleteStaleFlowchartFiles(currentFilePaths);
    }

    #endregion

    #region 规范化方法
    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static FlowchartConfigurationCatalog NormalizeCatalog(FlowchartConfigurationCatalog? catalog)
    {
        FlowchartConfigurationCatalog normalized = new()
        {
            Flowcharts = new ObservableCollection<FlowchartProfile>(
                (catalog?.Flowcharts ?? new ObservableCollection<FlowchartProfile>())
                    .Where(flowchart => flowchart is not null)
                    .Select(flowchart => flowchart.Clone()))
        };

        NormalizeFlowcharts(normalized.Flowcharts);
        return normalized;
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
                string.IsNullOrWhiteSpace(flowchart.Name) ? $"\u6d41\u7a0b\u56fe{index}" : flowchart.Name.Trim(),
                usedNames);
            flowchart.Document = NormalizeDocument(flowchart.Document);
            index++;
        }
    }

    private static FlowchartDocument NormalizeDocument(FlowchartDocument? document)
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
                Text = string.IsNullOrWhiteSpace(node.Text) ? "\u5904\u7406" : node.Text.Trim(),
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
        string baseName = string.IsNullOrWhiteSpace(name) ? "\u6d41\u7a0b\u56fe" : name.Trim();
        string candidate = baseName;
        int index = 2;

        while (!usedNames.Add(candidate))
        {
            candidate = $"{baseName} {index}";
            index++;
        }

        return candidate;
    }

    private static double NormalizeCoordinate(double value)
    {
        return double.IsNaN(value) || double.IsInfinity(value) ? 0 : value;
    }

    private static double NormalizeSize(double value, double fallback)
    {
        return double.IsNaN(value) || double.IsInfinity(value) || value <= 0 ? fallback : value;
    }

    private static string BuildFlowchartFilePath(FlowchartProfile flowchart)
    {
        string safeName = SanitizeFileName(flowchart.Name);
        string safeId = SanitizeFileName(flowchart.Id);
        return Path.Combine(ConfigDirectory, $"{safeName}_{safeId}.flowchart.config.json");
    }

    private static void DeleteStaleFlowchartFiles(HashSet<string> currentFilePaths)
    {
        foreach (string filePath in Directory.EnumerateFiles(ConfigDirectory, FlowchartConfigFileSearchPattern, SearchOption.TopDirectoryOnly))
        {
            if (!currentFilePaths.Contains(filePath))
            {
                File.Delete(filePath);
            }
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

    #endregion
}

