"use strict";

// ============================================================================
// 星輝編碼 (Starlight-C#) 沙盒執行核心 (Vercel / Node.js Serverless Engine)
// ============================================================================

const BRAILLE_NIBBLES = [
  "\u2801", // 0: ⠁
  "\u2803", // 1: ⠃
  "\u2807", // 2: ⠇
  "\u280F", // 3: ⠏
  "\u281F", // 4: ⠟
  "\u283F", // 5: ⠿
  "\u2837", // 6: ⠷
  "\u282E", // 7: ⠮
  "\u282A", // 8: ⠪
  "\u283A", // 9: ⠺
  "\u282D", // A: ⠭
  "\u282F", // B: ⠯
  "\u2835", // C: ⠵
  "\u283D", // D: ⠽
  "\u283E", // E: ⠾
  "\u28FF"  // F: ⣿
];

const RUNIC_DIGITS = [
  "\u16A0", // 0: ᚠ
  "\u16A2", // 1: ᚢ
  "\u16A6", // 2: ᚦ
  "\u16A8", // 3: ᚨ
  "\u16B1", // 4: ᚱ
  "\u16B2", // 5: ᚲ
  "\u16B7", // 6: ᚷ
  "\u16B9", // 7: ᚹ
  "\u16C1", // 8: ᛁ
  "\u16C3"  // 9: ᛃ
];

const RUNIC_DOT = "\u16BA";   // ᚺ
const RUNIC_MINUS = "\u16BE"; // ᚾ

const BRAILLE_MAP = new Map(BRAILLE_NIBBLES.map((c, i) => [c, i]));
const RUNE_MAP = new Map([
  ...RUNIC_DIGITS.map((c, i) => [c, String(i)]),
  [RUNIC_DOT, "."],
  [RUNIC_MINUS, "-"]
]);

class StarlightError extends Error {
  constructor(code) {
    super(code);
    this.code = code;
  }
}

function encodeStringLiteral(plainText) {
  const bytes = Buffer.from(String(plainText), "utf8");
  let out = "\u2E22";
  for (let i = 0; i < bytes.length; i++) {
    const mask = (0xA7 + i * 0x1F) & 0xFF;
    const masked = bytes[i] ^ mask;
    out += BRAILLE_NIBBLES[(masked >> 4) & 0x0F];
    out += BRAILLE_NIBBLES[masked & 0x0F];
  }
  out += "\u2E23";
  return out;
}

function decodeStringPayload(brailleContent) {
  if (!brailleContent) return "";
  if ((brailleContent.length & 1) !== 0) throw new StarlightError("STR_LEN");
  const byteCount = brailleContent.length / 2;
  const buf = Buffer.alloc(byteCount);
  for (let i = 0; i < byteCount; i++) {
    const hi = BRAILLE_MAP.get(brailleContent[i * 2]);
    const lo = BRAILLE_MAP.get(brailleContent[i * 2 + 1]);
    if (hi === undefined || lo === undefined) throw new StarlightError("STR_GLYPH");
    const masked = (hi << 4) | lo;
    const mask = (0xA7 + i * 0x1F) & 0xFF;
    buf[i] = masked ^ mask;
  }
  return buf.toString("utf8");
}

function encodeNumberLiteral(numText) {
  const s = String(numText);
  let out = "\u2341";
  for (const ch of s) {
    if (ch >= "0" && ch <= "9") out += RUNIC_DIGITS[ch.charCodeAt(0) - 48];
    else if (ch === ".") out += RUNIC_DOT;
    else if (ch === "-") out += RUNIC_MINUS;
    else throw new StarlightError("NUM_ENC");
  }
  out += "\u2342";
  return out;
}

function decodeNumberPayload(runicContent) {
  if (!runicContent) throw new StarlightError("NUM_EMPTY");
  let s = "";
  for (const ch of runicContent) {
    const d = RUNE_MAP.get(ch);
    if (d === undefined) throw new StarlightError("NUM_GLYPH");
    s += d;
  }
  const n = Number(s);
  if (Number.isNaN(n)) throw new StarlightError("NUM_PARSE");
  return n;
}

function isIdentifierGlyph(c) {
  const code = c.charCodeAt(0);
  return code >= 0x2D30 && code <= 0x2D67;
}

const SINGLE_TOKENS = new Map([
  ["\u29FC", "LBrace"],
  ["\u29FD", "RBrace"],
  ["\u29D8", "LParen"],
  ["\u29D9", "RParen"],
  ["\u29DA", "LBracket"],
  ["\u29DB", "RBracket"],
  ["\u236E", "Semicolon"],
  ["\u2AEF", "Comma"],
  ["\u2364", "Dot"],
  ["\u2AF0", "Colon"],
  ["\u2346", "Arrow"],
  ["\u235E", "Question"],

  ["\u2C00", "KwVar"],
  ["\u2C01", "KwInt"],
  ["\u2C02", "KwDouble"],
  ["\u2C03", "KwString"],
  ["\u2C04", "KwBool"],
  ["\u2C05", "KwObject"],
  ["\u2C06", "KwList"],
  ["\u2C07", "KwDict"],
  ["\u2C08", "KwVoid"],
  ["\u2C09", "KwAIConfig"],
  ["\u2C0A", "KwAIChat"],
  ["\u2C0B", "KwAIResponse"],
  ["\u2C0C", "KwTrue"],
  ["\u2C0D", "KwFalse"],
  ["\u2C0E", "KwNull"],
  ["\u2C0F", "KwNew"],

  ["\u2C10", "KwFunc"],
  ["\u2C11", "KwReturn"],
  ["\u2C12", "KwIf"],
  ["\u2C13", "KwElse"],
  ["\u2C14", "KwWhile"],
  ["\u2C15", "KwFor"],
  ["\u2C16", "KwForeach"],
  ["\u2C17", "KwIn"],
  ["\u2C18", "KwBreak"],
  ["\u2C19", "KwContinue"],
  ["\u2C1A", "KwTry"],
  ["\u2C1B", "KwCatch"],
  ["\u2C1C", "KwFinally"],
  ["\u2C1D", "KwThrow"],
  ["\u2C1E", "KwWriteLine"],
  ["\u2C1F", "KwWrite"],

  ["\u2370", "Assign"],
  ["\u2347", "AddAssign"],
  ["\u2348", "SubAssign"],
  ["\u234F", "MulAssign"],
  ["\u2356", "DivAssign"],
  ["\u2350", "Increment"],
  ["\u2357", "Decrement"],
  ["\u29FA", "Plus"],
  ["\u29FB", "Minus"],
  ["\u29F6", "Star"],
  ["\u29F8", "Slash"],
  ["\u29F9", "Percent"],
  ["\u234D", "Eq"],
  ["\u2354", "Neq"],
  ["\u2343", "Lt"],
  ["\u2344", "Gt"],
  ["\u2345", "Lte"],
  ["\u234C", "Gte"],
  ["\u234B", "And"],
  ["\u2352", "Or"],
  ["\u234E", "Not"]
]);

const TYPE_KEYWORDS = new Set([
  "KwVar", "KwInt", "KwDouble", "KwString", "KwBool", "KwObject",
  "KwList", "KwDict", "KwVoid", "KwAIConfig", "KwAIChat", "KwAIResponse"
]);

