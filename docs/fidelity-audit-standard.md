# Unity-Comdr 执行端保真审计标准（Fidelity Audit Standard）v1

**日期：** 2026-08-03  
**状态：** 生效（权威）  
**优先级：** 精准 / 准确 **先于** 节省 / 优化（见 [`product-ux-frontier.md`](product-ux-frontier.md) §0 / PR-0）

本文件定义：**什么叫「支持」、什么叫偷懒、允许用什么证据宣称、禁止用什么证据洗白。**

**有用即做（禁止延后空话）：** 凡产品有用的能力必须有可判定验收 — 见 [`acceptance-criteria.md`](acceptance-criteria.md)。不得以 P1/P2/「本轮非目标」代替 AC。

登记表实例：[`production-capability-audit.md`](production-capability-audit.md)  
债务台账：[`audit.md`](audit.md)

---

## 0. 为什么需要本标准

历史上把三类东西混成单一 **PASS**：

1. 工具接线能 call 通  
2. InMemory / headless 假世界行为  
3. Live Unity Editor 真实语义  

并用 **`dotnet test` 绿** 驱动「生产级 / 已对齐」叙事。结果是：假 `tests_run`、手改 `manifest.json`、永远 dry-run 的 escape、空 `properties:{}` 等 **偷懒实现被制度奖励**。

**本标准裁定：单元测试 / 集成测试默认不计入保真与对外宣称证据；写进审计 Evidence 列视为流程违规。**

---

## 1. 总原则

| # | 原则 |
|---|------|
| P1 | **精准 > 节省**。任何为过门禁、省工而伪造 Editor 语义的路径，一律不合格。 |
| P2 | **Claim 只看 Live 执行路径**（`LiveUnityBridgeServer` + skill 最终副作用），不看 headless 是否「成功」。 |
| P3 | **测试不计入审计证据**。见 §4。 |
| P4 | 发现禁止捷径 → **立即降级 Claim**，同一变更必须改登记表。 |
| P5 | 不确定 → **不可宣称（Claim NO）**，禁止乐观 PASS。 |
| P6 | 改执行端代码的 PR：**必须**更新登记表；**不要求**新增测试作为 DoD。 |

---

## 2. 两轴判定（Impl × Claim）

每个能力必须同时有 **Impl** 与 **Claim**。禁止再用单一 `PASS` 表示「生产可用」。

### 2.1 Impl — 实现落点

| Impl | 含义 | 判定方式 |
|------|------|----------|
| **STUB** | 空返回、写死常量、永远 dry-run、假 `ok` | 读源码，副作用不触及真实 Editor 能力 |
| **SIM** | 仅 InMemory/合成；或 Live 与 SIM **共用同一套假逻辑** | skill/host 未下发桥方法，或桥方法也是假的 |
| **MIXED** | Live 调了部分真 API，但关键语义是捷径 | 例如手改文件代替官方 API、属性永远空、目录硬编码 |
| **LIVE** | 主路径为 Unity/官方等价 API，语义与工具描述一致 | C1 源码锚点指向 Canonical API，且无 §3 禁止捷径 |

判定口诀：**打开最终执行文件，看最后副作用落在哪**——不是看工具是否注册。

### 2.2 Claim — 对外宣称资格

| Claim | 含义 | 条件 |
|-------|------|------|
| **YES** | 可写 README / 竞品矩阵「支持 / 已对齐」 | `Impl = LIVE` ∧ 无禁止捷径 ∧ 描述无夸大 |
| **LIMITED** | 仅可写「部分 / 有限制」 | `Impl = MIXED` ∧ 限制句已写入登记表与（理想）tool description |
| **NO** | 禁止宣称支持 | `STUB` \| `SIM` \| 有捷径未声明 \| 未做 C1 审查 |

```text
Claim YES     ⇔  Impl = LIVE  ∧  无禁止捷径  ∧  描述一致
Claim LIMITED ⇔  Impl = MIXED ∧  限制已声明
Claim NO      ⇔  其余一切（含「没读过 Live 代码」）
```

