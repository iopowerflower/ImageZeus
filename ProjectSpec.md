# ImageZeus — Project Specification v2

You are building a Windows-first desktop image viewer/editor called **ImageZeus** in C# + Avalonia.

---

## 1) Objective

Build a fast, stable image viewer (Honeyview-like) with built-in quick editing and "Caps" screen-capture tools. Performance and crash resilience are top priorities — the app must feel instant and never die silently.

---

## 2) Platform & Stack (locked)

1. Language/UI: C# + Avalonia 11.x.
2. Runtime: .NET 8.
3. OS support: Windows 10/11 official; Windows 8.1 best effort.
4. Packaging: Windows `.exe` usable via "Open with".
5. Render backend: Avalonia/Skia.
6. Keep architecture portable for future Linux support (isolate all Windows-only code behind interfaces; the App project must never directly reference Platform.Windows types — use dependency injection or a service locator initialized in the composition root).

---

## 3) Critical Implementation Constraints

These constraints exist because a prior implementation failed on all of them. Every one is mandatory.

### 3a) Skia → Avalonia display path (no PNG round-trip)

**Do not** encode to PNG and decode back to create an Avalonia `Bitmap`. Instead, write pixels directly into a `WriteableBitmap` using `Lock()` and `SKPixmap` or `Marshal.Copy`. This is the single most important performance requirement. Every path that sets `Image.Source` must use this direct-blit approach:
- Initial image load
- Animation frame swap
- Rotate/flip preview
- Any other display update

### 3b) ImageSharp → Skia pixel transfer (no per-pixel SetPixel)

When converting decoded `Image<Rgba32>` data to `SKBitmap`, do **not** loop with `SetPixel(x, y, color)`. Use bulk memory operations: pin the pixel buffer with `image.DangerousGetPixelRowMemory()` or `image.CopyPixelDataTo()` and write entire rows into the `SKBitmap`'s pixel pointer via `Marshal.Copy` or `unsafe` span operations.

### 3c) Thread-safe decode cache with reference counting

The LRU cache must not dispose bitmaps that are currently in use. Use one of:
- Reference counting (increment on `TryGet`, caller decrements when done)
- Immutable snapshot pattern (cache holds its own copy; callers get independent copies)
- Single-writer pattern with generation tokens

The prior implementation had concurrent `Put` calls disposing bitmaps the UI thread was still drawing — this caused random native crashes during navigation. Whichever pattern you choose, **prove it is safe** by reasoning about the interleaving of preload, navigation, and eviction.

### 3d) Decode pipeline deduplication and cancellation

- Use a `ConcurrentDictionary<string, Task>` or `SemaphoreSlim`-per-key to ensure only one decode runs per cache key at a time. A second caller for the same key must await the in-flight task, not start a duplicate decode.
- All preload tasks must use a linked `CancellationToken` that is cancelled on navigation, so stale preloads do not compete with the current image load.
- After every `await` in the load path, check a generation counter or the cancellation token before writing to UI state. A slow decode completing after a fast one must not overwrite the newer result.

### 3e) Bitmap lifecycle management

Every time `Image.Source` is reassigned, the previous `Bitmap`/`WriteableBitmap` must be explicitly disposed (if it implements `IDisposable`). Undisposed bitmaps leak GPU/memory handles, especially during GIF playback and rapid navigation.

### 3f) Global exception handling

Register `AppDomain.CurrentDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`, and Avalonia's `RenderThread` exception handler. Log to a file at `%LocalAppData%\ImageZeus\crash.log` with timestamp and full stack trace. The app should never exit silently — if it crashes, the log must explain why.

### 3g) No fire-and-forget without error handling

Every `async void` method and every `_ = SomeAsync()` call must have a `try/catch` that catches `Exception` (not just `OperationCanceledException`) and logs it. Unobserved task exceptions terminate the process in some .NET configurations.

### 3h) Rendering mode

Default to `Win32RenderingMode.Software` first with `AngleEgl` as fallback. Expose an environment variable `IMAGEZEUS_GPU=1` to reverse the order for users who want GPU acceleration. This avoids native crashes on systems with buggy GPU drivers.

