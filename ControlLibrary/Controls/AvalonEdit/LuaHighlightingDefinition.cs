using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using System;
using System.IO;
using System.Reflection;
using System.Xml;

namespace ControlLibrary.Controls.AvalonEdit
{
    internal static class LuaHighlightingDefinition
    {
        private static readonly Lazy<IHighlightingDefinition> LazyDefinition = new(LoadDefinition);

        public static IHighlightingDefinition Load()
        {
            return LazyDefinition.Value;
        }

        private static IHighlightingDefinition LoadDefinition()
        {
            Assembly assembly = typeof(LuaHighlightingDefinition).Assembly;
            using Stream stream = assembly.GetManifestResourceStream("ControlLibrary.Controls.AvalonEdit.Lua.xshd")
                ?? throw new InvalidOperationException("Lua highlighting definition resource was not found.");
            using XmlReader reader = new XmlTextReader(stream);
            return HighlightingLoader.Load(reader, HighlightingManager.Instance);
        }
    }
}
