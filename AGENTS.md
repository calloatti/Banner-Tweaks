Include ..\AGENTS.md

# Banner Tweaks (Decal Tweaks) — Mod Overview

## Purpose
Replaces the tiny "Browse" button dialog for banner/decal buildings with a large, scrollable, paginated grid panel that shows decal textures visually. Also enables recursive subfolder loading for custom decal images.

## Key Classes & Responsibilities
| Class | File | Role |
|---|---|---|
| `ModStarter` | `DecalTweaksModStarter.cs` | Entry point — creates Harmony instance and calls `PatchAll()` |
| `DecalTweaksConfigurator` | `DecalTweaksConfigurator.cs` | Bindito DI — registers `DecalGridPanel` as singleton in Game scope |
| `DecalGridPanel` | `DecalGridPanel.cs` | Core UI — programmatic `VisualElement` grid with pagination, subfolder nav, decal caching, selection highlighting |
| `DecalGridPatches` | `DecalGridPatches.cs` | All 5 Harmony patches (browse button override, double-click open, selection tracking, recursive subfolder texture loading) |

## How It Hooks Into the Game
- **Mod loading:** Native `IModStarter` (no BepInEx)
- **Harmony:** 5 patches across `DecalSupplierFragment`, `EntitySelectionService`, `UserDecalTextureRepository`
- **DI:** Bindito `[Context("Game")]` configurator
- **Event Bus:** Listens for `DecalsReloadedEvent` to refresh grid
- **Panel Stack:** Implements `IPanelController`, pushes/pops from `PanelStack`
- **Field access:** `UserDecalTextureRepository._loadedTextures` accessed **directly** (`__instance._loadedTextures` — DecalSystem is publicized via `CommonModSettings.props`; was `AccessTools.Field` reflection before publicization). `DecalSupplier.ActiveDecalChanged` is excluded via `DoNotPublicize` in the csproj.

## Architecture
- All UI built programmatically (no UXML/USS)
- Source in `Version-1.0/Source/`
- Targets `netstandard2.1`, C# 10, nullable disabled
- Uses `Krafs.Publicizer` (~60 assemblies via `CommonModSettings.props`, including `Timberborn.DecalSystem`)
- Harmony DLL referenced from Steam Workshop (ID `3284904751`)

## Notable Implementation Details
- Double-click detection via static fields (0.5s threshold). Game has NO built-in double-click detection for entity selection — `EntitySelectionService` does not distinguish single/double clicks; detection is entirely custom in the Harmony prefix.
- `EntitySelectionService.Select` → `SelectSelectable`: if the target is already `SelectedObject`, it does nothing (identity guard). Otherwise it calls `Unselect()` on the old selection first, then selects the new one. Only `Select` (not `SelectSelectable`) is patched.
- Subfolder navigation via decal ID string parsing (backslash-delimited paths) — **will be replaced by explicit group tree**
- Grid layout: 10 columns, 96px cells, 6px gaps, 47 content slots + "Original" + "Current" slots
- `_lastFoldersByCategory` persists subfolder state per category
- Debug mode (`_debugMode = false`) with visual border utilities inactive in production
- Single localization entry (`Calloatti.BannerTweaks.PanelTitle` = "Banners")
- ModdableDecalGroups support: no DLL dependency — we independently parse `DecalGroupSpec` from `*.blueprint.json` files across all enabled mods (both local and Steam Workshop) via `ModRepository.EnabledMods` (Timberborn.Modding API).
- Group model: two sources merged into one tree — folder paths (from filesystem scan at decal load time) + `DecalGroupSpec` entries (from blueprint JSONs). A spec group with a matching path overrides the folder-derived group's metadata.

## Unified Group Model (Replaces Folder-from-ID-Parsing)

### The problem
The current approach bakes the relative file path into the decal ID (`"Animals\Beaver\decal.png"`) and later parses `\` back out to reconstruct folders in `GetSubfolders()`. This conflates identity with location.

### The solution — three data layers

```csharp
// 1. Raw folder membership — populated at scan time in LoadCustomTexturesPostfix
// category → folderPath → [decalId, ...]
// e.g. "Banners" → { "Animals\Beaver": [ "Animals\Beaver\decal1.png" ], "": [ "root.png" ] }
Dictionary<string, Dictionary<string, List<string>>> _folderContents;

