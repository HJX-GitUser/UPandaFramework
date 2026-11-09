# Changelog

All notable changes to this package are documented here. The format follows Keep a Changelog and the project uses Semantic Versioning.

## [0.7.1] - 2026-11-10

### Fixed

- URP: the outline rendered nothing. URP's `DrawRenderers` executes a single matching pass per renderer, so the two-pass material only ran the invisible stencil pass. URP now builds two proxies per target and draws two single-pass materials (`OutlineMask` at queue 3100 and `OutlineFill` at queue 3101); Built-in keeps the original two-pass `Outline.shader`. Verified in URP 14 (Unity 2022.3) and Built-in (Unity 2021.3) players, plus EditMode 53/53 and PlayMode 324/324.

## [0.7.0] - 2026-11-09

### Added

- `OutlineWidthUnit` (`屏幕相对` / `像素`) backed by the shader property `_WidthMode`; pixel mode uses a fixed device-pixel thickness, while the default keeps the screen-relative thickness that compensates for view depth and orthographic height.
- A URP branch inside `Outline.shader` (`"RenderPipeline" = "UniversalPipeline"`, drawn by URP as `SRPDefaultUnlit`), so Built-in and URP share one shader and one material asset. Shared pass code moved to `Shaders/OutlineShared.cginc`.
- An opt-in HDRP template at `Shaders~/HDRP/OutlineHDRP.shader`. Unity skips folders ending in `~`, so Built-in/URP projects are unaffected; HDRP projects copy it into `Shaders/` and the runtime picks it up via `Shader.Find("UPandaGF/OutLine/Outline (HDRP)")`, failing with a single error log if absent.
- Proxy renderers are pooled (`OutlineProxyPool`, 64 per kind): toggling highlights reuses proxy objects instead of creating and destroying GameObjects.
- `OutLine` rebuilds incrementally: bindings whose source `Renderer` and mesh are unchanged are reused, so adding or removing a child no longer recreates every proxy. A `revealProxyInHierarchy` inspector switch shows proxies in the Hierarchy for debugging, and non-readable or very large meshes now log a one-time warning with a suggested fix.

### Changed

- Excel tool now ships **ExcelDataReader 3.6.0** + **ExcelDataReader.DataSet 3.6.0** (`netstandard2.0`, MIT) instead of ExcelDataReader 2.1.2 plus SharpZipLib 0.86 (GPLv2 + linking exception). The API moved to the `ExcelDataReader` namespace (`AsDataSet()` now comes from the DataSet package), the new version reads `.xlsx` through `System.IO.Compression`, and the package no longer contains a GPL component, so the SharpZipLib source-offer obligation is gone.
- `FileSaveService.Load` only falls back to `<slot>.save.bak` when the primary file is actually corrupt (`InvalidDataException`); file-in-use and permission errors now propagate instead of possibly overwriting a newer primary with an older backup. The primary file is read once and the bytes are reused for deserialization and backup comparison.
- `FileSaveService.Delete` also removes the `*.legacy.bak` / `*.legacy.<guid>.bak` copies that `MigrateFile` can leave behind.
- `OutlineMeshCache` keys entries by mesh instance ID and sweeps stale entries when a scene unloads, so destroyed source meshes no longer accumulate in the static cache.

### Changed (breaking)

- Samples are now optional: `Samples/` moved to `Samples~` and split into five importable samples (`Startup`, `Pooling`, `Coroutines`, `StateMachine`, `Tasks`) declared in `package.json`. The shared `UPandaGF.Samples` assembly is gone; each sample has its own asmdef. The `创建UPGameRoot` menu no longer adds `GameLaunchExample` to the scene, and the object-pool / interactive-scoring demo builders moved into the samples (their generated scenes go to `Assets/UPandaFrameworkDemos/`, not the read-only package folder). The demo scripts that stay in module folders (`TaskSystem/Demo`, `InteractiveScoringSystem/Example`, `Study`, `Save/*/Test`) now use their own `UPandaGF.Samples.*` assemblies instead of `.asmref` files.
- `OutLine` (`Runtime/Game/OutLine`) is now an original implementation. Outline rendering no longer appends two materials to the target `Renderer`: the component creates a hidden proxy `Renderer` under each target and draws a single two-pass shader (`UPandaGF/OutLine/Outline`) that writes stencil first and then expands along the normal. Per-object color and width go through a `MaterialPropertyBlock` over five shared mode materials, so target meshes, material arrays and material instances are left untouched.
- Smooth normals are area-weighted (triangle-area weighted face normals welded by quantized position) and written to the proxy mesh `NORMAL` channel instead of `UV3`, so models no longer need a free UV3. Readable meshes are merged into one submesh; non-readable meshes reuse the source geometry with one outline material per submesh. Proxy meshes are shared between components and destroyed with the last user.
- Skinned proxies share the source bones and mirror blend-shape weights each frame; proxies no longer cast shadows, receive light probes or generate motion vectors.
- Replaced `OutlineMask.shader` / `OutlineFill.shader` and both materials with `Outline.shader` and `Resources/Materials/Outline.mat`. The editor repair tool now repairs that single template material, and the demo generator writes scenes under `Runtime/Game/OutLine/Demo` via `UPandaGF → Runtime → OutLine`.
- `precomputeOutline` keeps its inspector name, but the baked payload fields were renamed to `bakedMeshes` / `bakedNormals`; existing precompute data must be rebuilt with the **重新计算描边法线** context menu.

