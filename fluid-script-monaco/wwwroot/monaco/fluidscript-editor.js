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
    showFoldingControls: "always",
    scrollBeyondLastLine: false,
    fontSize: 14,
    tabSize: 4,
    ...editorOptions
  });
  return editor;
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
