using Module.Business.Features.LuaScript;
using Module.Business.Models;
using Shared.Infrastructure.Extensions;
using Shared.Infrastructure.PackMethod;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Module.Business.Services;

#region Lua 脚本配置读写结果

/// <summary>
/// Lua 脚本加载结果。
/// 包含还原后的配置对象与对应的磁盘文件名，便于保存时定位旧文件。
/// </summary>
/// <param name="Profile">Lua 脚本配置对象。</param>
/// <param name="StorageFileName">脚本所在的存储文件名。</param>
public sealed record LuaScriptProfileLoadResult(
    LuaScriptProfile Profile,
    string StorageFileName);

/// <summary>
/// Lua 脚本保存结果。
/// 包含保存后的配置对象与实际写入的存储文件名，用于界面更新文件名映射。
/// </summary>
/// <param name="Profile">Lua 脚本配置对象。</param>
/// <param name="StorageFileName">实际写入的存储文件名。</param>
public sealed record LuaScriptProfileSaveResult(
    LuaScriptProfile Profile,
    string StorageFileName);

#endregion

/// <summary>
/// Lua 脚本配置存储。
/// 负责把 Lua 脚本配置读写到 Config/LuaScript 目录下的独立 JSON 文件，
/// 并处理文件名唯一化、非法字符清理与损坏文件跳过等细节。
/// </summary>
public static class LuaScriptConfigurationStore
{
    #region 配置与公开入口

    /// <summary>Lua 脚本配置文件目录。</summary>
    private static readonly string LuaScriptConfigDirectory =
        Path.Combine(AppContext.BaseDirectory, "Config", "LuaScript");

    /// <summary>
    /// 加载目录下全部 Lua 脚本配置。
    /// 单个文件损坏时跳过该文件，避免影响其他脚本的加载。
    /// </summary>
    /// <returns>加载结果集合，包含配置对象与文件名。</returns>
    public static IReadOnlyList<LuaScriptProfileLoadResult> LoadProfiles()
    {
        if (!Directory.Exists(LuaScriptConfigDirectory))
        {
            return Array.Empty<LuaScriptProfileLoadResult>();
        }

        List<LuaScriptProfileLoadResult> results = new();
        foreach (string filePath in Directory.EnumerateFiles(LuaScriptConfigDirectory, "*.json").OrderBy(Path.GetFileName))
        {
            string storageText = File.ReadAllText(filePath, Encoding.UTF8);
            LuaScriptProfileDocument? document = DeserializeProfileDocument(storageText);
            if (document is null)
            {
                continue;
            }

            results.Add(new LuaScriptProfileLoadResult(
                document.ToProfile(),
                Path.GetFileName(filePath)));
        }

        return results;
    }

    /// <summary>
    /// 保存全部 Lua 脚本配置。
    /// 以脚本名称生成唯一文件名写入磁盘；重命名的脚本会删除旧文件，保证目录与界面一致。
    /// </summary>
    /// <param name="profiles">待保存的脚本配置集合。</param>
    /// <param name="existingFileNames">保存前的脚本与旧文件名映射，用于重命名后清理旧文件。</param>
    /// <returns>保存结果集合。</returns>
    public static IReadOnlyList<LuaScriptProfileSaveResult> SaveProfiles(
        IEnumerable<LuaScriptProfile> profiles,
        IReadOnlyDictionary<LuaScriptProfile, string> existingFileNames)
    {
        Directory.CreateDirectory(LuaScriptConfigDirectory);

        List<LuaScriptProfile> profileList = profiles.ToList();
        HashSet<string> usedFileNames = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<LuaScriptProfile, string> targetFileNames = new();
        // 先统一校验并分配目标文件名，再统一写盘，避免中途失败留下半套文件。
        foreach (LuaScriptProfile profile in profileList)
        {
            ValidateProfileForSave(profile);
            targetFileNames[profile] = BuildUniqueStorageFileName(profile.Name, usedFileNames);
        }

        List<LuaScriptProfileSaveResult> results = new();
        foreach (LuaScriptProfile profile in profileList)
        {
            string fileName = targetFileNames[profile];
            string filePath = Path.Combine(LuaScriptConfigDirectory, fileName);
            string storageText = JsonHelper.SerializeObject(LuaScriptProfileDocument.FromProfile(profile));
            File.WriteAllText(filePath, storageText, Encoding.UTF8);
            results.Add(new LuaScriptProfileSaveResult(profile, fileName));
        }

        // 清理重命名后遗留的旧文件；旧文件名若被其他脚本占用则不删除。
        foreach (LuaScriptProfile profile in profileList)
        {
            string fileName = targetFileNames[profile];
            if (existingFileNames.TryGetValue(profile, out string? oldFileName) &&
                !string.Equals(oldFileName, fileName, StringComparison.OrdinalIgnoreCase) &&
                !usedFileNames.Contains(oldFileName))
            {
                TryDeleteStorageFile(oldFileName);
            }
        }

        return results;
    }

