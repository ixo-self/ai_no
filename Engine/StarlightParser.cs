namespace StarlightAI.Engine;

public sealed class StarlightParser
{
    private readonly List<StarlightToken> _tokens;
    private int _pos;

    public StarlightParser(List<StarlightToken> tokens)
    {
        _tokens = tokens;
        _pos = 0;
    }

    private StarlightToken Current => _pos < _tokens.Count ? _tokens[_pos] : _tokens[^1];

    private StarlightToken Peek(int offset = 1)
    {
        int idx = _pos + offset;
        return idx < _tokens.Count ? _tokens[idx] : _tokens[^1];
    }

    private bool Check(TokenKind kind) => Current.Kind == kind;

    private bool Match(params TokenKind[] kinds)
    {
        foreach (var k in kinds)
        {
            if (Current.Kind == k)
            {
                _pos++;
                return true;
            }
        }
        return false;
    }

    private StarlightToken Expect(TokenKind kind, string errCode = "SYNTAX_ERR")
    {
        if (Current.Kind != kind)
            throw new StarlightException(errCode);
        var tok = Current;
        _pos++;
        return tok;
    }

    private static bool IsTypeKeyword(TokenKind kind)
    {
        return kind is TokenKind.KwVar
            or TokenKind.KwInt
            or TokenKind.KwDouble
            or TokenKind.KwString
            or TokenKind.KwBool
            or TokenKind.KwObject
            or TokenKind.KwList
            or TokenKind.KwDict
            or TokenKind.KwVoid
            or TokenKind.KwAIConfig
            or TokenKind.KwAIChat
            or TokenKind.KwAIResponse;
    }

    public BlockStmt ParseProgram()
    {
        Expect(TokenKind.HeaderSeal, "MISSING_HEADER_SEAL");
        var stmts = new List<AstStmt>();
        while (!Check(TokenKind.FooterSeal) && !Check(TokenKind.Eof))
        {
            stmts.Add(ParseStatement());
        }
        Expect(TokenKind.FooterSeal, "MISSING_FOOTER_SEAL");
        Expect(TokenKind.Eof, "TRAILING_TOKENS");
        return new BlockStmt(stmts);
    }

    private AstStmt ParseStatement()
    {
        if (Check(TokenKind.LBrace))
            return ParseBlock();

        if (Check(TokenKind.KwFunc))
            return ParseFuncDecl();

        if (IsTypeKeyword(Current.Kind))
        {
            // Could also be a typed function declaration without Ⱀ if followed by Identifier + LParen
            if (Peek(1).Kind == TokenKind.Identifier && Peek(2).Kind == TokenKind.LParen)
                return ParseTypedFuncDeclWithoutPrefix();
            return ParseVarDecl(true);
        }

        if (Match(TokenKind.KwReturn))
        {
            AstExpr? val = null;
            if (!Check(TokenKind.Semicolon))
                val = ParseExpression();
            Expect(TokenKind.Semicolon);
            return new ReturnStmt(val);
        }

        if (Match(TokenKind.KwIf))
            return ParseIf();

        if (Match(TokenKind.KwWhile))
            return ParseWhile();

        if (Match(TokenKind.KwFor))
            return ParseFor();

        if (Match(TokenKind.KwForeach))
            return ParseForeach();

        if (Match(TokenKind.KwBreak))
        {
            Expect(TokenKind.Semicolon);
            return new BreakStmt();
        }

        if (Match(TokenKind.KwContinue))
        {
            Expect(TokenKind.Semicolon);
            return new ContinueStmt();
        }

        if (Match(TokenKind.KwTry))
            return ParseTryCatch();

        if (Match(TokenKind.KwThrow))
        {
            var expr = ParseExpression();
            Expect(TokenKind.Semicolon);
            return new ThrowStmt(expr);
        }

        if (Match(TokenKind.KwWriteLine))
        {
            Expect(TokenKind.LParen);
            AstExpr? expr = null;
            if (!Check(TokenKind.RParen))
                expr = ParseExpression();
            Expect(TokenKind.RParen);
            Expect(TokenKind.Semicolon);
            return new WriteStmt(expr, true);
        }

        if (Match(TokenKind.KwWrite))
        {
            Expect(TokenKind.LParen);
            AstExpr? expr = null;
            if (!Check(TokenKind.RParen))
                expr = ParseExpression();
            Expect(TokenKind.RParen);
            Expect(TokenKind.Semicolon);
            return new WriteStmt(expr, false);
        }

        var stmtExpr = ParseExpression();
        Expect(TokenKind.Semicolon);
        return new ExprStmt(stmtExpr);
    }

