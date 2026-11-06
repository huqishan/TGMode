using Module.Business.Features.BusinessConfiguration.ViewModels.PresentationModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Module.Business.Features.BusinessConfiguration.Services;

/// <summary>
/// 独立业务配置存储。每个业务使用单独文件，便于后续按 WorkStepId 查询引用和生成执行快照。
/// </summary>
public static class BusinessConfigurationStore
{
    #region 配置与公开入口

    /// <summary>业务配置文件目录。</summary>
    private static readonly string WorkStepDirectory = Path.Combine(AppContext.BaseDirectory, "Config", "BusinessConfiguration");

    /// <summary>业务配置文件搜索模式。</summary>
    private const string FileSearchPattern = "*.businessconfiguration.json";

    /// <summary>JSON 序列化选项：缩进输出、忽略只读属性、中文不转义。</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        IgnoreReadOnlyProperties = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// 加载全部业务配置。
    /// 目录不存在时返回空集合；单个文件损坏时跳过，不影响其他业务加载。
    /// </summary>
    /// <returns>业务配置集合，加载后重置变更标记。</returns>
    public static ObservableCollection<BusinessProfile> Load()
    {
        ObservableCollection<BusinessProfile> workSteps = new();
        if (!Directory.Exists(WorkStepDirectory))
        {
            return workSteps;
        }

        foreach (string filePath in Directory.EnumerateFiles(WorkStepDirectory, FileSearchPattern).OrderBy(path => path))
        {
            try
            {
                BusinessProfile? workStep = JsonSerializer.Deserialize<BusinessProfile>(File.ReadAllText(filePath), JsonOptions);
                if (workStep is null)
                {
                    continue;
                }

                workStep.AcceptChanges();
                workSteps.Add(workStep);
            }
            catch (JsonException)
            {
                // 单个配置文件损坏时跳过该文件，避免整个业务管理页面无法打开。
            }
        }

        return workSteps;
    }

    /// <summary>
    /// 保存全部业务配置，以业务 Id 命名独立文件写入磁盘；
    /// 磁盘上已不在集合中的文件视为已删除业务，保存时一并清理。
    /// </summary>
    /// <param name="workSteps">待保存的业务配置集合。</param>
    public static void Save(ObservableCollection<BusinessProfile> workSteps)
    {
        Directory.CreateDirectory(WorkStepDirectory);
        HashSet<string> currentFiles = new(StringComparer.OrdinalIgnoreCase);

        foreach (BusinessProfile workStep in workSteps)
        {
            string filePath = Path.Combine(WorkStepDirectory, $"{workStep.Id}.businessconfiguration.json");
            File.WriteAllText(filePath, JsonSerializer.Serialize(workStep, JsonOptions));
            currentFiles.Add(filePath);
        }

        // 集合中已删除的业务在保存后才正式移除，符合配置页面“保存后生效”的交互习惯。
        foreach (string staleFile in Directory.EnumerateFiles(WorkStepDirectory, FileSearchPattern)
                     .Where(path => !currentFiles.Contains(path)))
        {
            File.Delete(staleFile);
        }
    }

    #endregion
}