### Removed

- The shared `Samples/UPandaGF.Samples.asmdef` and the five `.asmref` files that pulled module demo scripts into it.
- The Quick Outline attribution section in `THIRD_PARTY_NOTICES.md`; the outline module is no longer derived from it.
- `Libraries/Excel.dll` and `Libraries/ICSharpCode.SharpZipLib.dll` (replaced by `Libraries/ExcelDataReader.dll`), and the GPL/SharpZipLib license texts that only applied to them.
- `Runtime/Shader/UnityShader学习文档/案例/1.纹理动画效果/Sword.rar`: an unlicensed teaching asset removed from the release package.

### Fixed

- WebGL player builds failed to compile `UPandaGF.Diagnostics`: `PLogHelper` 的浏览器持久化分支引用了 `UPandaGF.Assets` 里的 `PlatformStorage`，而 Diagnostics 与 Assets/Core 之间存在引用环。`PlatformStorage` 已移到 `UPandaGF.Contracts`（命名空间不变），Diagnostics/Assets/Save/Replay 均可直接引用，WebGL 构建恢复。
- Proxy pooling no longer tries to reparent a proxy while its target is being deactivated or destroyed (`Cannot set the parent of the GameObject ... while activating or deactivating the parent GameObject ...`). In that case the proxy is destroyed instead of being pooled, and a bind that Unity rejects is retried on the next frame.

## [0.6.0] - 2026-11-08

### Changed (breaking)

- Replaced the legacy runtime log panel with an original implementation: `UPandaLogConsole` (`Runtime/Composition/LogConsole/`). New code covers thread-safe bounded capture, main-thread store, severity/search/collapse filters, copy/export and stack inspection, with optional project-owned icons/skin and a text-only fallback.
- Added three pages (logs / display settings / framework diagnostics), safe-area-responsive layout, persisted column/toolbar/scale/opacity settings, and live `FrameworkDiagnostics` resource/module/startup data. Added a Chinese `UPandaLogConsoleEditor` Inspector with capacity/behavior/icon help and runtime status/actions. Log rows no longer use white alternating textures; the panel uses a dark surface with gray-white text, the header close button shows one X icon, and scrollbars are 14px draggable bars without arrow icons.
- 旧日志面板字段 → `UPGameRoot.logConsole` (`UPandaLogConsole`); `DebugerInit` uses `Show()` / `IsVisible`; `UPGameRootEditor` creates/destroys a `LogConsole` child instead of assigning image assets.
- Removed the old log-panel runtime files, editor menu/hook and regression test file; added `LogConsoleRegressionTests` and `LogConsoleIconTests`. Project-owned icon assets were migrated to `Runtime/Composition/LogConsole/Icons` with GUIDs preserved.
- Log console copy/export polish: the toolbar copy action copies only the selected entry (message + stack); display settings shows the export directory and last exported file path, with copy-path and export actions.\n- Excel binary tables: Android/WebGL now install `StreamingAssets/Data/BinaryData/*.binary` into `persistentDataPath` using the generated `BinaryDataIndex.txt` and UnityWebRequest; `UPGameRoot` awaits `BinaryDataMgrInit.InitAsync`, and both the framework build pipeline and the standard Android/WebGL preprocess build hook rebuild the index.
- Log console URL/UPM import fix: editor sync no longer depends on `OPEN_PLOG`, no longer writes script execution order into the read-only package, and `DebugerInit` auto-creates a text-mode console when `EnableDebugModel` is on but the scene has no serialized console.
- Added `AssetLoaddingMethod.NoAssetBundle`: startup skips AssetBundle module initialization, using Resources/packaged resources without a manifest, main bundle or hot update; scenes use Build Settings. Added `NoAssetBundleModeTests`.
- `UPandaGF.Diagnostics` now uses `autoReferenced: true` so the log/diagnostics assembly is referenced by default; `FrameworkConventionValidator` checks the setting.

