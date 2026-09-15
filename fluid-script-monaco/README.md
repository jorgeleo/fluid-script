# FluidScript Monaco sample

This is a small ASP.NET Core host that serves a Monaco-based FluidScript
playground. It demonstrates editing, syntax coloring, block folding, compiler
diagnostics, and running the current source through the real FluidScript .NET
compiler and VM. Click Monaco's glyph gutter to add checkpoints, select
**Debug**, inspect a paused stack frame, edit JSON-native variable values, and
choose **Continue** to resume from the detached checkpoint.

The browser code is intentionally split:

* `wwwroot/monaco/fluidscript-editor.js` is reusable Monaco-specific language
  registration, tokenization, folding, editor creation, and diagnostics
  mapping. It does not know this page's DOM or HTTP API.
* `wwwroot/monaco/loader.js` only loads the pinned Monaco AMD bundle and starts
  the demo.
* `wwwroot/demo/demo.js`, `wwwroot/index.html`, and `wwwroot/styles/site.css`
  are demo-specific UI and API wiring.
* `Program.cs` is the demo backend endpoint that compiles and executes source.
  Debug start and continue requests carry a portable `PCodeDebugStateJson`
  envelope; the server retains no debug session and recompiles/validates the
  source and P-code hash before continuing.

Run it from the repository root:

```text
dotnet run --project fluid-script-monaco/FluidScript.Monaco.csproj
```

Then open the URL printed by ASP.NET Core, normally
`http://localhost:5000` or `https://localhost:5001`. The page first loads
Monaco `0.55.1` from cdnjs and retries through jsDelivr if that AMD loader is
unavailable, so the browser needs access to at least one of those CDNs. The
backend requires no additional package beyond the existing FluidScript project.

The sample is deliberately a development playground, not a production host:
it has no authentication, persistence, multi-user isolation, or arbitrary
host capabilities. Add those at the application boundary before exposing an
execution endpoint outside a trusted environment.

Variable edits use JSON (`42`, `true`, `"text"`, `[1, 2]`, or
`{"name":"Ada"}`). Registered host objects cannot be transferred in a
detached checkpoint, so debug start reports that limitation instead of creating
an invalid continuation.
