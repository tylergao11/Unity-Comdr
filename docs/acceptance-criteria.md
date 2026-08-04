# Unity-Comdr 完整验收标准（v2）

**日期：** 2026-08-03  
**状态：** 生效 · **有用即做 · 禁止「以后再说」**  
**产品：** `com.unitycomdr.mcp` / `UnityComdr.McpHost`

---

## 0. 总则

| ID | 规则 |
|----|------|
| **R0** | 有用能力必须有本文件 AC；未达标 = **OPEN**。禁止 P1/P2/「本轮非目标」代替完成。 |
| **R1** | **精准 > 节省**（[`fidelity-audit-standard.md`](fidelity-audit-standard.md) · PR-0）。 |
| **R2** | 证据仅 **C1**（源码+API 名）或 **C4**（真人 live 操作记录）。**禁止**单测/`dotnet test` 勾产品完成。 |
| **R3** | 终态只有：**Claim YES** · **Claim LIMITED（限制句写死）** · **下架/WONTFIX（删宣称）**。禁止长期「假成功占位」。 |
| **R4** | 关闭债务 = 对应 AC **全部 PASS** + [`audit.md`](audit.md) + [`production-capability-audit.md`](production-capability-audit.md) 同步。 |

**图例（现状列）：**

| 标记 | 含义 |
|------|------|
| **OPEN** | 未达标，必须做 |
| **CODE** | 代码路径大体在，缺 C4 或细节 |
| **PASS** | 已满足 AC（仍可随时抽查） |
| **LIMITED** | 允许有限宣称，限制句必须在工具描述+登记表 |
| **A/B** | 必须二选一落地（真做或下架） |

---

## 1. 产品总闸门（Ship Gate）

下列 **全部 PASS** 才可对外写「Editor MCP 生产可用 / agent 能看见 / 对齐上游主路径」：

| Gate | 要求 | 依赖 AC |
|------|------|---------|
| **G-SHIP-1** | 协议可启动：`dotnet build` Release 成功；stdio host 可 initialize + tools/list | 基建 |
| **G-SHIP-2** | live 桥 loopback；`editor_state.hostMode=live` 可区分 headless | AC-R1 |
| **G-SHIP-3** | 三环可 live 跑通：code-fix · scene-build · playmode-verify（含视觉） | AC-L1…L3 |
| **G-SHIP-4** | 簇 V（真看见）AC-V1…V10 全过 | 簇 V |
| **G-SHIP-5** | 安装≤5 分钟 + 信任阻塞同意 | 簇 S |
| **G-SHIP-6** | 无 Claim YES 挂在 STUB/假成功上（登记表与代码一致） | 登记表审计 |

**未过 G-SHIP-4：** 允许写「可试用 / 协议可跑」；**禁止**写「agent 真看见 UI」。

---

## 2. 簇 L — 三条 Agent 主环（产品本体）

| ID | 验收标准 | 现状 |
|----|----------|------|
| **AC-L1 Code-fix** | live：`console_read`(Error) → `script_read/write` → `editor_compile` → 再 `console_read`；CS 错误在**不**依赖 vacuous `console_clear` 的情况下可清除或标记 stale；compile 中 call 得 busy+retry | CODE（需 C4） |
| **AC-L2 Scene-build** | live：create/open scene → GO/组件/材质或 prefab → `hierarchy_get` 可读 → **一张**截图确认布局（AC-V1） | CODE（视觉收尾绑 V） |
| **AC-L3 Playmode-verify** | live：load playmode → play →（可选 step）→ `screenshot_capture` **image** → agent 依图判断 → stop；prompt `playmode_verify_loop` 含视觉检查点 | OPEN（绑 AC-V6） |
| **AC-L4** | 任一环在 reload/compile 中不挂死（AC-R2） | CODE（缺 C4 kill-test） |

**簇 L 完成：** AC-L1…L4 + 各一条 C4 实录路径记入 `docs/ops-loop.md` 或 `audit` 附件。

---

## 3. 簇 V — Agent 真看见（最高优先）

