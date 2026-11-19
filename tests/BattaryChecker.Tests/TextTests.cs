using System.Reflection;
using BattaryChecker.UI;

namespace BattaryChecker.Tests;

/// <summary>Switches the shared language flag: must not run beside tests that read strings.</summary>
[CollectionDefinition(nameof(TextTests), DisableParallelization = true)]
public sealed class LanguageCollection;

[Collection(nameof(TextTests))]
public sealed class TextTests
{
    /// <summary>Every string the UI can show in one language, with sample arguments for the formatted ones.</summary>
    static List<string> All()
    {
        var all = new List<string>();
        foreach (var p in typeof(Text).GetProperties(BindingFlags.Public | BindingFlags.Static))
        {
            switch (p.GetValue(null))
            {
                case string s:
                    all.Add(s);
                    break;
                case string[] list:
                    all.AddRange(list);
                    break;
            }
        }
        foreach (var m in typeof(Text).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => !m.IsSpecialName))
        {
            var args = m.GetParameters().Select(a => a.ParameterType == typeof(int) ? (object)42 : "1.2.3").ToArray();
            all.Add((string)m.Invoke(null, args)!);
        }
        return all;
    }

    static bool HasCyrillic(string s) => s.Any(c => c is >= 'Ѐ' and <= 'ӿ');

    [Fact]
    public void BothLanguagesAreComplete()
    {
        var was = Text.Russian;
        try
        {
            Text.Russian = false;
            var english = All();
            Text.Russian = true;
            var russian = All();

            Assert.All(english, s => Assert.False(string.IsNullOrWhiteSpace(s)));
            Assert.All(english, s => Assert.False(HasCyrillic(s), s)); // a Russian string left untranslated
            Assert.All(russian, s => Assert.False(string.IsNullOrWhiteSpace(s)));
            Assert.Equal(english.Count, russian.Count);
            Assert.Contains(russian, HasCyrillic);
        }
        finally
        {
            Text.Russian = was;
        }
    }
}
