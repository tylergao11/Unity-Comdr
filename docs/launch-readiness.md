# Unity-Comdr Launch-Readiness Checklist

**Date:** 2026-08-03  
**Package:** `com.unitycomdr.mcp` **0.4.0**  

**权威：**

- 全局验收（有用即做）：[`acceptance-criteria.md`](acceptance-criteria.md)  
- 保真标准：[`fidelity-audit-standard.md`](fidelity-audit-standard.md)  
- 能力登记表：[`production-capability-audit.md`](production-capability-audit.md)  
- 债务：[`audit.md`](audit.md)

## Decision（拆轴，禁止单一 GO）

| Axis | Decision | 依据 |
|------|----------|------|
| **协议可启动**（build host、stdio、tools 注册、loopback 桥进程） | **GO（可试用）** | 源码路径；**不**用测试绿证明 |
| **Editor EXEC 保真（假成功债）** | **GO（登记表 Claim 已收口）** | EXEC-* 已 CLOSED：Canonical live 或诚实 plan-only/isError；见 `audit.md` |
| **产品「agent 真看见 UI」** | **NO-GO** | VISION-* 产品宣称仍 BLOCKED |

旧「单一 production GO / 全假 PASS」叙事已废止；以登记表 Claim 为准。

---

## Operator go-live（试用，非保真认证）

1. UPM: `packages/com.unitycomdr.mcp`  
2. `dotnet build UnityComdr.sln -c Release`  
3. MCP client → `dotnet exec …/UnityComdr.McpHost.dll`  
4. 打开 Unity（桥 `127.0.0.1:17890`）；首次工具调用同意 Bridge consent  
5. 需要 headless 进程自检时可用 `UNITY_COMDR_FORCE_HEADLESS=1`（**结果不可当 Editor 保真**）  

**不再**将 `dotnet test` 列为 launch gate 或保真证据。

---

## Summary by claim class

（与 [`production-capability-audit.md`](production-capability-audit.md) 一致；**勿用旧表**。）

| Class | Examples | Launch 含义 |
|-------|----------|-------------|
| Claim **YES**（live） | script/scene/GO、component get/list_types、playmode、selection、assets、**tests_***（TestRunnerApi）、**package_manage**（Client.*）、**ui.query**（Canvas 枚举）、截图主路径、hostMode | 可写「支持」且须 live Editor |
| Claim **LIMITED** | menu list（whitelist）、profiler JSON metrics、isolated 单视角、bridge JSON 复杂度、client config | 必须写限制 |
| Claim **NO** | reflect/execute（plan-only）、input.simulate、headless 上假成功 tests/packages | **禁止**写可执行/UPM/真测试于 headless |

详见登记表全文。

---

## Explicit open items

| ID | 状态 |
|----|------|
| EXEC-* 表 | **全部 CLOSED**（2026-08-03）— 见 `audit.md` |
| VISION-* / AC-V1…V10 | **OPEN** — 按 acceptance-criteria 簇 V 做满（含 AC-V6 C4） |
| 其它有用 OPEN | escape 真执行/下架、input 真注入/下架、安装五分钟、reload C4、多实例/Runtime 等 — 见 acceptance-criteria |

---

## Security defaults

| Item | Status |
|------|--------|
| Loopback bind | 是 |
| Escape default off | 是 |
| No forced cloud | 是 |

Cycle log: `docs/ops-loop.md`（历史轮次；结论以本文件 + 登记表为准）。
