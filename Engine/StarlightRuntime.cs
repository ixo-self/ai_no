using System.Globalization;
using System.Text;
using System.Text.Json;

namespace StarlightAI.Engine;

public sealed class StarlightScope
{
    private readonly Dictionary<string, object?> _vars = new();
    public StarlightScope? Parent { get; }

    public StarlightScope(StarlightScope? parent = null)
    {
        Parent = parent;
    }

    public void Define(string name, object? value)
    {
        _vars[name] = value;
    }

    public object? Get(string name)
    {
        if (_vars.TryGetValue(name, out var val))
            return val;
        if (Parent != null)
            return Parent.Get(name);
        throw new StarlightException("UNDEFINED_VAR");
    }

    public void Set(string name, object? value)
    {
        if (_vars.ContainsKey(name))
        {
            _vars[name] = value;
            return;
        }
        if (Parent != null)
        {
            Parent.Set(name, value);
            return;
        }
        throw new StarlightException("UNDEFINED_VAR");
    }
}

public sealed record StarlightUserFunction(
    FuncDeclStmt Declaration,
    StarlightScope Closure);

public sealed record BoundMethodTarget(object? Target, char Glyph);

public sealed class StarlightRuntime
{
    private const int MaxSteps = 50000;
    private const int MaxRecursionDepth = 64;
    private const int MaxAiCalls = 6;
    private const int MaxOutputLength = 65536;

    private readonly GeminiService _gemini;
    private readonly StringBuilder _output = new();
    private int _steps;
    private int _recursionDepth;
    private int _aiCalls;

    private sealed class BreakSignal : Exception { }
    private sealed class ContinueSignal : Exception { }
    private sealed class ReturnSignal : Exception
    {
        public object? Value { get; }
        public ReturnSignal(object? value) => Value = value;
    }

    public StarlightRuntime(GeminiService gemini)
    {
        _gemini = gemini;
    }

    private void TickStep()
    {
        _steps++;
        if (_steps > MaxSteps)
            throw new StarlightException("STEP_LIMIT");
    }

    private void AppendOutput(string text, bool newLine)
    {
        if (_output.Length + text.Length > MaxOutputLength)
            throw new StarlightException("OUTPUT_LIMIT");
        if (newLine)
            _output.AppendLine(text);
        else
            _output.Append(text);
    }

    public async Task<string> ExecuteProgramAsync(string sourceCode, CancellationToken ct = default)
    {
        var tokens = StarlightLexer.Tokenize(sourceCode);
        var parser = new StarlightParser(tokens);
        var program = parser.ParseProgram();

        var globalScope = new StarlightScope();
        try
        {
            await ExecBlockAsync(program, globalScope, false, ct);
        }
        catch (ReturnSignal)
        {
            // Top-level return terminates program cleanly
        }

        return _output.ToString().TrimEnd('\r', '\n');
    }

