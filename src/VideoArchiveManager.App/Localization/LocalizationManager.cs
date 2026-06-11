// Find That Shot - organize and search a large local video archive.
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Ingve Moss Liknes <findthatshot@ingve.no>
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.
using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Resources;

namespace VideoArchiveManager.App.Localization;

// One selectable UI language: the .NET culture name (e.g. "nb-NO") plus the
// label shown in the Settings picker (always in its own language so a user can
// recognise it regardless of the current UI language).
public sealed record LanguageOption(string Culture, string DisplayName);

// Central runtime localization hub. Backed by the Strings.resx family (neutral
// English + per-culture satellites), it exposes string lookup via an indexer so
// XAML can bind through the {loc:Tr Key} markup extension. Switching language
// raises the indexer-change notification ("Item[]"), so every {loc:Tr} binding
// re-pulls and the visible UI updates live — no restart for newly-built bindings.
//
// Strings are looked up by key; a missing key returns the key itself so
// untranslated UI stays legible and gaps are obvious during development.
public sealed class LocalizationManager : INotifyPropertyChanged
{
    public static LocalizationManager Instance { get; } = new();

    // Base name = RootNamespace + folder + file (no culture/extension). The
    // neutral Strings.resx compiles to "<asm>.Localization.Strings.resources".
    private static readonly ResourceManager Resources = new(
        "VideoArchiveManager.App.Localization.Strings",
        typeof(LocalizationManager).Assembly);

    private CultureInfo _currentCulture = CultureInfo.CurrentUICulture;

    // Friendlier labels than CultureInfo.NativeName for cultures we ship today.
    private static readonly Dictionary<string, string> DisplayNameOverrides =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = "English",
            ["nb"] = "Norsk (bokmål)",
            ["nb-NO"] = "Norsk (bokmål)",
        };

    private LocalizationManager() { }

    public event PropertyChangedEventHandler? PropertyChanged;

    // English plus every culture that has a Strings.<culture>.resx satellite
    // (e.g. added via a Crowdin pull request). No code change needed when a
    // new translation lands — only the resource file.
    public IReadOnlyList<LanguageOption> AvailableLanguages { get; } = DiscoverAvailableLanguages();

    public CultureInfo CurrentCulture => _currentCulture;

    // Indexer used by {loc:Tr Key} bindings (and code-behind via this[key]).
    public string this[string key]
    {
        get
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            return Resources.GetString(key, _currentCulture) ?? key;
        }
    }

    // Convenience for code-behind that needs a formatted string. Args are
    // nullable to mirror string.Format (which renders null as empty) so callers
    // can pass optional paths / messages without null-forgiving gymnastics.
    public string Format(string key, params object?[] args)
    {
        var pattern = this[key];
        try
        {
            return string.Format(_currentCulture, pattern, args);
        }
        catch (FormatException)
        {
            return pattern;
        }
    }

    public void SetCulture(string? cultureName)
    {
        CultureInfo culture;
        if (string.IsNullOrWhiteSpace(cultureName))
        {
            // Null/empty means "follow the operating system".
            culture = CultureInfo.InstalledUICulture;
        }
        else
        {
            try
            {
                culture = CultureInfo.GetCultureInfo(cultureName);
            }
            catch (CultureNotFoundException)
            {
                culture = CultureInfo.InstalledUICulture;
            }
        }

        SetCulture(culture);
    }

    public void SetCulture(CultureInfo culture)
    {
        if (culture is null) return;

        _currentCulture = culture;
        // Keep thread + future-thread UI cultures aligned so any
        // ResourceManager/format calls elsewhere agree with the picker.
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        // "Item[]" is the WPF convention to invalidate every indexer binding
        // on this source, refreshing all {loc:Tr} text in place.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentCulture)));
    }

    private static IReadOnlyList<LanguageOption> DiscoverAvailableLanguages()
    {
        var byCulture = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = GetDisplayName("en"),
        };

        var mainAssembly = typeof(LocalizationManager).Assembly;
        var assemblyDir = Path.GetDirectoryName(mainAssembly.Location);
        if (assemblyDir is null)
            return OrderLanguages(byCulture);

        var satelliteDllName = $"{mainAssembly.GetName().Name}.resources.dll";
        foreach (var cultureDir in Directory.EnumerateDirectories(assemblyDir))
        {
            var cultureName = Path.GetFileName(cultureDir);
            if (!IsPlausibleCultureName(cultureName)) continue;
            if (cultureName.Equals("en", StringComparison.OrdinalIgnoreCase)) continue;

            var satellitePath = Path.Combine(cultureDir, satelliteDllName);
            if (!File.Exists(satellitePath)) continue;

            try
            {
                var satellite = Assembly.LoadFrom(satellitePath);
                if (!HasStringResources(satellite, cultureName)) continue;

                byCulture[cultureName] = GetDisplayName(cultureName);
            }
            catch (FileLoadException)
            {
            }
            catch (BadImageFormatException)
            {
            }
        }

        return OrderLanguages(byCulture);
    }

    private static IReadOnlyList<LanguageOption> OrderLanguages(
        Dictionary<string, string> byCulture) =>
        byCulture
            .OrderBy(kv => kv.Key.Equals("en", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(kv => kv.Value, StringComparer.CurrentCultureIgnoreCase)
            .Select(kv => new LanguageOption(kv.Key, kv.Value))
            .ToList();

    private static bool IsPlausibleCultureName(string name)
    {
        if (name.Length is < 2 or > 11) return false;

        foreach (var ch in name)
        {
            if (!char.IsLetter(ch) && ch != '-') return false;
        }

        return true;
    }

    private static bool HasStringResources(Assembly satellite, string cultureName)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            var rm = new ResourceManager(
                "VideoArchiveManager.App.Localization.Strings",
                satellite);
            var set = rm.GetResourceSet(culture, tryParents: false, createIfNotExists: false);
            return set is not null && set.Cast<DictionaryEntry>().Any();
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    private static string GetDisplayName(string cultureName)
    {
        if (DisplayNameOverrides.TryGetValue(cultureName, out var name))
            return name;

        try
        {
            return CultureInfo.GetCultureInfo(cultureName).NativeName;
        }
        catch (CultureNotFoundException)
        {
            return cultureName;
        }
    }
}