### 3i) Settings writes must not block the UI thread

Use debounced async writes (e.g. 500ms `DispatcherTimer` that coalesces changes, then writes on a background thread). Never call `File.WriteAllText` synchronously from a UI event handler.

---

## 4) Must-Have Viewer Behavior

1. Open image from CLI arg and load image list from same folder.
2. Supported still formats v1: jpg/jpeg, png, bmp, tiff, webp, gif (still).
3. Animated formats v1: GIF + animated WebP (with playback controls).
4. Default fit rule:
   - If image is smaller than viewport on an axis → keep 1:1 on that axis.
   - If larger than viewport on an axis → scale down to fit.
   - Exception: when Zoom Fix is active.
5. Keyboard/mouse controls:
   - Left/Right arrow: previous/next image.
   - F: fullscreen toggle.
   - Q: immediate exit (even if unsaved edits).
   - Ctrl+C: copy current rendered image to clipboard.
   - Z: zoom 100%.
   - X: toggle zoom-fix.
   - Mouse wheel: zoom centered at cursor (pixel under cursor stays fixed on screen).
   - Mouse forward/back buttons (XButton1/XButton2): previous/next image.
6. Multi-monitor fullscreen: use current monitor only.

---

## 5) Top-Right Mini Panel (floating overlay)

1. Location: top-right corner of the viewer area.
2. Behavior:
   - Visible when mouse is over the viewer area.
   - Fade to transparent (~0.35 opacity) on mouse-out over ~300ms.
   - "Lock" checkbox keeps it fully visible when not hovered.
3. Contents:
   - Prev / Next buttons.
   - Lock checkbox.
   - Wrench button to show/hide left side panel.
   - Rating stars display (from Windows `System.SimpleRating`; hidden if rating is 0 or unset).
   - Scrub slider: one tick per image in the current sorted folder list; dragging navigates.
4. Animated-only controls (visible only when the current file is animated):
   - Play/Pause button.
   - Frame slider (seek to specific frame).

---

## 6) Right-Click Context Menu (exact items, in order)

1. Show in Explorer
2. Properties
3. Copy image to clipboard
4. Settings
5. Show/Hide EXIF
6. Zoom: Fix *(checkable toggle; session-only, not persisted)*
7. Zoom: 100%
8. Fullscreen *(toggle)*
9. Sort By *(submenu)*:
   - Name
   - Date Modified
   - Size
   - Type
   - Rating
   - *(separator)*
   - Ascending / Descending *(toggle)*
10. Print
11. Rate *(submenu)*:
    - 1 / 2 / 3 / 4 / 5
12. File *(submenu)*:
    - Copy
    - Cut
    - Delete *(Recycle Bin, no warning prompt)*
13. Exit

The "Zoom: Fix" menu item's checked state must stay in sync with the X keyboard shortcut (both read/write the same `_zoomFix` field; the menu item must read the current value when the menu opens, not just when it was created).

---

## 7) Sorting & Rating Rules

1. Default sort if none persisted: Name + Ascending.
2. Sorting drives both navigation order and scrub slider order.
3. Persist sort field and direction.
4. Rating source for read/write: Windows `System.SimpleRating`.

---

## 8) Left Side Panel (editing tools)

1. Docked left, takes layout space (does not float over image).
2. Open/close with wrench button in the mini panel.
3. Open/closed state is persisted.
4. Toggling the panel must keep the current zoom level and preserve visual stability of the viewed image region (use anchor math to adjust the offset so the center or cursor-position pixel stays fixed).
5. Buttons:
   - Rotate CW
   - Rotate CCW
   - Flip Horizontal
   - Flip Vertical
   - Save (overwrite original)
   - Save As… (file picker)
6. Edit model:
   - Rotate/flip are non-destructive in-memory until Save / Save As.
   - On successful save, clear dirty state and reload from disk.
7. Crop tool: do NOT implement in v1 (exclude from UI entirely).

---

## 9) Caps Tool (name must be "Caps")

1. Selection rectangle:
   - 1px solid border, color `#00FF00`.
2. Capture semantics:
   - Capture pixels as currently rendered on screen (post-zoom/pan/rotate/flip).
   - Do not reinterpret as an original-resolution crop.
