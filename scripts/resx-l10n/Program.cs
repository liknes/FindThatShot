using ClosedXML.Excel;
using ResxL10n;

return Run(args);

static int Run(string[] args)
{
    if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
    {
        PrintHelp();
        return 0;
    }

    try
    {
        var options = ParseOptions(args);
        return options.Command switch
        {
            "export" => Export(options),
            "import" => Import(options),
            _ => UnknownCommand(options.Command),
        };
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
}

static int UnknownCommand(string command)
{
    Console.Error.WriteLine($"Unknown command: {command}");
    PrintHelp();
    return 1;
}

static void PrintHelp()
{
    Console.WriteLine(
        """
        resx-l10n — export / import Find That Shot UI strings for Excel translation.

        Usage:
          dotnet run --project scripts/resx-l10n -- export [--dir <localization-dir>] [--out <file.xlsx>]
          dotnet run --project scripts/resx-l10n -- import [--dir <localization-dir>] [--in <file.xlsx>] [--dry-run]

        Or from the repo root:
          pwsh ./scripts/resx-l10n.ps1 export
          pwsh ./scripts/resx-l10n.ps1 import

        Defaults:
          --dir  src/VideoArchiveManager.App/Localization
          --out  localization-strings.xlsx (next to the resx files)
          --in   localization-strings.xlsx

        Spreadsheet columns:
          Key      — stable identifier (do not edit)
          Comment  — translator hint from the English resx (do not edit)
          en       — English source (Strings.resx)
          <culture> — one column per Strings.<culture>.resx (e.g. nb-NO)

        Rules for translators:
          • Keep placeholders like {0} exactly as in English unless the target language needs reordering.
          • In WPF strings, & marks a keyboard accelerator (e.g. &File). Keep the same position where possible.
          • Leave a translation cell empty to remove that key from a satellite resx (falls back to English).
        """);
}

static Options ParseOptions(string[] args)
{
    var command = args[0].ToLowerInvariant();
    string? dir = null;
    string? input = null;
    string? output = null;
    var dryRun = false;

    for (var i = 1; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--dir":
                dir = RequireValue(args, ref i, "--dir");
                break;
            case "--in":
                input = RequireValue(args, ref i, "--in");
                break;
            case "--out":
                output = RequireValue(args, ref i, "--out");
                break;
            case "--dry-run":
                dryRun = true;
                break;
            default:
                throw new ArgumentException($"Unknown argument: {args[i]}");
        }
    }

    var repoRoot = RepoPaths.FindRoot();
    var localizationDir = dir is not null
        ? Path.GetFullPath(dir, repoRoot)
        : Path.Combine(repoRoot, "src", "VideoArchiveManager.App", "Localization");

    var defaultWorkbook = Path.Combine(localizationDir, "localization-strings.xlsx");

    return new Options(
        command,
        localizationDir,
        input ?? defaultWorkbook,
        output ?? defaultWorkbook,
        dryRun);
}

static string RequireValue(string[] args, ref int index, string flag)
{
    if (++index >= args.Length)
        throw new ArgumentException($"Missing value for {flag}.");

    return args[index];
}