    private async Task ExecStmtAsync(AstStmt stmt, StarlightScope scope, CancellationToken ct)
    {
        TickStep();
        ct.ThrowIfCancellationRequested();

        switch (stmt)
        {
            case BlockStmt block:
                await ExecBlockAsync(block, scope, true, ct);
                break;

            case VarDeclStmt varDecl:
            {
                object? val = varDecl.Initializer != null
                    ? await EvalExprAsync(varDecl.Initializer, scope, ct)
                    : GetDefaultValue(varDecl.TypeKind);
                val = CoerceValue(val, varDecl.TypeKind);
                scope.Define(varDecl.Name, val);
                break;
            }

            case FuncDeclStmt funcDecl:
            {
                var fn = new StarlightUserFunction(funcDecl, scope);
                scope.Define(funcDecl.Name, fn);
                break;
            }

            case ReturnStmt retStmt:
            {
                object? val = retStmt.Value != null
                    ? await EvalExprAsync(retStmt.Value, scope, ct)
                    : null;
                throw new ReturnSignal(val);
            }

            case IfStmt ifStmt:
            {
                bool cond = IsTruthy(await EvalExprAsync(ifStmt.Condition, scope, ct));
                if (cond)
                    await ExecStmtAsync(ifStmt.ThenBranch, new StarlightScope(scope), ct);
                else if (ifStmt.ElseBranch != null)
                    await ExecStmtAsync(ifStmt.ElseBranch, new StarlightScope(scope), ct);
                break;
            }

            case WhileStmt whileStmt:
            {
                while (IsTruthy(await EvalExprAsync(whileStmt.Condition, scope, ct)))
                {
                    TickStep();
                    try
                    {
                        await ExecStmtAsync(whileStmt.Body, new StarlightScope(scope), ct);
                    }
                    catch (ContinueSignal)
                    {
                        continue;
                    }
                    catch (BreakSignal)
                    {
                        break;
                    }
                }
                break;
            }

            case ForStmt forStmt:
            {
                var loopScope = new StarlightScope(scope);
                if (forStmt.Init != null)
                    await ExecStmtAsync(forStmt.Init, loopScope, ct);

                while (forStmt.Condition == null || IsTruthy(await EvalExprAsync(forStmt.Condition, loopScope, ct)))
                {
                    TickStep();
                    try
                    {
                        await ExecStmtAsync(forStmt.Body, new StarlightScope(loopScope), ct);
                    }
                    catch (ContinueSignal)
                    {
                        // proceed to step
                    }
                    catch (BreakSignal)
                    {
                        break;
                    }

                    if (forStmt.Step != null)
                        await EvalExprAsync(forStmt.Step, loopScope, ct);
                }
                break;
            }

            case ForeachStmt foreachStmt:
            {
                object? colObj = await EvalExprAsync(foreachStmt.Collection, scope, ct);
                IEnumerable<object?> items = colObj switch
                {
                    List<object?> list => list.ToList(),
                    Dictionary<string, object?> dict => dict.Keys.Cast<object?>().ToList(),
                    string str => str.Select(c => (object?)c.ToString()).ToList(),
                    _ => throw new StarlightException("NOT_ITERABLE")
                };

                foreach (var item in items)
                {
                    TickStep();
                    var iterScope = new StarlightScope(scope);
                    iterScope.Define(foreachStmt.ItemName, CoerceValue(item, foreachStmt.ItemType));
                    try
                    {
                        await ExecStmtAsync(foreachStmt.Body, iterScope, ct);
                    }
                    catch (ContinueSignal)
                    {
                        continue;
                    }
                    catch (BreakSignal)
                    {
                        break;
                    }
                }
                break;
            }

            case BreakStmt:
                throw new BreakSignal();

            case ContinueStmt:
                throw new ContinueSignal();

            case TryCatchStmt tryStmt:
            {
                try
                {
                    await ExecBlockAsync(tryStmt.TryBlock, new StarlightScope(scope), false, ct);
                }
                catch (Exception ex) when (ex is not BreakSignal and not ContinueSignal and not ReturnSignal and not OperationCanceledException)
                {
                    if (tryStmt.CatchBlock != null)
                    {
                        var catchScope = new StarlightScope(scope);
                        if (!string.IsNullOrEmpty(tryStmt.CatchVarName))
                        {
                            string safeMsg = ex is StarlightException sle ? sle.Code : "RUNTIME_FAULT";
                            catchScope.Define(tryStmt.CatchVarName!, safeMsg);
                        }
                        await ExecBlockAsync(tryStmt.CatchBlock, catchScope, false, ct);
                    }
                    else
                    {
                        throw;
                    }
                }
                finally
                {
                    if (tryStmt.FinallyBlock != null)
                    {
                        await ExecBlockAsync(tryStmt.FinallyBlock, new StarlightScope(scope), false, ct);
                    }
                }
                break;
            }

            case ThrowStmt throwStmt:
            {
                object? errVal = await EvalExprAsync(throwStmt.ExceptionExpr, scope, ct);
                throw new StarlightException(FormatValue(errVal));
            }

            case WriteStmt writeStmt:
            {
                string text = writeStmt.Value != null
                    ? FormatValue(await EvalExprAsync(writeStmt.Value, scope, ct))
                    : string.Empty;
                AppendOutput(text, writeStmt.NewLine);
                break;
            }

            case ExprStmt exprStmt:
                await EvalExprAsync(exprStmt.Expression, scope, ct);
                break;
        }
    }

    private async Task ExecBlockAsync(BlockStmt block, StarlightScope parentScope, bool createNewScope, CancellationToken ct)
    {
        var activeScope = createNewScope ? new StarlightScope(parentScope) : parentScope;
        // Hoist function declarations inside the block (C# local/method semantics)
        foreach (var stmt in block.Statements)
        {
            if (stmt is FuncDeclStmt fnStmt)
                activeScope.Define(fnStmt.Name, new StarlightUserFunction(fnStmt, activeScope));
        }

        foreach (var stmt in block.Statements)
        {
            if (stmt is FuncDeclStmt)
                continue;
            await ExecStmtAsync(stmt, activeScope, ct);
        }
    }

