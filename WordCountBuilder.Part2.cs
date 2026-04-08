// WordCountBuilder.Part2.cs

using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;


namespace jpn_lang_dbm_desktop_app;

internal enum WordFormPreference
{
    DictionaryForm,
    SurfaceForm,
    CountBoth
}

internal enum KanaMergeTargets
{
    ToHiragana,
    ToKatakana,
    Neither
}

internal enum MathSymbolHandling
{
    Ignore,
    PassThrough,
    BindToNeighbors
}

internal sealed class WordCountSettings
{
    internal bool IgnoreDecorativeSymbolsAndEmoji;
    internal bool IgnoreParticlesAndCopula;
    internal bool IgnoreInterjections;
    internal bool IgnoreFillers;
    internal bool NormalizeIterationMarks;
    internal MathSymbolHandling HandleMathSymbols;
    internal bool MergeTeMiruConstructions;
    internal WordFormPreference FormPreference;
    internal bool MergeLatinCase;
    internal bool UnicodeNFCKNormalization;
    internal KanaMergeTargets MergeKanaTo;
    internal bool DeduplicateRepeatedChars;
    internal bool MergeNumericVariants;
    internal string UnknownTokenPolicy = "";
    internal string TokenKeyFields = "";
    internal bool IncludeReading;

    internal static WordCountSettings Load(SqliteConnection conn)
    {
        var s = new WordCountSettings();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT key, value FROM settings;";

        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            var key = rdr.GetString(0);
            var value = rdr.GetString(1);
            bool B() => value == "1";

            switch (key)
            {
                case "count.ignore_decorative_symbols_and_emoji": s.IgnoreDecorativeSymbolsAndEmoji = B(); break;
                case "count.ignore_particles_and_copula": s.IgnoreParticlesAndCopula = B(); break;
                case "count.ignore_interjections": s.IgnoreInterjections = B(); break;
                case "count.ignore_fillers": s.IgnoreFillers = B(); break;
                case "count.normalize_iteration_marks": s.NormalizeIterationMarks = B(); break;
                case "count.handle_math_symbols":
                {
                    if (!Enum.TryParse<MathSymbolHandling>(value, ignoreCase: true, out var h))
                        throw new InvalidOperationException($"Invalid MathSymbolHandling value: '{value}'");
                    s.HandleMathSymbols = h;
                    break;
                }
                case "count.form_preference":
                {
                    if (!Enum.TryParse<WordFormPreference>(value, ignoreCase: true, out var pref))
                        throw new InvalidOperationException($"Invalid WordFormPreference value: '{value}'");
                    s.FormPreference = pref;
                    break;
                }
                case "count.merge_latin_characters_if_different_case_and_character_set": s.MergeLatinCase = B(); break;
                case "count.do_unicode_nfkc_normalization": s.UnicodeNFCKNormalization = B(); break;
                case "count.merge_kana":
                {
                    if (!Enum.TryParse<KanaMergeTargets>(value, ignoreCase: true, out var km))
                        throw new InvalidOperationException($"Invalid KanaMergeTargets value: '{value}'");
                    s.MergeKanaTo = km;
                    break;
                }
                case "count.deduplicate_repeated_characters": s.DeduplicateRepeatedChars = B(); break;
                case "count.merge_numeric_variants": s.MergeNumericVariants = B(); break;
                case "count.unknown_token_policy": s.UnknownTokenPolicy = value; break;
                case "count.token_key_fields": s.TokenKeyFields = value; break;
                case "count.include_reading": s.IncludeReading = B(); break;
                case "count.merge_te_miru_constructions": s.MergeTeMiruConstructions = B(); break;
            }
        }

        return s;
    }
}

internal static class LinderaJsonHelpers
{
    internal static string GetSurface(JsonElement token)
    {
        if (token.ValueKind != JsonValueKind.Object)
            return "";

        if (!token.TryGetProperty("surface", out var s) || s.ValueKind != JsonValueKind.String)
            return "";

        return s.GetString() ?? "";
    }

    internal static JsonElement GetDetails(JsonElement token)
    {
        if (token.ValueKind != JsonValueKind.Object)
            return default;

        if (token.TryGetProperty("details", out var d) && d.ValueKind == JsonValueKind.Array)
            return d;

        return default;
    }

    internal static string GetDetail(JsonElement details, int index)
    {
        if (details.ValueKind != JsonValueKind.Array)
            return "";

        if (index < 0)
            return "";

        var i = 0;
        foreach (var el in details.EnumerateArray())
        {
            if (i == index)
                return el.ValueKind == JsonValueKind.String ? (el.GetString() ?? "") : "";
            i++;
        }

        return "";
    }

    internal static string BuildPosString(string pos1, string pos2, string pos3, string pos4)
    {
        var parts = new[] { pos1, pos2, pos3, pos4 }.Where(x => !string.IsNullOrEmpty(x) && x != "*");
        return string.Join("/", parts);
    }

    internal static JsonElement FindTokensArray(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
            return root;

        var found = FindTokensArrayRecursive(root, 0);
        if (found.HasValue)
            return found.Value;

        throw new InvalidOperationException(
            "Could not find a tokens array in Lindera JSON. Top-level kind: " + root.ValueKind
        );
    }