3. Constraints:
   - Aspect ratio mode: X:Y text inputs + checkbox.
   - Fixed pixel size mode: Width/Height text inputs + checkbox.
   - Aspect ratio and fixed-size modes are mutually exclusive (enabling one disables the other).
4. Optional post-resize:
   - Checkbox + single text input for largest dimension.
   - Scales uniformly so the longest side equals the target. Works both up and down.
   - Example: 640×480 capture with target 1280 → output 1280×960.
5. Save controls:
   - "Save Caps" checkbox controls visibility of a destination textbox + Browse button.
   - If "Save Caps" is enabled but no destination is chosen, save to the source image's folder.
6. Naming pattern:
   - `{base}_cap_{HHmmssfff}_{seq}.{ext}`
7. Clipboard:
   - "Copy Caps to Clipboard" checkbox; if enabled, every capture is also copied to clipboard.
8. Output format (selectable):
   - Same as source
   - PNG
   - JPEG
   - WebP
   - If "Same as source" is unsupported or ambiguous, fall back to PNG.

---

## 10) EXIF / Metadata Display (v1)

1. Minimal display only (not a full property dump).
2. Show: camera model, date taken, focal length, shutter speed, aperture, ISO.
3. Toggle visibility via context menu item "Show/Hide EXIF".
4. Display as a semi-transparent overlay bar at the bottom of the viewer area.

---

## 11) Save & Quality Rules

1. Preserve source format on Save when possible.
2. Save As allows explicit destination and format selection.
3. JPEG quality:
   - Try to detect and preserve the source encoder's quality setting.
   - If undetectable, use a configurable fallback (default 92).
4. Goal: output file size and quality should stay similar to the original unless the user explicitly changes format.

---

## 12) Persistence Rules

1. Per-user local JSON settings file at `%LocalAppData%\ImageZeus\settings.json`.
2. Load once at app startup.
3. Persist on setting changes (debounced, async — see §3i).
4. Running instances keep their own in-memory copy after startup (no live reload between instances).
5. Persist:
   - Sort field
   - Sort direction
   - Side panel open/closed
   - Caps options (save enabled, path, format, clipboard flag, aspect/fixed values)
   - JPEG fallback quality
6. Do NOT persist:
   - Zoom Fix state (session-only)

---

## 13) Windows Integration (must work)

1. Show in Explorer: open explorer.exe and select the current file.
2. Properties: open the Windows file properties dialog for the current file.
3. Delete: send to Recycle Bin with no warning prompt (use `SHFileOperationW` with `FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT`).
4. File Copy/Cut: place the file on the clipboard as a shell-compatible drop list with the appropriate `Preferred DropEffect`.
5. Rating read/write: via Windows shell property `System.SimpleRating` (use Windows API Code Pack `ShellFile` or equivalent COM interop).

All of these must be wrapped behind the `IShellService` / `IRatingService` interfaces defined in the Core project. The App project must receive them via constructor injection, not `new` up Windows-specific types directly.

---

## 14) Performance Requirements

1. Async decode pipeline with cancellation for stale requests (see §3d).
2. Preload neighbor images at ±2 around the current index, using cancellable background tasks.
3. Memory-bounded LRU cache with safe disposal (see §3c).
4. For large images in fit mode, decode at viewport-appropriate resolution when possible.
5. Decode full-resolution on demand for 100% zoom and save operations.
6. UI thread must never block on decode, IO, or settings writes.
7. Direct pixel blit for Skia → Avalonia (see §3a) and ImageSharp → Skia (see §3b).
8. Dispose old bitmaps when replacing `Image.Source` (see §3e).

---

## 15) Project Structure

