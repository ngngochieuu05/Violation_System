using System.Text;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Utilities;

public static class VietnameseText
{
    private static readonly Dictionary<char, byte> Windows1252SpecialBytes = new()
    {
        ['€'] = 0x80,
        ['‚'] = 0x82,
        ['ƒ'] = 0x83,
        ['„'] = 0x84,
        ['…'] = 0x85,
        ['†'] = 0x86,
        ['‡'] = 0x87,
        ['ˆ'] = 0x88,
        ['‰'] = 0x89,
        ['Š'] = 0x8A,
        ['‹'] = 0x8B,
        ['Œ'] = 0x8C,
        ['Ž'] = 0x8E,
        ['‘'] = 0x91,
        ['’'] = 0x92,
        ['“'] = 0x93,
        ['”'] = 0x94,
        ['•'] = 0x95,
        ['–'] = 0x96,
        ['—'] = 0x97,
        ['˜'] = 0x98,
        ['™'] = 0x99,
        ['š'] = 0x9A,
        ['›'] = 0x9B,
        ['œ'] = 0x9C,
        ['ž'] = 0x9E,
        ['Ÿ'] = 0x9F
    };

    public static string NormalizeMojibake(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var current = value;
        for (var i = 0; i < 3 && LooksLikeMojibake(current); i++)
        {
            var fixedValue = TryDecodeWindows1252Utf8(current);
            if (string.IsNullOrWhiteSpace(fixedValue) || fixedValue == current)
            {
                break;
            }

            current = fixedValue;
        }

        return current;
    }

    private static bool LooksLikeMojibake(string value)
    {
        return value.Contains('Ã')
            || value.Contains('Ä')
            || value.Contains('Æ')
            || value.Contains("áº", StringComparison.Ordinal)
            || value.Contains("á»", StringComparison.Ordinal)
            || value.Contains('Â');
    }

    private static string TryDecodeWindows1252Utf8(string value)
    {
        try
        {
            var bytes = new List<byte>(value.Length);
            foreach (var ch in value)
            {
                if (ch <= 0x7F)
                {
                    bytes.Add((byte)ch);
                }
                else if (ch <= 0xFF)
                {
                    bytes.Add((byte)ch);
                }
                else if (Windows1252SpecialBytes.TryGetValue(ch, out var b))
                {
                    bytes.Add(b);
                }
                else
                {
                    return value;
                }
            }

            return Encoding.UTF8.GetString(bytes.ToArray());
        }
        catch
        {
            return value;
        }
    }
}
