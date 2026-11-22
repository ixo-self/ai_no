using System.Globalization;
using System.Text;

namespace StarlightAI.Engine;

public static class StarlightCodec
{
    public static readonly char[] BrailleNibbles = new char[16]
    {
        '\u2801', // 0: ⠁
        '\u2803', // 1: ⠃
        '\u2807', // 2: ⠇
        '\u280F', // 3: ⠏
        '\u281F', // 4: ⠟
        '\u283F', // 5: ⠿
        '\u2837', // 6: ⠷
        '\u282E', // 7: ⠮
        '\u282A', // 8: ⠪
        '\u283A', // 9: ⠺
        '\u282D', // A: ⠭
        '\u282F', // B: ⠯
        '\u2835', // C: ⠵
        '\u283D', // D: ⠽
        '\u283E', // E: ⠾
        '\u28FF'  // F: ⣿
    };

    public static readonly char[] RunicDigits = new char[10]
    {
        '\u16A0', // 0: ᚠ
        '\u16A2', // 1: ᚢ
        '\u16A6', // 2: ᚦ
        '\u16A8', // 3: ᚨ
        '\u16B1', // 4: ᚱ
        '\u16B2', // 5: ᚲ
        '\u16B7', // 6: ᚷ
        '\u16B9', // 7: ᚹ
        '\u16C1', // 8: ᛁ
        '\u16C3'  // 9: ᛃ
    };

    public const char RunicDot = '\u16BA';   // ᚺ
    public const char RunicMinus = '\u16BE'; // ᚾ

    private static readonly Dictionary<char, int> BrailleToNibble = new();
    private static readonly Dictionary<char, char> RuneToAscii = new();

    static StarlightCodec()
    {
        for (int i = 0; i < 16; i++)
        {
            BrailleToNibble[BrailleNibbles[i]] = i;
        }
        for (int i = 0; i < 10; i++)
        {
            RuneToAscii[RunicDigits[i]] = (char)('0' + i);
        }
        RuneToAscii[RunicDot] = '.';
        RuneToAscii[RunicMinus] = '-';
    }

    public static string EncodeStringLiteral(string plainText)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(plainText);
        var sb = new StringBuilder(utf8.Length * 2 + 2);
        sb.Append('\u2E22'); // ⸢
        for (int i = 0; i < utf8.Length; i++)
        {
            byte mask = (byte)((0xA7 + (i * 0x1F)) & 0xFF);
            byte masked = (byte)(utf8[i] ^ mask);
            sb.Append(BrailleNibbles[(masked >> 4) & 0x0F]);
            sb.Append(BrailleNibbles[masked & 0x0F]);
        }
        sb.Append('\u2E23'); // ⸣
        return sb.ToString();
    }

    public static string DecodeStringPayload(string brailleContent)
    {
        if (brailleContent.Length == 0)
            return string.Empty;

        if ((brailleContent.Length & 1) != 0)
            throw new StarlightException("STR_LEN");

        int byteCount = brailleContent.Length / 2;
        byte[] raw = new byte[byteCount];
        for (int i = 0; i < byteCount; i++)
        {
            char hiChar = brailleContent[i * 2];
            char loChar = brailleContent[i * 2 + 1];
            if (!BrailleToNibble.TryGetValue(hiChar, out int hi) ||
                !BrailleToNibble.TryGetValue(loChar, out int lo))
            {
                throw new StarlightException("STR_GLYPH");
            }
            byte masked = (byte)((hi << 4) | lo);
            byte mask = (byte)((0xA7 + (i * 0x1F)) & 0xFF);
            raw[i] = (byte)(masked ^ mask);
        }

        return Encoding.UTF8.GetString(raw);
    }

    public static string EncodeNumberLiteral(string numericText)
    {
        var sb = new StringBuilder(numericText.Length + 2);
        sb.Append('\u2341'); // ⍁
        foreach (char c in numericText)
        {
            if (c >= '0' && c <= '9')
                sb.Append(RunicDigits[c - '0']);
            else if (c == '.')
                sb.Append(RunicDot);
            else if (c == '-')
                sb.Append(RunicMinus);
            else
                throw new StarlightException("NUM_ENC");
        }
        sb.Append('\u2342'); // ⍂
        return sb.ToString();
    }

    public static object DecodeNumberPayload(string runicContent)
    {
        if (string.IsNullOrEmpty(runicContent))
            throw new StarlightException("NUM_EMPTY");

        var sb = new StringBuilder(runicContent.Length);
        bool isFloat = false;
        foreach (char c in runicContent)
        {
            if (!RuneToAscii.TryGetValue(c, out char ascii))
                throw new StarlightException("NUM_GLYPH");
            if (ascii == '.')
                isFloat = true;
            sb.Append(ascii);
        }

        string numStr = sb.ToString();
        if (isFloat)
        {
            if (double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double dVal))
                return dVal;
            throw new StarlightException("NUM_PARSE");
        }
        else
        {
            if (long.TryParse(numStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out long lVal))
                return lVal;
            throw new StarlightException("NUM_PARSE");
        }
    }

    public static bool IsIdentifierGlyph(char c)
    {
        return c >= '\u2D30' && c <= '\u2D67';
    }
}

public sealed class StarlightException : Exception
{
    public string Code { get; }

    public StarlightException(string code) : base(code)
    {
        Code = code;
    }
}