static int Export(Options options)
{
    var sourcePath = Path.Combine(options.LocalizationDir, ResxFile.SourceFileName);
    if (!File.Exists(sourcePath))
        throw new FileNotFoundException($"Source file not found: {sourcePath}");

    var source = ResxFile.ReadEntries(sourcePath);
    var cultures = DiscoverCultures(options.LocalizationDir);
    var satellites = cultures.ToDictionary(
        culture => culture,
        culture => ResxFile.ReadEntries(Path.Combine(options.LocalizationDir, ResxFile.SatelliteFileName(culture))),
        StringComparer.OrdinalIgnoreCase);

    using var workbook = new XLWorkbook();
    var sheet = workbook.Worksheets.Add("Strings");

    var headers = new List<string> { "Key", "Comment", "en" };
    headers.AddRange(cultures);

    for (var col = 0; col < headers.Count; col++)
        sheet.Cell(1, col + 1).Value = headers[col];

    var headerRow = sheet.Row(1);
    headerRow.Style.Font.Bold = true;
    headerRow.Style.Fill.BackgroundColor = XLColor.FromHtml("#F3F4F6");
    sheet.SheetView.FreezeRows(1);
    sheet.Range(1, 1, 1, headers.Count).SetAutoFilter();

    var rowIndex = 2;
    foreach (var key in source.Keys.OrderBy(k => k, StringComparer.Ordinal))
    {
        var entry = source[key];
        sheet.Cell(rowIndex, 1).Value = key;
        sheet.Cell(rowIndex, 2).Value = entry.Comment ?? string.Empty;
        sheet.Cell(rowIndex, 3).Value = entry.Value;

        for (var i = 0; i < cultures.Count; i++)
        {
            var culture = cultures[i];
            if (satellites[culture].TryGetValue(key, out var translated))
                sheet.Cell(rowIndex, 4 + i).Value = translated.Value;
        }

        rowIndex++;
    }

    sheet.Column(1).Width = 36;
    sheet.Column(2).Width = 28;
    for (var col = 3; col <= headers.Count; col++)
        sheet.Column(col).Width = 48;

    Directory.CreateDirectory(Path.GetDirectoryName(options.OutputPath)!);
    workbook.SaveAs(options.OutputPath);

    Console.WriteLine($"Exported {source.Count} keys × {cultures.Count} translation column(s) to:");
    Console.WriteLine($"  {options.OutputPath}");
    return 0;
}

static int Import(Options options)
{
    if (!File.Exists(options.InputPath))
        throw new FileNotFoundException($"Workbook not found: {options.InputPath}");

    using var workbook = new XLWorkbook(options.InputPath);
    var sheet = workbook.Worksheet(1);
    var usedRange = sheet.RangeUsed();
    if (usedRange is null)
        throw new InvalidOperationException("Workbook is empty.");

    var headers = ReadHeaders(sheet);
    ValidateHeaders(headers);

    var rows = ReadRows(sheet, headers);
    if (rows.Count == 0)
        throw new InvalidOperationException("No string rows found below the header.");

    var englishUpdates = rows.ToDictionary(r => r.Key, r => r.English, StringComparer.Ordinal);
    var cultureUpdates = headers.Cultures.ToDictionary(
        culture => culture,
        culture => rows
            .Where(r => r.Translations.ContainsKey(culture))
            .ToDictionary(r => r.Key, r => r.Translations[culture], StringComparer.Ordinal),
        StringComparer.OrdinalIgnoreCase);

    var warnings = PlaceholderWarnings(rows);
    foreach (var warning in warnings)
        Console.WriteLine($"Warning: {warning}");

    if (options.DryRun)
    {
        Console.WriteLine("Dry run — no files written.");
        Console.WriteLine($"  English keys to update: {englishUpdates.Count}");
        foreach (var culture in headers.Cultures)
            Console.WriteLine($"  {culture} keys to update: {cultureUpdates[culture].Count}");
        return 0;
    }

    var sourcePath = Path.Combine(options.LocalizationDir, ResxFile.SourceFileName);
    var englishChanged = ResxFile.WriteUpdates(sourcePath, englishUpdates, isSource: true);

    var anyCultureChanged = false;
    foreach (var culture in headers.Cultures)
    {
        var satellitePath = Path.Combine(options.LocalizationDir, ResxFile.SatelliteFileName(culture));
        var changed = ResxFile.WriteUpdates(satellitePath, cultureUpdates[culture], isSource: false);
        anyCultureChanged |= changed;
        Console.WriteLine(changed
            ? $"Updated {culture}: {cultureUpdates[culture].Count} key(s)"
            : $"No changes for {culture}.");
    }

    Console.WriteLine(englishChanged
        ? $"Updated English: {englishUpdates.Count} key(s)"
        : "No changes for English.");

    if (!englishChanged && !anyCultureChanged)
        Console.WriteLine("Import complete — workbook matched the resx files; nothing was written.");
    else
        Console.WriteLine("Import complete.");
    return 0;
}

