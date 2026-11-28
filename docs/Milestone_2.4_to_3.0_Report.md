# Milestone 2.4 → 3.0 改进报告 / Improvement Report

> 生成日期 / Generated: 2026-11-28
> 仓库 / Repository: `HyperMedia_Lite`
> 范围 / Scope: 从 `Milestone 2.4` 提交到当前 `Milestone 3.0` 提交（含两者之间的全部提交）

---

## 一、范围与方法 / Scope & Method

**中文**

本次总结基于对 Git 历史的直接读取。当前环境没有可用的 `git.exe`，因此采用直接解析 `.git` 目录中的 `commit` / `tree` / `blob` 对象的方式，读取 reflog、提交信息、每次提交的文件树差异，并与工作区文件做 blob 哈希比对。统计区间为：

- 起点：`Milestone 2.4` — `387edcda74bef89b9a7c98146cb9bb152835948a`
- 终点：`Milestone 3.0` — `b012e200a418cc4043918638d50682c8ba6cc01e`（当前 `HEAD`）

工作区文件 blob 哈希与 `HEAD` 一致，说明本轮所有改动均已包含在 `Milestone 3.0` 提交中。

**English**

This report is based on a direct read of the Git history. No `git.exe` is available in this environment, so commit, tree and blob objects under `.git` were parsed directly: the reflog, commit messages, per-commit tree diffs, and blob hashes were compared with the working tree. The reviewed range is:

- Start: `Milestone 2.4` — `387edcda74bef89b9a7c98146cb9bb152835948a`
- End: `Milestone 3.0` — `b012e200a418cc4043918638d50682c8ba6cc01e` (current `HEAD`)

Working-tree blob hashes match `HEAD`, so all work described here is already contained in the `Milestone 3.0` commit.

---

## 二、提交时间线 / Commit Timeline

| 提交 / Commit | Hash (short) | 时间 (UTC+8) / Time | 主题 / Subject |
| --- | --- | --- | --- |
| Milestone 2.4 | `387edcd` | 2026-11-27 12:13 | 基线 / Baseline |
| Milestone 2.41 | `3779799` | 2026-11-27 18:00 | 多选与交互打磨 / Multi-select & interaction polish |
| Milestone 3.0_Pre1 | `4974140` | 2026-11-28 13:23 | 功能总补强 / Feature batch |
| Milestone 3.0_Pre2 | `6e0ddaf` | 2026-11-28 13:47 | 图片查看器输入体系 / Image viewer input |
| Milestone 3.0 | `b012e20` | 2026-11-28 14:30 | 设置归位与输入修复 / Settings consolidation & input fixes |

---

## 三、分阶段改进 / Improvements by Milestone

### 3.1 Milestone 2.41 — 多选与交互打磨 / Multi-select & Interaction Polish

**中文**

- 重构多选状态：媒体库、播放队列、播放列表各自拥有独立的多选状态和选中项集合，离开页面时统一清空，避免残留。
- 多选操作条按场景专业化：音乐、图片、队列、播放列表分别显示适用的操作，避免通用按钮误用。
- `MainPage.xaml.cs` 统一用 `SyncMultiSelectControls()` 同步各界面状态；`SelectionMode` 切换时先安全 `Clear()`，规避 `E_UNEXPECTED`。
- `PlaybackService` 增加按 `MediaItem` 实例从队列移除的重载。
- `docs/界面设计规范.md` 新增第 9 节，记录「深色背景按钮悬停变黑」的根因与正确做法：不用默认模板，改用自定义 `ControlTemplate` + `VisualStateManager`。

**English**

- Reworked multi-select into three independent states (library, playback queue, playlists), each with its own selected-item collection; all are cleared when leaving the page.
- Context-specific multi-select action bars: music, photos, queue and playlists each show only applicable actions.
- `MainPage.xaml.cs` now funnels state through `SyncMultiSelectControls()`; selection modes are safely cleared before toggling to avoid `E_UNEXPECTED`.
- `PlaybackService` gained a queue-removal overload that accepts a `MediaItem` instance.
- `docs/Interface Design Guidelines` gained section 9 documenting the recurring "buttons turn black on hover over dark surfaces" issue and the fix: custom `ControlTemplate` + `VisualStateManager` instead of the default template.

### 3.2 Milestone 3.0_Pre1 — 功能总补强 / Major Feature Batch

**中文**

