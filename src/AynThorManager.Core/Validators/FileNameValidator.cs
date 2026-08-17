using System.Text;
using AynThorManager.Core.Models;

namespace AynThorManager.Core.Validators;

public static class FileNameValidator
{
    private static readonly char[] InvalidChars = ['/', '\\', ':', '*', '?', '"', '<', '>', '|'];
    private static readonly HashSet<string> ReservedNames = [".", ".."];
    private const int MaxUtf8Bytes = 255;

    public static Result Validate(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(new Error(
                "INVALID_NAME",
                "File or directory name cannot be empty or whitespace-only."));
        }

        if (ReservedNames.Contains(name))
        {
            return Result.Failure(new Error(
                "INVALID_NAME",
                $"The name \"{name}\" is reserved and cannot be used."));
        }

        var invalidCharsFound = FindInvalidCharacters(name);
        if (invalidCharsFound.Count > 0)
        {
            var charList = string.Join(", ", invalidCharsFound.Select(FormatChar));
            return Result.Failure(new Error(
                "INVALID_NAME",
                $"Name contains invalid characters: {charList}",
                new Dictionary<string, object>
                {
                    ["invalidCharacters"] = invalidCharsFound
                }));
        }

        var utf8ByteLength = Encoding.UTF8.GetByteCount(name);
        if (utf8ByteLength > MaxUtf8Bytes)
        {
            return Result.Failure(new Error(
                "NAME_TOO_LONG",
                $"Name exceeds the maximum of {MaxUtf8Bytes} bytes in UTF-8 encoding (actual: {utf8ByteLength} bytes).",
                new Dictionary<string, object>
                {
                    ["maxBytes"] = MaxUtf8Bytes,
                    ["actualBytes"] = utf8ByteLength
                }));
        }

        return Result.Success();
    }

    private static List<char> FindInvalidCharacters(string name)
    {
        var found = new List<char>();
        var seen = new HashSet<char>();

        foreach (var ch in name)
        {
            if (IsInvalidCharacter(ch) && seen.Add(ch))
            {
                found.Add(ch);
            }
        }

        return found;
    }

    private static bool IsInvalidCharacter(char ch)
    {
        if (ch <= '\u001F')
            return true;

        return Array.IndexOf(InvalidChars, ch) >= 0;
    }

    private static string FormatChar(char ch)
    {
        if (ch <= '\u001F')
            return $"U+{(int)ch:X4}";

        return $"'{ch}'";
    }
}
