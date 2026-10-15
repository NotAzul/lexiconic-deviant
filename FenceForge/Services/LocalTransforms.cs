using System.Text;

namespace FenceForge.Services;

public static class LocalTransforms
{
    public static string EscapeXml(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var result = new StringBuilder(input.Length);
        foreach (var character in input)
        {
            result.Append(character switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '\"' => "&quot;",
                '\'' => "&apos;",
                _ => character.ToString(),
            });
        }

        return result.ToString();
    }

    public static string Sponge(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var result = input.ToCharArray();
        var uppercase = true;
        for (var index = 0; index < result.Length; index++)
        {
            if (!char.IsLetter(result[index]))
            {
                continue;
            }

            result[index] = uppercase
                ? char.ToUpperInvariant(result[index])
                : char.ToLowerInvariant(result[index]);
            uppercase = !uppercase;
        }

        return new string(result);
    }
}