    /// <summary>
    /// 删除指定脚本对应的存储文件。
    /// </summary>
    /// <param name="fileName">存储文件名。</param>
    public static void DeleteProfileFile(string fileName)
    {
        TryDeleteStorageFile(fileName);
    }

    #endregion

    #region 内部辅助方法

    /// <summary>
    /// 反序列化脚本文档，兼容明文与 DES 加密两种历史存储格式。
    /// </summary>
    /// <param name="storageText">文件原文。</param>
    /// <returns>文档对象，两种格式均解析失败时返回 null。</returns>
    private static LuaScriptProfileDocument? DeserializeProfileDocument(string storageText)
    {
        try
        {
            return JsonHelper.DeserializeObject<LuaScriptProfileDocument>(storageText);
        }
        catch
        {
            return JsonHelper.DeserializeObject<LuaScriptProfileDocument>(storageText.DesDecrypt());
        }
    }

    /// <summary>
    /// 保存前校验脚本配置，名称不能为空。
    /// </summary>
    /// <param name="profile">待保存的脚本配置。</param>
    private static void ValidateProfileForSave(LuaScriptProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            throw new InvalidOperationException("Lua script name cannot be empty.");
        }
    }

    /// <summary>
    /// 尝试删除存储文件，失败时静默忽略，避免删除失败中断保存流程。
    /// </summary>
    /// <param name="fileName">存储文件名。</param>
    private static void TryDeleteStorageFile(string fileName)
    {
        try
        {
            string filePath = Path.Combine(LuaScriptConfigDirectory, fileName);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
        }
    }

    /// <summary>
    /// 以脚本名称生成不重复的存储文件名，重名时追加序号后缀。
    /// </summary>
    /// <param name="profileName">脚本名称。</param>
    /// <param name="usedFileNames">本次保存已占用的文件名集合。</param>
    /// <returns>唯一的存储文件名。</returns>
    private static string BuildUniqueStorageFileName(string profileName, HashSet<string> usedFileNames)
    {
        string safeName = BuildSafeFileName(profileName);
        string fileName = $"{safeName}.json";
        for (int index = 2; usedFileNames.Contains(fileName); index++)
        {
            fileName = $"{safeName}_{index}.json";
        }

        usedFileNames.Add(fileName);
        return fileName;
    }

    /// <summary>
    /// 把脚本名称转换为合法文件名：非法字符与空白替换为下划线，长度截断到 80。
    /// </summary>
    /// <param name="value">原始名称。</param>
    /// <returns>安全的文件名主干。</returns>
    private static string BuildSafeFileName(string value)
    {
        HashSet<char> invalidChars = new(Path.GetInvalidFileNameChars());
        StringBuilder builder = new(value.Trim().Length);
        foreach (char current in value.Trim())
        {
            builder.Append(invalidChars.Contains(current) || char.IsControl(current)
                ? '_'
                : char.IsWhiteSpace(current) ? '_' : current);
        }

        string safeName = builder.ToString().Trim(' ', '.');
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "LuaScript";
        }

        return safeName.Length <= 80 ? safeName : safeName[..80];
    }

    #endregion
}