    private BlockStmt ParseBlock()
    {
        Expect(TokenKind.LBrace);
        var stmts = new List<AstStmt>();
        while (!Check(TokenKind.RBrace) && !Check(TokenKind.Eof))
        {
            stmts.Add(ParseStatement());
        }
        Expect(TokenKind.RBrace);
        return new BlockStmt(stmts);
    }

    private VarDeclStmt ParseVarDecl(bool requireSemicolon)
    {
        var typeTok = Current;
        _pos++;
        var idTok = Expect(TokenKind.Identifier);
        AstExpr? init = null;
        if (Match(TokenKind.Assign))
        {
            init = ParseExpression();
        }
        if (requireSemicolon)
            Expect(TokenKind.Semicolon);
        return new VarDeclStmt(typeTok.Kind, idTok.Raw, init);
    }

    private FuncDeclStmt ParseFuncDecl()
    {
        Expect(TokenKind.KwFunc);
        TokenKind retType = TokenKind.KwObject;
        if (IsTypeKeyword(Current.Kind))
        {
            retType = Current.Kind;
            _pos++;
        }
        var nameTok = Expect(TokenKind.Identifier);
        return ParseFuncSignatureAndBody(retType, nameTok.Raw);
    }

    private FuncDeclStmt ParseTypedFuncDeclWithoutPrefix()
    {
        TokenKind retType = Current.Kind;
        _pos++;
        var nameTok = Expect(TokenKind.Identifier);
        return ParseFuncSignatureAndBody(retType, nameTok.Raw);
    }

    private FuncDeclStmt ParseFuncSignatureAndBody(TokenKind retType, string funcName)
    {
        Expect(TokenKind.LParen);
        var parameters = new List<FuncParam>();
        if (!Check(TokenKind.RParen))
        {
            do
            {
                TokenKind pType = TokenKind.KwObject;
                if (IsTypeKeyword(Current.Kind))
                {
                    pType = Current.Kind;
                    _pos++;
                }
                var pName = Expect(TokenKind.Identifier).Raw;
                parameters.Add(new FuncParam(pType, pName));
            } while (Match(TokenKind.Comma));
        }
        Expect(TokenKind.RParen);

        BlockStmt body;
        if (Match(TokenKind.Arrow))
        {
            var expr = ParseExpression();
            Expect(TokenKind.Semicolon);
            body = new BlockStmt(new List<AstStmt> { new ReturnStmt(expr) });
        }
        else
        {
            body = ParseBlock();
        }

        return new FuncDeclStmt(retType, funcName, parameters, body);
    }

    private IfStmt ParseIf()
    {
        Expect(TokenKind.LParen);
        var cond = ParseExpression();
        Expect(TokenKind.RParen);
        var thenBranch = ParseStatement();
        AstStmt? elseBranch = null;
        if (Match(TokenKind.KwElse))
        {
            elseBranch = ParseStatement();
        }
        return new IfStmt(cond, thenBranch, elseBranch);
    }

    private WhileStmt ParseWhile()
    {
        Expect(TokenKind.LParen);
        var cond = ParseExpression();
        Expect(TokenKind.RParen);
        var body = ParseStatement();
        return new WhileStmt(cond, body);
    }

    private ForStmt ParseFor()
    {
        Expect(TokenKind.LParen);
        AstStmt? init = null;
        if (!Check(TokenKind.Semicolon))
        {
            if (IsTypeKeyword(Current.Kind))
                init = ParseVarDecl(false);
            else
                init = new ExprStmt(ParseExpression());
        }
        Expect(TokenKind.Semicolon);

        AstExpr? cond = null;
        if (!Check(TokenKind.Semicolon))
            cond = ParseExpression();
        Expect(TokenKind.Semicolon);

        AstExpr? step = null;
        if (!Check(TokenKind.RParen))
            step = ParseExpression();
        Expect(TokenKind.RParen);

        var body = ParseStatement();
        return new ForStmt(init, cond, step, body);
    }

    private ForeachStmt ParseForeach()
    {
        Expect(TokenKind.LParen);
        TokenKind itemType = TokenKind.KwVar;
        if (IsTypeKeyword(Current.Kind))
        {
            itemType = Current.Kind;
            _pos++;
        }
        var itemName = Expect(TokenKind.Identifier).Raw;
        Expect(TokenKind.KwIn);
        var collection = ParseExpression();
        Expect(TokenKind.RParen);
        var body = ParseStatement();
        return new ForeachStmt(itemType, itemName, collection, body);
    }