**债务：** `VISION-MCP-IMAGE` · `VISION-LIVE-ONLY` · `VISION-SCENE-VIEW`  
**关闭：** AC-V1…V10 **全部 PASS**。

| ID | 验收标准 | 证据 | 现状 |
|----|----------|------|------|
| **AC-V1** | `tools/call screenshot_capture` → content 含 `{"type":"image","mimeType":"image/png","data":...}` | C1 `McpServer` / `ToolResult.OkWithImages` | **CODE/PASS 协议** |
| **AC-V2** | 整帧默认最长边 ≤640；描述写明成本旋钮；**region 不被 640 压** | C1 桥 + tool description | **CODE** |
| **AC-V3** | `game_view` 无 target：含 Overlay UI；camera 路径结果声明 overlay 排除 | C1 + `overlayUiIncluded`/`note` | **CODE** |
| **AC-V4** | 无相机/无 SceneView → **isError**，无 marker 成功 | C1 | **CODE** |
| **AC-V5** | headless/无桥 → isError，`no_live_pixels` | C1 | **CODE** |
| **AC-V6** | **C4 强制：** 见 [`ac-v6-operator-procedure.md`](ac-v6-operator-procedure.md) | C4 | **OPEN（宣称阻塞）** |
| **AC-V7** | `batch=surround` → **一张** 6 视角 contact sheet；schema 有 batch | C1 `CaptureSurroundContactSheet` | **CODE** |
| **AC-V8** | 文档不用测试名勾 VISION 产品完成 | 文档审 | **PASS** |
| **AC-V9** | region crop **原生分辨率**（`regionNative`） | C1 | **CODE** |
| **AC-V10** | RectTransform/UI modify 回 `layout` + `vision.nextStep` region crop；全环 C4 仍 OPEN | C1 代码 / C4 收敛 | **CODE**（环 C4 待） |

**完成定义：** 三条 VISION-* CLOSED；禁止营销「真看见」直至 V 全过。

---

## 4. 簇 Core — 默认 ≤15 工具

| ID | 工具/能力 | 验收标准 | 现状 |
|----|-----------|----------|------|
| **AC-CORE-1** | 默认 tools/list | 数量 ≤15；无强制 skill 工具 | PASS |
| **AC-CORE-2** | `console_read` | 可 type/contains 过滤；条目可带 epoch/stale | PASS/CODE |
| **AC-CORE-3** | `console_clear` | 清桥日志；尽量同步 Editor Console；限制句若不同步 | LIMITED 可接受若已声明 |
| **AC-CORE-4** | `script_read/write/delete/list` | live 落盘 + Import/Delete；分页 list | PASS |
| **AC-CORE-5** | `editor_state` | 含 hostMode、phase、compileEpoch、sessionGeneration、play 标志 | PASS |
| **AC-CORE-6** | `editor_compile` | live=`CompilationPipeline.RequestScriptCompilation`；epoch 递增；busy 可观测 | CODE（C4） |
| **AC-CORE-7** | `scene_manage` | get/list/list_opened/create/open/save/unload/set_active；additive | PASS |
| **AC-CORE-8** | `hierarchy_get` | 紧凑树；截断有标记；digDeeper 指向真工具名 | PASS |
| **AC-CORE-9** | `gameobject_manage` | create/get/find/delete/duplicate/rename/parent/transform/tag/layer/primitive；mutation 回读 | PASS |
| **AC-CORE-10** | `component_manage` | 见簇 C | 见下 |
| **AC-CORE-11** | `assets_manage` | find/material/prefab/folder/copy/move/delete/refresh/shaders；delete 支持 dryRun | PASS |
| **AC-CORE-12** | `skill_manage` | list/load/unload；错误提示 load 哪个 skill | PASS |
| **AC-CORE-13** | `escape_hatches_set` | 默认关；开关后 list 出现/消失 escape | PASS |
| **AC-CORE-14** | 统一 envelope | 错误含 code/suggestion/nextStep（核心路径） | CODE |
| **AC-CORE-15** | dryRun | script_delete / go delete / assets delete / batch 可 preview | CODE |

---

## 5. 簇 C — 组件