    private static JsonElement? FindTokensArrayRecursive(JsonElement el, int depth)
    {
        if (depth > 10)
            return null;

        if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("surface", out _))
                    return el;
            }
            return null;
        }

        if (el.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in el.EnumerateObject())
            {
                var candidate = FindTokensArrayRecursive(prop.Value, depth + 1);
                if (candidate.HasValue)
                    return candidate.Value;
            }
        }

        return null;
    }
}

internal static class TokenNormalization
{
    internal static string NormalizeKanaToHiragana(string s)
    {
        if (s.Length == 0)
            return s;

        var chars = s.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            var c = chars[i];

            // Katakana block → Hiragana
            if (c >= 'ァ' && c <= 'ヶ')
                chars[i] = (char)(c - 0x60);
        }

        return new string(chars);
    }

    internal static string NormalizeKanaToKatakana(string s)
    {
        if (s.Length == 0)
            return s;

        var chars = s.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            var c = chars[i];

            // Hiragana block → Katakana
            if (c >= 'ぁ' && c <= 'ゖ')
                chars[i] = (char)(c + 0x60);
        }

        return new string(chars);
    }

    internal static string Deduplicate(string s)
    {
        if (s.Length < 2)
            return s;

        var result = new List<char> { s[0] };
        for (int i = 1; i < s.Length; i++)
        {
            if (s[i] != s[i - 1])
                result.Add(s[i]);
        }
        return new string(result.ToArray());
    }

    internal static string NormalizeDigits(string s) =>
        new string(s.Select(c =>
            c >= '０' && c <= '９'
                ? (char)('0' + (c - '０'))
                : c
        ).ToArray());
}

internal static class UnknownTokenClassifier
{
    internal sealed class UnkClassification
    {
        internal bool IsPunctuationOrDash;
        internal bool IsMathSymbol;
        internal bool IsDecorativeSymbolOrEmoji;
    }

    internal static bool ShouldKeepMathSymbols(WordCountSettings settings)
    {
        return settings.HandleMathSymbols != MathSymbolHandling.Ignore;
    }

    internal static UnkClassification Classify(string surface)
    {
        var c = new UnkClassification();

        foreach (var rune in surface.EnumerateRunes())
        {
            var v = rune.Value;

            if (v == 0x200D || v == 0xFE0F)
            {
                c.IsDecorativeSymbolOrEmoji = true;
                continue;
            }

            var cat = CharUnicodeInfo.GetUnicodeCategory(v);

            switch (cat)
            {
                case UnicodeCategory.DashPunctuation:
                case UnicodeCategory.OpenPunctuation:
                case UnicodeCategory.ClosePunctuation:
                case UnicodeCategory.InitialQuotePunctuation:
                case UnicodeCategory.FinalQuotePunctuation:
                case UnicodeCategory.OtherPunctuation:
                case UnicodeCategory.ConnectorPunctuation:
                    c.IsPunctuationOrDash = true;
                    break;

                case UnicodeCategory.MathSymbol:
                    c.IsMathSymbol = true;
                    break;

                case UnicodeCategory.CurrencySymbol:
                case UnicodeCategory.ModifierSymbol:
                case UnicodeCategory.OtherSymbol:
                    c.IsDecorativeSymbolOrEmoji = true;
                    break;
            }
        }

        return c;
    }
}

internal static class IpadicPosRules
{
    internal static bool IsAuxiliary(string pos1) =>
        pos1 == "助動詞";

    internal static bool IsParticle(string pos1) =>
        pos1 == "助詞";

    internal static bool IsInterjection(string pos1) =>
        pos1 == "感動詞";

    internal static bool IsTeMiruVerb(string pos1, string pos2, string baseForm) =>
        pos1 == "動詞" &&
        pos2 == "非自立" &&
        baseForm == "みる";

    internal static bool EndsWithTeDe(string s) =>
        s.EndsWith("て", StringComparison.Ordinal) ||
        s.EndsWith("で", StringComparison.Ordinal);

    internal static bool IsFiller(string pos1, string pos2) =>
        pos1 == "フィラー" || pos2 == "フィラー";

    internal static bool IsUnk(JsonElement details)
    {
        if (details.ValueKind != JsonValueKind.Array)
            return false;

        var first = LinderaJsonHelpers.GetDetail(details, 0);
        var second = LinderaJsonHelpers.GetDetail(details, 1);

        return first == "UNK" && second.Length == 0;
    }
}

internal static class UserDictionaryEntries
{
    internal sealed class UserEntry
    {
        internal string Surface = "";
        internal string? CountAs;
        internal string? ReadingKatakana;
    }

    internal static Dictionary<string, UserEntry> Load(SqliteConnection conn)
    {
        var map = new Dictionary<string, UserEntry>(StringComparer.Ordinal);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT
                surface,
                count_as,
                reading
            FROM user_submitted_dictionary_entries;
            ";

        try
        {
            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
            {
                var surface = rdr.IsDBNull(0) ? "" : rdr.GetString(0);
                if (surface.Length == 0)
                    continue;

                var entry = new UserEntry
                {
                    Surface = surface,
                    CountAs = rdr.IsDBNull(1) ? null : rdr.GetString(1),
                    ReadingKatakana = rdr.IsDBNull(2) ? null : rdr.GetString(2)
                };

                map[surface] = entry;
            }
        }
        catch (SqliteException ex)
        {
            throw new InvalidOperationException(
                "Failed to read user_submitted_dictionary_entries. Expected columns: surface, count_as, reading.",
                ex
            );
        }

        return map;
    }
}