    private async Task<object?> EvalExprAsync(AstExpr expr, StarlightScope scope, CancellationToken ct)
    {
        TickStep();
        ct.ThrowIfCancellationRequested();

        switch (expr)
        {
            case LiteralExpr lit:
                return lit.Value;

            case IdentifierExpr id:
                return scope.Get(id.Name);

            case SymbolExpr sym:
                return sym; // Global symbol glyph such as ⍙ (AI), Ⱛ (ToInt), etc.

            case ArrayLiteralExpr arrLit:
            {
                var list = new List<object?>();
                foreach (var el in arrLit.Elements)
                    list.Add(await EvalExprAsync(el, scope, ct));
                return list;
            }

            case DictLiteralExpr dictLit:
            {
                var dict = new Dictionary<string, object?>();
                foreach (var (kExpr, vExpr) in dictLit.Entries)
                {
                    string k = FormatValue(await EvalExprAsync(kExpr, scope, ct));
                    object? v = await EvalExprAsync(vExpr, scope, ct);
                    dict[k] = v;
                }
                return dict;
            }

            case NewObjectExpr newObj:
                return await EvalNewObjectAsync(newObj, scope, ct);

            case UnaryExpr unary:
                return await EvalUnaryAsync(unary, scope, ct);

            case BinaryExpr bin:
                return await EvalBinaryAsync(bin, scope, ct);

            case TernaryExpr tern:
            {
                bool cond = IsTruthy(await EvalExprAsync(tern.Condition, scope, ct));
                return cond
                    ? await EvalExprAsync(tern.TrueExpr, scope, ct)
                    : await EvalExprAsync(tern.FalseExpr, scope, ct);
            }

            case AssignExpr assign:
                return await EvalAssignAsync(assign, scope, ct);

            case MemberAccessExpr mem:
                return await EvalMemberAccessAsync(mem, scope, ct);

            case IndexAccessExpr idx:
                return await EvalIndexAccessAsync(idx, scope, ct);

            case CallExpr call:
                return await EvalCallAsync(call, scope, ct);

            default:
                throw new StarlightException("UNKNOWN_EXPR");
        }
    }

    private async Task<object?> EvalNewObjectAsync(NewObjectExpr newObj, StarlightScope scope, CancellationToken ct)
    {
        var evalArgs = new List<object?>();
        foreach (var a in newObj.Args)
            evalArgs.Add(await EvalExprAsync(a, scope, ct));

        switch (newObj.TypeKind)
        {
            case TokenKind.KwList:
            {
                var list = new List<object?>();
                if (newObj.InitializerItems != null)
                {
                    foreach (var item in newObj.InitializerItems)
                        list.Add(await EvalExprAsync(item, scope, ct));
                }
                return list;
            }

            case TokenKind.KwDict:
            {
                var dict = new Dictionary<string, object?>();
                if (newObj.DictInitItems != null)
                {
                    foreach (var (k, v) in newObj.DictInitItems)
                        dict[FormatValue(await EvalExprAsync(k, scope, ct))] = await EvalExprAsync(v, scope, ct);
                }
                return dict;
            }

            case TokenKind.KwAIConfig:
            {
                var cfg = new StarlightAIConfig();
                if (evalArgs.Count >= 1 && evalArgs[0] != null)
                    cfg.Model = FormatValue(evalArgs[0]);
                if (evalArgs.Count >= 2 && evalArgs[1] != null)
                    cfg.SystemInstruction = FormatValue(evalArgs[1]);
                if (evalArgs.Count >= 3 && evalArgs[2] != null)
                    cfg.Temperature = ToDouble(evalArgs[2]);
                return cfg;
            }

            case TokenKind.KwAIChat:
            {
                StarlightAIConfig? cfg = evalArgs.Count >= 1 ? evalArgs[0] as StarlightAIConfig : null;
                return new StarlightAIChat(cfg);
            }

            case TokenKind.KwAIResponse:
                return new StarlightAIResponse();

            default:
                return GetDefaultValue(newObj.TypeKind);
        }
    }

    private async Task<object?> EvalUnaryAsync(UnaryExpr unary, StarlightScope scope, CancellationToken ct)
    {
        if (unary.Operator is TokenKind.Increment or TokenKind.Decrement)
        {
            long delta = unary.Operator == TokenKind.Increment ? 1 : -1;
            object? current = await EvalExprAsync(unary.Operand, scope, ct);
            object updated = current is double d ? d + delta : ToInt64(current) + delta;
            await WriteTargetAsync(unary.Operand, updated, scope, ct);
            return unary.IsPostfix ? current : updated;
        }

        object? val = await EvalExprAsync(unary.Operand, scope, ct);
        return unary.Operator switch
        {
            TokenKind.Not => !IsTruthy(val),
            TokenKind.Minus => val is double d ? -d : -ToInt64(val),
            TokenKind.Plus => val is double d ? +d : +ToInt64(val),
            _ => throw new StarlightException("INVALID_UNARY")
        };
    }

    private async Task<object?> EvalBinaryAsync(BinaryExpr bin, StarlightScope scope, CancellationToken ct)
    {
        if (bin.Operator == TokenKind.And)
        {
            var l = await EvalExprAsync(bin.Left, scope, ct);
            if (!IsTruthy(l)) return false;
            return IsTruthy(await EvalExprAsync(bin.Right, scope, ct));
        }

        if (bin.Operator == TokenKind.Or)
        {
            var l = await EvalExprAsync(bin.Left, scope, ct);
            if (IsTruthy(l)) return true;
            return IsTruthy(await EvalExprAsync(bin.Right, scope, ct));
        }

        // Null-coalescing (⍞⍞)
        if (bin.Operator == TokenKind.Question)
        {
            var l = await EvalExprAsync(bin.Left, scope, ct);
            return l ?? await EvalExprAsync(bin.Right, scope, ct);
        }

        object? left = await EvalExprAsync(bin.Left, scope, ct);
        object? right = await EvalExprAsync(bin.Right, scope, ct);

        return EvalBinaryValues(left, bin.Operator, right);
    }

