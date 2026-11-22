using System.Text;

namespace StarlightAI.Engine;

public enum TokenKind
{
    HeaderSeal,    // ⧼⍉⧽
    FooterSeal,    // ⧼⍊⧽
    LBrace,        // ⧼
    RBrace,        // ⧽
    LParen,        // ⧘
    RParen,        // ⧙
    LBracket,      // ⧚
    RBracket,      // ⧛
    Semicolon,     // ⍮
    Comma,         // ⫯
    Dot,           // ⍤
    Colon,         // ⫰
    Arrow,         // ⍆
    Question,      // ⍞

    // Literals & Identifiers
    Identifier,    // Tifinagh sequence (ⴰ..ⵥ)
    NumberLiteral, // ⍁...⍂
    StringLiteral, // ⸢...⸣

    // Types & Constants (Glagolitic U+2C00..U+2C0F)
    KwVar,         // Ⰰ
    KwInt,         // Ⰱ
    KwDouble,      // Ⰲ
    KwString,      // Ⰳ
    KwBool,        // Ⰴ
    KwObject,      // Ⰵ
    KwList,        // Ⰶ
    KwDict,        // Ⰷ
    KwVoid,        // Ⰸ
    KwAIConfig,    // Ⰹ
    KwAIChat,      // Ⰺ
    KwAIResponse,  // Ⰻ
    KwTrue,        // Ⰼ
    KwFalse,       // Ⰽ
    KwNull,        // Ⰾ
    KwNew,         // Ⰿ

    // Control Flow, Functions, Exceptions, Output (Glagolitic U+2C10..U+2C1F)
    KwFunc,        // Ⱀ
    KwReturn,      // Ⱁ
    KwIf,          // Ⱂ
    KwElse,        // Ⱃ
    KwWhile,       // Ⱄ
    KwFor,         // Ⱅ
    KwForeach,     // Ⱆ
    KwIn,          // Ⱇ
    KwBreak,       // Ⱈ
    KwContinue,    // Ⱉ
    KwTry,         // Ⱊ
    KwCatch,       // Ⱋ
    KwFinally,     // Ⱌ
    KwThrow,       // Ⱍ
    KwWriteLine,   // Ⱎ
    KwWrite,       // Ⱏ

    // Built-in Members / Functions (Glagolitic U+2C20..U+2C2E and AI symbols U+2359..U+237A)
    SymbolGlyph,   // Member/Method/AI symbol

    // Operators
    Assign,        // ⍰ (=)
    AddAssign,     // ⍇ (+=)
    SubAssign,     // ⍈ (-=)
    MulAssign,     // ⍏ (*=)
    DivAssign,     // ⍖ (/=)
    Increment,     // ⍐ (++)
    Decrement,     // ⍗ (--)
    Plus,          // ⧺ (+)
    Minus,         // ⧻ (-)
    Star,          // ⧶ (*)
    Slash,         // ⧸ (/)
    Percent,       // ⧹ (%)
    Eq,            // ⍍ (==)
    Neq,           // ⍔ (!=)
    Lt,            // ⍃ (<)
    Gt,            // ⍄ (>)
    Lte,           // ⍅ (<=)
    Gte,           // ⍌ (>=)
    And,           // ⍋ (&&)
    Or,            // ⍒ (||)
    Not,           // ⍎ (!)

    Eof
}

public sealed record StarlightToken(TokenKind Kind, string Raw, object? Value = null);

