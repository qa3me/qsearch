using System.Globalization;
using System.Text;

namespace Qsearch.Core.Text;

public static class SearchTextNormalizer
{
    public const int Version = 1;

    public static string Normalize(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        string compatibilityNormalized = input.Normalize(NormalizationForm.FormKC);
        StringBuilder result = new(compatibilityNormalized.Length);

        foreach (char character in compatibilityNormalized)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (character == '\u0640' ||
                category is UnicodeCategory.NonSpacingMark or
                    UnicodeCategory.SpacingCombiningMark or
                    UnicodeCategory.EnclosingMark)
            {
                continue;
            }

            char mapped = character switch
            {
                '\u0643' => '\u06A9',
                '\u0649' or '\u064A' => '\u06CC',
                _ => character,
            };

            result.Append(char.ToLowerInvariant(mapped));
        }

        return result.ToString();
    }
}