| ID | 验收标准 | 现状 |
|----|----------|------|
| **AC-C1** | get + hierarchy 组件 **SerializedObject 有界 properties**，禁止固定 `{}` | CODE |
| **AC-C2** | modify：float/int/bool/string + Vector2/3 + Color + Enum **成功可回读** | CODE |
| **AC-C3** | 不支持的类型 → **失败**且消息含类型/属性名（禁止静默 skip 当 ok） | OPEN（数组等仍 skip） |
| **AC-C4** | list_types：程序集扫描 Component 派生；可 filter；上限声明 | CODE |
| **AC-C5** | ObjectReference：支持赋值则 C1 测通；否则失败声明 | OPEN |

**完成：** AC-C1…C5。

---

## 6. 簇 Skills — 九包

| ID | Skill | 验收标准 | 现状 |
|----|-------|----------|------|
| **AC-SK-T1** | testing | live TestRunnerApi job 跑通真实用例至 completed；list 非阻塞 | CODE（缺 C4 AC-T6） |
| **AC-SK-T2** | testing | headless isError，无假 passed | PASS |
| **AC-SK-P1** | packages | live Client.* 异步 job+status；失败 Fail；主线程无 Sleep 等 UPM | CODE（缺 C4） |
| **AC-SK-P2** | packages | headless isError | PASS |
| **AC-SK-PM** | playmode | play/pause/stop/step 反映 EditorApplication；get 回 state | PASS |
| **AC-SK-SEL** | selection | get/set GO + assets 真 Selection API | PASS |
| **AC-SK-MENU1** | menu list | whitelist + coverage 声明 **或** 可发现菜单；永不谎称完整 | LIMITED OK 若描述对 |
| **AC-SK-MENU2** | menu execute | ExecuteMenuItem；未知路径失败 | PASS |
| **AC-SK-PROF** | profiling | 指标集写死在描述；save/load 声明为 JSON metrics 非 .data；或收窄命名 | LIMITED OK 若诚实 |
| **AC-SK-SS** | screenshots | 见簇 V；skill 仅包装 live 像素 | 绑 V |
| **AC-SK-ISO** | isolated | 单视角须 LIMITED 声明；若宣称 Composite/多视角则实现 AC-V7 级 | LIMITED |
| **AC-SK-PRE** | prefab-advanced | batch instantiate + list prefab | PASS |
| **AC-SK-BAT** | batch | 顺序执行；stopOnError；dryRun 影响列表 | PASS |

---

## 7. 簇 E — Escape（真做或下架）

| ID | 验收标准 | 现状 |
|----|----------|------|
| **AC-E1** | `reflect_call`：**(A)** live 真反射返回值/异常；**(B)** 从 list 移除且文档不提 | A/B · 现 plan-only=未完成 |
| **AC-E2** | `execute_code`：**(A)** live 沙箱执行；**(B)** 移除 | A/B · 现 plan-only |
| **AC-E3** | 默认关闭；开启可审计 | PASS（门控） |
| **AC-E4** | 选 A 时 headless isError/plan-only 且描述一致 | 待 A |

**完成：** E1+E2 均已选边落地。

---

## 8. 簇 I — 输入与 UI

| ID | 验收标准 | 现状 |
|----|----------|------|
| **AC-I1** | `input.simulate`：**(A)** 真注入可观测；**(B)** 不下发工具、不宣称 | A/B · 现 Fail 占位 → 须收口 |
| **AC-I2** | `ui.query` live 枚举 id/path/rect/kind；filter 可用 | CODE |
| **AC-I3** | skill `ui`（若要）：RectTransform 锚点/sizeDelta 分页 + 与 crop 联用 C4 | OPEN |
| **AC-I4** | 改 UI 后 mutation 含 layout 摘要（rect/anchor） | OPEN |

---

## 9. 簇 R — 生命周期 / hostMode