### Fixed

- Excel binary table startup no longer treats `BinaryDataMgrInit.savePath` values beginning with `/` as absolute filesystem paths. On Android/iOS the leading-slash default (`/Data/BinaryData/`) pointed at the read-only system root and aborted startup with `IOException: Read-only file system`; leading separators are now resolved under `Application.persistentDataPath`, while Windows drive/UNC paths stay absolute. The resolved directory is also kept separate from the serialized `savePath`, so repeated `Init`/`InitAsync` calls no longer re-prefix the path.

### Removed

- The third-party log-viewer attribution section from `THIRD_PARTY_NOTICES.md`; the derived implementation it referenced has been deleted. The log-console icons are project-owned assets and were moved to `Runtime/Composition/LogConsole/Icons`, not third-party material.

### Documentation

- Added `Documentation~/MIGRATION-0.6.md`, `Runtime/Composition/LogConsole/README.md`; updated Diagnostics/Editor/Tests/root docs.

## [0.5.0] - 2026-11-08

### Added

- Assembly split: `UPandaGF.Diagnostics`, `UPandaGF.Core`, `UPandaGF.Assets`, `UPandaGF.UI`, `UPandaGF.Audio`, `UPandaGF.Save`; `UPandaGF.Runtime` is now the composition root (`UPGameRoot` / `GameServices` / 日志控制台).
- Optional gameplay assemblies: `UPandaGF.Gameplay`, `UPandaGF.Gameplay.Tasks`, `UPandaGF.Gameplay.InteractiveScoring`, `UPandaGF.Gameplay.Replay`（`autoReferenced = false`）。`UPandaGF.Samples` 也不再自动引用，不进入 Player。
- `Documentation~/MIGRATION-0.5.md`: assembly / namespace / path migration table and upgrade steps.

### Changed (breaking)

- All C# files now declare a namespace. Global-namespace runtime types move to `UPandaGF`; editor tools to `UPandaGF.GFEditor`; samples to `UPandaGF.Samples`; PlayMode tests to `UPandaGF.Tests`.
- `UPandaGF.RunTime.*` → `UPandaGF.Gameplay.*`; `UPandaGF.StateMechine` → `UPandaGF.Gameplay.StateMachine`; `ReplaySystem` → `UPandaGF.Gameplay.Replay`; `AssetBundleBrowser` → `UPandaGF.AssetBundleBrowser`; `UPandaGF.Extensions` assembly → `UPandaGF.Gameplay`.
- Directory moves: `Runtime/Manager/*` split into `Runtime/Core|Assets|UI|Audio|Save|Diagnostics|Composition`; `BineryMgr` renamed to `BinaryMgr`; serialization codecs moved to `Runtime/Assets/Serialization`.
- Module boundaries: diagnostic accessors (`SceneService.DiagnosticScopeCount` / `DiagnosticPendingRequestCount` / `UIManager.Diagnostic*` / `AssetsLoader.BundleLoader`), `FileUpdateTransaction` and `HotUpdateFileGate` are public so the composition root can use them across assemblies. `AssetLoaddingMethod` moved to Contracts; diagnostic data structs moved to Core.
- `ResourcesLoader.InvokeCallbacks` is public (used by Audio/UI).
- `FrameworkConventionValidator` scans the whole `Runtime` tree again after the composition-root asmdef moved to `Runtime/Composition`.

### Fixed

- PlayMode regression tests no longer assume `FileUpdateTransaction` / `HotUpdateFileGate` live in the `UPGameRoot` assembly.
- `Tools/ci/invoke-unity.ps1` quotes Unity command-line arguments containing spaces, so projects such as `Framework Validation` can be compiled/tested in batchmode.

### Documentation

- Updated root README assembly table/module links, module READMEs and relative links after the directory moves.
- Added `OptimzeLog/P1Structure-2026-11-08/` with compile and test evidence.

## [0.4.0] - 2026-11-08

### Added

