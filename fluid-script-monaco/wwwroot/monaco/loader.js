(function loadMonacoPlayground() {
  const monacoBase = "https://cdnjs.cloudflare.com/ajax/libs/monaco-editor/0.55.1/min/vs";
  const fallbackLoader = "https://cdn.jsdelivr.net/npm/monaco-editor@0.55.1/min/vs/loader.js";

  const showFailure = (error) => {
    const status = document.querySelector("#status");
    if (status) status.textContent = `Editor failed to load: ${error.message}`;
    console.error(error);
  };

  const startDemo = (monaco) => {
    import("/demo/demo.js")
      .then(({ startFluidScriptDemo }) => startFluidScriptDemo(monaco))
      .catch(showFailure);
  };

  const loadEditor = () => {
    if (typeof window.require !== "function" || typeof window.require.config !== "function") {
      showFailure(new Error("The Monaco AMD loader is unavailable."));
      return;
    }

    window.require.config({ paths: { vs: monacoBase } });
    window.require(["vs/editor/editor.main"], startDemo, showFailure);
  };

  if (typeof window.require === "function" && typeof window.require.config === "function") {
    loadEditor();
    return;
  }

  const status = document.querySelector("#status");
  if (status) status.textContent = "Loading editor fallback…";
  const loader = document.createElement("script");
  loader.src = fallbackLoader;
  loader.onload = loadEditor;
  loader.onerror = () => showFailure(new Error("Unable to download Monaco from either CDN."));
  document.head.append(loader);
}());