function isSymbolGlyph(c) {
  const code = c.charCodeAt(0);
  if (code >= 0x2C20 && code <= 0x2C2E) return true;
  if (code >= 0x2359 && code <= 0x237A && !SINGLE_TOKENS.has(c)) return true;
  return false;
}

function tokenize(source) {
  if (!source || !source.trim()) throw new StarlightError("EMPTY_SRC");
  const tokens = [];
  let i = 0;
  const n = source.length;

  while (i < n) {
    const c = source[i];
    if (/\s/.test(c) || c === "\uFEFF") {
      i++;
      continue;
    }

    // Comment: ⫱⫱
    if (c === "\u2AF1" && i + 1 < n && source[i + 1] === "\u2AF1") {
      i += 2;
      while (i < n && source[i] !== "\n" && source[i] !== "\r") i++;
      continue;
    }

    // Header: ⧼⍉⧽
    if (c === "\u29FC" && i + 2 < n && source[i + 1] === "\u2349" && source[i + 2] === "\u29FD") {
      tokens.push({ kind: "HeaderSeal", raw: "⧼⍉⧽" });
      i += 3;
      continue;
    }

    // Footer: ⧼⍊⧽
    if (c === "\u29FC" && i + 2 < n && source[i + 1] === "\u234A" && source[i + 2] === "\u29FD") {
      tokens.push({ kind: "FooterSeal", raw: "⧼⍊⧽" });
      i += 3;
      continue;
    }

    // String Literal: ⸢...⸣
    if (c === "\u2E22") {
      i++;
      let raw = "";
      while (i < n && source[i] !== "\u2E23") {
        if (!/\s/.test(source[i])) raw += source[i];
        i++;
      }
      if (i >= n || source[i] !== "\u2E23") throw new StarlightError("UNCLOSED_STR");
      i++;
      tokens.push({ kind: "StringLiteral", raw, value: decodeStringPayload(raw) });
      continue;
    }

    // Number Literal: ⍁...⍂
    if (c === "\u2341") {
      i++;
      let raw = "";
      while (i < n && source[i] !== "\u2342") {
        if (!/\s/.test(source[i])) raw += source[i];
        i++;
      }
      if (i >= n || source[i] !== "\u2342") throw new StarlightError("UNCLOSED_NUM");
      i++;
      tokens.push({ kind: "NumberLiteral", raw, value: decodeNumberPayload(raw) });
      continue;
    }

    // Identifier
    if (isIdentifierGlyph(c)) {
      const start = i;
      while (i < n && isIdentifierGlyph(source[i])) i++;
      const id = source.slice(start, i);
      tokens.push({ kind: "Identifier", raw: id, value: id });
      continue;
    }

    if (SINGLE_TOKENS.has(c)) {
      tokens.push({ kind: SINGLE_TOKENS.get(c), raw: c });
      i++;
      continue;
    }

    if (isSymbolGlyph(c)) {
      tokens.push({ kind: "SymbolGlyph", raw: c, value: c });
      i++;
      continue;
    }

    throw new StarlightError("ILLEGAL_GLYPH");
  }

  tokens.push({ kind: "Eof", raw: "" });
  return tokens;
}

class Parser {
  constructor(tokens) {
    this.tokens = tokens;
    this.pos = 0;
  }

  get current() {
    return this.pos < this.tokens.length ? this.tokens[this.pos] : this.tokens[this.tokens.length - 1];
  }

  peek(offset = 1) {
    const idx = this.pos + offset;
    return idx < this.tokens.length ? this.tokens[idx] : this.tokens[this.tokens.length - 1];
  }

  check(kind) {
    return this.current.kind === kind;
  }

  match(...kinds) {
    if (kinds.includes(this.current.kind)) {
      this.pos++;
      return true;
    }
    return false;
  }

  expect(kind, code = "SYNTAX_ERR") {
    if (this.current.kind !== kind) throw new StarlightError(code);
    const tok = this.current;
    this.pos++;
    return tok;
  }

  parseProgram() {
    this.expect("HeaderSeal", "MISSING_HEADER_SEAL");
    const statements = [];
    while (!this.check("FooterSeal") && !this.check("Eof")) {
      statements.push(this.parseStatement());
    }
    this.expect("FooterSeal", "MISSING_FOOTER_SEAL");
    this.expect("Eof", "TRAILING_TOKENS");
    return { type: "BlockStmt", statements };
  }

  parseStatement() {
    if (this.check("LBrace")) return this.parseBlock();
    if (this.check("KwFunc")) return this.parseFuncDecl();

    if (TYPE_KEYWORDS.has(this.current.kind)) {
      if (this.peek(1).kind === "Identifier" && this.peek(2).kind === "LParen") {
        const retType = this.current.kind;
        this.pos++;
        const name = this.expect("Identifier").raw;
        return this.parseFuncSignatureAndBody(retType, name);
      }
      return this.parseVarDecl(true);
    }

    if (this.match("KwReturn")) {
      let value = null;
      if (!this.check("Semicolon")) value = this.parseExpression();
      this.expect("Semicolon");
      return { type: "ReturnStmt", value };
    }

    if (this.match("KwIf")) return this.parseIf();
    if (this.match("KwWhile")) return this.parseWhile();
    if (this.match("KwFor")) return this.parseFor();
    if (this.match("KwForeach")) return this.parseForeach();

    if (this.match("KwBreak")) {
      this.expect("Semicolon");
      return { type: "BreakStmt" };
    }
    if (this.match("KwContinue")) {
      this.expect("Semicolon");
      return { type: "ContinueStmt" };
    }
    if (this.match("KwTry")) return this.parseTryCatch();

    if (this.match("KwThrow")) {
      const expr = this.parseExpression();
      this.expect("Semicolon");
      return { type: "ThrowStmt", expr };
    }

    if (this.match("KwWriteLine")) {
      this.expect("LParen");
      let value = null;
      if (!this.check("RParen")) value = this.parseExpression();
      this.expect("RParen");
      this.expect("Semicolon");
      return { type: "WriteStmt", value, newLine: true };
    }

    if (this.match("KwWrite")) {
      this.expect("LParen");
      let value = null;
      if (!this.check("RParen")) value = this.parseExpression();
      this.expect("RParen");
      this.expect("Semicolon");
      return { type: "WriteStmt", value, newLine: false };
    }

    const expression = this.parseExpression();
    this.expect("Semicolon");
    return { type: "ExprStmt", expression };
  }

  parseBlock() {
    this.expect("LBrace");
    const statements = [];
    while (!this.check("RBrace") && !this.check("Eof")) {
      statements.push(this.parseStatement());
    }
    this.expect("RBrace");
    return { type: "BlockStmt", statements };
  }

  parseVarDecl(requireSemicolon) {
    const typeKind = this.current.kind;
    this.pos++;
    const name = this.expect("Identifier").raw;
    let initializer = null;
    if (this.match("Assign")) initializer = this.parseExpression();
    if (requireSemicolon) this.expect("Semicolon");
    return { type: "VarDeclStmt", typeKind, name, initializer };
  }

  parseFuncDecl() {
    this.expect("KwFunc");
    let retType = "KwObject";
    if (TYPE_KEYWORDS.has(this.current.kind)) {
      retType = this.current.kind;
      this.pos++;
    }
    const name = this.expect("Identifier").raw;
    return this.parseFuncSignatureAndBody(retType, name);
  }

