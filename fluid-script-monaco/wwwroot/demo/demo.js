import {
  createFluidScriptEditor,
  getFluidScriptText,
  setFluidScriptDiagnostics,
  setFluidScriptText
} from "/monaco/fluidscript-editor.js";

const initialSource = `// Edit this program and press Ctrl+Enter.
function sumTo(limit: int): int
    dim total = 0
    for i = 1 to limit
        total += i
    end
    return total
end

dim total = sumTo(5)
if total > 10
    print("sum = {total}")
else
    print("the sum is small")
end
`;

/** Demo-only wiring: connects the reusable Monaco adapter to this page's API and controls. */
export function startFluidScriptDemo(monaco) {
  const editor = createFluidScriptEditor(monaco, document.querySelector("#editor"), {
    value: initialSource,
    ariaLabel: "FluidScript source editor"
  });
  const runButton = document.querySelector("#run-button");
  const resetButton = document.querySelector("#reset-button");
  const status = document.querySelector("#status");
  const output = document.querySelector("#output");
  const result = document.querySelector("#result");

  const setReady = () => {
    status.textContent = "Ready";
    runButton.disabled = false;
    resetButton.disabled = false;
  };

  const run = async () => {
    runButton.disabled = true;
    status.textContent = "Running…";
    output.textContent = "";
    result.textContent = "";

    try {
      const response = await fetch("/api/run", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ source: getFluidScriptText(editor) })
      });
      const payload = await response.json();
      if (!response.ok) {
        throw new Error(payload.message ?? `HTTP ${response.status}`);
      }

      setFluidScriptDiagnostics(monaco, editor, payload.diagnostics ?? []);
      output.textContent = payload.output?.length
        ? payload.output.join("\n")
        : (payload.success ? "(no output)" : "Execution stopped with an error.");
      result.textContent = payload.result === null || payload.result === undefined
        ? ""
        : `result: ${payload.result}`;
      status.textContent = payload.success ? "Completed" : "Fix the highlighted issue";
    } catch (error) {
      output.textContent = error.message;
      status.textContent = "Request failed";
    } finally {
      runButton.disabled = false;
    }
  };

  runButton.addEventListener("click", run);
  resetButton.addEventListener("click", () => {
    setFluidScriptText(editor, initialSource);
    output.textContent = "Run the program to see output.";
    result.textContent = "";
    status.textContent = "Ready";
  });
  editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.Enter, run);
  setReady();
}
