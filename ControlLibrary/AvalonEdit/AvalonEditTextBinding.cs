using ICSharpCode.AvalonEdit;
using System;
using System.Windows;
using System.Windows.Data;

namespace ControlLibrary.AvalonEdit
{
    public static class AvalonEditTextBinding
    {
        public static readonly DependencyProperty BindableTextProperty =
            DependencyProperty.RegisterAttached(
                "BindableText",
                typeof(string),
                typeof(AvalonEditTextBinding),
                new FrameworkPropertyMetadata(
                    string.Empty,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnBindableTextChanged));

        public static readonly DependencyProperty UseLuaHighlightingProperty =
            DependencyProperty.RegisterAttached(
                "UseLuaHighlighting",
                typeof(bool),
                typeof(AvalonEditTextBinding),
                new PropertyMetadata(false, OnUseLuaHighlightingChanged));

        private static readonly DependencyProperty IsTextChangeHandlerAttachedProperty =
            DependencyProperty.RegisterAttached(
                "IsTextChangeHandlerAttached",
                typeof(bool),
                typeof(AvalonEditTextBinding),
                new PropertyMetadata(false));

        private static readonly DependencyProperty IsInternalUpdateProperty =
            DependencyProperty.RegisterAttached(
                "IsInternalUpdate",
                typeof(bool),
                typeof(AvalonEditTextBinding),
                new PropertyMetadata(false));

        public static string GetBindableText(DependencyObject obj)
        {
            return (string?)obj.GetValue(BindableTextProperty) ?? string.Empty;
        }

        public static void SetBindableText(DependencyObject obj, string value)
        {
            obj.SetValue(BindableTextProperty, value ?? string.Empty);
        }

        public static bool GetUseLuaHighlighting(DependencyObject obj)
        {
            return (bool)obj.GetValue(UseLuaHighlightingProperty);
        }

        public static void SetUseLuaHighlighting(DependencyObject obj, bool value)
        {
            obj.SetValue(UseLuaHighlightingProperty, value);
        }

        private static void OnBindableTextChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
        {
            if (dependencyObject is not TextEditor editor)
            {
                return;
            }

            EnsureTextChangedHandler(editor);

            string text = NormalizeLineEndings(e.NewValue as string ?? string.Empty);
            if (GetIsInternalUpdate(editor) || string.Equals(NormalizeLineEndings(editor.Text), text, StringComparison.Ordinal))
            {
                return;
            }

            SetIsInternalUpdate(editor, true);
            try
            {
                editor.Text = text;
            }
            finally
            {
                SetIsInternalUpdate(editor, false);
            }
        }

        private static void OnUseLuaHighlightingChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
        {
            if (dependencyObject is not TextEditor editor || e.NewValue is not true)
            {
                return;
            }

            EnsureTextChangedHandler(editor);
            ConfigureLuaEditor(editor);
        }

        private static void EnsureTextChangedHandler(TextEditor editor)
        {
            if ((bool)editor.GetValue(IsTextChangeHandlerAttachedProperty))
            {
                return;
            }

            editor.TextChanged += Editor_TextChanged;
            editor.SetValue(IsTextChangeHandlerAttachedProperty, true);
        }

        private static void ConfigureLuaEditor(TextEditor editor)
        {
            editor.ShowLineNumbers = true;
            editor.WordWrap = false;
            editor.Options.ConvertTabsToSpaces = false;
            editor.Options.IndentationSize = 4;
            editor.Options.EnableHyperlinks = false;
            editor.Options.EnableEmailHyperlinks = false;
            editor.SyntaxHighlighting ??= LuaHighlightingDefinition.Load();
        }

        private static void Editor_TextChanged(object? sender, EventArgs e)
        {
            if (sender is not TextEditor editor || GetIsInternalUpdate(editor))
            {
                return;
            }

            string text = NormalizeLineEndings(editor.Text);
            if (string.Equals(GetBindableText(editor), text, StringComparison.Ordinal))
            {
                return;
            }

            SetIsInternalUpdate(editor, true);
            try
            {
                SetBindableText(editor, text);
                BindingOperations.GetBindingExpression(editor, BindableTextProperty)?.UpdateSource();
            }
            finally
            {
                SetIsInternalUpdate(editor, false);
            }
        }

        private static bool GetIsInternalUpdate(DependencyObject obj)
        {
            return (bool)obj.GetValue(IsInternalUpdateProperty);
        }

        private static void SetIsInternalUpdate(DependencyObject obj, bool value)
        {
            obj.SetValue(IsInternalUpdateProperty, value);
        }

        private static string NormalizeLineEndings(string text)
        {
            return (text ?? string.Empty)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\r", "\n", StringComparison.Ordinal)
                .Replace("\n", "\r\n", StringComparison.Ordinal);
        }
    }
}