  parseFuncSignatureAndBody(returnType, name) {
    this.expect("LParen");
    const params = [];
    if (!this.check("RParen")) {
      do {
        let pType = "KwObject";
        if (TYPE_KEYWORDS.has(this.current.kind)) {
          pType = this.current.kind;
          this.pos++;
        }
        const pName = this.expect("Identifier").raw;
        params.push({ typeKind: pType, name: pName });
      } while (this.match("Comma"));
    }
    this.expect("RParen");

    let body;
    if (this.match("Arrow")) {
      const expr = this.parseExpression();
      this.expect("Semicolon");
      body = { type: "BlockStmt", statements: [{ type: "ReturnStmt", value: expr }] };
    } else {
      body = this.parseBlock();
    }
    return { type: "FuncDeclStmt", returnType, name, params, body };
  }

  parseIf() {
    this.expect("LParen");
    const condition = this.parseExpression();
    this.expect("RParen");
    const thenBranch = this.parseStatement();
    let elseBranch = null;
    if (this.match("KwElse")) elseBranch = this.parseStatement();
    return { type: "IfStmt", condition, thenBranch, elseBranch };
  }

  parseWhile() {
    this.expect("LParen");
    const condition = this.parseExpression();
    this.expect("RParen");
    const body = this.parseStatement();
    return { type: "WhileStmt", condition, body };
  }

  parseFor() {
    this.expect("LParen");
    let init = null;
    if (!this.check("Semicolon")) {
      if (TYPE_KEYWORDS.has(this.current.kind)) init = this.parseVarDecl(false);
      else init = { type: "ExprStmt", expression: this.parseExpression() };
    }
    this.expect("Semicolon");
    let condition = null;
    if (!this.check("Semicolon")) condition = this.parseExpression();
    this.expect("Semicolon");
    let step = null;
    if (!this.check("RParen")) step = this.parseExpression();
    this.expect("RParen");
    const body = this.parseStatement();
    return { type: "ForStmt", init, condition, step, body };
  }

  parseForeach() {
    this.expect("LParen");
    let itemType = "KwVar";
    if (TYPE_KEYWORDS.has(this.current.kind)) {
      itemType = this.current.kind;
      this.pos++;
    }
    const itemName = this.expect("Identifier").raw;
    this.expect("KwIn");
    const collection = this.parseExpression();
    this.expect("RParen");
    const body = this.parseStatement();
    return { type: "ForeachStmt", itemType, itemName, collection, body };
  }

  parseTryCatch() {
    const tryBlock = this.parseBlock();
    let catchVarName = null;
    let catchBlock = null;
    let finallyBlock = null;

    if (this.match("KwCatch")) {
      if (this.match("LParen")) {
        if (TYPE_KEYWORDS.has(this.current.kind)) this.pos++;
        catchVarName = this.expect("Identifier").raw;
        this.expect("RParen");
      }
      catchBlock = this.parseBlock();
    }
    if (this.match("KwFinally")) {
      finallyBlock = this.parseBlock();
    }
    if (!catchBlock && !finallyBlock) throw new StarlightError("TRY_WITHOUT_HANDLER");
    return { type: "TryCatchStmt", tryBlock, catchVarName, catchBlock, finallyBlock };
  }

  parseExpression() {
    return this.parseAssignment();
  }

  parseAssignment() {
    const expr = this.parseTernary();
    if (["Assign", "AddAssign", "SubAssign", "MulAssign", "DivAssign"].includes(this.current.kind)) {
      const operator = this.current.kind;
      this.pos++;
      const value = this.parseAssignment();
      return { type: "AssignExpr", target: expr, operator, value };
    }
    return expr;
  }

  parseTernary() {
    const expr = this.parseLogicalOr();
    if (this.match("Question")) {
      if (this.match("Question")) {
        const right = this.parseTernary();
        return { type: "BinaryExpr", left: expr, operator: "Question", right };
      }
      const trueExpr = this.parseExpression();
      this.expect("Colon");
      const falseExpr = this.parseTernary();
      return { type: "TernaryExpr", condition: expr, trueExpr, falseExpr };
    }
    return expr;
  }

  parseLogicalOr() {
    let left = this.parseLogicalAnd();
    while (this.match("Or")) {
      const right = this.parseLogicalAnd();
      left = { type: "BinaryExpr", left, operator: "Or", right };
    }
    return left;
  }

  parseLogicalAnd() {
    let left = this.parseEquality();
    while (this.match("And")) {
      const right = this.parseEquality();
      left = { type: "BinaryExpr", left, operator: "And", right };
    }
    return left;
  }

  parseEquality() {
    let left = this.parseRelational();
    while (["Eq", "Neq"].includes(this.current.kind)) {
      const operator = this.current.kind;
      this.pos++;
      const right = this.parseRelational();
      left = { type: "BinaryExpr", left, operator, right };
    }
    return left;
  }

  parseRelational() {
    let left = this.parseAdditive();
    while (["Lt", "Gt", "Lte", "Gte"].includes(this.current.kind)) {
      const operator = this.current.kind;
      this.pos++;
      const right = this.parseAdditive();
      left = { type: "BinaryExpr", left, operator, right };
    }
    return left;
  }

  parseAdditive() {
    let left = this.parseMultiplicative();
    while (["Plus", "Minus"].includes(this.current.kind)) {
      const operator = this.current.kind;
      this.pos++;
      const right = this.parseMultiplicative();
      left = { type: "BinaryExpr", left, operator, right };
    }
    return left;
  }

  parseMultiplicative() {
    let left = this.parseUnary();
    while (["Star", "Slash", "Percent"].includes(this.current.kind)) {
      const operator = this.current.kind;
      this.pos++;
      const right = this.parseUnary();
      left = { type: "BinaryExpr", left, operator, right };
    }
    return left;
  }

  parseUnary() {
    if (["Not", "Minus", "Plus", "Increment", "Decrement"].includes(this.current.kind)) {
      const operator = this.current.kind;
      this.pos++;
      const operand = this.parseUnary();
      return { type: "UnaryExpr", operator, operand, isPostfix: false };
    }
    return this.parsePostfix();
  }

  parsePostfix() {
    let expr = this.parsePrimary();
    while (true) {
      if (this.match("Dot")) {
        if (this.check("Identifier")) {
          const memberName = this.current.raw;
          this.pos++;
          expr = { type: "MemberAccessExpr", target: expr, memberName, memberGlyph: null };
        } else if (this.check("SymbolGlyph")) {
          const glyph = this.current.value;
          this.pos++;
          expr = { type: "MemberAccessExpr", target: expr, memberName: glyph, memberGlyph: glyph };
        } else {
          throw new StarlightError("INVALID_MEMBER");
        }
      } else if (this.match("LParen")) {
        const args = [];
        if (!this.check("RParen")) {
          do {
            args.push(this.parseExpression());
          } while (this.match("Comma"));
        }
        this.expect("RParen");
        expr = { type: "CallExpr", callee: expr, args };
      } else if (this.match("LBracket")) {
        const index = this.parseExpression();
        this.expect("RBracket");
        expr = { type: "IndexAccessExpr", target: expr, index };
      } else if (["Increment", "Decrement"].includes(this.current.kind)) {
        const operator = this.current.kind;
        this.pos++;
        expr = { type: "UnaryExpr", operator, operand: expr, isPostfix: true };
      } else {
        break;
      }
    }
    return expr;
  }