- 新增 `Services/PlayStatsService.cs`：播放次数、最后播放时间本地持久化；**自动统计默认关闭**，需在设置页手动开启。
- 音乐元数据编辑器：`MetadataPage.xaml(.cs)` 支持标题、艺人、专辑、专辑艺人、副标题、曲目号、年份、评分、流派、作曲，并通过 `SavePropertiesAsync()` 写回 `MusicProperties`。
- 图片编辑修复与增强（`EditPage` / `ImageEditService`）：
  - 修正预览时按原图尺寸算索引导致的越界崩溃；
  - 滤镜切换实时预览；
  - 水印支持字体、颜色、字号和数值显示；
  - 相框支持颜色与位置（四边 / 仅左右 / 仅上下）；
  - 窄窗编辑面板改为响应式布局，按钮带边框和更大响应区，滑杆显示当前数值；
  - 旋转 / 翻转图标区分，另存为 JPEG / PNG。
- 主页重做：启动后停留在主页；最近播放 / 常听 / 收藏 / 最近添加四个分区；卡片等宽、真实 `Margin` 间距，宽窗两列、窄窗一列，内容居左。
- 工程与兼容性：`HyperMedia_Lite.csproj`、`Package.appxmanifest` 调整（编译期 16299、manifest 声明 15063、保留 `.NET Native` 与 `Windows.Mobile`）；`LibraryService`、`MediaItem`、`NavItem` 配套；`SettingsPage` 增加自动统计开关。

**English**

- Added `Services/PlayStatsService.cs`: play count and last-played time persisted locally; **automatic statistics are off by default** and must be enabled manually in Settings.
- Music metadata editor: `MetadataPage.xaml(.cs)` edits title, artist, album, album artist, subtitle, track number, year, rating, genre and composer, writing back through `SavePropertiesAsync()`.
- Image editing fixes and enhancements (`EditPage` / `ImageEditService`):
  - Fixed an out-of-bounds crash caused by applying original-size indices to the downscaled preview;
  - Filter changes now refresh the preview immediately;
  - Watermarks support font, color, size and a live numeric readout;
  - Frames support color and position (all sides / left-right only / top-bottom only);
  - Responsive narrow-window editing panel, bordered buttons with larger hit areas, sliders with numeric values;
  - Distinct rotation / flip icons and Save As to JPEG / PNG.
- Home page rework: app now starts on Home; four sections (Recently Played / Most Played / Favorites / Recently Added); equal-width cards with real `Margin` gaps, two columns on wide windows and one on narrow windows, left-aligned content.
- Engineering & compatibility: `HyperMedia_Lite.csproj` and `Package.appxmanifest` updated (16299 build target, 15063 manifest, .NET Native and `Windows.Mobile` retained); supporting changes in `LibraryService`, `MediaItem`, `NavItem`; automatic-statistics toggle added to Settings.

### 3.3 Milestone 3.0_Pre2 — 图片查看器输入体系 / Image Viewer Input

**中文**

- 图片查看器新增触摸滑动 / 鼠标拖动翻页：仅在「适应窗口」状态下触发，放大后拖动仍用于平移。
- 鼠标滚轮改为直接缩放（不再强制 Ctrl），按住 Ctrl 加速；双击在「适应窗口 ↔ 2 倍」之间按指针位置锚定缩放。
- 修正 `ZoomAt` 偏移计算，并加入放大后鼠标左键拖动平移。
- 引入 `PhotoViewerSettings.MouseWheelMode`（缩放 / 平移）。
- 修正多个 UWP API 用法：`ManipulationVelocities.Linear`、`PointerPoint.PointerDevice.PointerDeviceType`、`Pointer.PointerId` 为 `uint` 等编译错误。

**English**

- Added touch swipe / mouse drag paging in the image viewer, active only at fit zoom; dragging while zoomed still pans the image.
- Mouse wheel now zooms directly (Ctrl no longer required; Ctrl increases the step); double-tap toggles between fit zoom and 2x, anchored at the pointer.
- Corrected `ZoomAt` offset math and added left-button drag panning for zoomed images.
- Introduced `PhotoViewerSettings.MouseWheelMode` (Zoom / Scroll).
- Fixed several UWP API misuses that caused compile errors: `ManipulationVelocities.Linear`, `PointerPoint.PointerDevice.PointerDeviceType`, and the `uint` type of `Pointer.PointerId`.

### 3.4 Milestone 3.0 — 设置归位与输入修复 / Settings Consolidation & Input Fixes

**中文**

- 滚轮模式从查看器工具栏移到**设置页**，并扩展为三种：缩放图片 / 平移画面 / **切换图片**（向上滚上一张、向下滚下一张，带 180ms 节流）。
- 新增 `[WHEEL]` 日志：记录模式、`delta`、目标缩放、实际缩放、滚动偏移和切图动作，便于定位输入问题。
- 修复核心滚轮缺陷：
  - `ScrollViewer` 在图片放大后会自行处理滚轮并标记 `Handled`，导致普通事件处理器失效——改用 `AddHandler(..., handledEventsToo: true)`；
  - `ChangeView` 是异步的，连续滚轮会读到滞后的 `Scroller.ZoomFactor`——引入 `_targetZoom` 作为请求基准；
  - 曾尝试的 `CancelDirectManipulations()` 会取消刚发出的缩放请求，已移除；
  - 滚轮步长按 `delta` 比例缩放，触控板更平滑。
