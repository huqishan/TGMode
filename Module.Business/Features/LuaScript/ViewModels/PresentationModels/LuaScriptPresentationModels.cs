using ControlLibrary;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Module.Business.Features.LuaScript
{
    /// <summary>
    /// Lua 脚本配置对象。
    /// 承载脚本名称与正文内容，内容变化时自动刷新修改时间与界面摘要。
    /// </summary>
    public sealed class LuaScriptProfile : ViewModelProperties
    {
        #region 字段

        /// <summary>脚本名称。</summary>
        private string _name = string.Empty;

        /// <summary>脚本正文内容。</summary>
        private string _scriptText = string.Empty;

        /// <summary>最后修改时间，新建时默认当前时间。</summary>
        private DateTime _lastModifiedAt = DateTime.Now;

        #endregion

        #region 属性

        /// <summary>脚本名称，变化时自动刷新修改时间。</summary>
        public string Name
        {
            get => _name;
            set
            {
                if (SetField(ref _name, value ?? string.Empty))
                {
                    Touch();
                }
            }
        }

        /// <summary>
        /// 脚本正文内容。
        /// 写入时统一换行符为 Windows 风格，并同步通知行数与摘要更新。
        /// </summary>
        public string ScriptText
        {
            get => _scriptText;
            set
            {
                if (SetField(ref _scriptText, NormalizeLineEndings(value)))
                {
                    Touch();
                    OnPropertyChanged(nameof(LineCount));
                    OnPropertyChanged(nameof(Summary));
                }
            }
        }

        /// <summary>最后修改时间，变化时同步刷新摘要展示。</summary>
        public DateTime LastModifiedAt
        {
            get => _lastModifiedAt;
            set
            {
                if (SetField(ref _lastModifiedAt, value))
                {
                    OnPropertyChanged(nameof(Summary));
                }
            }
        }

        /// <summary>脚本行数，空内容按 1 行计算。</summary>
        public int LineCount => string.IsNullOrEmpty(ScriptText)
            ? 1
            : ScriptText.Count(character => character == '\n') + 1;

        /// <summary>列表展示摘要，格式为“行数 · 修改时间”。</summary>
        public string Summary => $"{LineCount} 行 · 修改于 {LastModifiedAt:yyyy-MM-dd HH:mm}";

        #endregion

        #region 方法

        /// <summary>
        /// 复制当前脚本生成新脚本，修改时间取当前时间。
        /// </summary>
        /// <param name="name">新脚本名称。</param>
        /// <returns>新的脚本配置对象。</returns>
        public LuaScriptProfile Clone(string name)
        {
            return new LuaScriptProfile
            {
                Name = name,
                ScriptText = ScriptText,
                LastModifiedAt = DateTime.Now
            };
        }

        /// <summary>
        /// 从磁盘加载后恢复最后修改时间，避免加载动作误更新修改时间。
        /// </summary>
        /// <param name="lastModifiedAt">文件中记录的修改时间，缺省时回退为当前时间。</param>
        internal void AcceptLoadedState(DateTime lastModifiedAt)
        {
            _lastModifiedAt = lastModifiedAt == default ? DateTime.Now : lastModifiedAt;
            OnPropertyChanged(nameof(LastModifiedAt));
            OnPropertyChanged(nameof(Summary));
        }

        /// <summary>刷新修改时间为当前时间并通知摘要更新。</summary>
        private void Touch()
        {
            LastModifiedAt = DateTime.Now;
            OnPropertyChanged(nameof(Summary));
        }

        /// <summary>
        /// 统一换行符：先把 \r\n、\r 归一为 \n，再统一替换为 \r\n。
        /// </summary>
        /// <param name="text">原始文本。</param>
        /// <returns>换行符规范化后的文本。</returns>
        private static string NormalizeLineEndings(string? text)
        {
            return (text ?? string.Empty)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\r", "\n", StringComparison.Ordinal)
                .Replace("\n", "\r\n", StringComparison.Ordinal);
        }

        /// <summary>
        /// 字段赋值并按需触发属性变更通知。
        /// </summary>
        /// <typeparam name="T">字段类型。</typeparam>
        /// <param name="field">目标字段引用。</param>
        /// <param name="value">新值。</param>
        /// <param name="propertyName">属性名，默认由编译器填充。</param>
        /// <returns>值发生变化返回 true，否则 false。</returns>
        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        #endregion
    }
}