  parsePrimary() {
    if (this.check("NumberLiteral") || this.check("StringLiteral")) {
      const val = this.current.value;
      this.pos++;
      return { type: "LiteralExpr", value: val };
    }
    if (this.match("KwTrue")) return { type: "LiteralExpr", value: true };
    if (this.match("KwFalse")) return { type: "LiteralExpr", value: false };
    if (this.match("KwNull")) return { type: "LiteralExpr", value: null };

    if (this.check("Identifier")) {
      const name = this.current.raw;
      this.pos++;
      return { type: "IdentifierExpr", name };
    }

    if (this.check("SymbolGlyph")) {
      const glyph = this.current.value;
      this.pos++;
      return { type: "SymbolExpr", glyph };
    }

    if (this.match("KwNew")) {
      if (!TYPE_KEYWORDS.has(this.current.kind)) throw new StarlightError("NEW_TYPE_EXPECTED");
      const typeKind = this.current.kind;
      this.pos++;
      const args = [];
      if (this.match("LParen")) {
        if (!this.check("RParen")) {
          do {
            args.push(this.parseExpression());
          } while (this.match("Comma"));
        }
        this.expect("RParen");
      }
      let listInit = null;
      let dictInit = null;
      if (this.match("LBrace")) {
        if (!this.check("RBrace")) {
          if (typeKind === "KwDict") {
            dictInit = [];
            do {
              const k = this.parseExpression();
              this.expect("Colon");
              const v = this.parseExpression();
              dictInit.push([k, v]);
            } while (this.match("Comma"));
          } else {
            listInit = [];
            do {
              listInit.push(this.parseExpression());
            } while (this.match("Comma"));
          }
        }
        this.expect("RBrace");
      }
      return { type: "NewObjectExpr", typeKind, args, listInit, dictInit };
    }

    if (this.match("LBracket")) {
      const elements = [];
      if (!this.check("RBracket")) {
        do {
          elements.push(this.parseExpression());
        } while (this.match("Comma"));
      }
      this.expect("RBracket");
      return { type: "ArrayLiteralExpr", elements };
    }

    if (this.match("LBrace")) {
      const entries = [];
      if (!this.check("RBrace")) {
        do {
          const k = this.parseExpression();
          this.expect("Colon");
          const v = this.parseExpression();
          entries.push([k, v]);
        } while (this.match("Comma"));
      }
      this.expect("RBrace");
      return { type: "DictLiteralExpr", entries };
    }

    if (this.match("LParen")) {
      const inner = this.parseExpression();
      this.expect("RParen");
      return inner;
    }

    throw new StarlightError("UNEXPECTED_TOKEN");
  }
}

class Scope {
  constructor(parent = null) {
    this.parent = parent;
    this.vars = new Map();
  }
  define(name, val) {
    this.vars.set(name, val);
  }
  get(name) {
    if (this.vars.has(name)) return this.vars.get(name);
    if (this.parent) return this.parent.get(name);
    throw new StarlightError("UNDEFINED_VAR");
  }
  set(name, val) {
    if (this.vars.has(name)) {
      this.vars.set(name, val);
      return;
    }
    if (this.parent) {
      this.parent.set(name, val);
      return;
    }
    throw new StarlightError("UNDEFINED_VAR");
  }
}

class AIConfigObj {
  constructor() {
    this.Model = "gemini-2.5-flash";
    this.SystemInstruction = null;
    this.Temperature = null;
    this.TopP = null;
    this.TopK = null;
    this.MaxOutputTokens = null;
    this.StopSequences = [];
    this.ResponseMimeType = null;
    this.ResponseSchema = null;
    this.ThinkingBudget = null;
    this.SafetyThreshold = null;
  }
  clone() {
    const c = new AIConfigObj();
    Object.assign(c, this);
    c.StopSequences = [...this.StopSequences];
    return c;
  }
}

class AIChatObj {
  constructor(cfg = null) {
    this.Config = cfg instanceof AIConfigObj ? cfg.clone() : new AIConfigObj();
    this.History = [];
  }
}

class AIResponseObj {
  constructor(init = {}) {
    this.Text = init.Text ?? "";
    this.FinishReason = init.FinishReason ?? "UNKNOWN";
    this.PromptTokens = init.PromptTokens ?? 0;
    this.CandidateTokens = init.CandidateTokens ?? 0;
    this.TotalTokens = init.TotalTokens ?? 0;
    this.ModelVersion = init.ModelVersion ?? "";
    this.IsSuccess = Boolean(init.IsSuccess);
  }
}

const HARM_CATEGORIES = [
  "HARM_CATEGORY_HARASSMENT",
  "HARM_CATEGORY_HATE_SPEECH",
  "HARM_CATEGORY_SEXUALLY_EXPLICIT",
  "HARM_CATEGORY_DANGEROUS_CONTENT"
];

function normalizeModel(m) {
  let model = typeof m === "string" && m.trim() ? m.trim() : "gemini-2.5-flash";
  if (model.toLowerCase().startsWith("models/")) model = model.slice(7);
  if (!/^[a-zA-Z0-9._-]+$/.test(model)) model = "gemini-2.5-flash";
  return model;
}

async function callGeminiGenerate(turns, config) {
  const apiKey = process.env.GEMINI_API_KEY ? process.env.GEMINI_API_KEY.trim() : "";
  if (!apiKey) throw new StarlightError("AI_KEY_UNSET");

  const model = normalizeModel(config.Model);
  const url = `https://generativelanguage.googleapis.com/v1beta/models/${model}:generateContent`;

  const payload = {
    contents: turns.map(t => ({
      role: t.role === "model" ? "model" : "user",
      parts: [{ text: String(t.text ?? "") }]
    }))
  };

  if (config.SystemInstruction) {
    payload.system_instruction = {
      parts: [{ text: String(config.SystemInstruction) }]
    };
  }

  const genCfg = {};
  if (config.Temperature !== null && config.Temperature !== undefined) {
    genCfg.temperature = Math.min(2.0, Math.max(0.0, Number(config.Temperature)));
  }
  if (config.TopP !== null && config.TopP !== undefined) {
    genCfg.topP = Math.min(1.0, Math.max(0.0, Number(config.TopP)));
  }
  if (config.TopK !== null && config.TopK !== undefined && Number(config.TopK) > 0) {
    genCfg.topK = Math.trunc(Number(config.TopK));
  }
  if (config.MaxOutputTokens !== null && config.MaxOutputTokens !== undefined && Number(config.MaxOutputTokens) > 0) {
    genCfg.maxOutputTokens = Math.trunc(Number(config.MaxOutputTokens));
  }
  if (Array.isArray(config.StopSequences) && config.StopSequences.length > 0) {
    genCfg.stopSequences = config.StopSequences.map(String);
  }
  if (config.ResponseMimeType) {
    genCfg.responseMimeType = String(config.ResponseMimeType);
  }
  if (config.ResponseSchema) {
    genCfg.responseSchema = config.ResponseSchema;
  }
  if (config.ThinkingBudget !== null && config.ThinkingBudget !== undefined && Number(config.ThinkingBudget) >= 0) {
    genCfg.thinkingConfig = { thinkingBudget: Math.trunc(Number(config.ThinkingBudget)) };
  }
  if (Object.keys(genCfg).length > 0) payload.generationConfig = genCfg;

  if (config.SafetyThreshold) {
    const th = String(config.SafetyThreshold).trim().toUpperCase();
    if (["BLOCK_NONE", "BLOCK_ONLY_HIGH", "BLOCK_MEDIUM_AND_ABOVE", "BLOCK_LOW_AND_ABOVE"].includes(th)) {
      payload.safetySettings = HARM_CATEGORIES.map(category => ({ category, threshold: th }));
    }
  }

  try {
    const resp = await fetch(url, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "x-goog-api-key": apiKey
      },
      body: JSON.stringify(payload)
    });

    if (!resp.ok) {
      return new AIResponseObj({
        Text: "",
        FinishReason: "API_ERROR",
        ModelVersion: model,
        IsSuccess: false
      });
    }

    const data = await resp.json();
    let text = "";
    let finishReason = "STOP";
    if (Array.isArray(data.candidates) && data.candidates.length > 0) {
      const first = data.candidates[0];
      if (first.finishReason) finishReason = first.finishReason;
      if (first.content && Array.isArray(first.content.parts)) {
        for (const part of first.content.parts) {
          if (part.thought === true) continue;
          if (typeof part.text === "string") text += part.text;
        }
      }
    }

    const usage = data.usageMetadata || {};
    return new AIResponseObj({
      Text: text,
      FinishReason: finishReason,
      PromptTokens: Number(usage.promptTokenCount || 0),
      CandidateTokens: Number(usage.candidatesTokenCount || 0),
      TotalTokens: Number(usage.totalTokenCount || 0),
      ModelVersion: data.modelVersion || model,
      IsSuccess: true
    });
  } catch (e) {
    if (e instanceof StarlightError) throw e;
    throw new StarlightError("AI_CALL_FAILED");
  }
}

