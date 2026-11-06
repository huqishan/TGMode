using Module.Business.Features.WorkStep.ViewModels.PresentationModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Module.Business.Features.WorkStep.Services;

/// <summary>
/// 独立业务配置存储。每个业务使用单独文件，便于后续按 WorkStepId 查询引用和生成执行快照。
/// </summary>
public static class WorkStepConfigurationStore
{
    #region 配置与公开入口

    private static readonly string WorkStepDirectory = Path.Combine(AppContext.BaseDirectory, "Config", "WorkStep");
    private const string FileSearchPattern = "*.workstep.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        IgnoreReadOnlyProperties = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static ObservableCollection<WorkStepProfile> Load()
    {
        ObservableCollection<WorkStepProfile> workSteps = new();
        if (!Directory.Exists(WorkStepDirectory))
        {
            return workSteps;
        }

        foreach (string filePath in Directory.EnumerateFiles(WorkStepDirectory, FileSearchPattern).OrderBy(path => path))
        {
            try
            {
                WorkStepProfile? workStep = JsonSerializer.Deserialize<WorkStepProfile>(File.ReadAllText(filePath), JsonOptions);
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

    public static void Save(ObservableCollection<WorkStepProfile> workSteps)
    {
        Directory.CreateDirectory(WorkStepDirectory);
        HashSet<string> currentFiles = new(StringComparer.OrdinalIgnoreCase);

        foreach (WorkStepProfile workStep in workSteps)
        {
            string filePath = Path.Combine(WorkStepDirectory, $"{workStep.Id}.workstep.json");
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