| ID | 验收标准 | 现状 |
|----|----------|------|
| **AC-R1** | `editor_state` 与握手含 `hostMode: live\|headless` | PASS |
| **AC-R2** | compiling / reloading / play_transition → 立即 busy + suggestedRetrySeconds；**C4 kill-test** 一次 | CODE（缺 C4） |
| **AC-R3** | reload 后旧 instanceId → stale 错误 + path 建议 | CODE |
| **AC-R4** | headless 对 tests/packages/截图像素无业务成功 | PASS |
| **AC-R5** | UPM/TestRunner **不**在 Unity 主线程 Sleep 空等（update 泵 + job） | PASS（代码） |

---

## 10. 簇 S — 安装与信任

| ID | 验收标准 | 现状 |
|----|----------|------|
| **AC-S1** | 干净机 ≤5 min：UPM → build → deeplink/project-local 配置 → 首次 call（C4 计时） | OPEN |
| **AC-S2** | Doctor：监听/端口/last call/host DLL | CODE |
| **AC-S3** | 首连 **阻塞** 同意；拒绝后不可静默执行 | CODE（缺 C4） |
| **AC-S4** | per-tool/skill 禁用生效 | CODE |
| **AC-S5** | 本地审计日志无外传 | CODE |
| **AC-S6** | 无 Python/Node/强制云 | PASS |

---

## 11. 簇 X — 多实例与协议

| ID | 验收标准 | 现状 |
|----|----------|------|
| **AC-X1** | 多 Editor：按 project path 或 PID 选择；错连可辨 isError | OPEN |
| **AC-X2** | 发现机制文档化；单实例默认不破坏 | OPEN |
| **AC-X3** | 客户端矩阵：Cursor / VS Code / Claude / Codex / Doggy 声明兼容与限制 | OPEN（部分安装） |
| **AC-X4** | 官方 MCP SDK **或** 手写协议在矩阵内验证通过（含 image） | CODE 手写 |

不做多实例 → `CAP-MULTI-INSTANCE` **WONTFIX** + 删宣称。

---

## 12. 簇 G — Runtime 游戏内 MCP

| ID | 验收标准 | 现状 |
|----|----------|------|
| **AC-G1** | Runtime 包进 Play；与 Editor 职责分离文档 | OPEN |
| **AC-G2** | 层级/组件摘要、截帧或纹理、日志；失败诚实 | OPEN |
| **AC-G3** | 默认 loopback；无默认公网 | OPEN |
| **AC-G4** | C4 一次 Play 会话 | OPEN |

不做 → `RUNTIME-WONTFIX` + 删所有 Runtime「将支持」。

---

## 13. 簇 P — Prefab / 资产长尾

| ID | 验收标准 | 现状 |
|----|----------|------|
| **AC-P1** | Prefab Mode open/save/close 若宣称则真 API | 未宣称则 N/A |
| **AC-P2** | SO/Texture/Shader skill 若上架则 CRUD+C4 | 未上架则 N/A |
| **AC-P3** | 材质创建 shader 可参数化或声明仅 Standard | LIMITED 可接受 |

---

## 14. 簇 M — 菜单 / Profiler / JSON 桥

| ID | 验收标准 | 现状 |
|----|----------|------|
| **AC-M1** | menu list 覆盖率诚实（whitelist LIMITED 或可发现） | LIMITED OK |
| **AC-M2** | menu execute 尊重返回值 | PASS |
| **AC-M3** | profiler 命名/描述与 JSON metrics 一致 | LIMITED OK |
| **AC-M4** | 桥 JSON：嵌套 Vector/Color 可改；数组不支持则 **modify 失败** 非静默 | OPEN 部分 |
| **AC-M5** | 主线程泵：delayCall 可接受；若失焦丢请求则改为 update 队列并 C1 证明 | LIMITED |

---

## 15. 资源 / Prompts

| ID | 验收标准 | 现状 |
|----|----------|------|
| **AC-RP-1** | `resources/list` + `unity://*` 可读（hierarchy/logs/editor-state/skills 等） | PASS |
| **AC-RP-2** | packages resource 在 headless 不假成功 | PASS |
| **AC-RP-3** | prompts：code_fix / scene_build / playmode_verify 可用 | PASS |
| **AC-RP-4** | playmode_verify prompt **嵌入视觉检查点**（AC-V / J4） | **CODE/PASS**（`PromptCatalog.playmode_verify_loop`） |