### 2.3 保真等级（与 Claim 对齐）

| Level | Impl | Claim |
|-------|------|-------|
| L0 Stub | STUB | NO |
| L1 Simulate-only | SIM | NO |
| L2 Shallow live | MIXED | LIMITED only |
| L3 Faithful live | LIVE | YES |
| L4 Upstream-parity+ | LIVE + 对照上游关键语义 | YES（标杆） |

**偷懒（正式定义）：** 把 L0/L1 标成可宣称支持，或把 L2 限制藏在注释里不进登记表 / 工具描述。

---

## 3. 禁止捷径黑名单（Canonical 表）

声称具备下表能力且命中「禁止捷径」→ **Impl 不得为 LIVE**，**Claim 不得为 YES**。

| 能力族 | 禁止捷径（L3 失格） | LIVE 最低要求（Canonical） |
|--------|---------------------|----------------------------|
| **Tests** | 用 console 错误数 / 脚本数冒充测试；写死 catalog；job 立即 `completed` 且未跑 Runner | `UnityEditor.TestTools.TestRunner.Api`（或产品明确删除真测试能力并 Claim NO） |
| **Packages** | 手改 `Packages/manifest.json`；只扫 `PackageCache`；硬编码 search hint 冒充 registry | `UnityEditor.PackageManager.Client` List/Add/Remove/Search（异步完成） |
| **Reflection / execute** | 永远 `dryRun:true` 却叙事为可执行 | 真反射/沙箱执行，或 tools 明确 `plan-only` 且 Claim NO（execute） |
| **Component get** | 固定 `properties:{}` | `SerializedObject` 有界属性导出 |
| **Component modify** | 跳过嵌套 JSON；仅 scalar | 至少 Vector / Color / Enum / ObjectReference 等常用类型 |
| **list_types** | 写死少量类型名冒充完整 | 程序集扫描，或声明「示例列表」且 Claim LIMITED |
| **Menu list** | 十余条硬编码冒充全菜单 catalog | 声明覆盖率，或可发现菜单 API |
| **Menu execute** | 未知路径仍返回 true | 尊重 `ExecuteMenuItem` 返回值 |
| **Profiler save/load** | 进程内字典塞 JSON 冒充 profiler 文件 | 真会话语义，或收窄 API/描述 |
| **Compile** | 仅 `AssetDatabase.Refresh` + 提前 bump epoch | 编译管道与 `isCompiling` / epoch 一致 |
| **UI query** | 返回 `[]` stub | 真枚举 UGUI/UIToolkit（按 scope），或不要暴露为成功工具 |
| **Input simulate** | `ok:true` 且无真实注入 | 真注入 **或** 诚实失败（非假成功） |
| **Screenshot** | marker/假像素当成功 | 真纹理编码；headless 必须失败；不可单靠协议测试宣称「看见」 |
| **Bridge JSON** | `IndexOf` / 手写扫描冒充完整 properties 协议 | 真 JSON 解析，或限制写入描述 |

上游对照（L4 时填写；L3 不强制逐行抄）：Coplay `RunTests`/`GetTestJob`/Cameras/Clients；Ivan package/profiler/isolated/reflection；Coder update 泵 / menu / batch。

---

## 4. 合法证据与非法证据

### 4.1 唯一合法证据（C1–C4）

| 代号 | 内容 | 用途 |
|------|------|------|
| **C1 源码锚点** | `文件路径` + 方法/分支 + **实际调用的 API 名** | 判定 Impl |
| **C2 禁止捷径扫描** | 对照 §3，确认有/无 | 强制降级 |
| **C3 描述一致性** | tool description / README / 竞品句是否与 Impl 一致 | Claim |
| **C4 操作员观察（可选）** | 人在 Unity 中操作一次并记录 | 争议时增强信心；**不是**门禁 |