async function callGeminiCountTokens(text, model) {
  const apiKey = process.env.GEMINI_API_KEY ? process.env.GEMINI_API_KEY.trim() : "";
  if (!apiKey) throw new StarlightError("AI_KEY_UNSET");
  const normModel = normalizeModel(model);
  const url = `https://generativelanguage.googleapis.com/v1beta/models/${normModel}:countTokens`;
  try {
    const resp = await fetch(url, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "x-goog-api-key": apiKey
      },
      body: JSON.stringify({
        contents: [{ parts: [{ text: String(text ?? "") }] }]
      })
    });
    if (!resp.ok) return 0;
    const data = await resp.json();
    return Number(data.totalTokens || 0);
  } catch (_) {
    throw new StarlightError("AI_COUNT_FAILED");
  }
}

class BreakSignal {}
class ContinueSignal {}
class ReturnSignal {
  constructor(value) {
    this.value = value;
  }
}

function formatValue(val) {
  if (val === null || val === undefined) return "";
  if (typeof val === "string") return val;
  if (typeof val === "boolean") return val ? "true" : "false";
  if (typeof val === "number") return String(val);
  if (val instanceof AIResponseObj) return val.Text;
  if (Array.isArray(val)) return "[" + val.map(formatValue).join(", ") + "]";
  if (typeof val === "object") return JSON.stringify(val);
  return String(val);
}

function isTruthy(val) {
  if (val === null || val === undefined) return false;
  if (typeof val === "boolean") return val;
  if (typeof val === "number") return val !== 0 && !Number.isNaN(val);
  if (typeof val === "string") return val.length > 0;
  if (Array.isArray(val)) return val.length > 0;
  if (typeof val === "object") return Object.keys(val).length > 0;
  return true;
}

function toNum(val) {
  if (typeof val === "number") return val;
  if (typeof val === "boolean") return val ? 1 : 0;
  if (typeof val === "string") {
    const n = Number(val.trim());
    return Number.isNaN(n) ? 0 : n;
  }
  return 0;
}

class Runtime {
  constructor() {
    this.steps = 0;
    this.recursionDepth = 0;
    this.aiCalls = 0;
    this.output = "";
  }

  tick() {
    this.steps++;
    if (this.steps > 50000) throw new StarlightError("STEP_LIMIT");
  }

  appendOutput(text, newLine) {
    const str = newLine ? text + "\n" : text;
    if (this.output.length + str.length > 65536) throw new StarlightError("OUTPUT_LIMIT");
    this.output += str;
  }

  async execute(source) {
    const tokens = tokenize(source);
    const parser = new Parser(tokens);
    const program = parser.parseProgram();
    const globalScope = new Scope();

    try {
      await this.execBlock(program, globalScope, false);
    } catch (sig) {
      if (!(sig instanceof ReturnSignal)) throw sig;
    }

    return this.output.replace(/[\r\n]+$/, "");
  }

  async execBlock(block, parentScope, createNew) {
    const scope = createNew ? new Scope(parentScope) : parentScope;
    for (const stmt of block.statements) {
      if (stmt.type === "FuncDeclStmt") {
        scope.define(stmt.name, { __userFn: true, decl: stmt, closure: scope });
      }
    }
    for (const stmt of block.statements) {
      if (stmt.type === "FuncDeclStmt") continue;
      await this.execStmt(stmt, scope);
    }
  }

  async execStmt(stmt, scope) {
    this.tick();
    switch (stmt.type) {
      case "BlockStmt":
        await this.execBlock(stmt, scope, true);
        break;
      case "VarDeclStmt": {
        const val = stmt.initializer ? await this.evalExpr(stmt.initializer, scope) : this.defaultValue(stmt.typeKind);
        scope.define(stmt.name, val);
        break;
      }
      case "FuncDeclStmt":
        scope.define(stmt.name, { __userFn: true, decl: stmt, closure: scope });
        break;
      case "ReturnStmt": {
        const val = stmt.value ? await this.evalExpr(stmt.value, scope) : null;
        throw new ReturnSignal(val);
      }
      case "IfStmt": {
        const cond = isTruthy(await this.evalExpr(stmt.condition, scope));
        if (cond) await this.execStmt(stmt.thenBranch, new Scope(scope));
        else if (stmt.elseBranch) await this.execStmt(stmt.elseBranch, new Scope(scope));
        break;
      }
      case "WhileStmt": {
        while (isTruthy(await this.evalExpr(stmt.condition, scope))) {
          this.tick();
          try {
            await this.execStmt(stmt.body, new Scope(scope));
          } catch (sig) {
            if (sig instanceof ContinueSignal) continue;
            if (sig instanceof BreakSignal) break;
            throw sig;
          }
        }
        break;
      }
      case "ForStmt": {
        const loopScope = new Scope(scope);
        if (stmt.init) await this.execStmt(stmt.init, loopScope);
        while (!stmt.condition || isTruthy(await this.evalExpr(stmt.condition, loopScope))) {
          this.tick();
          try {
            await this.execStmt(stmt.body, new Scope(loopScope));
          } catch (sig) {
            if (sig instanceof ContinueSignal) {
              // continue to step
            } else if (sig instanceof BreakSignal) {
              break;
            } else {
              throw sig;
            }
          }
          if (stmt.step) await this.evalExpr(stmt.step, loopScope);
        }
        break;
      }
      case "ForeachStmt": {
        const col = await this.evalExpr(stmt.collection, scope);
        let items;
        if (Array.isArray(col)) items = [...col];
        else if (typeof col === "string") items = [...col];
        else if (col && typeof col === "object") items = Object.keys(col);
        else throw new StarlightError("NOT_ITERABLE");

        for (const item of items) {
          this.tick();
          const iterScope = new Scope(scope);
          iterScope.define(stmt.itemName, item);
          try {
            await this.execStmt(stmt.body, iterScope);
          } catch (sig) {
            if (sig instanceof ContinueSignal) continue;
            if (sig instanceof BreakSignal) break;
            throw sig;
          }
        }
        break;
      }
      case "BreakStmt":
        throw new BreakSignal();
      case "ContinueStmt":
        throw new ContinueSignal();
      case "TryCatchStmt": {
        try {
          await this.execBlock(stmt.tryBlock, new Scope(scope), false);
        } catch (err) {
          if (err instanceof BreakSignal || err instanceof ContinueSignal || err instanceof ReturnSignal) {
            throw err;
          }
          if (stmt.catchBlock) {
            const catchScope = new Scope(scope);
            if (stmt.catchVarName) {
              const safeMsg = err instanceof StarlightError ? err.code : "RUNTIME_FAULT";
              catchScope.define(stmt.catchVarName, safeMsg);
            }
            await this.execBlock(stmt.catchBlock, catchScope, false);
          } else {
            throw err;
          }
        } finally {
          if (stmt.finallyBlock) {
            await this.execBlock(stmt.finallyBlock, new Scope(scope), false);
          }
        }
        break;
      }
      case "ThrowStmt": {
        const errVal = await this.evalExpr(stmt.expr, scope);
        throw new StarlightError(formatValue(errVal));
      }
      case "WriteStmt": {
        const text = stmt.value ? formatValue(await this.evalExpr(stmt.value, scope)) : "";
        this.appendOutput(text, stmt.newLine);
        break;
      }
      case "ExprStmt":
        await this.evalExpr(stmt.expression, scope);
        break;
    }
  }

