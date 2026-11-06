using Module.Business.Features.LuaScript;
using System;

namespace Module.Business.Models
{
    /// <summary>
    /// Lua 脚本配置文档。
    /// Lua 脚本在磁盘上的序列化模型，负责 JSON 文件与内存配置对象（LuaScriptProfile）之间的互相转换。
    /// </summary>
    public sealed class LuaScriptProfileDocument
    {
        /// <summary>脚本名称，为空时加载回内存会使用默认名称“Lua 脚本”。</summary>
        public string? Name { get; set; }

        /// <summary>脚本正文内容。</summary>
        public string? ScriptText { get; set; }

        /// <summary>最后修改时间，用于界面展示脚本的更新记录。</summary>
        public DateTime LastModifiedAt { get; set; }

        /// <summary>
        /// 由内存配置对象构建序列化文档。
        /// </summary>
        /// <param name="profile">Lua 脚本配置对象。</param>
        /// <returns>可写入磁盘的文档模型。</returns>
        public static LuaScriptProfileDocument FromProfile(LuaScriptProfile profile)
        {
            return new LuaScriptProfileDocument
            {
                Name = profile.Name,
                ScriptText = profile.ScriptText,
                LastModifiedAt = profile.LastModifiedAt
            };
        }

        /// <summary>
        /// 将序列化文档还原为内存配置对象，空名称会回退为默认名称。
        /// </summary>
        /// <returns>Lua 脚本配置对象。</returns>
        public LuaScriptProfile ToProfile()
        {
            LuaScriptProfile profile = new LuaScriptProfile
            {
                Name = string.IsNullOrWhiteSpace(Name) ? "Lua 脚本" : Name.Trim(),
                ScriptText = ScriptText ?? string.Empty
            };
            // 恢复文件中记录的最后修改时间，避免加载后误判为新建脚本。
            profile.AcceptLoadedState(LastModifiedAt);
            return profile;
        }
    }
}
