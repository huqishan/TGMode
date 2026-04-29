using System;

namespace ControlLibrary.ControlViews.LuaScrip.Models
{
    public sealed class LuaScriptProfileDocument
    {
        public string? Name { get; set; }

        public string? ScriptText { get; set; }

        public DateTime LastModifiedAt { get; set; }

        public static LuaScriptProfileDocument FromProfile(LuaScriptProfile profile)
        {
            return new LuaScriptProfileDocument
            {
                Name = profile.Name,
                ScriptText = profile.ScriptText,
                LastModifiedAt = profile.LastModifiedAt
            };
        }

        public LuaScriptProfile ToProfile()
        {
            LuaScriptProfile profile = new LuaScriptProfile
            {
                Name = string.IsNullOrWhiteSpace(Name) ? "Lua 脚本" : Name.Trim(),
                ScriptText = ScriptText ?? string.Empty
            };
            profile.AcceptLoadedState(LastModifiedAt);
            return profile;
        }
    }
}
