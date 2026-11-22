(function () {
  "use strict";

  const codeBox = document.getElementById("code-box");
  const execBtn = document.getElementById("exec-btn");
  const resultBox = document.getElementById("result-box");
  const refBtn = document.getElementById("ref-btn");
  const refModal = document.getElementById("ref-modal");
  const refCloseBtn = document.getElementById("ref-close-btn");
  const refContent = document.getElementById("ref-content");

  const REFERENCE_SECTIONS = [
    {
      title: "一、程式封印與結構界定符",
      note: "所有星輝編碼必須以起手星印 ⧼⍉⧽ 開頭，並以終結星印 ⧼⍊⧽ 結尾。",
      items: [
        ["⧼⍉⧽", "起手星印 (Main Start)"],
        ["⧼⍊⧽", "終結星印 (Main End)"],
        ["⧼", "{ 左大括號"],
        ["⧽", "} 右大括號"],
        ["⧘", "( 左小括號"],
        ["⧙", ") 右小括號"],
        ["⧚", "[ 左中括號"],
        ["⧛", "] 右中括號"],
        ["⍮", "; 敘述結尾分號"],
        ["⫯", ", 參數分隔逗號"],
        ["⍤", ". 成員存取點"],
        ["⫰", ": 冒號"],
        ["⍆", "=> 表達式箭頭"],
        ["⫱⫱", "// 單行註解"]
      ]
    },
    {
      title: "二、C# 變數型別與常數 (U+2C00–U+2C0F)",
      items: [
        ["Ⰰ", "var"],
        ["Ⰱ", "int / long"],
        ["Ⰲ", "double"],
        ["Ⰳ", "string"],
        ["Ⰴ", "bool"],
        ["Ⰵ", "object / dynamic"],
        ["Ⰶ", "List"],
        ["Ⰷ", "Dict"],
        ["Ⰸ", "void"],
        ["Ⰹ", "AIConfig"],
        ["Ⰺ", "AIChat"],
        ["Ⰻ", "AIResponse"],
        ["Ⰼ", "true"],
        ["Ⰽ", "false"],
        ["Ⰾ", "null"],
        ["Ⰿ", "new"]
      ]
    },
    {
      title: "三、C# 流程控制、函數、例外處理與輸出 (U+2C10–U+2C1F)",
      items: [
        ["Ⱀ", "function 函數定義"],
        ["Ⱁ", "return 回傳"],
        ["Ⱂ", "if 條件判斷"],
        ["Ⱃ", "else 否則分支"],
        ["Ⱄ", "while 條件迴圈"],
        ["Ⱅ", "for 計數迴圈"],
        ["Ⱆ", "foreach 集合迴圈"],
        ["Ⱇ", "in 集合迭代"],
        ["Ⱈ", "break 跳出迴圈"],
        ["Ⱉ", "continue 下一輪"],
        ["Ⱊ", "try 例外嘗試"],
        ["Ⱋ", "catch 例外捕捉"],
        ["Ⱌ", "finally 最終區塊"],
        ["Ⱍ", "throw 拋出例外"],
        ["Ⱎ", "Console.WriteLine"],
        ["Ⱏ", "Console.Write"]
      ]
    },
    {
      title: "四、C# 運算子",
      items: [
        ["⍰", "= 賦值"],
        ["⍇", "+= 加等於"],
        ["⍈", "-= 減等於"],
        ["⍏", "*= 乘等於"],
        ["⍖", "/= 除等於"],
        ["⍐", "++ 遞增"],
        ["⍗", "-- 遞減"],
        ["⧺", "+ 加法 / 串接"],
        ["⧻", "- 減法 / 負號"],
        ["⧶", "* 乘法"],
        ["⧸", "/ 除法"],
        ["⧹", "% 取餘數"],
        ["⍍", "== 等於"],
        ["⍔", "!= 不等於"],
        ["⍃", "< 小於"],
        ["⍄", "> 大於"],
        ["⍅", "<= 小於等於"],
        ["⍌", ">= 大於等於"],
        ["⍋", "&& 邏輯且"],
        ["⍒", "|| 邏輯或"],
        ["⍎", "! 邏輯非"],
        ["⍞", "? 三元條件"],
        ["⍞⍞", "?? 空值合併"]
      ]
    },
    {
      title: "五、Gemini AI API 調用核心系統 (U+2359–U+237A)",
      note: "靜態呼叫範例：⍙⍤⍚⧘⸢...⸣⧙ (AI.Ask) ｜ 完整配置呼叫：⍙⍤⍜⧘⸢...⸣ ⫯ cfg⧙ (AI.Invoke)",
      items: [
        ["⍙", "AI 核心靜態類別"],
        ["⍚", ".Ask(prompt)"],
        ["⍛", ".Generate(p,sys,t)"],
        ["⍜", ".Invoke(p,cfg)"],
        ["⍝", ".StartChat(cfg)"],
        ["⍟", ".CountTokens(p)"],
        ["⍠", "cfg.Model"],
        ["⍡", "cfg.SystemInstruction"],
        ["⍢", "cfg.Temperature"],
        ["⍣", "cfg.TopP"],
        ["⍥", "cfg.TopK"],
        ["⍦", "cfg.MaxOutputTokens"],
        ["⍧", "cfg.StopSequences"],
        ["⍨", "cfg.ResponseMimeType"],
        ["⍩", "cfg.ResponseSchema"],
        ["⍪", "cfg.ThinkingBudget"],
        ["⍫", "cfg.SafetyThreshold"],
        ["⍬", "chat.Send(msg)"],
        ["⍭", "chat.SendText(msg)"],
        ["⍯", "chat.AddUserMessage"],
        ["⍱", "chat.AddModelMessage"],
        ["⍲", "chat.Clear()"],
        ["⍳", "chat.HistoryCount"],
        ["⍴", "res.Text"],
        ["⍵", "res.FinishReason"],
        ["⍶", "res.PromptTokens"],
        ["⍷", "res.CandidateTokens"],
        ["⍸", "res.TotalTokens"],
        ["⍹", "res.ModelVersion"],
        ["⍺", "res.IsSuccess"]
      ]
    },
    {
      title: "六、C# 標準庫屬性與方法 (U+2C20–U+2C2E)",
      items: [
        ["Ⱐ", ".Length / .Count"],
        ["Ⱑ", ".Add(item)"],
        ["Ⱒ", ".Remove(k)"],
        ["Ⱓ", ".Contains(v)"],
        ["Ⱔ", ".Substring(i,n)"],
        ["Ⱕ", ".Replace(a,b)"],
        ["Ⱖ", ".Split(sep)"],
        ["Ⱗ", ".Trim()"],
        ["Ⱘ", ".ToUpper()"],
        ["Ⱙ", ".ToLower()"],
        ["Ⱚ", ".ToString()"],
        ["Ⱛ", "ToInt(v)"],
        ["Ⱜ", "ToDouble(v)"],
        ["Ⱝ", "JsonParse(s)"],
        ["Ⱞ", "JsonStringify(o)"]
      ]
    },
    {
      title: "七、數值常數盧恩星紋 (包覆於 ⍁...⍂ 內)",
      note: "範例：0 = ⍁ᚠ⍂ ｜ 1 = ⍁ᚢ⍂ ｜ 0.7 = ⍁ᚠᚺᚹ⍂ ｜ 256 = ⍁ᚦᚲᚷ⍂",
      items: [
        ["⍁", "數值左界定符"],
        ["⍂", "數值右界定符"],
        ["ᚠ", "數字 0"],
        ["ᚢ", "數字 1"],
        ["ᚦ", "數字 2"],
        ["ᚨ", "數字 3"],
        ["ᚱ", "數字 4"],
        ["ᚲ", "數字 5"],
        ["ᚷ", "數字 6"],
        ["ᚹ", "數字 7"],
        ["ᛁ", "數字 8"],
        ["ᛃ", "數字 9"],
        ["ᚺ", "小數點 ."],
        ["ᚾ", "負號 -"]
      ]
    },
    {
      title: "八、字串常數盲文星紋 (包覆於 ⸢...⸣ 內) 與提非納變數識別字",
      note: "字串第 i 個 UTF-8 位元組經 B[i] ^ ((0xA7 + i * 0x1F) & 0xFF) 遮罩後，拆為高低 4-bit 映射至 0–F 盲文符；變數與函數名由提非納字母 (ⴰ–ⵥ) 組成。",
      items: [
        ["⸢", "字串左界定符"],
        ["⸣", "字串右界定符"],
        ["⠁", "Hex 0"],
        ["⠃", "Hex 1"],
        ["⠇", "Hex 2"],
        ["⠏", "Hex 3"],
        ["⠟", "Hex 4"],
        ["⠿", "Hex 5"],
        ["⠷", "Hex 6"],
        ["⠮", "Hex 7"],
        ["⠪", "Hex 8"],
        ["⠺", "Hex 9"],
        ["⠭", "Hex A"],
        ["⠯", "Hex B"],
        ["⠵", "Hex C"],
        ["⠽", "Hex D"],
        ["⠾", "Hex E"],
        ["⣿", "Hex F"],
        ["ⴰ", "變數識別字 ⴰ"],
        ["ⴱ", "變數識別字 ⴱ"],
        ["ⴲ", "變數識別字 ⴲ"],
        ["ⴳ", "變數識別字 ⴳ"],
        ["ⴴ", "變數識別字 ⴴ"],
        ["ⴵ", "變數識別字 ⴵ"],
        ["ⵀ", "變數識別字 ⵀ"],
        ["ⵁ", "變數識別字 ⵁ"]
      ]
    }
  ];

  function insertGlyphAtCursor(glyph) {
    const start = codeBox.selectionStart || 0;
    const end = codeBox.selectionEnd || 0;
    const val = codeBox.value;
    codeBox.value = val.substring(0, start) + glyph + val.substring(end);
    const nextPos = start + glyph.length;
    codeBox.selectionStart = codeBox.selectionEnd = nextPos;
    codeBox.focus();
  }

  function renderReferenceModal() {
    const frag = document.createDocumentFragment();
    for (const sec of REFERENCE_SECTIONS) {
      const wrap = document.createElement("div");

      const h = document.createElement("div");
      h.className = "ref-section-title";
      h.textContent = "✦ " + sec.title;
      wrap.appendChild(h);

      if (sec.note) {
        const p = document.createElement("div");
        p.className = "ref-note";
        p.textContent = sec.note;
        wrap.appendChild(p);
      }

      const grid = document.createElement("div");
      grid.className = "ref-grid";

      for (const [glyph, desc] of sec.items) {
        const card = document.createElement("div");
        card.className = "ref-item";
        card.addEventListener("click", function () {
          insertGlyphAtCursor(glyph);
        });

        const gSpan = document.createElement("span");
        gSpan.className = "ref-glyph";
        gSpan.textContent = glyph;

        const dSpan = document.createElement("span");
        dSpan.className = "ref-desc";
        dSpan.textContent = desc;

        card.appendChild(gSpan);
        card.appendChild(dSpan);
        grid.appendChild(card);
      }

      wrap.appendChild(grid);
      frag.appendChild(wrap);
    }
    refContent.appendChild(frag);
  }

  renderReferenceModal();

  refBtn.addEventListener("click", function () {
    refModal.classList.remove("hidden");
  });

  refCloseBtn.addEventListener("click", function () {
    refModal.classList.add("hidden");
  });

  refModal.addEventListener("click", function (e) {
    if (e.target === refModal) {
      refModal.classList.add("hidden");
    }
  });

  document.addEventListener("keydown", function (e) {
    if (e.key === "Escape" && !refModal.classList.contains("hidden")) {
      refModal.classList.add("hidden");
    }
  });

  codeBox.addEventListener("keydown", function (e) {
    if (e.key === "Tab") {
      e.preventDefault();
      insertGlyphAtCursor("  ");
    }
  });

  // 載入伺服器記憶體中僅存的一次執行結果
  async function loadLastResult() {
    try {
      const response = await fetch("./api/execute", { method: "GET" });
      if (!response.ok) return;
      const data = await response.json();
      if (typeof data.result === "string") {
        resultBox.textContent = data.result;
      }
    } catch (_) {
      // 無 Debug 輸出
    }
  }

  async function executeCode() {
    const code = codeBox.value;
    // 每次按下執行，代碼輸入白框立即回歸空白
    codeBox.value = "";

    if (!code || !code.trim()) {
      resultBox.textContent = "";
      return;
    }

    execBtn.disabled = true;
    resultBox.textContent = "";

    try {
      const response = await fetch("./api/execute", {
        method: "POST",
        headers: {
          "Content-Type": "application/json"
        },
        body: JSON.stringify({ code: code })
      });

      if (response.status === 404 || response.status === 405) {
        if (window.StarlightClientVM) {
          resultBox.textContent = await window.StarlightClientVM.run(code);
          return;
        }
      }

      if (!response.ok) {
        resultBox.textContent = "✖";
        return;
      }

      const data = await response.json();
      resultBox.textContent = typeof data.result === "string" ? data.result : "";
    } catch (_) {
      if (window.StarlightClientVM) {
        resultBox.textContent = await window.StarlightClientVM.run(code);
      } else {
        resultBox.textContent = "✖";
      }
    } finally {
      execBtn.disabled = false;
    }
  }

  execBtn.addEventListener("click", executeCode);
  loadLastResult();
})();
