using Shared.Infrastructure.Lua;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ControlLibrary.ControlViews.LuaScrip
{
    /// <summary>
    /// LuaScriptCompilerView.xaml 的交互逻辑
    /// </summary>
    public partial class LuaScriptCompilerView : UserControl, INotifyPropertyChanged
    {
        private static readonly Brush SuccessBrush = CreateBrush("#16A34A");
        private static readonly Brush WarningBrush = CreateBrush("#EA580C");
        private static readonly Brush NeutralBrush = CreateBrush("#64748B");
        private static readonly JsonSerializerOptions LuaTableJsonOptions = new()
        {
            WriteIndented = true
        };

        private string _luaCompileResultText = "\u5728\u8fd9\u91cc\u67e5\u770b\u811a\u672c\u6267\u884c\u8fd4\u56de\u6216\u9519\u8bef\u3002";
        private Brush _luaEditorStatusBrush = NeutralBrush;

        public LuaScriptCompilerView()
        {
            InitializeComponent();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public static readonly DependencyProperty ScriptTextProperty =
            DependencyProperty.Register(
                nameof(ScriptText),
                typeof(string),
                typeof(LuaScriptCompilerView),
                new FrameworkPropertyMetadata(
                    string.Empty,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty ExecutionPrefixScriptProperty =
            DependencyProperty.Register(
                nameof(ExecutionPrefixScript),
                typeof(string),
                typeof(LuaScriptCompilerView),
                new PropertyMetadata(string.Empty));

        public string ScriptText
        {
            get => (string?)GetValue(ScriptTextProperty) ?? string.Empty;
            set => SetValue(ScriptTextProperty, NormalizeLuaLineEndings(value));
        }

        public string ExecutionPrefixScript
        {
            get => (string?)GetValue(ExecutionPrefixScriptProperty) ?? string.Empty;
            set => SetValue(ExecutionPrefixScriptProperty, NormalizeLuaLineEndings(value));
        }

        public string LuaCompileResultText
        {
            get => _luaCompileResultText;
            private set => SetField(ref _luaCompileResultText, value);
        }

        public Brush LuaEditorStatusBrush
        {
            get => _luaEditorStatusBrush;
            private set => SetField(ref _luaEditorStatusBrush, value);
        }

        private void CompileLuaButton_Click(object sender, RoutedEventArgs e)
        {
            string script = NormalizeLuaLineEndings(LuaScriptEditorControl.Text);
            UpdateScriptTextProperty(script);
            string executionScript = string.IsNullOrWhiteSpace(ExecutionPrefixScript)
                ? script
                : $"{ExecutionPrefixScript}{Environment.NewLine}{script}";

            if (TryExecuteLuaScript(executionScript, out string message))
            {
                LuaCompileResultText = message;
                SetLuaEditorFeedback(SuccessBrush);
            }
            else
            {
                LuaCompileResultText = message;
                SetLuaEditorFeedback(WarningBrush);
            }
        }

        private void SetLuaEditorFeedback(Brush brush)
        {
            LuaEditorStatusBrush = brush;
        }

        private void UpdateScriptTextProperty(string text)
        {
            text = NormalizeLuaLineEndings(text);
            if (string.Equals(ScriptText, text, StringComparison.Ordinal))
            {
                return;
            }

            SetCurrentValue(ScriptTextProperty, text);
            GetBindingExpression(ScriptTextProperty)?.UpdateSource();
        }

        private static bool TryExecuteLuaScript(string script, out string message)
        {
            LuaManage lua = new();
            try
            {
                object[] results = lua.DoString(script);
                if (results.Length == 1 && results[0] is Exception exception)
                {
                    message = exception.Message;
                    return false;
                }

                message = FormatLuaExecutionResults(results);
                return true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }

        private static string FormatLuaExecutionResults(object[] results)
        {
            if (results.Length == 0)
            {
                return "\u6267\u884c\u5b8c\u6210\uff0c\u65e0\u8fd4\u56de\u503c\u3002";
            }

            return string.Join(Environment.NewLine, results.Select(FormatLuaResultValue));
        }

        private static string FormatLuaResultValue(object? value)
        {
            if (value is IDictionary<string, object?> dictionary)
            {
                return JsonSerializer.Serialize(dictionary, LuaTableJsonOptions);
            }

            if (TryConvertLuaTable(value, out object? luaTableValue))
            {
                return JsonSerializer.Serialize(luaTableValue, LuaTableJsonOptions);
            }

            return value switch
            {
                null => "nil",
                Exception exception => exception.Message,
                string text => text,
                _ => value.ToString() ?? string.Empty
            };
        }

        private static bool TryConvertLuaTable(object? value, out object? convertedValue)
        {
            convertedValue = null;
            if (value is null || value.GetType().FullName != "NLua.LuaTable")
            {
                return false;
            }

            convertedValue = ConvertLuaTable(value);
            return true;
        }

        private static object ConvertLuaValue(object? value)
        {
            return TryConvertLuaTable(value, out object? convertedValue)
                ? convertedValue ?? new Dictionary<string, object?>()
                : value ?? "nil";
        }

        private static Dictionary<string, object?> ConvertLuaTable(object luaTable)
        {
            Dictionary<string, object?> values = new(StringComparer.OrdinalIgnoreCase);
            Type tableType = luaTable.GetType();
            IEnumerable? keys = tableType.GetProperty("Keys")?.GetValue(luaTable) as IEnumerable;
            System.Reflection.PropertyInfo? indexer = tableType.GetProperty("Item");
            if (keys is null || indexer is null)
            {
                return values;
            }

            foreach (object key in keys)
            {
                string? fieldName = Convert.ToString(key);
                if (string.IsNullOrWhiteSpace(fieldName))
                {
                    continue;
                }

                object? rawValue = indexer.GetValue(luaTable, new[] { key });
                values[fieldName] = ConvertLuaValue(rawValue);
            }

            return values;
        }

        private static string NormalizeLuaLineEndings(string text)
        {
            return (text ?? string.Empty)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\r", "\n", StringComparison.Ordinal)
                .Replace("\n", "\r\n", StringComparison.Ordinal);
        }

        private static Brush CreateBrush(string color)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        }

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }
}