    private static object? EvalBinaryValues(object? left, TokenKind op, object? right)
    {
        switch (op)
        {
            case TokenKind.Plus:
                if (left is string || right is string)
                    return FormatValue(left) + FormatValue(right);
                if (left is double || right is double)
                    return ToDouble(left) + ToDouble(right);
                return ToInt64(left) + ToInt64(right);

            case TokenKind.Minus:
                if (left is double || right is double)
                    return ToDouble(left) - ToDouble(right);
                return ToInt64(left) - ToInt64(right);

            case TokenKind.Star:
                if (left is double || right is double)
                    return ToDouble(left) * ToDouble(right);
                return ToInt64(left) * ToInt64(right);

            case TokenKind.Slash:
                if (left is double || right is double)
                {
                    double denom = ToDouble(right);
                    if (Math.Abs(denom) < 1e-15) throw new StarlightException("DIV_ZERO");
                    return ToDouble(left) / denom;
                }
                else
                {
                    long denom = ToInt64(right);
                    if (denom == 0) throw new StarlightException("DIV_ZERO");
                    return ToInt64(left) / denom;
                }

            case TokenKind.Percent:
                if (left is double || right is double)
                    return ToDouble(left) % ToDouble(right);
                {
                    long denom = ToInt64(right);
                    if (denom == 0) throw new StarlightException("DIV_ZERO");
                    return ToInt64(left) % denom;
                }

            case TokenKind.Eq:
                return AreEqual(left, right);

            case TokenKind.Neq:
                return !AreEqual(left, right);

            case TokenKind.Lt:
                return CompareValues(left, right) < 0;

            case TokenKind.Gt:
                return CompareValues(left, right) > 0;

            case TokenKind.Lte:
                return CompareValues(left, right) <= 0;

            case TokenKind.Gte:
                return CompareValues(left, right) >= 0;

            default:
                throw new StarlightException("INVALID_BIN_OP");
        }
    }

    private async Task<object?> EvalAssignAsync(AssignExpr assign, StarlightScope scope, CancellationToken ct)
    {
        object? rhs = await EvalExprAsync(assign.Value, scope, ct);
        object? finalVal = rhs;

        if (assign.Operator != TokenKind.Assign)
        {
            object? current = await EvalExprAsync(assign.Target, scope, ct);
            TokenKind binOp = assign.Operator switch
            {
                TokenKind.AddAssign => TokenKind.Plus,
                TokenKind.SubAssign => TokenKind.Minus,
                TokenKind.MulAssign => TokenKind.Star,
                TokenKind.DivAssign => TokenKind.Slash,
                _ => throw new StarlightException("INVALID_ASSIGN_OP")
            };
            finalVal = EvalBinaryValues(current, binOp, rhs);
        }

        await WriteTargetAsync(assign.Target, finalVal, scope, ct);
        return finalVal;
    }

    private async Task WriteTargetAsync(AstExpr target, object? value, StarlightScope scope, CancellationToken ct)
    {
        switch (target)
        {
            case IdentifierExpr id:
                scope.Set(id.Name, value);
                return;

            case IndexAccessExpr idxExpr:
            {
                object? container = await EvalExprAsync(idxExpr.Target, scope, ct);
                object? index = await EvalExprAsync(idxExpr.Index, scope, ct);

                if (container is List<object?> list)
                {
                    int i = (int)ToInt64(index);
                    if (i < 0 || i >= list.Count)
                        throw new StarlightException("INDEX_OUT_OF_RANGE");
                    list[i] = value;
                    return;
                }
                if (container is Dictionary<string, object?> dict)
                {
                    dict[FormatValue(index)] = value;
                    return;
                }
                throw new StarlightException("INVALID_INDEX_TARGET");
            }

            case MemberAccessExpr memExpr:
            {
                object? obj = await EvalExprAsync(memExpr.Target, scope, ct);
                if (obj is StarlightAIConfig cfg && memExpr.MemberGlyph.HasValue)
                {
                    SetAIConfigProperty(cfg, memExpr.MemberGlyph.Value, value);
                    return;
                }
                if (obj is Dictionary<string, object?> dict)
                {
                    dict[memExpr.MemberName] = value;
                    return;
                }
                throw new StarlightException("INVALID_MEMBER_ASSIGN");
            }

            default:
                throw new StarlightException("INVALID_LVALUE");
        }
    }