  defaultValue(typeKind) {
    switch (typeKind) {
      case "KwInt":
      case "KwDouble":
        return 0;
      case "KwString":
        return "";
      case "KwBool":
        return false;
      case "KwList":
        return [];
      case "KwDict":
        return {};
      case "KwAIConfig":
        return new AIConfigObj();
      case "KwAIChat":
        return new AIChatObj();
      case "KwAIResponse":
        return new AIResponseObj();
      default:
        return null;
    }
  }

  async evalExpr(expr, scope) {
    this.tick();
    switch (expr.type) {
      case "LiteralExpr":
        return expr.value;
      case "IdentifierExpr":
        return scope.get(expr.name);
      case "SymbolExpr":
        return { __symbol: expr.glyph };
      case "ArrayLiteralExpr": {
        const arr = [];
        for (const el of expr.elements) arr.push(await this.evalExpr(el, scope));
        return arr;
      }
      case "DictLiteralExpr": {
        const obj = {};
        for (const [kExpr, vExpr] of expr.entries) {
          const k = formatValue(await this.evalExpr(kExpr, scope));
          obj[k] = await this.evalExpr(vExpr, scope);
        }
        return obj;
      }
      case "NewObjectExpr": {
        const args = [];
        for (const a of expr.args) args.push(await this.evalExpr(a, scope));
        if (expr.typeKind === "KwList") {
          const arr = [];
          if (expr.listInit) {
            for (const item of expr.listInit) arr.push(await this.evalExpr(item, scope));
          }
          return arr;
        }
        if (expr.typeKind === "KwDict") {
          const obj = {};
          if (expr.dictInit) {
            for (const [k, v] of expr.dictInit) {
              obj[formatValue(await this.evalExpr(k, scope))] = await this.evalExpr(v, scope);
            }
          }
          return obj;
        }
        if (expr.typeKind === "KwAIConfig") {
          const cfg = new AIConfigObj();
          if (args.length >= 1 && args[0] != null) cfg.Model = formatValue(args[0]);
          if (args.length >= 2 && args[1] != null) cfg.SystemInstruction = formatValue(args[1]);
          if (args.length >= 3 && args[2] != null) cfg.Temperature = toNum(args[2]);
          return cfg;
        }
        if (expr.typeKind === "KwAIChat") {
          return new AIChatObj(args[0] instanceof AIConfigObj ? args[0] : null);
        }
        return this.defaultValue(expr.typeKind);
      }
      case "UnaryExpr": {
        if (expr.operator === "Increment" || expr.operator === "Decrement") {
          const delta = expr.operator === "Increment" ? 1 : -1;
          const cur = toNum(await this.evalExpr(expr.operand, scope));
          const updated = cur + delta;
          await this.writeTarget(expr.operand, updated, scope);
          return expr.isPostfix ? cur : updated;
        }
        const val = await this.evalExpr(expr.operand, scope);
        if (expr.operator === "Not") return !isTruthy(val);
        if (expr.operator === "Minus") return -toNum(val);
        if (expr.operator === "Plus") return +toNum(val);
        throw new StarlightError("INVALID_UNARY");
      }
      case "BinaryExpr": {
        if (expr.operator === "And") {
          const l = await this.evalExpr(expr.left, scope);
          if (!isTruthy(l)) return false;
          return isTruthy(await this.evalExpr(expr.right, scope));
        }
        if (expr.operator === "Or") {
          const l = await this.evalExpr(expr.left, scope);
          if (isTruthy(l)) return true;
          return isTruthy(await this.evalExpr(expr.right, scope));
        }
        if (expr.operator === "Question") {
          const l = await this.evalExpr(expr.left, scope);
          return l !== null && l !== undefined ? l : await this.evalExpr(expr.right, scope);
        }
        const left = await this.evalExpr(expr.left, scope);
        const right = await this.evalExpr(expr.right, scope);
        return this.evalBinValues(left, expr.operator, right);
      }
      case "TernaryExpr": {
        const cond = isTruthy(await this.evalExpr(expr.condition, scope));
        return cond ? await this.evalExpr(expr.trueExpr, scope) : await this.evalExpr(expr.falseExpr, scope);
      }
      case "AssignExpr": {
        const rhs = await this.evalExpr(expr.value, scope);
        let finalVal = rhs;
        if (expr.operator !== "Assign") {
          const cur = await this.evalExpr(expr.target, scope);
          const mapOp = {
            AddAssign: "Plus",
            SubAssign: "Minus",
            MulAssign: "Star",
            DivAssign: "Slash"
          }[expr.operator];
          finalVal = this.evalBinValues(cur, mapOp, rhs);
        }
        await this.writeTarget(expr.target, finalVal, scope);
        return finalVal;
      }
      case "MemberAccessExpr":
        return await this.evalMember(expr, scope);
      case "IndexAccessExpr": {
        const target = await this.evalExpr(expr.target, scope);
        const index = await this.evalExpr(expr.index, scope);
        if (Array.isArray(target) || typeof target === "string") {
          const idx = Math.trunc(toNum(index));
          if (idx < 0 || idx >= target.length) throw new StarlightError("INDEX_OUT_OF_RANGE");
          return target[idx];
        }
        if (target && typeof target === "object") {
          const k = formatValue(index);
          return Object.prototype.hasOwnProperty.call(target, k) ? target[k] : null;
        }
        throw new StarlightError("NOT_INDEXABLE");
      }
      case "CallExpr":
        return await this.evalCall(expr, scope);
      default:
        throw new StarlightError("UNKNOWN_EXPR");
    }
  }

