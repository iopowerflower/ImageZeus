# Architecture Notes

## Project layering
- `ImageViewer.App`
  - Avalonia UI composition root.
  - Owns app startup, window wiring, exception hooks, render-mode preference wiring.
  - Depends only on interfaces/contracts from Core plus service implementations injected at startup.
- `ImageViewer.Core`
  - Pure domain logic and contracts.
  - Includes sorting, viewport math (`FitZoomCalculator`, `ZoomAnchorMath`), caps constraints, settings models, and service interfaces (`IShellService`, `IRatingService`, `ISettingsStore`, `ICrashLogger`).
- `ImageViewer.Imaging`
  - Decode pipeline and cache ownership.
  - Includes decode limits, in-flight decode dedupe, navigation generation/cancellation coordination, and reference-counted LRU cache semantics.
- `ImageViewer.Platform.Windows`
  - Windows-specific shell/rating abstractions behind Core interfaces.
  - Current implementation includes explorer/properties/print launch and fallback delete/copy behavior.
- `ImageViewer.Persistence`
  - JSON settings store and debounced async settings writer.
- `ImageViewer.Tests`
  - Executable test harness covering math, sorting, caps constraints, persistence roundtrip, and cancellation behavior.

## Safety and stability choices
- Global crash logging writes to `%LocalAppData%\ImageZeus\crash.log` through `FileCrashLogger`.
- Decode path applies pre-decode file/dimension/pixel/frame limits.
- Navigation uses generation IDs and cancellation tokens to prevent stale decode results from replacing current view state.
- Cache uses lease-based reference counting to avoid disposing image buffers that are still in use.
- Settings writes are debounced and executed off the UI thread.

## Platform isolation
- UI/viewmodel code does not directly instantiate platform interop classes.
- `AppServices` is the composition root that wires concrete `WindowsShellService` / `WindowsRatingService` behind Core interfaces.