- Excel binary **v2** format: `UPXF` magic, version, table/key/column schema, row data and CRC32 tail. `ExcelBinaryCodec` is shared by the generator (`ExcelTableBuilder`) and the runtime reader, validates magic/schema/row count/CRC, and rejects truncated or corrupted files with `InvalidDataException`.
- `BinaryDataMgr.LoadTable<T, K>(byte[] bytes)` for in-memory tables (tests/custom installation). v2 columns map to data-class fields **by name**, so Excel column order can change without breaking reads; legacy v1 tables remain readable.
- ExcelTool "校验二进制表（v2 自检）" action and generation self-check (encode → decode before writing).
- `ABLoadMgr.ReleaseLegacyBundles()` / `IsLegacyPinned()`; legacy `GetAssetBundle` / `LoadResAsync` pins now use the lease model (`AssetBundleDiagnostics.LegacyPins`).
- `EditorDestructiveGuard.ConfirmOverwriteDirectory` / `ConfirmUpload`, used by AB upload, StreamingAssets copy, build-tab rebuild and HTTP upload.
- CI tooling: `Tools/ci/` PowerShell + bash scripts (`sync-package`, `invoke-unity`, `run-tests`, `check-test-results`, `build-player`) and `.github/workflows/ci.yml` (EditMode/PlayMode matrix + Windows/Android/WebGL builds, XML gating, artifacts).
- `FrameworkBuildPipeline.BuildFromCommandLine` (exit codes 0/1/2, `-buildTarget`/`-buildPath` or `UPANDA_BUILD_TARGET`/`UPANDA_BUILD_OUTPUT`) and an explicit error for non-ASCII Android project paths.

### Changed

- AB native loads now await `AsyncOperation.completed` instead of polling with `Task.Yield`; progress events are advanced by a single `ABLoadMgr.Update` pump while loads are in flight.
- Legacy AB callers are pinned through the same lease counter as `AcquireBundleAsync`; `UnLoadAB` refuses while a legacy pin exists and `ReleaseLegacyBundles()` releases them. `legacyBundles` (the "never unload" side set) was removed.
- FTP upload is now asynchronous and ordered: all bundles upload first, and the manifest (`assetData.assetref`) is submitted only when every bundle succeeded. `UpLoadAllABFile` / `UpLoadFile` are obsolete wrappers.
- FTP password is no longer written to `EditorPrefs` (it lives in session memory or `UPANDA_FTP_PASSWORD`); the legacy plaintext key is deleted after a one-time migration. The UI uses a password field.
- ExcelTool generation asks for confirmation, refuses to write generated classes outside `Assets`, and uses `ExcelTableBuilder` (DataTable → typed rows → codec).
- `AssetBundleBuildTab` rebuild deletion and StreamingAssets copy use `EditorDestructiveGuard`; cubemap overwrite and Nginx upload ask for confirmation.
- `AssetBundleDiagnostics` gained `LegacyPins` (optional constructor parameter).

### Fixed

- FTP upload streams are disposed on failures (previously leaked when `GetRequestStream`/copy threw).
- Generation-side Excel pipeline is now covered by EditMode tests without a real `.xlsx`, including Chinese text, empty cells, bool text and negative floats.

### Documentation

- Updated StorageDataMgr (Excel v2), AssetBundleMgr (lease model/progress), Editor AssetBundleTools (ordered upload/credentials) and root README (CI/Android ASCII path).
- Added `Tools/ci/README.md` and `OptimzeLog/P1-2026-11-08/` verification evidence.

## [0.3.0] - 2026-11-08

### Added

- `IHttpService` / `HttpResult<T>` / `HttpRequestOptions`: cancellable Task-based HTTP contract with global headers, concurrency limit, retry/timeout/header options. `HttpManager` implements it (callback/pack API retained).
- `IPoolService` / `IPool`: pool factory for a root-lifetime `Shared` pool and scope-owned pools; `GameObjectPoolMgr` implements `IPool`.
- `ILogService` / `LogLevel` and `LogService`: injectable logging that is **not** stripped by `OPEN_PLOG`, while still honouring the runtime `LogConfig` switches. `PLogger.Write/WriteException` are the non-conditional bridge.
- `IUIService` / `IUIServiceConsumer`: UI contract for scene components (`ShowPanel*`, `ClosePanel`, `GetPanel`, canvas/camera registration).
- `services.Inject(GameObject, IUIService)`: injection entry for runtime-instantiated objects (scene scanning cannot see them).
- `ISaveService.Delete(slot)`.

### Changed (breaking)

