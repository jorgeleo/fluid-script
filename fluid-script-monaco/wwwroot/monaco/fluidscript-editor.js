const LANGUAGE_ID = "fluidscript";
const MARKER_OWNER = "fluidscript";
const registeredMonacoInstances = new WeakSet();

const keywords = [
  "dim", "const", "type", "if", "else", "while", "for", "to", "step",
  "break", "continue", "switch", "case", "otherwise", "function", "return",
  "try", "catch", "finally", "throw", "import", "end", "true", "false", "null"
];

const operators = [
  "=>", "+=", "-=", "*=", "/=", "%=", "==", "!=", "<=", ">=", "&&", "||",
  "=", "+", "-", "*", "/", "%", "<", ">", "!"
];

/**
 * Registers FluidScript's Monaco language definition. This module intentionally
 * contains no demo DOM selectors, HTTP calls, or server assumptions, so it can
 * be reused by another Monaco host.
 */
export function registerFluidScriptLanguage(monaco) {
  if (registeredMonacoInstances.has(monaco)) {
    return LANGUAGE_ID;
  }

  monaco.languages.register({
    id: LANGUAGE_ID,
    extensions: [".fluid", ".fluidscript"],
    aliases: ["FluidScript", "fluid"]
  });

  monaco.languages.setLanguageConfiguration(LANGUAGE_ID, {
    comments: { lineComment: "//", blockComment: ["/*", "*/"] },
    brackets: [["{", "}"], ["[", "]"], ["(", ")"]],
    autoClosingPairs: [
      { open: "{", close: "}" },
      { open: "[", close: "]" },
      { open: "(", close: ")" },
      { open: "\"", close: "\"" }
    ],
    surroundingPairs: [
      { open: "{", close: "}" },
      { open: "[", close: "]" },
      { open: "(", close: ")" },
      { open: "\"", close: "\"" }
    ],
    folding: {
      markers: {
        start: /^\s*(if|while|for|switch|function|type|try)\b/,
        end: /^\s*end\b/
      }
    }
  });

  monaco.languages.setMonarchTokensProvider(LANGUAGE_ID, {
    defaultToken: "",
    tokenPostfix: ".fluidscript",
    keywords,
    operators,
    tokenizer: {
      root: [
        [/\/\/.*$/, "comment"],
        [/\/\*/, "comment", "@comment"],
        [/#\d{4}-\d{2}-\d{2}(?:T| )?[^#]*#/, "number.date"],
        [/\{[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\}/, "number.guid"],
        [/0x[0-9a-fA-F]+/, "number.hex"],
        [/\d+\.\d+/, "number.float"],
        [/\d+/, "number"],
        [/"/, { token: "string.quote", next: "@string" }],
        [/[a-zA-Z_][a-zA-Z0-9_]*/, { cases: { "@keywords": "keyword", "@default": "identifier" } }],
        [/[{}()[\]]/, "@brackets"],
        [/[;,:.]/, "delimiter"],
        [/[=+\-*/%<>!&|]+/, { cases: { "@operators": "operator", "@default": "operator" } }],
        [/\s+/, "white"]
      ],
      comment: [
        [/[^/*]+/, "comment"],
        [/\*\//, "comment", "@pop"],
        [/[/\*]/, "comment"]
      ],
      string: [
        [/[^\\"]+/, "string"],
        [/\\(?:["\\/bfnrt]|u[0-9a-fA-F]{4})/, "string.escape"],
        [/"/, { token: "string.quote", next: "@pop" }],
        [/\\./, "string.invalid"]
      ]
    }
  });

  monaco.languages.registerFoldingRangeProvider(LANGUAGE_ID, {
    provideFoldingRanges(model) {
      const ranges = [];
      const openBlocks = [];
      const startPattern = /^\s*(if|while|for|switch|function|type|try)\b/;
      const endPattern = /^\s*end\b/;

      for (let lineNumber = 1; lineNumber <= model.getLineCount(); lineNumber += 1) {
        const line = model.getLineContent(lineNumber);
        if (startPattern.test(line)) {
          openBlocks.push(lineNumber);
        } else if (endPattern.test(line) && openBlocks.length > 0) {
          const start = openBlocks.pop();
          if (lineNumber > start) {
            ranges.push({
              start,
              end: lineNumber,
              kind: monaco.languages.FoldingRangeKind.Region
            });
          }
        }
      }

      return ranges;
    }
  });

  registeredMonacoInstances.add(monaco);
  return LANGUAGE_ID;
}

/** Creates an editor configured for FluidScript without knowing anything about the host UI. */
export function createFluidScriptEditor(monaco, container, options = {}) {
  registerFluidScriptLanguage(monaco);
  const { model: providedModel, value, ...editorOptions } = options;
  const model = providedModel ?? monaco.editor.createModel(value ?? "", LANGUAGE_ID);
  const editor = monaco.editor.create(container, {
    model,
    language: LANGUAGE_ID,
    theme: "vs-dark",
    automaticLayout: true,
    minimap: { enabled: false },
    folding: true,
    foldingStrategy: "auto",
    glyphMargin: true,
    showFoldingControls: "always",
    scrollBeyondLastLine: false,
    fontSize: 14,
    tabSize: 4,
    ...editorOptions
  });
  return editor;
}

/** Adds source-line breakpoint and paused-line decorations without page-specific behavior. */
export function createFluidScriptBreakpointController(monaco, editor) {
  let breakpointDecorations = [];
  let pausedDecorations = [];

  const getBreakLines = () => breakpointDecorations
    .map((id) => editor.getModel()?.getDecorationRange(id)?.startLineNumber)
    .filter((line) => Number.isInteger(line))
    .sort((left, right) => left - right);

  const setBreakLines = (lines) => {
    const model = editor.getModel();
    breakpointDecorations = editor.deltaDecorations(
      breakpointDecorations,
      [...new Set(lines)].filter((line) => line > 0).map((line) => ({
        range: new monaco.Range(line, 1, line, 1),
        options: { glyphMarginClassName: "fluidscript-breakpoint-glyph" }
      }))
    );
    return model ? getBreakLines() : [];
  };

  const toggleBreakpoint = (line) => {
    const lines = getBreakLines();
    setBreakLines(lines.includes(line) ? lines.filter((item) => item !== line) : [...lines, line]);
  };

  const setPausedLine = (line) => {
    pausedDecorations = editor.deltaDecorations(pausedDecorations, line > 0 ? [{
      range: new monaco.Range(line, 1, line, 1),
      options: { isWholeLine: true, className: "fluidscript-paused-line", glyphMarginClassName: "fluidscript-paused-glyph" }
    }] : []);
  };

  const mouseSubscription = editor.onMouseDown((event) => {
    if (event.target.type === monaco.editor.MouseTargetType.GUTTER_GLYPH_MARGIN && event.target.position) {
      toggleBreakpoint(event.target.position.lineNumber);
    }
  });

  return {
    getBreakLines,
    setBreakLines,
    clearBreakpoints: () => setBreakLines([]),
    setPausedLine,
    dispose: () => mouseSubscription.dispose()
  };
}

export function getFluidScriptText(editor) {
  return editor.getModel()?.getValue() ?? "";
}

export function setFluidScriptText(editor, text) {
  editor.getModel()?.setValue(text);
}

export function setFluidScriptDiagnostics(monaco, editor, diagnostics = []) {
  const markers = diagnostics.map((diagnostic) => {
    const span = diagnostic.span ?? diagnostic;
    const line = Math.max(1, diagnostic.line ?? span.line ?? 1);
    const column = Math.max(1, (diagnostic.column ?? span.column ?? 0) + 1);
    const length = Math.max(1, diagnostic.length ?? span.length ?? 1);
    const severity = diagnostic.severity === "Warning" || diagnostic.severity === 1
      ? monaco.MarkerSeverity.Warning
      : monaco.MarkerSeverity.Error;
    const suffix = diagnostic.functionName ? ` (${diagnostic.functionName})` : "";
    return {
      severity,
      message: `${diagnostic.code ?? "FS"}: ${diagnostic.message ?? "Unknown diagnostic"}${suffix}`,
      startLineNumber: line,
      startColumn: column,
      endLineNumber: line,
      endColumn: column + length
    };
  });

  const model = editor.getModel();
  if (model) {
    monaco.editor.setModelMarkers(model, MARKER_OWNER, markers);
  }
}

export function clearFluidScriptDiagnostics(monaco, editor) {
  const model = editor.getModel();
  if (model) {
    monaco.editor.setModelMarkers(model, MARKER_OWNER, []);
  }
}

export function disposeFluidScriptEditor(editor) {
  const model = editor.getModel();
  editor.dispose();
  if (model) {
    model.dispose();
  }
}

export { LANGUAGE_ID };