public static class StarlightLexer
{
    private static readonly Dictionary<char, TokenKind> SingleCharTokens = new()
    {
        ['\u29FC'] = TokenKind.LBrace,      // ⧼
        ['\u29FD'] = TokenKind.RBrace,      // ⧽
        ['\u29D8'] = TokenKind.LParen,      // ⧘
        ['\u29D9'] = TokenKind.RParen,      // ⧙
        ['\u29DA'] = TokenKind.LBracket,    // ⧚
        ['\u29DB'] = TokenKind.RBracket,    // ⧛
        ['\u236E'] = TokenKind.Semicolon,   // ⍮
        ['\u2AEF'] = TokenKind.Comma,       // ⫯
        ['\u2364'] = TokenKind.Dot,         // ⍤
        ['\u2AF0'] = TokenKind.Colon,       // ⫰
        ['\u2346'] = TokenKind.Arrow,       // ⍆
        ['\u235E'] = TokenKind.Question,    // ⍞

        // Types & Literals
        ['\u2C00'] = TokenKind.KwVar,       // Ⰰ
        ['\u2C01'] = TokenKind.KwInt,       // Ⰱ
        ['\u2C02'] = TokenKind.KwDouble,    // Ⰲ
        ['\u2C03'] = TokenKind.KwString,    // Ⰳ
        ['\u2C04'] = TokenKind.KwBool,      // Ⰴ
        ['\u2C05'] = TokenKind.KwObject,    // Ⰵ
        ['\u2C06'] = TokenKind.KwList,      // Ⰶ
        ['\u2C07'] = TokenKind.KwDict,      // Ⰷ
        ['\u2C08'] = TokenKind.KwVoid,      // Ⰸ
        ['\u2C09'] = TokenKind.KwAIConfig,  // Ⰹ
        ['\u2C0A'] = TokenKind.KwAIChat,    // Ⰺ
        ['\u2C0B'] = TokenKind.KwAIResponse,// Ⰻ
        ['\u2C0C'] = TokenKind.KwTrue,      // Ⰼ
        ['\u2C0D'] = TokenKind.KwFalse,     // Ⰽ
        ['\u2C0E'] = TokenKind.KwNull,      // Ⰾ
        ['\u2C0F'] = TokenKind.KwNew,       // Ⰿ

        // Control flow & keywords
        ['\u2C10'] = TokenKind.KwFunc,      // Ⱀ
        ['\u2C11'] = TokenKind.KwReturn,    // Ⱁ
        ['\u2C12'] = TokenKind.KwIf,        // Ⱂ
        ['\u2C13'] = TokenKind.KwElse,      // Ⱃ
        ['\u2C14'] = TokenKind.KwWhile,     // Ⱄ
        ['\u2C15'] = TokenKind.KwFor,       // Ⱅ
        ['\u2C16'] = TokenKind.KwForeach,   // Ⱆ
        ['\u2C17'] = TokenKind.KwIn,        // Ⱇ
        ['\u2C18'] = TokenKind.KwBreak,     // Ⱈ
        ['\u2C19'] = TokenKind.KwContinue,  // Ⱉ
        ['\u2C1A'] = TokenKind.KwTry,       // Ⱊ
        ['\u2C1B'] = TokenKind.KwCatch,     // Ⱋ
        ['\u2C1C'] = TokenKind.KwFinally,   // Ⱌ
        ['\u2C1D'] = TokenKind.KwThrow,     // Ⱍ
        ['\u2C1E'] = TokenKind.KwWriteLine, // Ⱎ
        ['\u2C1F'] = TokenKind.KwWrite,     // Ⱏ

        // Operators
        ['\u2370'] = TokenKind.Assign,      // ⍰
        ['\u2347'] = TokenKind.AddAssign,   // ⍇
        ['\u2348'] = TokenKind.SubAssign,   // ⍈
        ['\u234F'] = TokenKind.MulAssign,   // ⍏
        ['\u2356'] = TokenKind.DivAssign,   // ⍖
        ['\u2350'] = TokenKind.Increment,   // ⍐
        ['\u2357'] = TokenKind.Decrement,   // ⍗
        ['\u29FA'] = TokenKind.Plus,        // ⧺
        ['\u29FB'] = TokenKind.Minus,       // ⧻
        ['\u29F6'] = TokenKind.Star,        // ⧶
        ['\u29F8'] = TokenKind.Slash,       // ⧸
        ['\u29F9'] = TokenKind.Percent,     // ⧹
        ['\u234D'] = TokenKind.Eq,          // ⍍
        ['\u2354'] = TokenKind.Neq,         // ⍔
        ['\u2343'] = TokenKind.Lt,          // ⍃
        ['\u2344'] = TokenKind.Gt,          // ⍄
        ['\u2345'] = TokenKind.Lte,         // ⍅
        ['\u234C'] = TokenKind.Gte,         // ⍌
        ['\u234B'] = TokenKind.And,         // ⍋
        ['\u2352'] = TokenKind.Or,          // ⍒
        ['\u234E'] = TokenKind.Not          // ⍎
    };