    private static void SetAIConfigProperty(StarlightAIConfig cfg, char glyph, object? value)
    {
        switch (glyph)
        {
            case '\u2360': // ⍠ Model
                cfg.Model = FormatValue(value);
                break;
            case '\u2361': // ⍡ SystemInstruction
                cfg.SystemInstruction = value == null ? null : FormatValue(value);
                break;
            case '\u2362': // ⍢ Temperature
                cfg.Temperature = value == null ? null : ToDouble(value);
                break;
            case '\u2363': // ⍣ TopP
                cfg.TopP = value == null ? null : ToDouble(value);
                break;
            case '\u2365': // ⍥ TopK
                cfg.TopK = value == null ? null : ToInt64(value);
                break;
            case '\u2366': // ⍦ MaxOutputTokens
                cfg.MaxOutputTokens = value == null ? null : ToInt64(value);
                break;
            case '\u2367': // ⍧ StopSequences
                if (value is List<object?> list)
                    cfg.StopSequences = list.Select(FormatValue).ToList();
                else if (value != null)
                    cfg.StopSequences = new List<string> { FormatValue(value) };
                else
                    cfg.StopSequences.Clear();
                break;
            case '\u2368': // ⍨ ResponseMimeType
                cfg.ResponseMimeType = value == null ? null : FormatValue(value);
                break;
            case '\u2369': // ⍩ ResponseSchema
                cfg.ResponseSchema = value;
                break;
            case '\u236A': // ⍪ ThinkingBudget
                cfg.ThinkingBudget = value == null ? null : ToInt64(value);
                break;
            case '\u236B': // ⍫ SafetyThreshold
                cfg.SafetyThreshold = value == null ? null : FormatValue(value);
                break;
            default:
                throw new StarlightException("UNKNOWN_CONFIG_PROP");
        }
    }

    private async Task<object?> EvalMemberAccessAsync(MemberAccessExpr mem, StarlightScope scope, CancellationToken ct)
    {
        object? target = await EvalExprAsync(mem.Target, scope, ct);

        if (mem.MemberGlyph.HasValue)
        {
            char g = mem.MemberGlyph.Value;

            // Property getters that don't require method invocation parentheses:
            // 1. .Length / .Count (Ⱐ)
            if (g == '\u2C20')
            {
                return target switch
                {
                    string s => (long)s.Length,
                    List<object?> l => (long)l.Count,
                    Dictionary<string, object?> d => (long)d.Count,
                    _ => 0L
                };
            }

            // 2. AIConfig properties
            if (target is StarlightAIConfig cfg)
            {
                return g switch
                {
                    '\u2360' => cfg.Model,
                    '\u2361' => cfg.SystemInstruction,
                    '\u2362' => cfg.Temperature,
                    '\u2363' => cfg.TopP,
                    '\u2365' => cfg.TopK,
                    '\u2366' => cfg.MaxOutputTokens,
                    '\u2367' => cfg.StopSequences.Cast<object?>().ToList(),
                    '\u2368' => cfg.ResponseMimeType,
                    '\u2369' => cfg.ResponseSchema,
                    '\u236A' => cfg.ThinkingBudget,
                    '\u236B' => cfg.SafetyThreshold,
                    _ => new BoundMethodTarget(target, g)
                };
            }

            // 3. AIChat HistoryCount property (⍳ \u2373)
            if (target is StarlightAIChat chat && g == '\u2373')
            {
                return (long)chat.History.Count;
            }

            // 4. AIResponse properties
            if (target is StarlightAIResponse resp)
            {
                return g switch
                {
                    '\u2374' => resp.Text,             // ⍴ Text
                    '\u2375' => resp.FinishReason,     // ⍵ FinishReason
                    '\u2376' => resp.PromptTokens,     // ⍶ PromptTokens
                    '\u2377' => resp.CandidateTokens,  // ⍷ CandidateTokens
                    '\u2378' => resp.TotalTokens,      // ⍸ TotalTokens
                    '\u2379' => resp.ModelVersion,     // ⍹ ModelVersion
                    '\u237A' => resp.IsSuccess,        // ⍺ IsSuccess
                    _ => new BoundMethodTarget(target, g)
                };
            }

            return new BoundMethodTarget(target, g);
        }

        if (target is Dictionary<string, object?> dict && dict.TryGetValue(mem.MemberName, out var val))
            return val;

        throw new StarlightException("MEMBER_NOT_FOUND");
    }

    private async Task<object?> EvalIndexAccessAsync(IndexAccessExpr idxExpr, StarlightScope scope, CancellationToken ct)
    {
        object? target = await EvalExprAsync(idxExpr.Target, scope, ct);
        object? index = await EvalExprAsync(idxExpr.Index, scope, ct);

        if (target is List<object?> list)
        {
            int i = (int)ToInt64(index);
            if (i < 0 || i >= list.Count)
                throw new StarlightException("INDEX_OUT_OF_RANGE");
            return list[i];
        }

        if (target is Dictionary<string, object?> dict)
        {
            string key = FormatValue(index);
            return dict.TryGetValue(key, out var val) ? val : null;
        }

        if (target is string str)
        {
            int i = (int)ToInt64(index);
            if (i < 0 || i >= str.Length)
                throw new StarlightException("INDEX_OUT_OF_RANGE");
            return str[i].ToString();
        }

        throw new StarlightException("NOT_INDEXABLE");
    }

