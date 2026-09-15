import {
  createFluidScriptBreakpointController,
  createFluidScriptEditor,
  getFluidScriptText,
  setFluidScriptDiagnostics,
  setFluidScriptText
} from "/monaco/fluidscript-editor.js";

const initialSource = `// Click the gutter beside a line, then choose Debug.
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
  const breakpoints = createFluidScriptBreakpointController(monaco, editor);
  const runButton = document.querySelector("#run-button");
  const debugButton = document.querySelector("#debug-button");
  const continueButton = document.querySelector("#continue-button");
  const resetButton = document.querySelector("#reset-button");
  const status = document.querySelector("#status");
  const output = document.querySelector("#output");
  const result = document.querySelector("#result");
  const frameSelect = document.querySelector("#frame-select");
  const variables = document.querySelector("#variables");
  const debugHint = document.querySelector("#debug-hint");
  let checkpoint = null;
  let frames = [];
  let globals = [];

  const setReady = () => {
    status.textContent = "Ready";
    runButton.disabled = false;
    debugButton.disabled = false;
    resetButton.disabled = false;
  };

  const writeOutput = (lines, replace) => {
    if (replace) {
      output.textContent = lines?.length ? lines.join("\n") : "(no output yet)";
    } else if (lines?.length) {
      output.textContent = output.textContent === "(no output yet)"
        ? lines.join("\n")
        : `${output.textContent}\n${lines.join("\n")}`;
    }
  };

  const clearCheckpoint = (message = "Click the editor gutter to add a checkpoint, then choose Debug.") => {
    checkpoint = null;
    frames = [];
    globals = [];
    continueButton.disabled = true;
    frameSelect.disabled = true;
    frameSelect.replaceChildren();
    variables.replaceChildren();
    debugHint.textContent = message;
    breakpoints.setPausedLine(0);
  };

  const renderVariables = () => {
    variables.replaceChildren();
    const isGlobalScope = frameSelect.value === "globals";
    const frame = isGlobalScope ? null : frames[Number(frameSelect.value) || 0];
    const scopeVariables = isGlobalScope ? globals : frame?.variables;
    if (!scopeVariables) return;
    for (const variable of scopeVariables) {
      const row = document.createElement("div");
      row.className = "variable-row";
      const label = document.createElement("label");
      const input = document.createElement("input");
      const id = `variable-${isGlobalScope ? "global" : frame.index}-${variable.name}`;
      label.htmlFor = id;
      label.textContent = variable.name;
      input.id = id;
      input.dataset.name = variable.name;
      input.dataset.frameIndex = String(frame?.index ?? -1);
      input.dataset.scope = isGlobalScope ? "global" : "frame";
      input.value = variable.jsonValue ?? variable.displayValue;
      input.disabled = !variable.editable;
      input.title = variable.editable
        ? "Use a JSON value, for example 42, true, \"text\", [1, 2], or {\"name\": \"Ada\"}."
        : "This value cannot be changed through the portable debug checkpoint.";
      row.append(label, input);
      variables.append(row);
    }
  };

  const renderFrames = () => {
    frameSelect.replaceChildren();
    if (globals.length > 0) {
      const option = document.createElement("option");
      option.value = "globals";
      option.textContent = "globals";
      frameSelect.append(option);
    }
    for (const frame of frames) {
      const option = document.createElement("option");
      option.value = String(frame.index);
      option.textContent = `${frame.index}: ${frame.functionName} @ ${frame.instructionPointer}`;
      frameSelect.append(option);
    }
    frameSelect.disabled = frames.length === 0 && globals.length === 0;
    renderVariables();
  };

  const request = async (path, body) => {
    const response = await fetch(path, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body)
    });
    const payload = await response.json();
    if (!response.ok) throw new Error(payload.message ?? `HTTP ${response.status}`);
    return payload;
  };

  const showDebugResponse = (payload, replaceOutput) => {
    setFluidScriptDiagnostics(monaco, editor, payload.diagnostics ?? []);
    writeOutput(payload.output ?? [], replaceOutput);
    result.textContent = payload.result === null || payload.result === undefined ? "" : `result: ${payload.result}`;
    if (!payload.success) {
      clearCheckpoint("Fix the highlighted issue before starting a debug run.");
      status.textContent = "Fix the highlighted issue";
      return;
    }
    if (!payload.stopped) {
      clearCheckpoint("Completed. Click the gutter to add a checkpoint, then choose Debug.");
      status.textContent = "Completed";
      return;
    }
    checkpoint = payload.state;
    frames = payload.frames ?? [];
    globals = payload.globals ?? [];
    breakpoints.setPausedLine(payload.line);
    editor.revealLineInCenter(payload.line);
    editor.setPosition({ lineNumber: payload.line, column: 1 });
    continueButton.disabled = false;
    debugHint.textContent = "Edit JSON values below, then continue from this detached checkpoint.";
    renderFrames();
    status.textContent = `Stopped before line ${payload.line}`;
  };

  const run = async () => {
    runButton.disabled = true;
    debugButton.disabled = true;
    status.textContent = "Running…";
    output.textContent = "";
    result.textContent = "";
    clearCheckpoint();
    try {
      const payload = await request("/api/run", { source: getFluidScriptText(editor) });
      setFluidScriptDiagnostics(monaco, editor, payload.diagnostics ?? []);
      output.textContent = payload.output?.length
        ? payload.output.join("\n")
        : (payload.success ? "(no output)" : "Execution stopped with an error.");
      result.textContent = payload.result === null || payload.result === undefined ? "" : `result: ${payload.result}`;
      status.textContent = payload.success ? "Completed" : "Fix the highlighted issue";
    } catch (error) {
      output.textContent = error.message;
      status.textContent = "Request failed";
    } finally {
      runButton.disabled = false;
      debugButton.disabled = false;
    }
  };

  const startDebug = async () => {
    const breakLines = breakpoints.getBreakLines();
    if (breakLines.length === 0) {
      status.textContent = "Add a checkpoint in the gutter first";
      return;
    }
    runButton.disabled = true;
    debugButton.disabled = true;
    status.textContent = "Starting debug run…";
    output.textContent = "";
    result.textContent = "";
    clearCheckpoint();
    try {
      const payload = await request("/api/debug/start", { source: getFluidScriptText(editor), breakLines });
      showDebugResponse(payload, true);
    } catch (error) {
      output.textContent = error.message;
      status.textContent = "Request failed";
    } finally {
      runButton.disabled = false;
      debugButton.disabled = false;
    }
  };

  const continueFromCheckpoint = async () => {
    if (!checkpoint) return;
    continueButton.disabled = true;
    status.textContent = "Continuing…";
    const edits = [...variables.querySelectorAll("input:not(:disabled)")].map((input) => ({
      frameIndex: Number(input.dataset.frameIndex),
      name: input.dataset.name,
      valueJson: input.value,
      scope: input.dataset.scope
    }));
    try {
      const payload = await request("/api/debug/continue", {
        source: getFluidScriptText(editor), state: checkpoint, edits
      });
      showDebugResponse(payload, false);
    } catch (error) {
      output.textContent = `${output.textContent}\n${error.message}`.trim();
      status.textContent = "Continue failed";
      continueButton.disabled = false;
    }
  };

  runButton.addEventListener("click", run);
  debugButton.addEventListener("click", startDebug);
  continueButton.addEventListener("click", continueFromCheckpoint);
  frameSelect.addEventListener("change", renderVariables);
  resetButton.addEventListener("click", () => {
    setFluidScriptText(editor, initialSource);
    breakpoints.clearBreakpoints();
    clearCheckpoint();
    output.textContent = "Run the program to see output.";
    result.textContent = "";
    status.textContent = "Ready";
  });
  editor.onDidChangeModelContent(() => {
    if (checkpoint) {
      clearCheckpoint("Source changed. Start a new debug run to create a matching checkpoint.");
      status.textContent = "Source changed";
    }
  });
  editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.Enter, run);
  setReady();
}
