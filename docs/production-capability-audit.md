# Unity-Comdr 能力保真登记表（Fidelity Register）

**Date:** 2026-08-03 (EXEC closeout pass)  
**Product version:** 0.4.0  
**权威标准：** [`docs/fidelity-audit-standard.md`](fidelity-audit-standard.md) v1  
**判定方法：** C1 源码锚点 + C2 禁止捷径扫描（**不使用**单元测试作证据）

> **Impl：** `LIVE` | `MIXED` | `SIM` | `STUB`  
> **Claim：** `YES` | `LIMITED` | `NO`

**整体结论：**

| 面 | 结论 |
|----|------|
| 协议可启动 | 可启动 |
| Editor 保真（本轮 EXEC） | **大幅收口**：假成功路径已改 Canonical 或诚实失败；escape 明确 plan-only |
| Agent 真看见 UI | **CODE ready / CLAIM BLOCKED** — AC-V1…V5/V7/V9/V10 代码路径；**AC-V6 C4** 未做则禁止营销「真看见」。见 acceptance-criteria 簇 V + `ac-v6-operator-procedure.md` |

---

## 1. Core tools

| Capability | Impl | Level | Claim | Canonical? | C1 源码锚点 | 禁止捷径 | 限制句 |
|------------|------|-------|-------|------------|-------------|----------|--------|
| `console_read` | LIVE | L3 | **YES** | Yes | `LiveUnityBridgeServer` `console.get` + log callback | 无 | 桥内日志 |
| `console_clear` | MIXED | L2 | **LIMITED** | Partial | 清 Logs + `LogEntries.Clear` 反射 | 反射可选 | 可能与 Console UI 不同步 |
| `script_*` | LIVE | L3 | **YES** | Yes | 文件系统 + AssetDatabase | 无 | — |
| `editor_state` | LIVE | L3 | **YES** | Yes | `SerializeState` + `hostMode`；`IEditorHost.HostMode` | 无 | 强制暴露 hostMode |
| `editor_compile` | LIVE | L3 | **YES** | Yes | `CompilationPipeline.RequestScriptCompilation` | 已去掉 Refresh-only | headless 仍为 epoch 合成 |
| `scene_manage` | LIVE | L3 | **YES** | Yes | EditorSceneManager | 无 | — |
| `hierarchy_get` | LIVE | L3 | **YES** | Yes | `SerializeGo` + `SerializeComponentData`（有界属性） | 无空 properties 捷径 | 属性条数有界 |
| `gameobject_manage` | LIVE | L3 | **YES** | Yes | GameObject API | 无 | — |
| `component_manage` get/modify | LIVE | L3 | **YES** | Yes | `SerializedObject` 导出；modify 支持 scalar/Vector2/3/Color/Enum | 数组未改 | 嵌套数组 skip |
| `component_manage` add/remove | LIVE | L3 | **YES** | Yes | AddComponent / DestroyImmediate | 无 | — |
| `component_manage` list_types | LIVE | L3 | **YES** | Yes | 程序集扫描 `Component` 派生 | 无硬编码六类型 | 上限 500 |
| `assets_manage` | LIVE | L3 | **YES** | Yes | AssetDatabase / PrefabUtility | 材质默认 Standard | — |
| `skill_manage` | LIVE | L3 | **YES** | n/a | ToolRegistry | 无 | — |
| `escape_hatches_set` | LIVE | L3 | **YES** | n/a | 门控 | 无 | — |

---

## 2. Domain skills