  evalBinValues(left, op, right) {
    switch (op) {
      case "Plus":
        if (typeof left === "string" || typeof right === "string") {
          return formatValue(left) + formatValue(right);
        }
        return toNum(left) + toNum(right);
      case "Minus":
        return toNum(left) - toNum(right);
      case "Star":
        return toNum(left) * toNum(right);
      case "Slash": {
        const r = toNum(right);
        if (Math.abs(r) < 1e-15) throw new StarlightError("DIV_ZERO");
        return toNum(left) / r;
      }
      case "Percent": {
        const r = toNum(right);
        if (Math.abs(r) < 1e-15) throw new StarlightError("DIV_ZERO");
        return toNum(left) % r;
      }
      case "Eq":
        return left === right;
      case "Neq":
        return left !== right;
      case "Lt":
        return left < right;
      case "Gt":
        return left > right;
      case "Lte":
        return left <= right;
      case "Gte":
        return left >= right;
      default:
        throw new StarlightError("INVALID_BIN_OP");
    }
  }

  async writeTarget(target, value, scope) {
    if (target.type === "IdentifierExpr") {
      scope.set(target.name, value);
      return;
    }
    if (target.type === "IndexAccessExpr") {
      const container = await this.evalExpr(target.target, scope);
      const index = await this.evalExpr(target.index, scope);
      if (Array.isArray(container)) {
        const idx = Math.trunc(toNum(index));
        if (idx < 0 || idx >= container.length) throw new StarlightError("INDEX_OUT_OF_RANGE");
        container[idx] = value;
        return;
      }
      if (container && typeof container === "object") {
        container[formatValue(index)] = value;
        return;
      }
      throw new StarlightError("INVALID_INDEX_TARGET");
    }
    if (target.type === "MemberAccessExpr") {
      const obj = await this.evalExpr(target.target, scope);
      if (obj instanceof AIConfigObj && target.memberGlyph) {
        this.setConfigProp(obj, target.memberGlyph, value);
        return;
      }
      if (obj && typeof obj === "object") {
        obj[target.memberName] = value;
        return;
      }
    }
    throw new StarlightError("INVALID_LVALUE");
  }

  setConfigProp(cfg, g, value) {
    switch (g) {
      case "\u2360": cfg.Model = formatValue(value); break;
      case "\u2361": cfg.SystemInstruction = value == null ? null : formatValue(value); break;
      case "\u2362": cfg.Temperature = value == null ? null : toNum(value); break;
      case "\u2363": cfg.TopP = value == null ? null : toNum(value); break;
      case "\u2365": cfg.TopK = value == null ? null : Math.trunc(toNum(value)); break;
      case "\u2366": cfg.MaxOutputTokens = value == null ? null : Math.trunc(toNum(value)); break;
      case "\u2367":
        cfg.StopSequences = Array.isArray(value) ? value.map(formatValue) : (value != null ? [formatValue(value)] : []);
        break;
      case "\u2368": cfg.ResponseMimeType = value == null ? null : formatValue(value); break;
      case "\u2369": cfg.ResponseSchema = value; break;
      case "\u236A": cfg.ThinkingBudget = value == null ? null : Math.trunc(toNum(value)); break;
      case "\u236B": cfg.SafetyThreshold = value == null ? null : formatValue(value); break;
      default: throw new StarlightError("UNKNOWN_CONFIG_PROP");
    }
  }

  async evalMember(mem, scope) {
    const target = await this.evalExpr(mem.target, scope);
    if (mem.memberGlyph) {
      const g = mem.memberGlyph;
      if (g === "\u2C20") {
        if (typeof target === "string" || Array.isArray(target)) return target.length;
        if (target && typeof target === "object") return Object.keys(target).length;
        return 0;
      }
      if (target instanceof AIConfigObj) {
        switch (g) {
          case "\u2360": return target.Model;
          case "\u2361": return target.SystemInstruction;
          case "\u2362": return target.Temperature;
          case "\u2363": return target.TopP;
          case "\u2365": return target.TopK;
          case "\u2366": return target.MaxOutputTokens;
          case "\u2367": return target.StopSequences;
          case "\u2368": return target.ResponseMimeType;
          case "\u2369": return target.ResponseSchema;
          case "\u236A": return target.ThinkingBudget;
          case "\u236B": return target.SafetyThreshold;
        }
      }
      if (target instanceof AIChatObj && g === "\u2373") {
        return target.History.length;
      }
      if (target instanceof AIResponseObj) {
        switch (g) {
          case "\u2374": return target.Text;
          case "\u2375": return target.FinishReason;
          case "\u2376": return target.PromptTokens;
          case "\u2377": return target.CandidateTokens;
          case "\u2378": return target.TotalTokens;
          case "\u2379": return target.ModelVersion;
          case "\u237A": return target.IsSuccess;
        }
      }
      return { __bound: true, target, glyph: g };
    }
    if (target && typeof target === "object" && Object.prototype.hasOwnProperty.call(target, mem.memberName)) {
      return target[mem.memberName];
    }
    throw new StarlightError("MEMBER_NOT_FOUND");
  }

  async evalCall(call, scope) {
    const callee = await this.evalExpr(call.callee, scope);
    const args = [];
    for (const a of call.args) args.push(await this.evalExpr(a, scope));

    if (callee && callee.__userFn) {
      if (this.recursionDepth >= 64) throw new StarlightError("STACK_OVERFLOW");
      this.recursionDepth++;
      try {
        const fnScope = new Scope(callee.closure);
        const params = callee.decl.params;
        for (let i = 0; i < params.length; i++) {
          fnScope.define(params[i].name, i < args.length ? args[i] : this.defaultValue(params[i].typeKind));
        }
        try {
          await this.execBlock(callee.decl.body, fnScope, false);
          return null;
        } catch (sig) {
          if (sig instanceof ReturnSignal) return sig.value;
          throw sig;
        }
      } finally {
        this.recursionDepth--;
      }
    }

    if (callee && callee.__symbol) {
      return await this.invokeBuiltIn(null, callee.__symbol, args);
    }

    if (callee && callee.__bound) {
      return await this.invokeBuiltIn(callee.target, callee.glyph, args);
    }

    throw new StarlightError("NOT_CALLABLE");
  }

  checkAiQuota() {
    this.aiCalls++;
    if (this.aiCalls > 6) throw new StarlightError("AI_QUOTA_EXCEEDED");
  }

