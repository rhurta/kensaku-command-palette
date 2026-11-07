using System;
using System.Globalization;
using System.Resources;

namespace KensakuCmdPal;

/// <summary>
/// Minimal localization helper on top of .resx resources.
/// English (Strings/Strings.resx) is the neutral/default language; every other
/// language lives in Strings/Strings.&lt;culture&gt;.resx and is picked up automatically.
/// </summary>
internal static class Loc
{
    // Resource base name = <namespace>.Strings.Strings (folder "Strings", file "Strings.resx").
    // If you change the folder or file name, update it here.
    private static readonly ResourceManager Rm =
        new($"{typeof(Loc).Namespace}.Strings.Strings", typeof(Loc).Assembly);

    // Handy for testing: set the KENSAKU_LANG user environment variable (e.g. "es") to force a language.
    private static readonly CultureInfo? Override = ReadOverride();

    private static CultureInfo Culture => Override ?? CultureInfo.CurrentUICulture;

    public static string Get(string key) => Rm.GetString(key, Culture) ?? key;

    public static string Format(string key, params object[] args) =>
        string.Format(Culture, Get(key), args);

    /// <summary>Localizes the match type reported by Kensaku (Name, Content, Both...); falls back to the raw value.</summary>
    public static string MatchType(string raw) => Rm.GetString("MatchType_" + raw, Culture) ?? raw;

    private static CultureInfo? ReadOverride()
    {
        var v = Environment.GetEnvironmentVariable("KENSAKU_LANG");
        if (string.IsNullOrWhiteSpace(v)) return null;
        try { return CultureInfo.GetCultureInfo(v); }
        catch (CultureNotFoundException) { return null; }
    }
}