    private async Task<object?> EvalCallAsync(CallExpr call, StarlightScope scope, CancellationToken ct)
    {
        object? callee = await EvalExprAsync(call.Callee, scope, ct);
        var args = new List<object?>();
        foreach (var a in call.Args)
            args.Add(await EvalExprAsync(a, scope, ct));

        // 1. User-defined C# function
        if (callee is StarlightUserFunction userFn)
        {
            if (_recursionDepth >= MaxRecursionDepth)
                throw new StarlightException("STACK_OVERFLOW");

            _recursionDepth++;
            try
            {
                var fnScope = new StarlightScope(userFn.Closure);
                var declParams = userFn.Declaration.Params;
                for (int i = 0; i < declParams.Count; i++)
                {
                    object? argVal = i < args.Count ? args[i] : GetDefaultValue(declParams[i].TypeKind);
                    fnScope.Define(declParams[i].Name, CoerceValue(argVal, declParams[i].TypeKind));
                }

                try
                {
                    await ExecBlockAsync(userFn.Declaration.Body, fnScope, false, ct);
                    return null;
                }
                catch (ReturnSignal ret)
                {
                    return CoerceValue(ret.Value, userFn.Declaration.ReturnType);
                }
            }
            finally
            {
                _recursionDepth--;
            }
        }

        // 2. Direct global SymbolExpr call (e.g., Ⱛ(val), Ⱜ(val), Ⱝ(str), Ⱞ(obj), or direct AI glyph ⍚(prompt))
        if (callee is SymbolExpr sym)
        {
            return await InvokeBuiltInMethodAsync(null, sym.Glyph, args, ct);
        }

        // 3. Bound member method call (e.g., ⍙⍤⍚(prompt), list⍤Ⱑ(item), str⍤Ⱗ(), chat⍤⍬(msg))
        if (callee is BoundMethodTarget bound)
        {
            return await InvokeBuiltInMethodAsync(bound.Target, bound.Glyph, args, ct);
        }

        throw new StarlightException("NOT_CALLABLE");
    }

    private void CheckAiQuota()
    {
        _aiCalls++;
        if (_aiCalls > MaxAiCalls)
            throw new StarlightException("AI_QUOTA_EXCEEDED");
    }

