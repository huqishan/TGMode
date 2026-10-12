using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Shared.Infrastructure.Lua;

/// <summary>
/// 提供 Lua 脚本执行前后可复用的格式化与返回值识别逻辑。
/// </summary>
public static class LuaExecutionHelper
{
    #region Lua 字面量

    /// <summary>
    /// 将文本转换为可安全嵌入 Lua 脚本的字符串字面量。
    /// </summary>
    public static string ToLuaStringLiteral(string value)
    {
        value ??= string.Empty;
        return "\"" + string.Concat(value.Select(character => character switch
        {
            '\\' => "\\\\",
            '"' => "\\\"",
            '\r' => "\\r",
            '\n' => "\\n",
            '\t' => "\\t",
            _ => character.ToString()
        })) + "\"";
    }

    #endregion

    #region 返回值键

    /// <summary>
    /// 按协议预览规则从 Lua 返回数据提取返回值键；标量使用 Data，无返回值不生成键。
    /// </summary>
    public static IReadOnlyList<string> GetReturnValueNames(object? value)
    {
        IEnumerable<string> names = value switch
        {
            IReadOnlyDictionary<string, object?> readOnlyDictionary => readOnlyDictionary.Keys,
            IDictionary<string, object?> dictionary => dictionary.Keys,
            null => Enumerable.Empty<string>(),
            _ => new[] { "Data" }
        };

        return names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// 扫描脚本文本中的所有 return，并合并直接返回的表字段；非表表达式使用 Data。
    /// </summary>
    public static IReadOnlyList<string> GetReturnValueNamesFromScript(string? script)
    {
        if (string.IsNullOrWhiteSpace(script))
        {
            return Array.Empty<string>();
        }

        List<string> names = new();
        int searchIndex = 0;
        while ((searchIndex = script.IndexOf("return", searchIndex, StringComparison.Ordinal)) >= 0)
        {
            int returnIndex = searchIndex;
            searchIndex += "return".Length;
            if ((returnIndex > 0 && IsLuaIdentifierCharacter(script[returnIndex - 1])) ||
                (searchIndex < script.Length && IsLuaIdentifierCharacter(script[searchIndex])))
            {
                continue;
            }

            int expressionIndex = searchIndex;
            while (expressionIndex < script.Length && char.IsWhiteSpace(script[expressionIndex]))
            {
                expressionIndex++;
            }

            if (expressionIndex >= script.Length || script[expressionIndex] == ';' ||
                IsLuaKeywordAt(script, expressionIndex, "end") ||
                IsLuaKeywordAt(script, expressionIndex, "else") ||
                IsLuaKeywordAt(script, expressionIndex, "elseif") ||
                IsLuaKeywordAt(script, expressionIndex, "nil"))
            {
                continue;
            }

            if (script[expressionIndex] == '{')
            {
                names.AddRange(GetLuaTableFieldNames(script, expressionIndex));
            }
            else
            {
                names.Add("Data");
            }
        }

        return names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    #endregion

    #region Lua 文本解析

    /// <summary>
    /// 从表构造器中读取顶层字段，忽略嵌套表和字符串内部的分隔符。
    /// </summary>
    private static IReadOnlyList<string> GetLuaTableFieldNames(string script, int openBraceIndex)
    {
        List<string> names = new();
        int fieldStart = openBraceIndex + 1;
        int nestedBraceDepth = 0;
        int parenthesisDepth = 0;
        int bracketDepth = 0;
        int arrayFieldIndex = 0;
        char stringDelimiter = '\0';

        for (int index = fieldStart; index < script.Length; index++)
        {
            char character = script[index];
            if (stringDelimiter != '\0')
            {
                if (character == '\\')
                {
                    index++;
                }
                else if (character == stringDelimiter)
                {
                    stringDelimiter = '\0';
                }

                continue;
            }

            if (character is '\'' or '"')
            {
                stringDelimiter = character;
                continue;
            }

            if (character == '-' && index + 1 < script.Length && script[index + 1] == '-')
            {
                while (index < script.Length && script[index] is not '\r' and not '\n')
                {
                    index++;
                }

                continue;
            }

            switch (character)
            {
                case '{':
                    nestedBraceDepth++;
                    break;
                case '}':
                    if (nestedBraceDepth > 0)
                    {
                        nestedBraceDepth--;
                    }
                    else
                    {
                        AddLuaTableFieldName(script[fieldStart..index], names, ref arrayFieldIndex);
                        return names;
                    }
                    break;
                case '(':
                    parenthesisDepth++;
                    break;
                case ')':
                    parenthesisDepth = Math.Max(0, parenthesisDepth - 1);
                    break;
                case '[':
                    bracketDepth++;
                    break;
                case ']':
                    bracketDepth = Math.Max(0, bracketDepth - 1);
                    break;
                case ',' or ';' when nestedBraceDepth == 0 && parenthesisDepth == 0 && bracketDepth == 0:
                    AddLuaTableFieldName(script[fieldStart..index], names, ref arrayFieldIndex);
                    fieldStart = index + 1;
                    break;
            }
        }

        return names;
    }

    /// <summary>
    /// 提取一个表字段的显式键；无键字段按 Lua 数组字段生成递增数字键。
    /// </summary>
    private static void AddLuaTableFieldName(string field, ICollection<string> names, ref int arrayFieldIndex)
    {
        field = field.Trim();
        if (field.Length == 0)
        {
            return;
        }

        int assignmentIndex = GetTopLevelAssignmentIndex(field);
        if (assignmentIndex < 0)
        {
            names.Add((++arrayFieldIndex).ToString(CultureInfo.InvariantCulture));
            return;
        }

        string key = field[..assignmentIndex].Trim();
        if (IsLuaIdentifier(key))
        {
            names.Add(key);
            return;
        }

        if (key.Length < 2 || key[0] != '[' || key[^1] != ']')
        {
            return;
        }

        string literalKey = key[1..^1].Trim();
        if (literalKey.Length >= 2 && literalKey[0] is '\'' or '"' && literalKey[^1] == literalKey[0])
        {
            names.Add(literalKey[1..^1]
                .Replace("\\\"", "\"", StringComparison.Ordinal)
                .Replace("\\'", "'", StringComparison.Ordinal)
                .Replace("\\\\", "\\", StringComparison.Ordinal));
        }
        else if (double.TryParse(literalKey, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            names.Add(literalKey);
        }
    }

    /// <summary>
    /// 查找表字段中不位于括号、索引或字符串里的赋值符。
    /// </summary>
    private static int GetTopLevelAssignmentIndex(string field)
    {
        int braceDepth = 0;
        int parenthesisDepth = 0;
        int bracketDepth = 0;
        char stringDelimiter = '\0';

        for (int index = 0; index < field.Length; index++)
        {
            char character = field[index];
            if (stringDelimiter != '\0')
            {
                if (character == '\\')
                {
                    index++;
                }
                else if (character == stringDelimiter)
                {
                    stringDelimiter = '\0';
                }

                continue;
            }

            if (character is '\'' or '"')
            {
                stringDelimiter = character;
                continue;
            }

            switch (character)
            {
                case '{': braceDepth++; break;
                case '}': braceDepth = Math.Max(0, braceDepth - 1); break;
                case '(': parenthesisDepth++; break;
                case ')': parenthesisDepth = Math.Max(0, parenthesisDepth - 1); break;
                case '[': bracketDepth++; break;
                case ']': bracketDepth = Math.Max(0, bracketDepth - 1); break;
                case '=' when braceDepth == 0 && parenthesisDepth == 0 && bracketDepth == 0:
                    return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// 判断文本位置是否以指定 Lua 关键字开头。
    /// </summary>
    private static bool IsLuaKeywordAt(string text, int index, string keyword)
    {
        return text.AsSpan(index).StartsWith(keyword, StringComparison.Ordinal) &&
            (index + keyword.Length >= text.Length || !IsLuaIdentifierCharacter(text[index + keyword.Length]));
    }

    /// <summary>
    /// 判断字符是否可出现在 Lua 标识符中。
    /// </summary>
    private static bool IsLuaIdentifierCharacter(char character)
    {
        return character == '_' || char.IsLetterOrDigit(character);
    }

    /// <summary>
    /// 判断文本是否为 Lua 标识符。
    /// </summary>
    private static bool IsLuaIdentifier(string text)
    {
        return text.Length > 0 &&
            (text[0] == '_' || char.IsLetter(text[0])) &&
            text.Skip(1).All(IsLuaIdentifierCharacter);
    }

    #endregion
}