// 2. Spec groups — parsed from *.blueprint.json files across all enabled mods
// category → [DecalGroupSpec, ...]
Dictionary<string, List<DecalGroupSpec>> _specGroups;

// 3. Merged tree — built from (1) + (2), consumed by UI
// category → root GroupNodes
Dictionary<string, List<GroupNode>> _groupTrees;
```

- Decal IDs **remain** full relative paths for uniqueness (`"Animals\Beaver\decal.png"`)
- Folder metadata comes from the actual `Directory.GetFiles` result at scan time, not by parsing the ID later
- `_groupTrees` is rebuilt on `DecalsReloadedEvent` — no need to re-scan blueprint files at runtime

### GroupNode structure
```csharp
class GroupNode {
    string Id;                    // e.g. "Animals\Beaver" or "BannerGood"
    string DisplayName;           // last path segment or localized spec title
    int Order;
    List<GroupNode> Children;     // sub-groups
    List<Decal> Decals;           // decals in this group
    bool IsSpecGroup;             // false = folder-derived, true = from JSON
}
```

### Group merging rules
- Folder-derived groups: hierarchical (path segments → depth)
- Spec groups: flat, merged at root level; matching path overrides folder group
- Root files (empty folder) → implicit "All" group
- Dedup at scan time: first file with a given name wins (root takes priority)

## Lessons Learned
- **Do not use a manual `IsShowing` flag.** `PanelStack` exposes `IsPanelOnTop(IPanelController)` which directly queries whether a panel is on top of the stack. Use it as a computed property: `internal bool IsShowing => _panelStack.IsPanelOnTop(this);` instead of a stored `{ get; private set; }` flag. This eliminates the risk of re-entrancy during `_panelStack.Push()` (where the flag was historically set too late) and avoids manual `IsShowing = true/false` management in `Show()`/`Close()`.
- **`_panelStack.Push` calls `GetPanel()` synchronously**, which triggers `Populate()` and builds the full visual tree. Any work before `IsShowing` was set (in the old approach) was vulnerable to re-entrant `Show()` calls. With the computed property approach, the guard is always accurate because it queries live stack state.
- **`EntitySelectionService.Select` has a postfix** that can close the panel when a different entity is selected. The `_openedPanel` static flag in `DecalGridPatches` coordinates between the double-click prefix and postfixes to prevent the panel from closing immediately after being opened.
- **The `OnUnselectPostfix` runs during `SelectSelectable` when switching entities** (because `SelectSelectable` calls `Unselect()` before selecting the new entity). This can clear the `_openedPanel` flag prematurely if not carefully ordered.
- **There is no double-click detection built into Timberborn's `EntitySelectionService`** or `CursorTool`. The mod's double-click behavior is entirely custom via a Harmony prefix on `Select()` comparing `_lastClickedEntity` and `_lastClickTime` with a 0.5s threshold.
- **`CursorTool.ProcessSelectObject()` checks `_inputService.MouseOverUI`** — if the mouse is over UI, the 3D raycast selection is skipped. This prevents click conflicts with the Browse button when the entity panel appears.
- **The `SelectableObjectSelectedEvent` fires on EVERY single-click selection** (via `EventBus.Post` in `SelectSelectable`), not just on double-click. `EntityPanelSystem` handles this event to show the entity fragment.
- **Double-click opening the panel twice = old mod still active.** If a renamed/duplicate mod is also installed, both instances register their Harmony patches and both open the panel. Check for stale mod directories.
- **Don't scan the filesystem directly for mod files.** Use `ModRepository.EnabledMods` (from `Timberborn.Modding`) to get all mods across local + Steam Workshop, resolved to the active version folder. Each `Mod.ModDirectory.Directory` is a `DirectoryInfo` for the active version (e.g. `version-1.0/`).
- **ModdableDecalGroups has no runtime API we can depend on without coupling.** Instead, independently parse `DecalGroupSpec` entries from `*.blueprint.json` files — the JSON format is stable and documented. No DLL reference needed.
- **The `UserDecalTextureRepository.LoadCustomTextures` is non-recursive.** It uses `Directory.GetFiles(text)` (single directory). The mod's `LoadCustomTexturesPostfix` adds recursive subdirectory scanning and stores the relative path as the decal name for uniqueness.

## Implementation Plan (Next Steps)

### Phase 1 — Foundation: Data Model
1. **Create `GroupNode.cs`** — tree node class with `Id`, `DisplayName`, `Order`, `Children`, `Decals`, `IsSpecGroup`
2. **Create `DecalGroupSpec.cs`** — flat spec model parsed from `*.blueprint.json` files (`Id`, `DisplayName`, `Order`, `Category`)
3. **Create `DecalGroupStore.cs`** — singleton holding:
   - `_folderContents` (category → folder → decalIds) — populated at scan time
   - `_specGroups` (category → specs) — parsed at startup from enabled mods' blueprints
   - `_groupTrees` (category → tree roots) — merged, rebuilt on `DecalsReloadedEvent`
   - Methods: `RebuildGroupTree(category)`, `GetGroupsForCategory(category)`, `GetDecalsInGroup(category, groupId)`

### Phase 2 — Integration: Hook into Existing Flow
4. **Modify `DecalTweaksConfigurator.cs`** — register `DecalGroupStore` as singleton in Game scope
5. **Modify `DecalGridPatches.cs` (`LoadCustomTexturesPostfix`)** — at scan time, populate `_folderContents[category][folder].Add(tex.name)` before setting `tex.name = relativePath`. Store the relative folder path explicitly instead of parsing it from the ID later.
6. **Modify `DecalGridPanel.cs`**:
   - Inject `DecalGroupStore`
   - Replace `_currentSubPath` (folder path string) with `_currentGroupNode` (GroupNode reference)
   - Replace `GetSubfolders()` with `GetChildGroups()` (returns child GroupNodes)
   - Root view shows children of root GroupNode for the category
   - Drill-down navigates into child GroupNodes
   - `..` button navigates up to parent GroupNode
   - Remove all ID-string-splitting folder logic

### Phase 3 — Polish
7. Remove old `_lastFoldersByCategory` dict and folder-from-ID-parsing code
8. Handle edge cases: empty categories, spec groups with no matching folder, root-level decals
9. Build, deploy, test in-game

### Phase 4 — Verification
10. Verify double-click open still works
11. Verify subfolder decals appear correctly via group tree
12. Verify `DecalsReloadedEvent` rebuilds group tree without rescanning blueprints

## Build
- Pre/post-build scripts in `..\..\tools\`
- Project file: `Version-1.0/Banner Tweaks.csproj`
- Mod v1.0.0, requires Harmony 2.4.1
- Game restart script: `C:\Users\calloatti\source\repos\Tools\timberborn_restart_and_continue.ps1`

## Banner Mesh Modifications (Borderless Full-Face Decals)

### Problem
SquareBanner display surface had a wooden frame border and paper texture border. Goal: full 1m×1m decal with no border, plain white background.

### Solution
Modified both faction timbermeshes (`SquareBanner.Folktails.Model.timbermesh`, `SquareBanner.IronTeeth.Model.timbermesh`):

1. **Display surface geometry** (`#display1x1_FT` / `#display1x1_IT`):
   - Position vertices scaled to ±0.5 (full 1×1 face in world space after node offset 0.5,0.5)
   - Z = -0.12 (in front of wooden frame lip at z≈0.1; frame hidden behind decal)
   - Center vertex at (0,0,-0.12)