    private async Task<object?> InvokeBuiltInMethodAsync(
        object? target,
        char glyph,
        List<object?> args,
        CancellationToken ct)
    {
        // Global or Static AI Calls on ⍙ (\u2359)
        if (target is SymbolExpr { Glyph: '\u2359' } || (target == null && glyph is >= '\u235A' and <= '\u235F'))
        {
            switch (glyph)
            {
                case '\u235A': // ⍚ AI.Ask(prompt) -> string
                {
                    CheckAiQuota();
                    string prompt = args.Count > 0 ? FormatValue(args[0]) : string.Empty;
                    var cfg = new StarlightAIConfig();
                    var turns = new List<ChatTurn> { new("user", prompt) };
                    var resp = await _gemini.GenerateAsync(turns, cfg, ct);
                    if (!resp.IsSuccess)
                        throw new StarlightException("AI_ERROR");
                    return resp.Text;
                }

                case '\u235B': // ⍛ AI.Generate(prompt, sys?, temp?) -> string
                {
                    CheckAiQuota();
                    string prompt = args.Count > 0 ? FormatValue(args[0]) : string.Empty;
                    var cfg = new StarlightAIConfig();
                    if (args.Count > 1 && args[1] != null)
                        cfg.SystemInstruction = FormatValue(args[1]);
                    if (args.Count > 2 && args[2] != null)
                        cfg.Temperature = ToDouble(args[2]);
                    if (args.Count > 3 && args[3] != null)
                        cfg.Model = FormatValue(args[3]);
                    var turns = new List<ChatTurn> { new("user", prompt) };
                    var resp = await _gemini.GenerateAsync(turns, cfg, ct);
                    if (!resp.IsSuccess)
                        throw new StarlightException("AI_ERROR");
                    return resp.Text;
                }

                case '\u235C': // ⍜ AI.Invoke(prompt, config?) -> AIResponse
                {
                    CheckAiQuota();
                    string prompt = args.Count > 0 ? FormatValue(args[0]) : string.Empty;
                    var cfg = (args.Count > 1 ? args[1] as StarlightAIConfig : null) ?? new StarlightAIConfig();
                    var turns = new List<ChatTurn> { new("user", prompt) };
                    return await _gemini.GenerateAsync(turns, cfg, ct);
                }

                case '\u235D': // ⍝ AI.StartChat(config?) -> AIChat
                {
                    var cfg = args.Count > 0 ? args[0] as StarlightAIConfig : null;
                    return new StarlightAIChat(cfg);
                }

                case '\u235F': // ⍟ AI.CountTokens(text, model?) -> long
                {
                    CheckAiQuota();
                    string text = args.Count > 0 ? FormatValue(args[0]) : string.Empty;
                    string? model = args.Count > 1 && args[1] != null ? FormatValue(args[1]) : null;
                    return await _gemini.CountTokensAsync(text, model, ct);
                }
            }
        }

        // AIChat methods
        if (target is StarlightAIChat chat)
        {
            switch (glyph)
            {
                case '\u236C': // ⍬ chat.Send(msg) -> AIResponse
                {
                    CheckAiQuota();
                    string msg = args.Count > 0 ? FormatValue(args[0]) : string.Empty;
                    chat.History.Add(new ChatTurn("user", msg));
                    var resp = await _gemini.GenerateAsync(chat.History, chat.Config, ct);
                    if (resp.IsSuccess)
                        chat.History.Add(new ChatTurn("model", resp.Text));
                    return resp;
                }

                case '\u236D': // ⍭ chat.SendText(msg) -> string
                {
                    CheckAiQuota();
                    string msg = args.Count > 0 ? FormatValue(args[0]) : string.Empty;
                    chat.History.Add(new ChatTurn("user", msg));
                    var resp = await _gemini.GenerateAsync(chat.History, chat.Config, ct);
                    if (!resp.IsSuccess)
                        throw new StarlightException("AI_ERROR");
                    chat.History.Add(new ChatTurn("model", resp.Text));
                    return resp.Text;
                }

                case '\u236F': // ⍯ chat.AddUserMessage(msg)
                {
                    string msg = args.Count > 0 ? FormatValue(args[0]) : string.Empty;
                    chat.History.Add(new ChatTurn("user", msg));
                    return chat;
                }

                case '\u2371': // ⍱ chat.AddModelMessage(msg)
                {
                    string msg = args.Count > 0 ? FormatValue(args[0]) : string.Empty;
                    chat.History.Add(new ChatTurn("model", msg));
                    return chat;
                }

                case '\u2372': // ⍲ chat.Clear()
                {
                    chat.History.Clear();
                    return null;
                }

                case '\u2373': // ⍳ chat.HistoryCount()
                    return (long)chat.History.Count;
            }
        }

        // Standard Library Methods (Glagolitic U+2C20..U+2C2E)
        switch (glyph)
        {
            case '\u2C20': // Ⱐ .Length / .Count
                return target switch
                {
                    string s => (long)s.Length,
                    List<object?> l => (long)l.Count,
                    Dictionary<string, object?> d => (long)d.Count,
                    _ => 0L
                };

            case '\u2C21': // Ⱑ .Add(item)
                if (target is List<object?> listAdd)
                {
                    listAdd.Add(args.Count > 0 ? args[0] : null);
                    return listAdd;
                }
                if (target is Dictionary<string, object?> dictAdd && args.Count >= 2)
                {
                    dictAdd[FormatValue(args[0])] = args[1];
                    return dictAdd;
                }
                throw new StarlightException("INVALID_ADD");

            case '\u2C22': // Ⱒ .Remove(itemOrKey)
                if (target is List<object?> listRem && args.Count > 0)
                    return listRem.Remove(args[0]);
                if (target is Dictionary<string, object?> dictRem && args.Count > 0)
                    return dictRem.Remove(FormatValue(args[0]));
                throw new StarlightException("INVALID_REMOVE");

            case '\u2C23': // Ⱓ .Contains(val)
                if (target is string sCont && args.Count > 0)
                    return sCont.Contains(FormatValue(args[0]), StringComparison.Ordinal);
                if (target is List<object?> lCont && args.Count > 0)
                    return lCont.Any(x => AreEqual(x, args[0]));
                if (target is Dictionary<string, object?> dCont && args.Count > 0)
                    return dCont.ContainsKey(FormatValue(args[0]));
                return false;

            case '\u2C24': // Ⱔ .Substring(start, [len])
            {
                string s = FormatValue(target);
                int start = args.Count > 0 ? Math.Clamp((int)ToInt64(args[0]), 0, s.Length) : 0;
                int len = args.Count > 1 ? Math.Clamp((int)ToInt64(args[1]), 0, s.Length - start) : s.Length - start;
                return s.Substring(start, len);
            }

            case '\u2C25': // Ⱕ .Replace(old, new)
            {
                string s = FormatValue(target);
                string oldVal = args.Count > 0 ? FormatValue(args[0]) : string.Empty;
                string newVal = args.Count > 1 ? FormatValue(args[1]) : string.Empty;
                return string.IsNullOrEmpty(oldVal) ? s : s.Replace(oldVal, newVal, StringComparison.Ordinal);
            }

            case '\u2C26': // Ⱖ .Split(sep)
            {
                string s = FormatValue(target);
                string sep = args.Count > 0 ? FormatValue(args[0]) : " ";
                return s.Split(sep).Select(p => (object?)p).ToList();
            }

            case '\u2C27': // Ⱗ .Trim()
                return FormatValue(target).Trim();

            case '\u2C28': // Ⱘ .ToUpper()
                return FormatValue(target).ToUpperInvariant();

            case '\u2C29': // Ⱙ .ToLower()
                return FormatValue(target).ToLowerInvariant();

            case '\u2C2A': // Ⱚ .ToString()
                return FormatValue(target);

            case '\u2C2B': // Ⱛ ToInt
            {
                object? v = target ?? (args.Count > 0 ? args[0] : 0L);
                return ToInt64(v);
            }

            case '\u2C2C': // Ⱜ ToDouble
            {
                object? v = target ?? (args.Count > 0 ? args[0] : 0.0);
                return ToDouble(v);
            }

            case '\u2C2D': // Ⱝ JsonParse
            {
                string json = FormatValue(target ?? (args.Count > 0 ? args[0] : "{}"));
                using var doc = JsonDocument.Parse(json);
                return ConvertJsonElement(doc.RootElement);
            }

            case '\u2C2E': // Ⱞ JsonStringify
            {
                object? obj = target ?? (args.Count > 0 ? args[0] : null);
                return JsonSerializer.Serialize(obj);
            }
        }

        throw new StarlightException("UNKNOWN_METHOD");
    }

