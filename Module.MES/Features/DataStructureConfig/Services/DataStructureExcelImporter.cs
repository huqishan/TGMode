using Module.MES.Features.DataStructureConfig.ViewModels.PresentationModels;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace Module.MES.Features.DataStructureConfig.Services;

/// <summary>
/// 读取数据结构导出的 XLSX 工作簿，并根据结构树工作表中的行分组层级重建配置树。
/// </summary>
internal static class DataStructureExcelImporter
{
    #region Excel 固定配置

    private static readonly XNamespace SpreadsheetNamespace =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    #endregion

    #region 导入入口

    /// <summary>
    /// 导入标准导出工作簿。工作表按固定包路径读取，不依赖本机安装 Excel。
    /// </summary>
    public static DataStructureExcelImportResult Import(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            throw new FileNotFoundException("未找到需要导入的 Excel 文件。", filePath);
        }

        using ZipArchive archive = ZipFile.OpenRead(filePath);
        IReadOnlyList<string> sharedStrings = ReadSharedStrings(archive);
        XDocument basicSheet = ReadWorksheet(archive, "xl/worksheets/sheet1.xml", "基本信息");
        XDocument structureSheet = ReadWorksheet(archive, "xl/worksheets/sheet2.xml", "结构树");

        Dictionary<string, string> basicInformation = ReadBasicInformation(basicSheet, sharedStrings);
        ObservableCollection<DataStructureLayout> structure = ReadStructure(structureSheet, sharedStrings);
        if (structure.Count == 0)
        {
            throw new InvalidDataException("结构树工作表中没有可导入的字段。");
        }

        basicInformation.TryGetValue("结构名称", out string? name);
        basicInformation.TryGetValue("结构类型", out string? structureType);
        return new DataStructureExcelImportResult(
            name ?? string.Empty,
            string.IsNullOrWhiteSpace(structureType) ? "JSON" : structureType,
            structure);
    }

    #endregion

    #region 工作表解析

    /// <summary>
    /// 结构树中每行是一个节点，outlineLevel 表示深度。栈中保存每一级最后出现的父节点。
    /// </summary>
    private static ObservableCollection<DataStructureLayout> ReadStructure(
        XDocument worksheet,
        IReadOnlyList<string> sharedStrings)
    {
        ObservableCollection<DataStructureLayout> roots = new();
        List<DataStructureLayout> parentStack = new();
        foreach (XElement row in worksheet.Descendants(SpreadsheetNamespace + "row").Skip(1))
        {
            Dictionary<int, string> values = ReadRow(row, sharedStrings);
            string clientCode = GetValue(values, 3);
            string mesCode = GetValue(values, 4);
            string dataType = GetValue(values, 2);

            // 匿名 MODEL/LIST/ARRAY 的客户端字段和 MES 字段允许同时为空，只要树名称或类型存在就仍是有效节点。
            // 只有整行没有任何节点特征时才作为空行跳过，否则会丢失父节点并导致后续子节点层级不连续。
            if (string.IsNullOrWhiteSpace(GetValue(values, 1)) &&
                string.IsNullOrWhiteSpace(dataType) &&
                string.IsNullOrWhiteSpace(clientCode) &&
                string.IsNullOrWhiteSpace(mesCode))
            {
                continue;
            }

            int level = ParseInteger(row.Attribute("outlineLevel")?.Value);
            if (level > parentStack.Count)
            {
                throw new InvalidDataException($"结构树第 {row.Attribute("r")?.Value} 行的层级不连续。");
            }

            DataStructureLayout field = new()
            {
                DataType = dataType,
                ClientCode = clientCode,
                MesCode = mesCode,
                DefaultValue = GetValue(values, 5),
                WhileCount = ParseInteger(GetValue(values, 6)),
                IsNull = string.Equals(GetValue(values, 7), "是", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(GetValue(values, 7), "TRUE", StringComparison.OrdinalIgnoreCase),
                KeepCount = ParseInteger(GetValue(values, 8)),
                XmlNamespace = GetValue(values, 9),
                JudgeValue = GetValue(values, 10),
                OKText = GetValue(values, 11),
                NGText = GetValue(values, 12)
            };

            if (level == 0)
            {
                roots.Add(field);
            }
            else
            {
                parentStack[level - 1].Children.Add(field);
            }

            if (parentStack.Count > level) parentStack.RemoveRange(level, parentStack.Count - level);
            parentStack.Add(field);
        }
        return roots;
    }

    private static Dictionary<string, string> ReadBasicInformation(
        XDocument worksheet,
        IReadOnlyList<string> sharedStrings)
    {
        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (XElement row in worksheet.Descendants(SpreadsheetNamespace + "row").Skip(1))
        {
            Dictionary<int, string> values = ReadRow(row, sharedStrings);
            string key = GetValue(values, 1);
            if (!string.IsNullOrWhiteSpace(key)) result[key] = GetValue(values, 2);
        }
        return result;
    }

    private static Dictionary<int, string> ReadRow(XElement row, IReadOnlyList<string> sharedStrings)
    {
        Dictionary<int, string> values = new();
        foreach (XElement cell in row.Elements(SpreadsheetNamespace + "c"))
        {
            int column = GetColumnNumber(cell.Attribute("r")?.Value);
            string cellType = cell.Attribute("t")?.Value ?? string.Empty;
            string value = cellType switch
            {
                "inlineStr" => string.Concat(cell.Descendants(SpreadsheetNamespace + "t").Select(text => text.Value)),
                "s" => ResolveSharedString(cell.Element(SpreadsheetNamespace + "v")?.Value, sharedStrings),
                _ => cell.Element(SpreadsheetNamespace + "v")?.Value ?? string.Empty
            };
            values[column] = value;
        }
        return values;
    }

    private static XDocument ReadWorksheet(ZipArchive archive, string path, string displayName)
    {
        ZipArchiveEntry? entry = archive.GetEntry(path);
        if (entry is null) throw new InvalidDataException($"Excel 中缺少“{displayName}”工作表。");
        using Stream stream = entry.Open();
        return XDocument.Load(stream);
    }

    #endregion

    #region 单元格辅助方法

    private static IReadOnlyList<string> ReadSharedStrings(ZipArchive archive)
    {
        ZipArchiveEntry? entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return Array.Empty<string>();
        using Stream stream = entry.Open();
        XDocument document = XDocument.Load(stream);
        return document.Descendants(SpreadsheetNamespace + "si")
            .Select(item => string.Concat(item.Descendants(SpreadsheetNamespace + "t").Select(text => text.Value)))
            .ToList();
    }

    private static string ResolveSharedString(string? indexText, IReadOnlyList<string> sharedStrings)
    {
        if (!int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)) return string.Empty;
        return index >= 0 && index < sharedStrings.Count ? sharedStrings[index] : string.Empty;
    }

    private static string GetValue(IReadOnlyDictionary<int, string> values, int column)
    {
        return values.TryGetValue(column, out string? value) ? value : string.Empty;
    }

    private static int ParseInteger(string? value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : 0;
    }

    private static int GetColumnNumber(string? cellReference)
    {
        int column = 0;
        foreach (char character in cellReference ?? string.Empty)
        {
            if (!char.IsLetter(character)) break;
            column = column * 26 + char.ToUpperInvariant(character) - 'A' + 1;
        }
        return column;
    }

    #endregion
}

/// <summary>
/// Excel 导入后的结构数据，交由页面决定新增或覆盖配置。
/// </summary>
internal sealed record DataStructureExcelImportResult(
    string Name,
    string StructureType,
    ObservableCollection<DataStructureLayout> Structure);