2. **UV0 (paper base texture)**: Shrunk inward by 64px (0.0625 UV) on all sides within the 512×512 paper quadrant, excluding the paper texture's baked border.

3. **UV1 (decal texture)**: Left at full 0..1 coverage (half-texel inset 1/1024) for proper decal mapping.

### Tooling
Extended `Tools/timbermesh/patch_banner.py`:
- Parametrized node name (`#display1x1_FT` / `#display1x1_IT`)
- Parametrized Z offset (default -0.12)
- Parametrized UV0 shrink (default 0.0625 = 64px/1024)
- Generates `_patched.timbermesh` outputs

### Cleanup
Removed `Version-1.0/materials/` folder — contained failed paper texture overrides that affected all paper materials game-wide.

### Decal Rendering Fixes (Harmony Patches)

**File:** `Version-1.0/Source/DecalEdgeFadePatch.cs`

Postfix on `DecalSupplierBuildingIcon.UpdateIcon` (filtered to `TemplateName.Contains("SquareBanner")`):

1. `_DetailAlbedoUV3Gradient = 0` — removes edge fade on decal
2. `_DetailAlbedoUV3Color.w = 1` — full opacity (default alpha 0)
3. Decal texture wrap mode = `Clamp` — prevents vertical seam between adjacent banners in multi-tile setups