    private static bool IsSymbolGlyph(char c)
    {
        // Standard library methods/properties: Glagolitic U+2C20..U+2C2E
        if (c >= '\u2C20' && c <= '\u2C2E')
            return true;

        // AI API glyphs: U+2359..U+237A (excluding those already in SingleCharTokens: ⍞, ⍤, ⍮, ⍰)
        if (c >= '\u2359' && c <= '\u237A' && !SingleCharTokens.ContainsKey(c))
            return true;

        return false;
    }

    public static List<StarlightToken> Tokenize(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new StarlightException("EMPTY_SRC");

        var tokens = new List<StarlightToken>();
        int i = 0;
        int n = source.Length;

        while (i < n)
        {
            char c = source[i];

            // Skip whitespace or BOM
            if (char.IsWhiteSpace(c) || c == '\uFEFF')
            {
                i++;
                continue;
            }

            // Line comment: ⫱⫱ (\u2AF1\u2AF1)
            if (c == '\u2AF1' && i + 1 < n && source[i + 1] == '\u2AF1')
            {
                i += 2;
                while (i < n && source[i] != '\n' && source[i] != '\r')
                    i++;
                continue;
            }

            // Header Seal: ⧼⍉⧽ (\u29FC\u2349\u29FD)
            if (c == '\u29FC' && i + 2 < n && source[i + 1] == '\u2349' && source[i + 2] == '\u29FD')
            {
                tokens.Add(new StarlightToken(TokenKind.HeaderSeal, "⧼⍉⧽"));
                i += 3;
                continue;
            }

            // Footer Seal: ⧼⍊⧽ (\u29FC\u234A\u29FD)
            if (c == '\u29FC' && i + 2 < n && source[i + 1] == '\u234A' && source[i + 2] == '\u29FD')
            {
                tokens.Add(new StarlightToken(TokenKind.FooterSeal, "⧼⍊⧽"));
                i += 3;
                continue;
            }

            // String Literal: ⸢...⸣ (\u2E22 ... \u2E23)
            if (c == '\u2E22')
            {
                i++;
                var sb = new StringBuilder();
                while (i < n && source[i] != '\u2E23')
                {
                    if (!char.IsWhiteSpace(source[i]))
                        sb.Append(source[i]);
                    i++;
                }
                if (i >= n || source[i] != '\u2E23')
                    throw new StarlightException("UNCLOSED_STR");
                i++; // skip ⸣
                string decoded = StarlightCodec.DecodeStringPayload(sb.ToString());
                tokens.Add(new StarlightToken(TokenKind.StringLiteral, sb.ToString(), decoded));
                continue;
            }

            // Numeric Literal: ⍁...⍂ (\u2341 ... \u2342)
            if (c == '\u2341')
            {
                i++;
                var sb = new StringBuilder();
                while (i < n && source[i] != '\u2342')
                {
                    if (!char.IsWhiteSpace(source[i]))
                        sb.Append(source[i]);
                    i++;
                }
                if (i >= n || source[i] != '\u2342')
                    throw new StarlightException("UNCLOSED_NUM");
                i++; // skip ⍂
                object numVal = StarlightCodec.DecodeNumberPayload(sb.ToString());
                tokens.Add(new StarlightToken(TokenKind.NumberLiteral, sb.ToString(), numVal));
                continue;
            }

            // Tifinagh Identifier: U+2D30..U+2D67
            if (StarlightCodec.IsIdentifierGlyph(c))
            {
                int start = i;
                while (i < n && StarlightCodec.IsIdentifierGlyph(source[i]))
                    i++;
                string id = source.Substring(start, i - start);
                tokens.Add(new StarlightToken(TokenKind.Identifier, id, id));
                continue;
            }

            // Single-char Keywords, Types, Operators, Delimiters
            if (SingleCharTokens.TryGetValue(c, out TokenKind kind))
            {
                tokens.Add(new StarlightToken(kind, c.ToString()));
                i++;
                continue;
            }

            // Built-in / AI Symbol Glyphs
            if (IsSymbolGlyph(c))
            {
                tokens.Add(new StarlightToken(TokenKind.SymbolGlyph, c.ToString(), c));
                i++;
                continue;
            }

            // Any ASCII or unrecognized character is strictly forbidden
            throw new StarlightException("ILLEGAL_GLYPH");
        }

        tokens.Add(new StarlightToken(TokenKind.Eof, ""));
        return tokens;
    }
}