### 4.2 非法证据（写入即文档不合格）

- 任何测试类名：`*Production*`、`FullLoop*`、`P0Handler*`、`VisionProtocol*`、`*Tests` 等  
- `dotnet test` exit code  
- 「tools/call 能通」单独一条  
- headless 返回的 `passed: true`、合成 packages、合成 profiler  

**审计表 Evidence / 锚点列只允许 C1–C4。出现测试类名 = 违规。**

### 4.3 测试在本仓库的定位（干扰源管控）

| 允许 | 禁止 |
|------|------|
| 开发者本地自用 | 审计 Evidence 引用测试 |
| 与保真无关的个人烟雾 | 用测试证明「对齐 Coplay/Ivan」或「production PASS」 |
| | 本标准要求「补保真测试」作为关闭债务条件 |

保真验收 **无视** 测试结果。删除/改名干扰测试是可选后续任务，**不是**本标准的关闭条件。

---

## 5. mode 披露

- Agent 必须能区分 `hostMode: live | headless`（握手 / `editor_state` 或等价）。缺失 → 登记 **EXEC-MODE-DISCLOSE**，整体宣称降风险。  
- headless 走出「像真 Editor 业务成功」的结果且未声明 simulated → **描述造假**，相关能力 Claim **NO**。  
- 静默回落 InMemory（桥不通）若未暴露 mode → 同上。

---

## 6. 登记表强制列

| Capability | Impl | Level | Claim | Canonical? | C1 源码锚点 | 禁止捷径 | 限制句（LIMITED） |
|------------|------|-------|-------|------------|-------------|----------|-------------------|

删除并禁止：

- 单列 `PASS` 表示生产可用  
- Evidence 写测试名  
- 用 `RESIDUAL（环境）` 把 F 轴洗成 PASS  

无 Unity 环境时：只做 **C1+C2 静态审计**；不得因「没法跑」沿用旧 PASS。Claim YES **不要求** C4，但要求 C1 无捷径且描述一致；有争议时偏 NO。

---

## 7. 文档与宣称门禁

| 动作 | 规则 |
|------|------|
| README「支持 X」 | 仅 Claim **YES** |
| 竞品「已对齐」 | 仅 Claim **YES**；否则写「部分 / 未对齐」 |
| launch-readiness | 拆成 **协议可启动** vs **Editor 保真**；后者只看登记表 Claim |
| PR 改执行端 | 必须更新登记表 + audit 债务；**不要求**新测试 |

权威顺序：

1. **本标准** — 何谓合格  
2. [`production-capability-audit.md`](production-capability-audit.md) — 登记表实例  
3. [`audit.md`](audit.md) — 债务 ID 与关闭条件（关闭条件 **禁止**写「补单测」）  
4. [`borrow-plan.md`](borrow-plan.md) — 抄谁（不替代保真）  
5. [`competitive-audit-full.md`](competitive-audit-full.md) — 矩阵用词必须服从 Claim  

---

## 8. 状态迁移

```text
默认：未审查 → Claim NO
完成 C1+C2：
  有禁止捷径 → STUB/SIM/MIXED，Claim NO 或 LIMITED
  无捷径且 API 正确 → LIVE，Claim YES
描述夸大 → 即使 LIVE 也降为 LIMITED 或 NO，直到改描述
```

**降级优先于升级。**

---

## 9. 债务关闭模板（audit.md）

```text
关闭条件：
1. C1 锚点指向 Canonical API（写明类型/方法名）
2. C2 确认 §3 禁止捷径已不存在
3. Impl = LIVE（或产品决定删除该能力）
4. Claim 升级为 YES 或能力从对外清单移除
5. 禁止：以新增/通过单元测试作为关闭条件
```

---

## 10. 修订

| 版本 | 日期 | 说明 |
|------|------|------|
| v1 | 2026-08-03 | 首版：Impl×Claim、禁止捷径、证据无测试、测试为干扰源 |
