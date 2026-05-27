using System.Globalization;
using System.Text.RegularExpressions;

namespace VideoArchiveManager.Core.Helpers;

public class ParsedFolderName
{
    public DateTime? FolderDate { get; init; }
    public string? LocationText { get; init; }
    public string? ContextText { get; init; }
}

public static class FolderNameParser
{
    private static readonly Regex DatePrefix = new(
        @"^\s*(?<date>\d{4}-\d{2}-\d{2})\b\s*(?<rest>.*)$",
        RegexOptions.Compiled);

    public static ParsedFolderName Parse(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return new ParsedFolderName();
        }

        var name = folderName.Trim();
        DateTime? date = null;
        string remainder = name;

        var dateMatch = DatePrefix.Match(name);
        if (dateMatch.Success &&
            DateTime.TryParseExact(
                dateMatch.Groups["date"].Value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            date = parsed;
            remainder = dateMatch.Groups["rest"].Value.Trim();
            remainder = TrimLeadingSeparators(remainder);
        }

        if (string.IsNullOrWhiteSpace(remainder))
        {
            return new ParsedFolderName { FolderDate = date };
        }

        var parts = remainder
            .Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToArray();

        string? location = null;
        string? context = null;

        if (parts.Length >= 2)
        {
            location = parts[0];
            context = string.Join(" - ", parts.Skip(1));
        }
        else if (parts.Length == 1)
        {
            if (date.HasValue)
            {
                location = parts[0];
            }
            else
            {
                context = parts[0];
            }
        }

        return new ParsedFolderName
        {
            FolderDate = date,
            LocationText = location,
            ContextText = context
        };
    }

    private static string TrimLeadingSeparators(string value)
    {
        return value.TrimStart(' ', '\t', '-', '_').Trim();
    }
}