---

## 16. 安全默认（始终）

| ID | 验收标准 | 现状 |
|----|----------|------|
| **AC-SEC-1** | 桥仅 127.0.0.1 | PASS |
| **AC-SEC-2** | 无强制云/phone-home | PASS |
| **AC-SEC-3** | escape 默认关 | PASS |
| **AC-SEC-4** | 破坏性操作需明确 path；dryRun 可用 | CODE |

---

## 17. 总清单速查（OPEN 优先）

### 必须做满（有用 · 阻塞产品宣称）

| 序 | AC 集合 | 一句话 |
|----|---------|--------|
| 1 | **AC-V1…V10** | 真看见（含 AC-V6 会话、V7 contact sheet 或删宣称、V10 UI 精修） |
| 2 | **AC-L1…L4** | 三环 + reload 不挂死 C4 |
| 3 | **AC-S1…S6** | 五分钟安装 + 信任 |
| 4 | **AC-R2 C4** | reload kill-test |
| 5 | **AC-T1…T6 · 包/测 C4** | live 可依赖 |
| 6 | **AC-C3 · C5 · M4** | 组件/JSON 不静默假成功 |
| 7 | **AC-E1/E2 · AC-I1** | 反射/执行/输入：真做或下架 |
| 8 | **AC-I3/I4 · AC-RP-4** | UI 结构+视觉环、prompt |
| 9 | **AC-X\*** 或 WONTFIX | 多实例/协议矩阵 |
| 10 | **AC-G\*** 或 WONTFIX | Runtime |

### 已基本达标（保持不回退）

- 默认 ≤15 core、多数 scene/GO/script/assets/playmode/selection/batch  
- hostMode、headless 拒假 UPM/测试/像素  
- UPM/TestRunner 异步 job 模型（代码）  
- escape 门控默认关、plan-only 暂诚实（但 E 簇仍须收口 A/B）

---

## 18. 推荐开工顺序（完整版路线）

```text
Wave 1  看见闭环
        AC-V1…V5 抽查锁定 → AC-V7/V9 补齐 → AC-V10 → AC-V6 会话（C4）
        同步 AC-L2/L3、AC-RP-4

Wave 2  可复现 live
        AC-S1…S5、AC-R2 C4、AC-T6、AC-SK-P1 C4

Wave 3  组件与诚实
        AC-C3/C5、AC-M4、AC-SK-ISO 宣称对齐

Wave 4  占位收口
        AC-E1/E2、AC-I1 选 A 或 B 落地
        AC-I3/I4（若要 UI 精修产品）

Wave 5  边界产品
        AC-X 做满或 WONTFIX
        AC-G 做满或 WONTFIX
        AC-P 仅在有宣称时
```

---

## 19. 单条 AC 记录模板

```markdown
### AC-xx
- [ ] 标准：（粘贴原文）
- 证据类型：C1 / C4
- C1 锚点：`path` `symbol` `API`
- C4 记录：日期 / Unity 版本 / 客户端 / 日志路径
- 登记表 Claim 已更新：是/否
- audit 债务：CLOSED / 仍 OPEN 因…
```

---

## 20. 关联文件

| 文件 | 角色 |
|------|------|
| 本文件 | **完整验收权威** |
| [`fidelity-audit-standard.md`](fidelity-audit-standard.md) | 何谓偷懒 / Claim 规则 |
| [`production-capability-audit.md`](production-capability-audit.md) | 能力 Impl×Claim 登记 |
| [`audit.md`](audit.md) | 债务 ID |
| [`product-ux-frontier.md`](product-ux-frontier.md) | 产品原则与 AC-V 设计来源 |
| [`launch-readiness.md`](launch-readiness.md) | 发布闸门摘要 |

---

## 21. 修订

| 版本 | 日期 | 说明 |
|------|------|------|
| v1 | 2026-08-03 | 有用即做初版 |
| **v2** | 2026-08-03 | **完整版**：Ship Gate、三环、Core/Skills 全表、OPEN 总清单、五波开工顺序 |