| Skill / tools | Impl | Level | Claim | Canonical? | C1 源码锚点 | 禁止捷径 | 限制句 |
|---------------|------|-------|-------|------------|-------------|----------|--------|
| `testing` | LIVE（live）/ honest fail（headless） | L3 / L0 | **YES** on live; **NO** as headless success | Yes | `tests.run`/`tests.list` 立即返回 jobId；callback/`tests.status` 完成；**无** main-thread Wait | 已删除假测试与 list Wait | headless isError |
| `prefab-advanced` | LIVE | L3 | **YES** | Yes | PrefabUtility 路径 | 无 | — |
| `playmode` | LIVE | L3 | **YES** | Yes | EditorApplication play | 无 | — |
| `selection` | LIVE | L3 | **YES** | Yes | Selection API | 无 | — |
| `packages` | LIVE | L3 | **YES** | Yes | `Client.*` 异步 job：`package.*` start + `package.status`；`EditorApplication.update` 泵；host 侧轮询；失败 `Fail` | 无 main-thread Sleep；无假 packages 错误项 | headless isError |
| `menu` list | MIXED | L2 | **LIMITED** | Declared | `BuiltinMenuCatalog` + `coverage:whitelist` | 明确非全菜单 | whitelist only |
| `menu` execute | LIVE | L3 | **YES** | Yes | `ExecuteMenuItem`；未知路径 false | 无假 true | — |
| `profiling` | MIXED | L2 | **LIMITED** | Declared | `Profiler.Get*`；save=JSON metrics 文件 | 描述标明非 .data 二进制 | metrics snapshot only |
| `screenshots` | LIVE | L3 | **YES**（像素路径） | Yes | CaptureScreenshotJson + MCP image；headless `no_live_pixels` | 无 marker 成功 | 产品「真看见」仍要 AC-V6 |
| `screenshots` batch=surround | LIVE | L3 | **YES** | Yes | `CaptureSurroundContactSheet` 6 视角一张 | 非 N 图 | 需 target |
| `screenshots` isolated | MIXED | L2 | **LIMITED** | Partial | 单 Front；surround 另参 | 非 Ivan 全套 lights | 见 note |
| `component_manage` UI mutate | LIVE | L3 | **YES** | Yes | modify 回 layout + vision.nextStep region crop | — | AC-V10 代码路径 |
| `batch` | LIVE | L3 | **YES** | n/a | CallAsync 编排 | 无 | 取决于子调用 |

---

## 3. Escape hatches

| Capability | Impl | Level | Claim | Canonical? | C1 源码锚点 | 禁止捷径 | 限制句 |
|------------|------|-------|-------|------------|-------------|----------|--------|
| `reflect_call` | STUB plan-only | L0 | **NO**（execute） | Honest | `planOnly=true`, `executed=false` 描述写明 | 不再伪装可执行 | plan-only |
| `execute_code` | STUB plan-only | L0 | **NO**（execute） | Honest | 同上 | 同上 | plan-only |

---

## 4. Bridge / host

| Item | Impl | Level | Claim | C1 源码锚点 | 问题 | 限制句 |
|------|------|-------|-------|-------------|------|--------|
| `hostMode` 披露 | LIVE | L3 | **YES** | `EditorState.HostMode` + `IEditorHost.HostMode` + `editor_state` 返回 | 已闭合 EXEC-MODE-DISCLOSE | live\|headless |
| Headless 回落 | LIVE 诚实 | L3 | **YES**（诚实） | Factory 仍可回落；package/tests 在 headless isError | 已闭合静默假 UPM/假测试 | 勿当真实工程 |
| `ui.query` | LIVE | L3 | **YES** | Canvas/RectTransform 枚举 | 非 stub [] | 无 Canvas 则空列表 |
| `input.simulate` | STUB honest fail | L0 | **NO** | `Fail(...)` 非 ok:true | 已闭合假成功 | 用 menu/selection |
| Bridge JSON | MIXED | L2 | **LIMITED** | 手写 Extract + Vector 嵌套 apply | 数组 properties 不改 | 复杂 JSON 有限 |
| TCP delayCall 泵 | MIXED | L2 | **LIMITED** | delayCall + Wait | 非 update 队列 | 见前 |
| MCP stdio | MIXED | L2 | **LIMITED** | 手写 newline | 非官方 SDK | — |

---

## 5. 宣称速查

**Claim YES：** live 下脚本/场景/GO/组件属性/程序集 list_types/UPM Client/TestRunner/playmode/selection/截图主路径/hostMode。  

**Claim LIMITED：** menu whitelist、profiler JSON metrics、bridge JSON 复杂度、isolated 单视角。  

**Claim NO（execute）：** reflect/execute（plan-only）；input.simulate；headless 上的 tests/packages 成功。

---

## 6. 维护

改执行端 → 同步本表与 `audit.md` EXEC 关闭状态。证据禁止写测试类名。