- Removed static singleton entry points: `UIManager.Instance`, `AudioMgr.Instance`/`Release`, `QuestManager.Instance`/`InstanceOrNull`/`AutoCreateInstance`, `HttpManager.Instance`, `GameObjectPoolMgr.Instance`. Instances are owned by `GameServices` / `UPGameRoot` / the scene.
- `AudioMgr` takes dependencies in its constructor (`IAssetService`, optional `PublicMono`, optional `ILogService`, optional driver provider) instead of `Configure` + global lookups.
- `UIManager` is created by `UPGameRoot` and configured with `Configure(IAssetService, ILogService)`. `BasePanel.uiManager` is injected after instantiation (available from `OnOpen` onward). `SetResourcesLoader` is an obsolete migration bridge.
- UI, audio and pool resource loading now have a single path through `IAssetService`; the `ResourcesLoader.Instance` / `UPGameRoot.Instance.GetAssetsLoader()` fallbacks were removed and now fail with an explicit message when the service is missing.
- `GameServicesExtensions.CreatePool` returns `IPool` and delegates to `services.Pools.CreatePool(scope)`.
- `QuestManager` is scene-owned and injected through `IGameServicesConsumer`; `RewardReceiver` must be assigned explicitly (the `PlayerRewardService.Instance` fallback was removed). Saves move to `ISaveService` slots (`SaveToSlot` / `LoadFromSlot` / `HasSave` / `DeleteSave`, default slot `quests`); the old `quests.json` file API was removed.
- `QuestHudTracker` / `QuestLogPanel` / `QuestDemoDriver` receive the `QuestManager` through serialized references (the installer wires them).
- Shutdown order is now `Scenes → Flow → Pools → Http → Timer → AppLifecycle → Audio → Input → Time`; the container-created HTTP host is destroyed on shutdown.

### Fixed

- HTTP Task cancellation no longer leaks the token registration until the scope ends; cancellation reaches the underlying pack and the returned task ends in a cancelled state.
- `UIManager` no longer has two resource-loading paths with different ownership semantics.

### Documentation

- Updated Contracts/Services/UI/Audio/ObjectPool/HTTP/DebugSystem/TaskSystem READMEs and the root README for injection-only usage.
- Added `OptimzeLog/P1Services-2026-11-08/` with the verification evidence (compile, EditMode, PlayMode).

## [0.2.0] - 2026-11-07

### Added

- `package.json` for UPM Git URL installation and dependency declaration (`com.unity.textmeshpro`).
- `EditorDestructiveGuard` with confirmation, project-root/Assets-root rejection and source/target overlap checks.
- Build-time AssetBundle output validation (`FrameworkBuildPipeline.ValidateAssetBundleOutput`).
- `ExcelBinaryFormat` shared constants shared by the Excel generator and runtime reader.
- Desktop/console installation of Excel tables from StreamingAssets to the writable data directory.

### Changed

- Unity 2021.3 LTS compatibility: singleton lookup uses `FindAnyObjectByType` on 2022.2+ and `FindObjectOfType` on older editors; PlayMode test cleanup uses a shared compatibility helper.
- `DebugerInit` caches `EnableDebugModel` once instead of reading the defensive `UPGameRoot.Config` copy in `Update`/`OnGUI` (which performs a JSON round-trip per access). `Config` itself keeps its documented defensive-copy semantics.
- Excel `bool` columns are written and read as 4 bytes; row count uses `BEGIN_INDEX`; output files are truncated before writing; empty cells and `TRUE/FALSE/1/0` are handled with explicit diagnostics.
- Default AssetBundle target is `StandaloneWindows64`; `LoadAssetPath` defaults were aligned to `AssetBundles/StandaloneWindows64/`.
- `PlayerPrefsDataMgr` supports null fields, arrays and enums, and only serializes public instance fields.
- HTTP retries are limited to retryable failures; POST/PUT require `RetryNonIdempotent = true`.
- `StateMachineManager` rebuilds the active path only when the active leaf changes.
- `UIManager.Shutdown` calls `OnClose` for open panels before destroying them.
- Object-pool instances reset local position/rotation/scale when reused.
- The uGUI log panel hides only its panel content; `F12`/error auto-open no longer disable the manager itself.

### Fixed

- Excel generation/read layout mismatch for `bool` columns and the StreamingAssets/persistentDataPath directory mismatch.
- DLL copy tool cleared the target directory before enumerating the source; it now validates first and never deletes when the source is empty.
- UI script generator no longer writes outside `Assets`, confirms overwrite, and handles object names that become empty after sanitizing.
- `Clean Missing Scripts` now asks for confirmation and registers an Undo step.
- EditMode tests no longer use unsupported `async Task` test methods; PlayMode log expectations no longer depend on the `OPEN_PLOG` define (Error logs are always emitted).

### Documentation

- Added `OPTIMIZATION_PLAN.md` with phased commercial-readiness plan and acceptance criteria.
- Updated `README.md` integration/limitations notes for the changed behavior.

## [0.1.0] - 2026-11-07

- Documentation truth pass and third-party notices (baseline `c6af6bd`).
- Existing framework features: startup lifecycle, services, asset leases, content update, UI/audio/save/logging, gameplay modules and editor tools.