Affects: SquareBanner only (PoleBanner uses same class but different TemplateName, excluded by filter).

### Build & Deploy
- `dotnet build -c Release` → auto-deploys via pre/post-build scripts to `Documents\Timberborn\Mods\Banner Tweaks`
- Verified: both faction meshes deployed (2881/2886 bytes), materials folder gone, build clean.

### Result
- Full 1×1 borderless banner face
- No paper border, no wooden frame visible
- No decal edge fade
- No vertical seams between adjacent banners (Clamp wrap mode)
- PoleBanner unchanged (retains vanilla edge fade)

## BorderlessSquareBanner Building (New Building)

### Purpose
A new standalone building that provides the borderless banner without overriding the vanilla SquareBanner. Keeps vanilla SquareBanner intact for compatibility.

### Structure
```
Buildings/Decoration/BorderlessSquareBanner/
  BorderlessSquareBanner.Folktails.Model.timbermesh (2884)  ← full 1x1, original Z, UV0 shrunk 64px
  BorderlessSquareBanner.IronTeeth.Model.timbermesh (2889)
  BorderlessSquareBanner.Folktails.blueprint.json
  BorderlessSquareBanner.IronTeeth.blueprint.json
  BorderlessSquareBannerIcon.asset / .png
  (ConstructionStage0 uses vanilla SquareBanner models)
```

### Blueprint Changes
- **TemplateName**: `BorderlessSquareBanner.Folktails` / `BorderlessSquareBanner.IronTeeth`
- **Model path**: `Buildings/Decoration/BorderlessSquareBanner/BorderlessSquareBanner.{Faction}.Model`
- **Icon**: `Buildings/Decoration/BorderlessSquareBanner/BorderlessSquareBannerIcon`
- **ConstructionStage0**: Reuses vanilla `SquareBanner.{Faction}.ConstructionStage0.Model`
- **Loc keys**: `Building.BorderlessSquareBanner.DisplayName/Description/FlavorDescription`
- **IconRendererName**: `#display1x1_FT` / `#display1x1_IT` (matches custom timbermesh node)

### Template Collections
- `TemplateCollections/TemplateCollection.Buildings.Folktails.blueprint.json` → appends to `Buildings.Folktails`
- `TemplateCollections/TemplateCollection.Buildings.IronTeeth.blueprint.json` → appends to `Buildings.IronTeeth`

### Localization
All 15 locale files (`enUS` + 14 locales) updated with:
- `Building.BorderlessSquareBanner.DisplayName` — "Borderless Square Banner"
- `Building.BorderlessSquareBanner.Description` — "A square customizable banner without borders."
- `Building.BorderlessSquareBanner.FlavorDescription` — "Think of the bigger picture."

### Icon
Copied vanilla `SquareBannerIcon.asset` + `.png` → renamed `BorderlessSquareBannerIcon`

### Runtime Behavior
- Uses same `DecalSupplierBuildingIcon` component as SquareBanner
- Harmony patch `DecalEdgeFadePatch` applies (filtered by `TemplateName.Contains("SquareBanner")`) → edge fade removed, Clamp wrap mode
- ConstructionStage0 uses vanilla models (no borderless variant needed for unfinished state)

### Build & Deploy
- `dotnet build -c Release` → auto-deploys to `Documents\Timberborn\Mods\Banner Tweaks`
- All assets verified in deployed mod folder

## Hard Rule
DO NOT EVER TOUCH THE DEPLOY FOLDER.

BUILD DOES EVERYTHING, NEVER EVER MESS WITH THE DEPLOY PROCESS.