- 胶片条：当前图片以强调色边框凸显并自动居中；支持滚轮横向滚动和鼠标 / 手指拖动，点击选图不受影响。
- 将查看器中的临时滚轮按钮 / 菜单移除，设置入口统一收敛到设置页。
- 用户已确认滚轮问题得到解决。

**English**

- Moved the mouse-wheel mode out of the viewer toolbar into **Settings** and expanded it to three modes: Zoom / Pan / **Switch Photo** (wheel up = previous, wheel down = next, throttled to 180 ms).
- Added `[WHEEL]` logging: mode, `delta`, target zoom, actual zoom, scroll offsets and photo-switch actions, for easier input debugging.
- Fixed the core wheel defect:
  - Once an image is zoomed, `ScrollViewer` handles the wheel itself and marks the event `Handled`; the normal XAML handler stops firing — fixed with `AddHandler(..., handledEventsToo: true)`;
  - `ChangeView` is asynchronous, so consecutive wheel events read a stale `Scroller.ZoomFactor` — introduced `_targetZoom` as the request-side source of truth;
  - `CancelDirectManipulations()` cancelled the just-issued zoom request and was removed;
  - Wheel step now scales with `delta` for smoother trackpad input.
- Filmstrip: the current image is highlighted with an accent border and auto-centered; it supports wheel scrolling and mouse/finger dragging without breaking click-to-select.
- Removed the temporary wheel button/menu from the viewer; the single source of truth is now the Settings page.
- The user confirmed the wheel issue is resolved.

---

## 四、主题归纳 / Themes

| 主题 / Theme | 关键改进 / Key Improvements |
| --- | --- |
| 输入与交互 / Input & Interaction | 场景化多选、滚轮三模式、滑动翻页、鼠标拖动平移、胶片条手势 / Contextual multi-select, three wheel modes, swipe paging, mouse-drag panning, filmstrip gestures |
| 创作与编辑 / Creation & Editing | 编辑页崩溃修复、滤镜实时预览、水印 / 相框选项、元数据写回、另存为 / Edit crash fix, live filter preview, watermark & frame options, metadata write-back, Save As |
| 信息架构 / Information Architecture | 默认主页、响应式卡片布局、居中等宽卡片 / Home as startup page, responsive card layout, left-aligned equal-width cards |
| 数据与统计 / Data & Statistics | 播放次数 / 最后播放持久化，自动统计默认关闭 / Play counts and last-played persisted, automatic stats opt-in |
| 兼容与工程 / Compatibility & Engineering | 15063 manifest + 16299 编译目标、API 守卫、日志 / 15063 manifest + 16299 build target, API guards, logging |
| 文档 / Documentation | ROADMAP、未来规划、界面设计规范第 9 节 / ROADMAP, future plans, UI guidelines section 9 |

---

## 五、验证状态与风险 / Verification Status & Risks

**中文**

- 本环境无法编译或部署 UWP；所有 XAML、事件处理器、括号与 blob 校验通过，但实际编译和运行仍需用户在 Visual Studio 中完成。
- 滚轮问题已由用户确认修复；触摸手势、胶片条凸显、图片编辑等需在真机 / VS 中继续回归。
- 所有改动均已提交至 `Milestone 3.0`；工作区仅存在未跟踪的 `HyperMedia_Lite.csproj.user` 及行尾规范化相关的配置差异。

**English**

- UWP cannot be built or deployed in this environment. XAML, event handlers, brace checks and blob hashes pass static validation, but actual compilation and runtime verification still require Visual Studio on the user's machine.
- The wheel issue has been confirmed fixed by the user; touch gestures, filmstrip highlighting and image editing still need regression testing on device / in VS.
- All work is committed in `Milestone 3.0`; the working tree only contains an untracked `HyperMedia_Lite.csproj.user` and line-ending normalization differences in configuration files.

---

## 六、结论 / Conclusion

**中文**：从 Milestone 2.4 到 3.0，项目完成了从「功能可用」到「输入顺手、设置收敛、编辑可创作」的跨越：多选按场景专业化，主页成为默认落点且排版响应式，图片编辑补齐并稳定，播放统计与元数据落盘，图片查看器的滚轮 / 手势 / 胶片条形成一套可配置、可观测的输入体系。

**English**: From Milestone 2.4 to 3.0, the project moved from "features work" to "input feels right, settings are consolidated, and editing is creative": context-aware multi-select, a responsive Home landing page, a stabilized and expanded image editor, persisted playback statistics and metadata, and a configurable, observable input system for the image viewer covering wheel, gestures and the filmstrip.