static IReadOnlyList<string> DiscoverCultures(string localizationDir)
{
    if (!Directory.Exists(localizationDir))
        return Array.Empty<string>();

    return Directory.EnumerateFiles(localizationDir, "Strings.*.resx")
        .Select(Path.GetFileNameWithoutExtension)
        .Select(name => name!["Strings.".Length..])
        .Where(culture => !string.IsNullOrWhiteSpace(culture))
        .OrderBy(culture => culture, StringComparer.OrdinalIgnoreCase)
        .ToList();
}

static SheetHeaders ReadHeaders(IXLWorksheet sheet)
{
    var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
    if (lastColumn < 3)
        throw new InvalidOperationException("Expected at least Key, Comment, and en columns.");

    var headers = new List<string>();
    for (var col = 1; col <= lastColumn; col++)
        headers.Add(sheet.Cell(1, col).GetString().Trim());

    return new SheetHeaders(headers);
}

static void ValidateHeaders(SheetHeaders headers)
{
    if (!string.Equals(headers.All[0], "Key", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Column A must be named Key.");

    if (!string.Equals(headers.All[1], "Comment", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Column B must be named Comment.");

    if (!string.Equals(headers.EnglishColumn, "en", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Column C must be named en.");
}

static List<ImportRow> ReadRows(IXLWorksheet sheet, SheetHeaders headers)
{
    var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
    var rows = new List<ImportRow>();

    for (var row = 2; row <= lastRow; row++)
    {
        var key = sheet.Cell(row, 1).GetString().Trim();
        if (string.IsNullOrEmpty(key))
            continue;

        var english = sheet.Cell(row, headers.EnglishIndex + 1).GetString();
        var translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < headers.Cultures.Count; i++)
        {
            var culture = headers.Cultures[i];
            var columnIndex = headers.CultureStartIndex + i + 1;
            translations[culture] = sheet.Cell(row, columnIndex).GetString();
        }

        rows.Add(new ImportRow(key, english, translations));
    }

    return rows;
}

static IEnumerable<string> PlaceholderWarnings(IReadOnlyList<ImportRow> rows)
{
    foreach (var row in rows)
    {
        var englishPlaceholders = CountPlaceholders(row.English);
        foreach (var (culture, value) in row.Translations)
        {
            if (string.IsNullOrEmpty(value))
                continue;

            var translatedPlaceholders = CountPlaceholders(value);
            if (translatedPlaceholders != englishPlaceholders)
            {
                yield return
                    $"{row.Key} [{culture}] placeholder count {translatedPlaceholders} != English {englishPlaceholders}.";
            }
        }
    }
}

static int CountPlaceholders(string text)
{
    var count = 0;
    for (var i = 0; i < text.Length - 1; i++)
    {
        if (text[i] == '{' && char.IsDigit(text[i + 1]))
            count++;
    }

    return count;
}

internal sealed record Options(
    string Command,
    string LocalizationDir,
    string InputPath,
    string OutputPath,
    bool DryRun);

internal sealed record SheetHeaders(IReadOnlyList<string> All)
{
    public int EnglishIndex => 2;
    public string EnglishColumn => All[EnglishIndex];
    public int CultureStartIndex => 3;
    public IReadOnlyList<string> Cultures => All.Skip(CultureStartIndex).ToList();
}

internal sealed record ImportRow(
    string Key,
    string English,
    IReadOnlyDictionary<string, string> Translations);

internal static class RepoPaths
{
    public static string FindRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "FindThatShot.sln")))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root (FindThatShot.sln).");
    }
}