    private TryCatchStmt ParseTryCatch()
    {
        var tryBlock = ParseBlock();
        string? catchVar = null;
        BlockStmt? catchBlock = null;
        BlockStmt? finallyBlock = null;

        if (Match(TokenKind.KwCatch))
        {
            if (Match(TokenKind.LParen))
            {
                if (IsTypeKeyword(Current.Kind))
                    _pos++;
                catchVar = Expect(TokenKind.Identifier).Raw;
                Expect(TokenKind.RParen);
            }
            catchBlock = ParseBlock();
        }

        if (Match(TokenKind.KwFinally))
        {
            finallyBlock = ParseBlock();
        }

        if (catchBlock == null && finallyBlock == null)
            throw new StarlightException("TRY_WITHOUT_HANDLER");

        return new TryCatchStmt(tryBlock, catchVar, catchBlock, finallyBlock);
    }

    private AstExpr ParseExpression() => ParseAssignment();

    private AstExpr ParseAssignment()
    {
        var expr = ParseTernary();
        if (Current.Kind is TokenKind.Assign
            or TokenKind.AddAssign
            or TokenKind.SubAssign
            or TokenKind.MulAssign
            or TokenKind.DivAssign)
        {
            var op = Current.Kind;
            _pos++;
            var val = ParseAssignment();
            return new AssignExpr(expr, op, val);
        }
        return expr;
    }

    private AstExpr ParseTernary()
    {
        var expr = ParseLogicalOr();
        if (Match(TokenKind.Question))
        {
            // Null-coalescing: ⍞⍞
            if (Match(TokenKind.Question))
            {
                var fallback = ParseTernary();
                return new BinaryExpr(expr, TokenKind.Question, fallback);
            }
            var trueExpr = ParseExpression();
            Expect(TokenKind.Colon);
            var falseExpr = ParseTernary();
            return new TernaryExpr(expr, trueExpr, falseExpr);
        }
        return expr;
    }

    private AstExpr ParseLogicalOr()
    {
        var left = ParseLogicalAnd();
        while (Match(TokenKind.Or))
        {
            var right = ParseLogicalAnd();
            left = new BinaryExpr(left, TokenKind.Or, right);
        }
        return left;
    }

    private AstExpr ParseLogicalAnd()
    {
        var left = ParseEquality();
        while (Match(TokenKind.And))
        {
            var right = ParseEquality();
            left = new BinaryExpr(left, TokenKind.And, right);
        }
        return left;
    }

    private AstExpr ParseEquality()
    {
        var left = ParseRelational();
        while (Current.Kind is TokenKind.Eq or TokenKind.Neq)
        {
            var op = Current.Kind;
            _pos++;
            var right = ParseRelational();
            left = new BinaryExpr(left, op, right);
        }
        return left;
    }

    private AstExpr ParseRelational()
    {
        var left = ParseAdditive();
        while (Current.Kind is TokenKind.Lt or TokenKind.Gt or TokenKind.Lte or TokenKind.Gte)
        {
            var op = Current.Kind;
            _pos++;
            var right = ParseAdditive();
            left = new BinaryExpr(left, op, right);
        }
        return left;
    }

    private AstExpr ParseAdditive()
    {
        var left = ParseMultiplicative();
        while (Current.Kind is TokenKind.Plus or TokenKind.Minus)
        {
            var op = Current.Kind;
            _pos++;
            var right = ParseMultiplicative();
            left = new BinaryExpr(left, op, right);
        }
        return left;
    }

    private AstExpr ParseMultiplicative()
    {
        var left = ParseUnary();
        while (Current.Kind is TokenKind.Star or TokenKind.Slash or TokenKind.Percent)
        {
            var op = Current.Kind;
            _pos++;
            var right = ParseUnary();
            left = new BinaryExpr(left, op, right);
        }
        return left;
    }

    private AstExpr ParseUnary()
    {
        if (Current.Kind is TokenKind.Not or TokenKind.Minus or TokenKind.Plus or TokenKind.Increment or TokenKind.Decrement)
        {
            var op = Current.Kind;
            _pos++;
            var operand = ParseUnary();
            return new UnaryExpr(op, operand, false);
        }
        return ParsePostfix();
    }

