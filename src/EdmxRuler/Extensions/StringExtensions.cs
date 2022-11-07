﻿using System.Diagnostics;
using System.Globalization;

namespace EdmxRuler.Extensions;

internal static class StringExtensions {
    /// <summary> Return null if the given value is empty.  Return the original value otherwise. </summary>
    [DebuggerStepThrough]
    public static string NullIfEmpty(this string str) { return string.IsNullOrEmpty(str) ? null : str; }

    /// <summary> Return null if the given value is whitespace or empty.  Return the original value otherwise. </summary>
    [DebuggerStepThrough]
    public static string NullIfWhitespace(this string str) { return string.IsNullOrWhiteSpace(str) ? null : str; }

    /// <summary> Indicates whether the specified string is null or an System.String.Empty string. </summary>
    [DebuggerStepThrough]
    public static bool IsNullOrEmpty(this string str) { return string.IsNullOrEmpty(str); }

    /// <summary> Indicates whether the specified string is null or an System.String.Empty string. </summary>
    [DebuggerStepThrough]
    public static bool HasCharacters(this string str) { return !string.IsNullOrEmpty(str); }

    /// <summary> Indicates whether a specified string is null, empty, or consists only of white-space characters. </summary>
    [DebuggerStepThrough]
    public static bool IsNullOrWhiteSpace(this string str) { return string.IsNullOrWhiteSpace(str); }

    /// <summary> Indicates whether a specified string is null, empty, or consists only of white-space characters. </summary>
    [DebuggerStepThrough]
    public static bool HasNonWhiteSpace(this string str) { return !string.IsNullOrWhiteSpace(str); }

    /// <summary>
    /// Concatenates the members of a constructed System.Collections.Generic.IEnumerable
    /// <T> collection of type System.String, using the specified separator between each member.
    /// </summary>
    [DebuggerStepThrough]
    public static string Join(this IEnumerable<string> strs, string separator = ", ") {
        return string.Join(separator, strs);
    }

    /// <summary> Take the first string that is not null or empty </summary>
    [DebuggerStepThrough]
    public static string Coalesce(this string str, params string[] strings) {
        return string.IsNullOrEmpty(str) ? strings.FirstOrDefault(s => !string.IsNullOrEmpty(s)) : str;
    }

    /// <summary> Take the first string that is not null or empty </summary>
    [DebuggerStepThrough]
    public static string Coalesce(this string str, params Func<string>[] strings) {
        return string.IsNullOrEmpty(str)
            ? strings.Select(o => o?.Invoke()).FirstOrDefault(s => !string.IsNullOrEmpty(s))
            : str;
    }

    /// <summary> Take the first string that is not null or white space </summary>
    [DebuggerStepThrough]
    public static string CoalesceWhiteSpace(this string str, params string[] strings) {
        return string.IsNullOrWhiteSpace(str)
            ? (strings?.Length > 0 ? strings.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) : null)
            : str;
    }

    /// <summary> Take the first string that is not null or white space </summary>
    [DebuggerStepThrough]
    public static string CoalesceWhiteSpace(this string str, params Func<string>[] strings) {
        return string.IsNullOrWhiteSpace(str)
            ? (strings?.Length > 0
                ? strings.Select(o => o?.Invoke()).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))
                : null)
            : str;
    }

    /// <summary> will return true if the string is a valid symbol name </summary>
    internal static bool IsValidSymbolName(this string str) {
        if (string.IsNullOrEmpty(str)) return false;
        for (var i = 0; i < str.Length; i++) {
            var c = str[i];
            if (!IsValidInIdentifier(c, i == 0)) return false;
        }

        return true;
    }

    /// <summary> will return _ in place of invalid chars </summary>
    internal static string CleanseSymbolName(this string str) {
        if (string.IsNullOrEmpty(str)) return "";
        return new string(CleanseSymbolNameChars(str).ToArray());

        static IEnumerable<char> CleanseSymbolNameChars(string str) {
            if (string.IsNullOrEmpty(str)) yield break;
            for (var i = 0; i < str.Length; i++) {
                var c = str[i];
                if (IsValidInIdentifier(c, i == 0))
                    yield return c;
                else
                    yield return '_';
            }
        }
    }

    private static bool IsValidInIdentifier(this char c, bool firstChar = true) {
        switch (char.GetUnicodeCategory(c)) {
            case UnicodeCategory.UppercaseLetter:
            case UnicodeCategory.LowercaseLetter:
            case UnicodeCategory.TitlecaseLetter:
            case UnicodeCategory.ModifierLetter:
            case UnicodeCategory.OtherLetter:
                // Always allowed in C# identifiers
                return true;

            case UnicodeCategory.LetterNumber:
            case UnicodeCategory.NonSpacingMark:
            case UnicodeCategory.SpacingCombiningMark:
            case UnicodeCategory.DecimalDigitNumber:
            case UnicodeCategory.ConnectorPunctuation:
            case UnicodeCategory.Format:
                // Only allowed after first char
                return c.In('_') || !firstChar;
            default:
                return false;
        }
    }
}