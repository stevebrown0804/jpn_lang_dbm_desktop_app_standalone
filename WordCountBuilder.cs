// WordCountBuilder.cs

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace jpn_lang_dbm_desktop_app_standalone;

public static class WordCountBuilder
{
    public static Dictionary<string, int> Build(
        SqliteConnection conn,
        string linderaJson
    )
    {
        var settings = WordCountSettings.Load(conn);
        var userEntries = UserDictionaryEntries.Load(conn);

        var result = new Dictionary<string, int>();

        using var doc = JsonDocument.Parse(linderaJson);
        var root = doc.RootElement;

        var tokensArray = LinderaJsonHelpers.FindTokensArray(root);

        Group? current = null;

        foreach (var token in tokensArray.EnumerateArray())
        {
            var surface = LinderaJsonHelpers.GetSurface(token);
            if (surface.Length == 0)
                continue;

            if (surface.Trim().Length == 0)
            {
                FlushCurrent(result, settings, ref current);
                continue;
            }

            if (surface == "ー" || surface == "ｰ")
            {
                FlushCurrent(result, settings, ref current);
                continue;
            }

            if (userEntries.TryGetValue(surface, out var ue))
            {
                FlushCurrent(result, settings, ref current);

                var countAs = string.IsNullOrWhiteSpace(ue.CountAs) ? surface : ue.CountAs;
                var reading = ue.ReadingKatakana ?? "";

                current = new Group
                {
                    Surface = countAs,
                    BaseForm = countAs,
                    Reading = reading,
                    Pos = "USER",
                    IsUserEntry = true
                };

                continue;
            }

            var details = LinderaJsonHelpers.GetDetails(token);

            var pos1 = LinderaJsonHelpers.GetDetail(details, 0);
            var pos2 = LinderaJsonHelpers.GetDetail(details, 1);
            var pos3 = LinderaJsonHelpers.GetDetail(details, 2);
            var pos4 = LinderaJsonHelpers.GetDetail(details, 3);

            var baseForm = LinderaJsonHelpers.GetDetail(details, 6);
            if (baseForm.Length == 0)
                baseForm = surface;

            var readingFromLindera = LinderaJsonHelpers.GetDetail(details, 7);

            {
                var looksLikeSymbol = pos1 == "記号" || IpadicPosRules.IsUnk(details);
                if (looksLikeSymbol)
                {
                    var cls = UnknownTokenClassifier.Classify(surface);

                    if (cls.IsMathSymbol)
                    {
                        if (!UnknownTokenClassifier.ShouldKeepMathSymbols(settings))
                        {
                            FlushCurrent(result, settings, ref current);
                            continue;
                        }
                    }
                    else if (cls.IsPunctuationOrDash)
                    {
                        FlushCurrent(result, settings, ref current);
                        continue;
                    }
                    else if (cls.IsDecorativeSymbolOrEmoji)
                    {
                        if (settings.IgnoreDecorativeSymbolsAndEmoji)
                        {
                            FlushCurrent(result, settings, ref current);
                            continue;
                        }
                    }
                    else if (pos1 == "記号")
                    {
                        FlushCurrent(result, settings, ref current);
                        continue;
                    }
                }
            }

            if (IpadicPosRules.IsParticle(pos1))
            {
                if (pos2 == "接続助詞" && current != null)
                {
                    current.Surface += surface;
                    if (!string.IsNullOrEmpty(readingFromLindera))
                        current.Reading += readingFromLindera;

                    current.BaseForm = current.Surface;
                }
                else
                {
                    if (settings.IgnoreParticlesAndCopula)
                    {
                        FlushCurrent(result, settings, ref current);
                    }
                    else
                    {
                        FlushCurrent(result, settings, ref current);
                        current = new Group
                        {
                            Surface = surface,
                            BaseForm = baseForm,
                            Reading = readingFromLindera,
                            Pos = LinderaJsonHelpers.BuildPosString(pos1, pos2, pos3, pos4)
                        };
                    }
                }

                continue;
            }

            if (settings.IgnoreInterjections && IpadicPosRules.IsInterjection(pos1))
            {
                FlushCurrent(result, settings, ref current);
                continue;
            }

            if (settings.IgnoreFillers && IpadicPosRules.IsFiller(pos1, pos2))
            {
                FlushCurrent(result, settings, ref current);
                continue;
            }

            var isAux = IpadicPosRules.IsAuxiliary(pos1);

            if (settings.MergeTeMiruConstructions &&
                current != null &&
                IpadicPosRules.EndsWithTeDe(current.Surface) &&
                IpadicPosRules.IsTeMiruVerb(pos1, pos2, baseForm))
            {
                current.Surface += surface;

                if (!string.IsNullOrEmpty(readingFromLindera))
                    current.Reading += readingFromLindera;

                current.BaseForm = current.Surface;
                continue;
            }

            if (isAux)
            {
                if (current != null)
                {
                    current.Surface += surface;

                    if (!string.IsNullOrEmpty(readingFromLindera))
                        current.Reading += readingFromLindera;
                }

                continue;
            }

            FlushCurrent(result, settings, ref current);

            current = new Group
            {
                Surface = surface,
                BaseForm = baseForm,
                Reading = readingFromLindera,
                Pos = LinderaJsonHelpers.BuildPosString(pos1, pos2, pos3, pos4)
            };
        }

        FlushCurrent(result, settings, ref current);

        return result;
    }

    private sealed class Group
    {
        public string Surface = "";
        public string BaseForm = "";
        public string Reading = "";
        public string Pos = "";
        public bool IsUserEntry;
    }

    private static void FlushCurrent(
        Dictionary<string, int> result,
        WordCountSettings settings,
        ref Group? current
    )
    {
        if (current == null)
            return;
            
        var wordForm = settings.FormPreference switch
        {
            WordFormPreference.DictionaryForm => current.BaseForm,
            WordFormPreference.SurfaceForm => current.Surface,
            WordFormPreference.CountBoth => current.Surface,
            _ => current.Surface
        };


        if (!current.IsUserEntry)
        {
            if (settings.MergeLatinCase)
                wordForm = wordForm.ToLowerInvariant();

            if (settings.UnicodeNFCKNormalization)
                wordForm = wordForm.Normalize(NormalizationForm.FormKC);

            switch (settings.MergeKanaTo)
            {
                case KanaMergeTargets.ToHiragana:
                    wordForm = TokenNormalization.NormalizeKanaToHiragana(wordForm);
                    break;

                case KanaMergeTargets.ToKatakana:
                    wordForm = TokenNormalization.NormalizeKanaToKatakana(wordForm);
                    break;

                case KanaMergeTargets.Neither:
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Unhandled KanaMergeTargets value: {settings.MergeKanaTo}"
                    );
            }

            if (settings.DeduplicateRepeatedChars)
                wordForm = TokenNormalization.Deduplicate(wordForm);

            if (settings.MergeNumericVariants)
                wordForm = TokenNormalization.NormalizeDigits(wordForm);
        }

        var keyParts = new List<string>();

        if (!string.IsNullOrEmpty(wordForm))
            keyParts.Add(wordForm);

        if (settings.TokenKeyFields != "surface_only" && !string.IsNullOrEmpty(current.Pos))
            keyParts.Add(current.Pos);

        if (settings.IncludeReading && !string.IsNullOrEmpty(current.Reading))
            keyParts.Add(current.Reading);

        var key = string.Join(" | ", keyParts);

        if (key.Length != 0)
        {
            result.TryGetValue(key, out var count);
            result[key] = count + 1;
        }

        current = null;
    }
}

