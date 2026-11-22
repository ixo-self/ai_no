namespace StarlightAI.Engine;

public abstract record AstStmt;

public sealed record BlockStmt(List<AstStmt> Statements) : AstStmt;

public sealed record VarDeclStmt(TokenKind TypeKind, string Name, AstExpr? Initializer) : AstStmt;

public sealed record FuncParam(TokenKind TypeKind, string Name);

public sealed record FuncDeclStmt(TokenKind ReturnType, string Name, List<FuncParam> Params, BlockStmt Body) : AstStmt;

public sealed record ReturnStmt(AstExpr? Value) : AstStmt;

public sealed record IfStmt(AstExpr Condition, AstStmt ThenBranch, AstStmt? ElseBranch) : AstStmt;

public sealed record WhileStmt(AstExpr Condition, AstStmt Body) : AstStmt;

public sealed record ForStmt(AstStmt? Init, AstExpr? Condition, AstExpr? Step, AstStmt Body) : AstStmt;

public sealed record ForeachStmt(TokenKind ItemType, string ItemName, AstExpr Collection, AstStmt Body) : AstStmt;

public sealed record BreakStmt() : AstStmt;

public sealed record ContinueStmt() : AstStmt;

public sealed record TryCatchStmt(BlockStmt TryBlock, string? CatchVarName, BlockStmt? CatchBlock, BlockStmt? FinallyBlock) : AstStmt;

public sealed record ThrowStmt(AstExpr ExceptionExpr) : AstStmt;

public sealed record WriteStmt(AstExpr? Value, bool NewLine) : AstStmt;

public sealed record ExprStmt(AstExpr Expression) : AstStmt;

public abstract record AstExpr;

public sealed record LiteralExpr(object? Value) : AstExpr;

public sealed record IdentifierExpr(string Name) : AstExpr;

public sealed record SymbolExpr(char Glyph) : AstExpr;

public sealed record NewObjectExpr(TokenKind TypeKind, List<AstExpr> Args, List<AstExpr>? InitializerItems, List<(AstExpr Key, AstExpr Value)>? DictInitItems) : AstExpr;

public sealed record ArrayLiteralExpr(List<AstExpr> Elements) : AstExpr;

public sealed record DictLiteralExpr(List<(AstExpr Key, AstExpr Value)> Entries) : AstExpr;

public sealed record UnaryExpr(TokenKind Operator, AstExpr Operand, bool IsPostfix = false) : AstExpr;

public sealed record BinaryExpr(AstExpr Left, TokenKind Operator, AstExpr Right) : AstExpr;

public sealed record TernaryExpr(AstExpr Condition, AstExpr TrueExpr, AstExpr FalseExpr) : AstExpr;

public sealed record AssignExpr(AstExpr Target, TokenKind Operator, AstExpr Value) : AstExpr;

public sealed record MemberAccessExpr(AstExpr Target, string MemberName, char? MemberGlyph) : AstExpr;

public sealed record IndexAccessExpr(AstExpr Target, AstExpr Index) : AstExpr;

public sealed record CallExpr(AstExpr Callee, List<AstExpr> Args) : AstExpr;
