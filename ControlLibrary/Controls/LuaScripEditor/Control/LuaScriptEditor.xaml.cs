using Shared.Infrastructure.Lua;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ControlLibrary.Controls.LuaScripEditor.Control
{
    /// <summary>
    /// LuaScriptEditor.xaml 的交互逻辑
    /// </summary>
    public partial class LuaScriptEditor : UserControl, INotifyPropertyChanged
    {
        private static readonly string[] LuaKeywords =
        {
            "and", "break", "do", "else", "elseif", "end", "false", "for", "function",
            "goto", "if", "in", "local", "nil", "not", "or", "repeat", "return",
            "then", "true", "until", "while"
        };
        public event PropertyChangedEventHandler? PropertyChanged;
        private sealed record LuaTextToken(string Text, string ForegroundResourceKey);
        private const double LuaEditorLineHeight = 24;
        private bool _isUpdatingLuaEditorDocument;
        private const string LuaPlainTextBrushKey = "LuaEditorPlainTextBrush";
        private const string LuaKeywordBrushKey = "LuaEditorKeywordBrush";
        private const string LuaCommentBrushKey = "LuaEditorCommentBrush";
        private const string LuaStringBrushKey = "LuaEditorStringBrush";
        private const string LuaNumberBrushKey = "LuaEditorNumberBrush";
        private const string LuaFunctionBrushKey = "LuaEditorFunctionBrush";
        private string _luaCurrentTokenText = string.Empty;
        public string LuaCurrentTokenText
        {
            get => _luaCurrentTokenText;
            private set => SetField(ref _luaCurrentTokenText, value);
        }
        private bool _isLuaEditorRefreshScheduled;


        private string _luaLineNumberText= "1";
        public string LuaLineNumberText
        {
            get => _luaLineNumberText;
            private set => SetField(ref _luaLineNumberText, value);
        }
        private string _luaCompileResultText = "在这里查看脚本执行返回或错误。";
        public string LuaCompileResultText
        {
            get => _luaCompileResultText;
            private set => SetField(ref _luaCompileResultText, value);
        }

        public static readonly DependencyProperty ScriptTextProperty =
            DependencyProperty.Register(
                nameof(ScriptText),
                typeof(string),
                typeof(LuaScriptEditor),
                new FrameworkPropertyMetadata(
                    string.Empty,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnScriptTextChanged));


        private static readonly Brush SuccessBrush = CreateBrush("#16A34A");
        private static readonly Brush WarningBrush = CreateBrush("#EA580C");
        private static readonly Brush NeutralBrush = CreateBrush("#64748B");
        private Brush _luaEditorStatusBrush = NeutralBrush;
        public Brush LuaEditorStatusBrush
        {
            get => _luaEditorStatusBrush;
            private set => SetField(ref _luaEditorStatusBrush, value);
        }
        private static string FormatLuaResultValue(object? value)
        {
            return value switch
            {
                null => "nil",
                Exception exception => exception.Message,
                string text => text,
                _ => value.ToString() ?? string.Empty
            };
        }
        private bool _isSyncingScriptText;
        public string ScriptText
        {
            get => (string?)GetValue(ScriptTextProperty) ?? string.Empty;
            set => SetValue(ScriptTextProperty, NormalizeLuaLineEndings(value));
        }

        public LuaScriptEditor()
        {
            InitializeComponent();
        }

        private static void OnScriptTextChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
        {
            if (dependencyObject is not LuaScriptEditor editor || editor._isSyncingScriptText)
            {
                return;
            }

            string text = NormalizeLuaLineEndings(e.NewValue as string ?? string.Empty);
            if (string.Equals(editor.GetLuaEditorText(), text, StringComparison.Ordinal))
            {
                editor.UpdateLuaLineNumbers(text);
                return;
            }

            editor._isSyncingScriptText = true;
            try
            {
                editor.SetScriptText(text);
            }
            finally
            {
                editor._isSyncingScriptText = false;
            }
        }

        private void LuaEditorRichTextBox_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            LuaLineNumberOffsetTransform.Y = -e.VerticalOffset;
        }

        private void LuaEditorRichTextBox_SelectionChanged(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingLuaEditorDocument)
            {
                return;
            }

            RefreshLuaCurrentTokenText();
        }
        private void RefreshLuaCurrentTokenText()
        {
            LuaCurrentTokenText = GetCurrentLuaToken();
        }
        private string GetCurrentLuaToken()
        {
            string text = GetLuaEditorText();
            int caretOffset = GetTextOffset(LuaEditorRichTextBox.Selection.Start);
            caretOffset = Math.Min(caretOffset, text.Length);
            if (caretOffset == 0 || text.Length == 0)
            {
                return string.Empty;
            }

            int start = caretOffset;
            while (start > 0 && IsIdentifierPart(text[start - 1]))
            {
                start--;
            }

            int end = caretOffset;
            while (end < text.Length && IsIdentifierPart(text[end]))
            {
                end++;
            }

            return start == end ? string.Empty : text[start..end];
        }
        private string GetLuaEditorText()
        {
            string text = new TextRange(
                LuaEditorRichTextBox.Document.ContentStart,
                LuaEditorRichTextBox.Document.ContentEnd).Text;

            if (text.EndsWith("\r\n", StringComparison.Ordinal))
            {
                text = text[..^2];
            }

            return NormalizeLuaLineEndings(text);
        }
        private void SetScriptText(string text)
        {
            text = NormalizeLuaLineEndings(text);
            ApplyLuaHighlighting(text, text.Length, 0);
            RefreshLuaCurrentTokenText();
            UpdateLuaLineNumbers(text);
        }
        private static string NormalizeLuaLineEndings(string text)
        {
            return (text ?? string.Empty)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\r", "\n", StringComparison.Ordinal)
                .Replace("\n", "\r\n", StringComparison.Ordinal);
        }
        private static bool IsIdentifierPart(char value)
        {
            return char.IsLetterOrDigit(value) || value == '_';
        }
        private int GetTextOffset(TextPointer position)
        {
            return new TextRange(LuaEditorRichTextBox.Document.ContentStart, position).Text.Length;
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

        private void LuaEditorRichTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingLuaEditorDocument)
            {
                return;
            }

            ScheduleLuaEditorRefresh();
        }
        private void ScheduleLuaEditorRefresh()
        {
            if (_isLuaEditorRefreshScheduled)
            {
                return;
            }

            _isLuaEditorRefreshScheduled = true;
            Dispatcher.BeginInvoke(
                new Action(RefreshLuaEditorAfterTextChange),
                DispatcherPriority.Background);
        }
        private void RefreshLuaEditorAfterTextChange()
        {
            _isLuaEditorRefreshScheduled = false;
            if (_isUpdatingLuaEditorDocument)
            {
                return;
            }

            GetLuaEditorSelection(out int selectionStart, out int selectionLength);
            string text = GetLuaEditorText();
            ApplyLuaHighlighting(text, selectionStart, selectionLength);
            RefreshLuaCurrentTokenText();
            UpdateLuaLineNumbers(text);
            UpdateScriptTextProperty(text);
        }

        private void UpdateScriptTextProperty(string text)
        {
            text = NormalizeLuaLineEndings(text);
            if (_isSyncingScriptText || string.Equals(ScriptText, text, StringComparison.Ordinal))
            {
                return;
            }

            _isSyncingScriptText = true;
            try
            {
                SetCurrentValue(ScriptTextProperty, text);
                GetBindingExpression(ScriptTextProperty)?.UpdateSource();
            }
            finally
            {
                _isSyncingScriptText = false;
            }
        }
        private void GetLuaEditorSelection(out int selectionStart, out int selectionLength)
        {
            selectionStart = GetTextOffset(LuaEditorRichTextBox.Selection.Start);
            int selectionEnd = GetTextOffset(LuaEditorRichTextBox.Selection.End);
            selectionLength = Math.Max(0, selectionEnd - selectionStart);
        }
        private void UpdateLuaLineNumbers(string text)
        {
            text = NormalizeLuaLineEndings(text);
            int lineCount = string.IsNullOrEmpty(text)
                ? 1
                : text.Count(character => character == '\n') + 1;

            LuaLineNumberText = string.Join(
                Environment.NewLine,
                Enumerable.Range(1, lineCount));
        }
        private void ApplyLuaHighlighting(string text, int selectionStart, int selectionLength)
        {
            _isUpdatingLuaEditorDocument = true;
            try
            {
                FlowDocument document = LuaEditorRichTextBox.Document;
                document.Blocks.Clear();
                document.PagePadding = new Thickness(0);

                Paragraph paragraph = new()
                {
                    Margin = new Thickness(0),
                    LineHeight = LuaEditorLineHeight,
                    LineStackingStrategy = LineStackingStrategy.BlockLineHeight
                };

                foreach (LuaTextToken token in TokenizeLua(text))
                {
                    AppendToken(paragraph.Inlines, token);
                }

                document.Blocks.Add(paragraph);

                TextPointer startPointer = GetTextPointerAtOffset(Math.Min(selectionStart, text.Length));
                TextPointer endPointer = GetTextPointerAtOffset(Math.Min(selectionStart + selectionLength, text.Length));
                LuaEditorRichTextBox.Selection.Select(startPointer, endPointer);
            }
            finally
            {
                _isUpdatingLuaEditorDocument = false;
            }
        }
        private TextPointer GetTextPointerAtOffset(int offset)
        {
            TextPointer navigator = LuaEditorRichTextBox.Document.ContentStart;
            int currentOffset = 0;

            while (navigator is not null)
            {
                TextPointerContext context = navigator.GetPointerContext(LogicalDirection.Forward);
                if (context == TextPointerContext.Text)
                {
                    string runText = navigator.GetTextInRun(LogicalDirection.Forward);
                    if (currentOffset + runText.Length >= offset)
                    {
                        return navigator.GetPositionAtOffset(offset - currentOffset, LogicalDirection.Forward)
                               ?? LuaEditorRichTextBox.Document.ContentEnd;
                    }

                    currentOffset += runText.Length;
                    navigator = navigator.GetPositionAtOffset(runText.Length, LogicalDirection.Forward)
                                ?? LuaEditorRichTextBox.Document.ContentEnd;
                    continue;
                }

                if (context == TextPointerContext.ElementEnd && navigator.Parent is Paragraph)
                {
                    if (currentOffset + Environment.NewLine.Length >= offset)
                    {
                        return navigator;
                    }

                    currentOffset += Environment.NewLine.Length;
                }

                TextPointer? next = navigator.GetNextContextPosition(LogicalDirection.Forward);
                if (next is null)
                {
                    break;
                }

                navigator = next;
            }

            return LuaEditorRichTextBox.Document.ContentEnd;
        }
        private void AppendToken(InlineCollection inlines, LuaTextToken token)
        {
            int index = 0;
            while (index < token.Text.Length)
            {
                int lineBreakIndex = token.Text.IndexOfAny(new[] { '\r', '\n' }, index);
                if (lineBreakIndex < 0)
                {
                    AddRun(inlines, token.Text[index..], token.ForegroundResourceKey);
                    return;
                }

                if (lineBreakIndex > index)
                {
                    AddRun(inlines, token.Text[index..lineBreakIndex], token.ForegroundResourceKey);
                }

                if (token.Text[lineBreakIndex] == '\r' &&
                    lineBreakIndex + 1 < token.Text.Length &&
                    token.Text[lineBreakIndex + 1] == '\n')
                {
                    index = lineBreakIndex + 2;
                }
                else
                {
                    index = lineBreakIndex + 1;
                }

                inlines.Add(new LineBreak());
            }
        }
        private static void AddRun(InlineCollection inlines, string text, string foregroundResourceKey)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            Run run = new(text);
            run.SetResourceReference(TextElement.ForegroundProperty, foregroundResourceKey);
            inlines.Add(run);
        }
        private static List<LuaTextToken> TokenizeLua(string text)
        {
            List<LuaTextToken> tokens = new();
            int index = 0;

            while (index < text.Length)
            {
                if (text[index] is '\r' or '\n')
                {
                    if (text[index] == '\r' &&
                        index + 1 < text.Length &&
                        text[index + 1] == '\n')
                    {
                        tokens.Add(new LuaTextToken("\r\n", LuaPlainTextBrushKey));
                        index += 2;
                    }
                    else
                    {
                        tokens.Add(new LuaTextToken(text[index].ToString(), LuaPlainTextBrushKey));
                        index++;
                    }

                    continue;
                }

                if (TryReadBlockComment(text, ref index, out string? blockComment))
                {
                    tokens.Add(new LuaTextToken(blockComment!, LuaCommentBrushKey));
                    continue;
                }

                if (TryReadLineComment(text, ref index, out string? lineComment))
                {
                    tokens.Add(new LuaTextToken(lineComment!, LuaCommentBrushKey));
                    continue;
                }

                if (TryReadQuotedString(text, ref index, out string? quotedString))
                {
                    tokens.Add(new LuaTextToken(quotedString!, LuaStringBrushKey));
                    continue;
                }

                if (TryReadLongString(text, ref index, out string? longString))
                {
                    tokens.Add(new LuaTextToken(longString!, LuaStringBrushKey));
                    continue;
                }

                if (char.IsDigit(text[index]))
                {
                    int start = index;
                    index++;
                    while (index < text.Length &&
                           (char.IsLetterOrDigit(text[index]) || text[index] == '.'))
                    {
                        index++;
                    }

                    tokens.Add(new LuaTextToken(text[start..index], LuaNumberBrushKey));
                    continue;
                }

                if (IsIdentifierStart(text[index]))
                {
                    int start = index;
                    index++;
                    while (index < text.Length && IsIdentifierPart(text[index]))
                    {
                        index++;
                    }

                    string identifier = text[start..index];
                    string foregroundKey = LuaPlainTextBrushKey;
                    if (LuaKeywords.Contains(identifier, StringComparer.Ordinal))
                    {
                        foregroundKey = LuaKeywordBrushKey;
                    }
                    else
                    {
                        int next = index;
                        while (next < text.Length && char.IsWhiteSpace(text[next]))
                        {
                            next++;
                        }

                        if (next < text.Length && text[next] == '(')
                        {
                            foregroundKey = LuaFunctionBrushKey;
                        }
                    }

                    tokens.Add(new LuaTextToken(identifier, foregroundKey));
                    continue;
                }

                tokens.Add(new LuaTextToken(text[index].ToString(), LuaPlainTextBrushKey));
                index++;
            }

            return tokens;
        }
        private static bool IsIdentifierStart(char value)
        {
            return char.IsLetter(value) || value == '_';
        }
        private static bool TryReadBlockComment(string text, ref int index, out string? value)
        {
            value = null;
            if (!text.AsSpan(index).StartsWith("--[[", StringComparison.Ordinal))
            {
                return false;
            }

            int end = text.IndexOf("]]", index + 4, StringComparison.Ordinal);
            end = end < 0 ? text.Length : end + 2;
            value = text[index..end];
            index = end;
            return true;
        }

        private static bool TryReadLineComment(string text, ref int index, out string? value)
        {
            value = null;
            if (!text.AsSpan(index).StartsWith("--", StringComparison.Ordinal))
            {
                return false;
            }

            int end = text.IndexOfAny(new[] { '\r', '\n' }, index);
            end = end < 0 ? text.Length : end;
            value = text[index..end];
            index = end;
            return true;
        }

        private static bool TryReadQuotedString(string text, ref int index, out string? value)
        {
            value = null;
            if (text[index] is not ('\'' or '"'))
            {
                return false;
            }

            char quote = text[index];
            int start = index++;
            bool escape = false;
            while (index < text.Length)
            {
                char current = text[index++];
                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (current == '\\')
                {
                    escape = true;
                    continue;
                }

                if (current == quote)
                {
                    break;
                }
            }

            value = text[start..index];
            return true;
        }

        private void CompileLuaButton_Click(object sender, RoutedEventArgs e)
        {
            string script = GetLuaEditorText();
            UpdateScriptTextProperty(script);
            if (TryExecuteLuaScript(script, out string message))
            {
                LuaCompileResultText = message;
                SetLuaEditorFeedback($"执行完成。", SuccessBrush);
            }
            else
            {
                LuaCompileResultText = message;
                SetLuaEditorFeedback($"执行失败，请查看结果。", WarningBrush);
            }
        }
        private void SetLuaEditorFeedback(string text, Brush brush)
        {
            LuaEditorStatusBrush = brush;
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
                return "执行完成，无返回值。";
            }

            return string.Join(Environment.NewLine, results.Select(FormatLuaResultValue));
        }
        private static bool TryReadLongString(string text, ref int index, out string? value)
        {
            value = null;
            if (!text.AsSpan(index).StartsWith("[[", StringComparison.Ordinal))
            {
                return false;
            }

            int end = text.IndexOf("]]", index + 2, StringComparison.Ordinal);
            end = end < 0 ? text.Length : end + 2;
            value = text[index..end];
            index = end;
            return true;
        }
        private static Brush CreateBrush(string color)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        }
    }
}
