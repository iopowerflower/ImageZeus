# Feature Matrix

Status legend:
- `Implemented`: available in current code.
- `Partial`: implemented with limitations.
- `Deferred`: not yet implemented.

| Spec Area | Status | Notes |
|---|---|---|
| Layered solution structure (`App/Core/Imaging/Platform.Windows/Persistence/Tests`) | Implemented | Projects created and wired by references. |
| CLI image open + folder indexing | Implemented | First CLI arg loads and folder list is indexed/sorted. |
| Supported still image formats (jpg/png/bmp/tiff/webp/gif) | Implemented | SkiaSharp decoder supports all required formats including WebP. |
| Animated GIF/WebP playback + controls | Partial | Decode path enumerates frames; playback timer/seek UI not wired yet. |
| Keyboard navigation (Left/Right), fullscreen (`F`), exit (`Q`), zoom reset (`Z`), zoom fix (`X`) | Implemented | Wired in `MainWindow` key handler. |
| Ctrl+C copy rendered image to clipboard | Implemented | Copies current rendered bitmap as PNG to clipboard. |
| Mouse wheel zoom anchored at cursor | Implemented | Correct cursor-anchored zoom using Canvas positioning. Pixel under cursor stays fixed. |
| Mouse side buttons nav (XButton1/XButton2) | Implemented | Wired in pointer pressed handler. |
| Mini panel top-right + lock | Implemented | Overlay with 300ms animated opacity transition and lock checkbox. |
| Right-click menu required ordering | Implemented | All spec items present in correct order. |
| Properties dialog | Implemented | Uses `ShellExecuteEx` with `properties` verb and `SEE_MASK_INVOKEIDLIST`. |
| Zoom Fix sync (menu + `X`) | Implemented | Shared VM state and menu-open sync. |
| Sorting rules + persistence | Implemented | Sort field/direction persisted and applied to nav order. |
| Windows `System.SimpleRating` read/write | Partial | In-process fallback; shell property store integration deferred. |
| Left side panel open/close + persistence | Implemented | Toggle + persisted state. |
| Rotate/flip/edit save model | Deferred | Buttons present; transform/save pipeline not yet implemented. |
| Caps capture tool | Implemented | Interactive selection rectangle with #00FF00 border, aspect ratio/fixed size constraints, post-resize, format selection, save to folder, copy to clipboard. |
| EXIF overlay | Partial | Bottom overlay with file info; full EXIF tag extraction deferred. |
| Save / Save As / JPEG quality preservation | Deferred | Not yet wired. |
| Decode dedupe + cancellation + stale generation checks | Implemented | `ImageDecodePipeline` + `NavigationLoadCoordinator`. |
| Safe LRU cache disposal with leases | Implemented | Lease/ref-counted disposal prevents in-use release. |
| Direct pixel blit to display | Implemented | `PixelFrameAvaloniaBlitter` writes directly into `WriteableBitmap.Lock()`. |
| Delete to Recycle Bin | Implemented | Uses `SHFileOperation` with `FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT`. |
| Global exception logging to `%LocalAppData%\ImageZeus\crash.log` | Implemented | AppDomain + unobserved task + UI thread handlers log via `FileCrashLogger`. |
| No fire-and-forget without catch | Partial | Guarded wrappers in key async paths; further audit recommended. |
| Rendering preference via `IMAGEZEUS_GPU` | Implemented | Render ordering selection at startup. |
| Debounced async settings writes | Implemented | `DebouncedSettingsWriter` (500ms default). |
| Decode safety limits (size/dimension/pixel/frame) | Implemented | Enforced before/while decode in SkiaSharp decoder. |
| Dark theme / text visibility | Implemented | Forced `Dark` theme variant with explicit foreground colors on all UI elements. |
| Dependency pinning | Implemented | All package versions pinned in csproj. |
| Unit test coverage requested by spec | Implemented | Zoom anchor, fit scale, sort, caps, persistence, decode cancellation tests all pass. |