  async invokeBuiltIn(target, glyph, args) {
    if ((target && target.__symbol === "\u2359") || (!target && glyph >= "\u235A" && glyph <= "\u235F")) {
      switch (glyph) {
        case "\u235A": { // ⍚ AI.Ask
          this.checkAiQuota();
          const prompt = args.length > 0 ? formatValue(args[0]) : "";
          const resp = await callGeminiGenerate([{ role: "user", text: prompt }], new AIConfigObj());
          if (!resp.IsSuccess) throw new StarlightError("AI_ERROR");
          return resp.Text;
        }
        case "\u235B": { // ⍛ AI.Generate
          this.checkAiQuota();
          const prompt = args.length > 0 ? formatValue(args[0]) : "";
          const cfg = new AIConfigObj();
          if (args.length > 1 && args[1] != null) cfg.SystemInstruction = formatValue(args[1]);
          if (args.length > 2 && args[2] != null) cfg.Temperature = toNum(args[2]);
          if (args.length > 3 && args[3] != null) cfg.Model = formatValue(args[3]);
          const resp = await callGeminiGenerate([{ role: "user", text: prompt }], cfg);
          if (!resp.IsSuccess) throw new StarlightError("AI_ERROR");
          return resp.Text;
        }
        case "\u235C": { // ⍜ AI.Invoke
          this.checkAiQuota();
          const prompt = args.length > 0 ? formatValue(args[0]) : "";
          const cfg = args.length > 1 && args[1] instanceof AIConfigObj ? args[1] : new AIConfigObj();
          return await callGeminiGenerate([{ role: "user", text: prompt }], cfg);
        }
        case "\u235D": // ⍝ AI.StartChat
          return new AIChatObj(args.length > 0 && args[0] instanceof AIConfigObj ? args[0] : null);
        case "\u235F": { // ⍟ AI.CountTokens
          this.checkAiQuota();
          const text = args.length > 0 ? formatValue(args[0]) : "";
          const model = args.length > 1 && args[1] != null ? formatValue(args[1]) : null;
          return await callGeminiCountTokens(text, model);
        }
      }
    }

    if (target instanceof AIChatObj) {
      switch (glyph) {
        case "\u236C": { // ⍬ Send
          this.checkAiQuota();
          const msg = args.length > 0 ? formatValue(args[0]) : "";
          target.History.push({ role: "user", text: msg });
          const resp = await callGeminiGenerate(target.History, target.Config);
          if (resp.IsSuccess) target.History.push({ role: "model", text: resp.Text });
          return resp;
        }
        case "\u236D": { // ⍭ SendText
          this.checkAiQuota();
          const msg = args.length > 0 ? formatValue(args[0]) : "";
          target.History.push({ role: "user", text: msg });
          const resp = await callGeminiGenerate(target.History, target.Config);
          if (!resp.IsSuccess) throw new StarlightError("AI_ERROR");
          target.History.push({ role: "model", text: resp.Text });
          return resp.Text;
        }
        case "\u236F": // ⍯ AddUserMessage
          target.History.push({ role: "user", text: args.length > 0 ? formatValue(args[0]) : "" });
          return target;
        case "\u2371": // ⍱ AddModelMessage
          target.History.push({ role: "model", text: args.length > 0 ? formatValue(args[0]) : "" });
          return target;
        case "\u2372": // ⍲ Clear
          target.History = [];
          return null;
        case "\u2373": // ⍳ HistoryCount
          return target.History.length;
      }
    }

    switch (glyph) {
      case "\u2C20": // Ⱐ Length / Count
        if (typeof target === "string" || Array.isArray(target)) return target.length;
        if (target && typeof target === "object") return Object.keys(target).length;
        return 0;
      case "\u2C21": // Ⱑ Add
        if (Array.isArray(target)) {
          target.push(args.length > 0 ? args[0] : null);
          return target;
        }
        if (target && typeof target === "object" && args.length >= 2) {
          target[formatValue(args[0])] = args[1];
          return target;
        }
        throw new StarlightError("INVALID_ADD");
      case "\u2C22": // Ⱒ Remove
        if (Array.isArray(target) && args.length > 0) {
          const idx = target.indexOf(args[0]);
          if (idx >= 0) {
            target.splice(idx, 1);
            return true;
          }
          return false;
        }
        if (target && typeof target === "object" && args.length > 0) {
          const k = formatValue(args[0]);
          if (Object.prototype.hasOwnProperty.call(target, k)) {
            delete target[k];
            return true;
          }
          return false;
        }
        throw new StarlightError("INVALID_REMOVE");
      case "\u2C23": // Ⱓ Contains
        if (typeof target === "string" && args.length > 0) return target.includes(formatValue(args[0]));
        if (Array.isArray(target) && args.length > 0) return target.includes(args[0]);
        if (target && typeof target === "object" && args.length > 0) {
          return Object.prototype.hasOwnProperty.call(target, formatValue(args[0]));
        }
        return false;
      case "\u2C24": { // Ⱔ Substring
        const s = formatValue(target);
        const start = args.length > 0 ? Math.max(0, Math.min(s.length, Math.trunc(toNum(args[0])))) : 0;
        const len = args.length > 1 ? Math.max(0, Math.min(s.length - start, Math.trunc(toNum(args[1])))) : s.length - start;
        return s.slice(start, start + len);
      }
      case "\u2C25": { // Ⱕ Replace
        const s = formatValue(target);
        const oldVal = args.length > 0 ? formatValue(args[0]) : "";
        const newVal = args.length > 1 ? formatValue(args[1]) : "";
        return oldVal ? s.split(oldVal).join(newVal) : s;
      }
      case "\u2C26": { // Ⱖ Split
        const s = formatValue(target);
        const sep = args.length > 0 ? formatValue(args[0]) : " ";
        return s.split(sep);
      }
      case "\u2C27": // Ⱗ Trim
        return formatValue(target).trim();
      case "\u2C28": // Ⱘ ToUpper
        return formatValue(target).toUpperCase();
      case "\u2C29": // Ⱙ ToLower
        return formatValue(target).toLowerCase();
      case "\u2C2A": // Ⱚ ToString
        return formatValue(target);
      case "\u2C2B": // Ⱛ ToInt
        return Math.trunc(toNum(target ?? (args.length > 0 ? args[0] : 0)));
      case "\u2C2C": // Ⱜ ToDouble
        return toNum(target ?? (args.length > 0 ? args[0] : 0));
      case "\u2C2D": // Ⱝ JsonParse
        return JSON.parse(formatValue(target ?? (args.length > 0 ? args[0] : "{}")));
      case "\u2C2E": // Ⱞ JsonStringify
        return JSON.stringify(target ?? (args.length > 0 ? args[0] : null));
    }

    throw new StarlightError("UNKNOWN_METHOD");
  }
}

let lastResult = "";

async function handler(req, res) {
  if (req.method === "GET") {
    res.statusCode = 200;
    res.setHeader("Content-Type", "application/json; charset=utf-8");
    res.end(JSON.stringify({ result: lastResult }));
    return;
  }

  if (req.method !== "POST") {
    res.statusCode = 200;
    res.setHeader("Content-Type", "application/json; charset=utf-8");
    res.end(JSON.stringify({ result: "✖" }));
    return;
  }

  try {
    let body = req.body;
    if (typeof body === "string") {
      body = JSON.parse(body);
    } else if (!body) {
      const chunks = [];
      for await (const chunk of req) chunks.push(chunk);
      const raw = Buffer.concat(chunks).toString("utf8");
      body = raw ? JSON.parse(raw) : {};
    }

    const code = body && typeof body.code === "string" ? body.code : "";
    if (!code.trim() || code.length > 131072) {
      lastResult = "✖";
      res.statusCode = 200;
      res.setHeader("Content-Type", "application/json; charset=utf-8");
      res.end(JSON.stringify({ result: lastResult }));
      return;
    }

    const runtime = new Runtime();
    const result = await runtime.execute(code);
    lastResult = result;
    res.statusCode = 200;
    res.setHeader("Content-Type", "application/json; charset=utf-8");
    res.end(JSON.stringify({ result: lastResult }));
  } catch (_) {
    lastResult = "✖";
    res.statusCode = 200;
    res.setHeader("Content-Type", "application/json; charset=utf-8");
    res.end(JSON.stringify({ result: lastResult }));
  }
}

module.exports = handler;
module.exports.Runtime = Runtime;
module.exports.encodeStringLiteral = encodeStringLiteral;
module.exports.decodeStringPayload = decodeStringPayload;
module.exports.encodeNumberLiteral = encodeNumberLiteral;
module.exports.decodeNumberPayload = decodeNumberPayload;