    private static object? ConvertJsonElement(JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var dict = new Dictionary<string, object?>();
                foreach (var prop in el.EnumerateObject())
                    dict[prop.Name] = ConvertJsonElement(prop.Value);
                return dict;
            }
            case JsonValueKind.Array:
            {
                var list = new List<object?>();
                foreach (var item in el.EnumerateArray())
                    list.Add(ConvertJsonElement(item));
                return list;
            }
            case JsonValueKind.String:
                return el.GetString();
            case JsonValueKind.Number:
                return el.TryGetInt64(out long l) ? l : el.GetDouble();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            default:
                return null;
        }
    }

    private static object? GetDefaultValue(TokenKind typeKind)
    {
        return typeKind switch
        {
            TokenKind.KwInt => 0L,
            TokenKind.KwDouble => 0.0,
            TokenKind.KwString => string.Empty,
            TokenKind.KwBool => false,
            TokenKind.KwList => new List<object?>(),
            TokenKind.KwDict => new Dictionary<string, object?>(),
            TokenKind.KwAIConfig => new StarlightAIConfig(),
            TokenKind.KwAIChat => new StarlightAIChat(),
            TokenKind.KwAIResponse => new StarlightAIResponse(),
            _ => null
        };
    }

    private static object? CoerceValue(object? val, TokenKind typeKind)
    {
        if (val == null)
            return null;
        return typeKind switch
        {
            TokenKind.KwInt => ToInt64(val),
            TokenKind.KwDouble => ToDouble(val),
            TokenKind.KwString => FormatValue(val),
            TokenKind.KwBool => IsTruthy(val),
            _ => val
        };
    }

    public static bool IsTruthy(object? val)
    {
        return val switch
        {
            null => false,
            bool b => b,
            long l => l != 0,
            int i => i != 0,
            double d => Math.Abs(d) > 1e-15 && !double.IsNaN(d),
            string s => s.Length > 0,
            List<object?> list => list.Count > 0,
            Dictionary<string, object?> dict => dict.Count > 0,
            _ => true
        };
    }

    public static long ToInt64(object? val)
    {
        return val switch
        {
            null => 0L,
            long l => l,
            int i => i,
            double d => (long)d,
            bool b => b ? 1L : 0L,
            string s when long.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long res) => res,
            string s when double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double dres) => (long)dres,
            _ => 0L
        };
    }

    public static double ToDouble(object? val)
    {
        return val switch
        {
            null => 0.0,
            double d => d,
            long l => l,
            int i => i,
            bool b => b ? 1.0 : 0.0,
            string s when double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double res) => res,
            _ => 0.0
        };
    }

    public static bool AreEqual(object? a, object? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        if (a is long or int or double && b is long or int or double)
            return Math.Abs(ToDouble(a) - ToDouble(b)) < 1e-12;
        return Equals(a, b);
    }

    public static int CompareValues(object? a, object? b)
    {
        if (a is string sa && b is string sb)
            return string.Compare(sa, sb, StringComparison.Ordinal);
        return ToDouble(a).CompareTo(ToDouble(b));
    }

    public static string FormatValue(object? val)
    {
        return val switch
        {
            null => string.Empty,
            string s => s,
            bool b => b ? "true" : "false",
            double d => d.ToString(CultureInfo.InvariantCulture),
            long l => l.ToString(CultureInfo.InvariantCulture),
            StarlightAIResponse resp => resp.Text,
            List<object?> list => "[" + string.Join(", ", list.Select(FormatValue)) + "]",
            Dictionary<string, object?> dict => JsonSerializer.Serialize(dict),
            _ => val.ToString() ?? string.Empty
        };
    }
}
