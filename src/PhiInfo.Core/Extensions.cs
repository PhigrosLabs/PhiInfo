using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PhiInfo.Core.Type;

namespace PhiInfo.Core;

public static class Extensions
{
    private static readonly Dictionary<string, Language> LangFromStringMap =
        typeof(Language)
            .GetFields(BindingFlags.Static | BindingFlags.Public)
            .ToDictionary(
                x => x.GetCustomAttribute<LanguageStringIdAttribute>()?.Id
                     ?? throw new ArgumentNullException(),
                x => (Language)x.GetValue(null)!
            );

    private static readonly Dictionary<int, Language> LangFromIntMap =
        typeof(Language)
            .GetFields(BindingFlags.Static | BindingFlags.Public)
            .ToDictionary(
                x => Convert.ToInt32((Language)x.GetValue(null)!),
                x => (Language)x.GetValue(null)!
            );

    internal static Language FromString(string id)
    {
        if (LangFromStringMap.TryGetValue(id, out var lang))
            return lang;

        throw new ArgumentException($"Unknown language string id: {id}");
    }

    internal static Language FromInt(int value)
    {
        if (LangFromIntMap.TryGetValue(value, out var lang))
            return lang;

        throw new ArgumentException($"Unknown language value: {value}");
    }
}