    private AstExpr ParsePostfix()
    {
        var expr = ParsePrimary();
        while (true)
        {
            if (Match(TokenKind.Dot))
            {
                if (Check(TokenKind.Identifier))
                {
                    string name = Current.Raw;
                    _pos++;
                    expr = new MemberAccessExpr(expr, name, null);
                }
                else if (Check(TokenKind.SymbolGlyph))
                {
                    char glyph = (char)Current.Value!;
                    string raw = Current.Raw;
                    _pos++;
                    expr = new MemberAccessExpr(expr, raw, glyph);
                }
                else
                {
                    throw new StarlightException("INVALID_MEMBER");
                }
            }
            else if (Match(TokenKind.LParen))
            {
                var args = new List<AstExpr>();
                if (!Check(TokenKind.RParen))
                {
                    do
                    {
                        args.Add(ParseExpression());
                    } while (Match(TokenKind.Comma));
                }
                Expect(TokenKind.RParen);
                expr = new CallExpr(expr, args);
            }
            else if (Match(TokenKind.LBracket))
            {
                var idx = ParseExpression();
                Expect(TokenKind.RBracket);
                expr = new IndexAccessExpr(expr, idx);
            }
            else if (Current.Kind is TokenKind.Increment or TokenKind.Decrement)
            {
                var op = Current.Kind;
                _pos++;
                expr = new UnaryExpr(op, expr, true);
            }
            else
            {
                break;
            }
        }
        return expr;
    }

    private AstExpr ParsePrimary()
    {
        if (Check(TokenKind.NumberLiteral))
        {
            var val = Current.Value;
            _pos++;
            return new LiteralExpr(val);
        }

        if (Check(TokenKind.StringLiteral))
        {
            var val = Current.Value;
            _pos++;
            return new LiteralExpr(val);
        }

        if (Match(TokenKind.KwTrue))
            return new LiteralExpr(true);

        if (Match(TokenKind.KwFalse))
            return new LiteralExpr(false);

        if (Match(TokenKind.KwNull))
            return new LiteralExpr(null);

        if (Check(TokenKind.Identifier))
        {
            string name = Current.Raw;
            _pos++;
            return new IdentifierExpr(name);
        }

        if (Check(TokenKind.SymbolGlyph))
        {
            char glyph = (char)Current.Value!;
            _pos++;
            return new SymbolExpr(glyph);
        }

        if (Match(TokenKind.KwNew))
        {
            if (!IsTypeKeyword(Current.Kind))
                throw new StarlightException("NEW_TYPE_EXPECTED");
            var typeKind = Current.Kind;
            _pos++;

            var args = new List<AstExpr>();
            if (Match(TokenKind.LParen))
            {
                if (!Check(TokenKind.RParen))
                {
                    do
                    {
                        args.Add(ParseExpression());
                    } while (Match(TokenKind.Comma));
                }
                Expect(TokenKind.RParen);
            }

            List<AstExpr>? listInit = null;
            List<(AstExpr Key, AstExpr Value)>? dictInit = null;

            if (Match(TokenKind.LBrace))
            {
                if (!Check(TokenKind.RBrace))
                {
                    if (typeKind == TokenKind.KwDict)
                    {
                        dictInit = new List<(AstExpr, AstExpr)>();
                        do
                        {
                            var k = ParseExpression();
                            Expect(TokenKind.Colon);
                            var v = ParseExpression();
                            dictInit.Add((k, v));
                        } while (Match(TokenKind.Comma));
                    }
                    else
                    {
                        listInit = new List<AstExpr>();
                        do
                        {
                            listInit.Add(ParseExpression());
                        } while (Match(TokenKind.Comma));
                    }
                }
                Expect(TokenKind.RBrace);
            }

            return new NewObjectExpr(typeKind, args, listInit, dictInit);
        }

        // Array literal: ⧚ e1 ⫯ e2 ⧛
        if (Match(TokenKind.LBracket))
        {
            var items = new List<AstExpr>();
            if (!Check(TokenKind.RBracket))
            {
                do
                {
                    items.Add(ParseExpression());
                } while (Match(TokenKind.Comma));
            }
            Expect(TokenKind.RBracket);
            return new ArrayLiteralExpr(items);
        }

        // Dict literal: ⧼ k1 ⫰ v1 ⫯ k2 ⫰ v2 ⧽
        if (Match(TokenKind.LBrace))
        {
            var entries = new List<(AstExpr, AstExpr)>();
            if (!Check(TokenKind.RBrace))
            {
                do
                {
                    var k = ParseExpression();
                    Expect(TokenKind.Colon);
                    var v = ParseExpression();
                    entries.Add((k, v));
                } while (Match(TokenKind.Comma));
            }
            Expect(TokenKind.RBrace);
            return new DictLiteralExpr(entries);
        }

        if (Match(TokenKind.LParen))
        {
            var inner = ParseExpression();
            Expect(TokenKind.RParen);
            return inner;
        }

        throw new StarlightException("UNEXPECTED_TOKEN");
    }
}