1. `ImageViewer.App` — Avalonia UI, composition root, dependency wiring. No direct references to `ImageViewer.Platform.Windows` types in view/viewmodel code; use interfaces from Core.
2. `ImageViewer.Core` — State management, commands, navigation/sort logic, viewport math (`FitZoomCalculator`, `ZoomAnchorMath`), Caps constraint math, `IShellService` / `IRatingService` interfaces.
3. `ImageViewer.Imaging` — Decode pipeline, animation frame management, transforms, encode/save, LRU cache. This project owns all `SKBitmap` and `Image<Rgba32>` lifetime.
4. `ImageViewer.Platform.Windows` — Shell interop, rating service, recycle bin, clipboard. All Win32 / WinForms / COM usage is confined here.
5. `ImageViewer.Persistence` — Settings model and JSON store.
6. `ImageViewer.Tests` — Unit tests for core math, cache safety, sort/order, Caps constraints, persistence, and decode pipeline cancellation.

---

## 16) Security Requirements

This app opens untrusted image files from arbitrary sources. Image parsers are a historically common attack vector (buffer overflows, heap corruption, decompression bombs). These requirements are mandatory.

### 16a) Decode limits

Configure ImageSharp's `Configuration` to enforce hard maximums before decoding begins:
- Maximum pixel dimensions: 32,768 × 32,768 (configurable constant).
- Maximum GIF/WebP frame count: 10,000.
- Maximum decoded pixel area: 256 megapixels (prevents decompression bombs where a small file decodes to an enormous allocation).

If any limit is exceeded, the decode must fail gracefully (display an error message in the viewer, do not crash, do not attempt partial decode).

### 16b) Pre-decode validation

Before passing a file to ImageSharp or Skia, perform cheap sanity checks:
- File size: reject files larger than 1 GB (configurable).
- `Image.Identify()` (which only reads headers) to check dimensions before `Image.Load()`. If dimensions exceed the configured max, skip the file.
- Wrap all decode calls in `try/catch` — a malformed file must never produce an unhandled exception. Log the error and show a placeholder or skip to the next image.

### 16c) Metadata is untrusted display-only data

EXIF strings (camera model, date, etc.) are attacker-controlled. They must only be used for display in a `TextBlock`. Never:
- Use them as file paths, directory names, or arguments to `Process.Start`.
- Use them in string interpolation that gets passed to shell commands.
- Use them to construct SQL, JSON keys, or any structured format without escaping.

The Caps naming pattern (`{base}_cap_{HHmmssfff}_{seq}.{ext}`) uses the original *file name*, which comes from the file system, not from image metadata — this is acceptable.

### 16d) Shell execution safety

The spec requires `Process.Start` for "Show in Explorer," "Properties," and "Print." In all cases:
- The only argument must be the file path that the user opened (from `args[]` or folder enumeration), never a value extracted from image content.
- Always use the fully-qualified, canonicalized path (`Path.GetFullPath`).
- Never construct shell command strings via concatenation with unsanitized input.

### 16e) Dependency hygiene

- Pin all NuGet package versions in the `.csproj` files (no floating `*` versions).
- Document the exact versions of ImageSharp and SkiaSharp in the README.
- When updating dependencies, check the relevant CVE databases (NVD, GitHub Advisory) for known vulnerabilities in the image parsing paths.

### 16f) Settings file safety

The JSON settings file at `%LocalAppData%\ImageZeus\settings.json` is deserialized at startup. Use `System.Text.Json` with its default secure settings (no `TypeNameHandling`, no polymorphic deserialization). Malformed JSON must not crash the app — deserialize inside a `try/catch` and fall back to defaults.

---

## 17) Non-Goals (v1)

1. No crop feature.
2. No Linux/Mac release.
3. No advanced color management pipeline.
4. No plugin system.
5. No sandboxed decode process (out-of-process decode would add complexity disproportionate to v1 scope; the limits in §16a–16b are the pragmatic mitigation).

---

## 18) Acceptance Checklist

All of these must pass before delivery:

1. Arrow keys and mouse side buttons navigate correctly.
2. Cursor-anchored zoom is stable (pixel under cursor stays fixed — no jump).
3. Fullscreen toggles on current monitor.
4. Mini panel hover fade (~300ms) and lock behavior work.
5. Scrub slider matches current sorted order and navigates when dragged.
6. Context menu includes all required items and actions work.
7. Context menu "Zoom: Fix" stays in sync with the X key toggle.
8. Sort field, sort direction, and side panel state persist across runs.
9. Zoom Fix does not persist across runs.
10. Caps border style is exactly 1px `#00FF00`.
11. Caps ratio/fixed-size mutual exclusion works.
12. Caps capture reflects rendered on-screen pixels (not original-resolution crop).
13. Caps auto-resize largest-dimension logic works (both up and down).
14. Caps save destination fallback and naming pattern `{base}_cap_{HHmmssfff}_{seq}.{ext}` work.
15. Animated controls (play/pause, frame slider) appear only for animated files.
16. GIF/WebP playback + frame seek work smoothly at native frame rate.
17. Delete sends to Recycle Bin with no warning.
18. Q exits immediately.
19. App starts and shows first paint within 1 second for a typical JPEG (cold start; excludes .NET JIT first-ever launch).
20. Navigating rapidly (holding arrow key) does not crash.
21. GIF playback does not cause growing memory usage or frame drops.
22. `%LocalAppData%\ImageZeus\crash.log` is written on any unhandled exception.
23. No `async void` or `_ = SomeAsync()` call site exists without a `try/catch` around the full body.

---

## 19) Delivery Requirements

1. Runnable solution with build/run instructions in README.
2. Short architecture note explaining the project layering and where Windows-specific code lives.
3. Feature matrix marking each spec item as implemented, deferred, or partial (with explanation for partial).
4. Unit tests for:
   - Zoom anchor math (cursor stays fixed after scale change)
   - Fit-scale calculation (never upscales past 1.0)
   - Sort/order (name, date, size, type, rating — ascending and descending)
   - Caps constraint math (mutual exclusion, fixed size override, largest-dimension resize both up and down)
   - Persistence roundtrip (save + load + verify all fields)
   - Decode pipeline cancellation (cancelled token prevents result from being cached/returned)
5. If any requirement is partially implemented, explicitly list the gap and the reason.

---

## 20) Known Pitfalls (from prior failed implementation)

The following mistakes were made in a prior attempt. Read this section before writing any code.

1. **PNG round-trip for display:** Converting `SKBitmap` → PNG → `MemoryStream` → `Avalonia.Bitmap` for every `Image.Source` assignment. This made the app visibly slow and caused GIF playback to stutter. Use `WriteableBitmap.Lock()` + direct pixel copy instead.

2. **Per-pixel SetPixel loop:** Converting ImageSharp pixels to Skia via `bmp.SetPixel(x, y, ...)` in a nested loop. Orders of magnitude slower than bulk memory copy. Use `Marshal.Copy` or unsafe span operations on the pixel buffers.

3. **Cache use-after-dispose:** The LRU cache's `Put` method disposed old bitmaps without checking if callers still held references. Combined with concurrent preload + navigation, this caused native crashes (SIGSEGV / access violations) when the UI tried to draw a disposed `SKBitmap`.

4. **No decode deduplication:** Multiple `LoadAsync` calls for the same cache key each started independent decodes. The second to finish would `Put` and dispose the first's result — even if the first had already been returned to the UI.

5. **Stale load overwrites newer load:** After `await` in `LoadCurrentAsync`, the code did not verify whether the user had navigated away. A slow decode could overwrite a fast one, showing the wrong image.

6. **Unobserved task exceptions:** `_ = LoadCurrentAsync(ct)` with only `catch (OperationCanceledException)` — any other exception became unobserved and could terminate the process.

7. **XAML initialization race:** Setting `ComboBox.SelectedIndex` in `LoadCapsUiFromSettings()` fired `SelectionChanged` before all named controls existed, causing a `NullReferenceException` during `InitializeComponent`. Guard all event handlers that persist state with an `_uiReady` flag set at the end of the constructor.

8. **Synchronous settings writes:** `File.WriteAllText` called from UI event handlers on every checkbox toggle.

9. **Bitmap leak during playback:** Old `Bitmap` instances assigned to `Image.Source` were never disposed, causing memory growth proportional to animation length × navigation count.

10. **1,070-line code-behind:** All state, IO, threading, platform calls, and view logic in a single `MainWindow.axaml.cs`. Prefer a ViewModel (or at minimum, extract state and commands into a separate class) so the code-behind only handles Avalonia-specific wiring.
