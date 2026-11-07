# Changelog

All notable changes to this package are documented here. The format follows Keep a Changelog and the project uses Semantic Versioning.

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
