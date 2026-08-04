# Code audit notes（债务台账）

> **保真权威标准：** [`docs/fidelity-audit-standard.md`](fidelity-audit-standard.md)  
> **全局验收（有用即做）：** [`docs/acceptance-criteria.md`](acceptance-criteria.md) — **禁止「以后再说」**  
> **能力登记表：** [`docs/production-capability-audit.md`](production-capability-audit.md)  
> **关闭条件禁止以测试绿为依据；关闭 = 对应 AC 全 PASS。**

## Structure

| Layer | Path | Role |
|-------|------|------|
| UPM | `packages/com.unitycomdr.mcp` | LiveUnityBridgeServer |
| Core | `src/UnityComdr.Core` | IEditorHost, tools, skills |
| Host | `src/UnityComdr.McpHost` | MCP stdio |
| Tests | `tests/` | 不参与保真验收 |

## Security

- Loopback only; escape default off; no forced cloud.

---

## EXEC-* 执行端保真债务

| 债务 ID | 状态 | 关闭处置 | C1 关闭锚点 |
|---------|------|----------|-------------|
| `EXEC-TEST-FAKE` | **CLOSED** | LIVE: `TestRunnerApi` async jobs (no main-thread Wait); headless isError | HandleTestsRun/List/Status；SampleSkills |
| `EXEC-PKG-MANIFEST` | **CLOSED** | LIVE: `Client.*` async + `package.status` + update pump; Fail on error; headless isError | StartPackageJob/HandlePackageStatus；BridgeClient poll |
| `EXEC-ESCAPE-DRY` | **CLOSED** | Honest plan-only: `planOnly`/`executed:false` + description | `CoreTools.RegisterEscapeHatches` |
| `EXEC-UI-STUB` | **CLOSED** | LIVE: Canvas/RectTransform enumeration | `QueryUiJson` |
| `EXEC-INPUT-FAKEOK` | **CLOSED** | Honest Fail (no ok:true shell) | `input.simulate` → Fail |
| `EXEC-COMP-EMPTY` | **CLOSED** | LIVE: `SerializeComponentData` / SerializedObject | `comp.get` + `SerializeGo` |
| `EXEC-COMP-TYPES` | **CLOSED** | LIVE: assembly scan Component types | `ListComponentTypesJson` |
| `EXEC-MENU-CATALOG` | **CLOSED** | Honest LIMITED whitelist + coverage field | `ListMenusJson` + tool description |
| `EXEC-PROF-DICT` | **CLOSED** | Honest LIMITED: JSON metrics snapshot on disk; description | `profiler.save/load` + `profiler_manage` text |
| `EXEC-COMPILE-REFRESH` | **CLOSED** | LIVE: `CompilationPipeline.RequestScriptCompilation` | `editor.compile` case |
| `EXEC-JSON-HANDROLL` | **CLOSED** | LIMITED: Vector/Color nested apply + documented array skip | `ApplyComponentProperties` |
| `EXEC-HEADLESS-SILENT` | **CLOSED** | hostMode + headless package/tests refuse fake success | `IEditorHost.HostMode`; skills guards |
| `EXEC-MODE-DISCLOSE` | **CLOSED** | `EditorState.HostMode` + `editor_state` payload | CoreTools editor_state; SerializeState |

---

## VISION-*（产品看见 — OPEN 直至 AC-V1…V10）

> 关闭条件见 [`acceptance-criteria.md`](acceptance-criteria.md) 簇 V。操作员 C4：[`ac-v6-operator-procedure.md`](ac-v6-operator-procedure.md)。

| 债务 ID | 状态 | 关闭 = | Wave-1 代码备注 |
|---------|------|--------|-----------------|
| `VISION-MCP-IMAGE` | **OPEN**（产品宣称）/ **CODE**（协议） | AC-V1+V6 等 | Host 发 `type:image`；fixture 测试锁协议 |
| `VISION-LIVE-ONLY` | **CODE** headless isError | AC-V5 + 不回退 | `no_live_pixels` envelope |
| `VISION-SCENE-VIEW` | **CODE** 主路径 + surround | AC-V3/V4/V7/V9 + V6 | region native；`batch=surround` contact sheet |

## 其它有用能力（一律按 AC，禁止延后空话）

| 债务 ID | 状态 | 关闭 = acceptance-criteria 簇 |
|---------|------|-------------------------------|
| `EXEC-ESCAPE-EXECUTE` | **OPEN** | 簇 E（真执行或下架） |
| `CAP-INPUT-REAL` | **OPEN** | 簇 I AC-I1（真注入或下架） |
| `CAP-INSTALL-5MIN` | **OPEN** | 簇 S AC-S1…S5 |
| `CAP-RELOAD-C4` | **OPEN** | 簇 R AC-R2 C4 |
| `CAP-MULTI-INSTANCE` | **OPEN** 或未来 WONTFIX | 簇 X；不做则删宣称并 WONTFIX |
| `CAP-RUNTIME-MCP` | **OPEN** 或未来 WONTFIX | 簇 G；同上 |

## Upstream

NOTICE + THIRD_PARTY.
