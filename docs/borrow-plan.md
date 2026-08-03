# Unity-Comdr 取长计划（Borrow Plan）— 具体抄谁、抄哪、怎么抄

**日期：** 2026-08-03
**状态：** 执行中（用户已解锁 ops-loop）— Writer→Test→Audit 循环至上线品质
**优先级锁：** 精准/准确 > 节省/优化（[`product-ux-frontier.md`](product-ux-frontier.md) §0 / PR-0）
**上游结构核实：** 2026-08-03 经 GitHub API 逐目录确认（下表路径均为真实存在的文件/目录，非凭记忆）。

---

## 1. AI-first：我（agent）自己用 MCP 的真实痛点

> 这是本计划的**第一排序依据**。不是竞品清单，是我作为 agent 每天用 MCP 工具时真实被卡住的地方。每条给出：痛 → 我要什么 → 需求编号。

| # | 痛点（第一人称） | 我要什么 | 需求 ID |
|---|------------------|----------|---------|
| A1 | **图像以 text+base64 回来，我根本看不见**，还烧几万 token。这是最痛的，没有之一。 | MCP `type:"image"` content block，客户端直接喂给我的视觉模态 | FR-V1 / `VISION-MCP-IMAGE` |
| A2 | **每个工具返回形状都不同**，我每次都在"猜格式"，猜错就多一轮调用。 | 统一 envelope：`ok` / `data` / `hint` / `error`；tool description 写明返回形状 | **FR-A1（新）** |
| A3 | **写操作只回 `true`**，我不知道现在世界长什么样，必须再调一次 read 才敢继续。 | mutation 直接返回**改后新状态摘要**（改了哪个 GO、现在 transform 是什么）——省一轮，防误判 | **FR-A2（新）** |
| A4 | **错误不可行动**："not found" 不告诉我有什么近似项；"tool not found" 不告诉我去 load 哪个 skill。 | 错误里带 nearest-match 建议 + 正确下一步命令（错误即文档）。例：`unknown tool screenshot_capture — load skill first: skill_manage action=load id=screenshots` | **FR-A3（新）** |
| A5 | **长操作把我挂死**：compile/test 一卡 60 秒，不知道是死了还是在干活，客户端可能直接超时砍掉我。 | 短操作同步；长操作 job 化（提交→轮询），或至少 busy 状态 + 建议重试间隔 | FR-R1 + **FR-A4（新）** |
| A6 | **domain reload 后 ID 全体失效但没人告诉我**，我拿着旧 instanceId 操作错对象——这是**准确性事故**。 | reload 后显式声明 ID 代际失效；提供 path-based 稳定引用作为替代 | **FR-A5（新）** |
| A7 | **console 时序不可信**：修完代码重新编译再读 console，分不清哪些是旧错误、哪些是新错误。code-fix loop 的准确性命门。 | console 条目带 **compile generation/epoch** 标记；`editor_compile` 返回新 epoch 号 | **FR-A6（新）** |
| A8 | **工具选择靠猜**：描述含糊时我选错工具、传错参数，浪费轮次。 | description 必含：1 个调用示例、参数默认值、失败模式 | **FR-A7（新）** |
| A9 | **静默截断最致命**：分页没有 total，截断不声明——我会把不完整当完整，然后基于残缺数据做决定。 | 显式 truncation marker + `digDeeper` 指向真实工具（我们已有雏形，要成为硬规范） | **FR-A8（新）** |
| A10 | **破坏性操作没有 preview**：delete 前我想知道会删掉什么。 | destructive 工具支持 `dryRun=true` 返回影响清单 | **FR-A9（新）** |
| A11 | **结构拼得出，细节修不了**（2026-08-03 落霞实测）：我看不见 4px 错位、字重、间距节奏；改完 RectTransform 只回数字不回画面；没有放大镜、没有对稿工具——所以我只能交出"大体效果"。 | ① region crop 原生分辨率截取（整屏 640 上限不适用于裁剪）② 写 UI 后返回受影响元素视觉回读 ③ 参考稿对比支持 ④ design token 兜底一致性 | **FR-A10（新）** / G8 / AC-V9/V10 |

**结论：** A1（看见）与 A6/A7（reload/时序准确）直接命中「精准第一」；A2–A5、A8–A10 是「在准确之上省轮次」。竞品没有一家把 A6/A7 做成协议级契约——**这是我们的原创机会点**。

---

## 2. 抄点总表（能力 × 来源 × 抄法 × 落点）

抄法图例：
- **算法移植** = 读上游实现，用我们自己的 C# 在 `IEditorHost`/桥里重写同等逻辑（默认方式）
- **模式移植** = 只抄交互/结构设计，实现完全自己写
- **带署名改编** = 接近逐行的移植，文件头加 SPDX + 来源注释，NOTICE 登记

### 2.1 CoplayDev/unity-mcp（MIT · beta 分支已全面 C# 化 · ~13k★）

> 核实：`MCPForUnity/Editor/` 下有 `Tools/`（Cameras、GameObjects、Prefabs、Profiler、ManageUI.cs、GetTestJob.cs、BatchExecute.cs、UnityReflect.cs、ExecuteCode.cs…）、`Clients/`、`Security/`、`Windows/`、`Setup/`、`Helpers/`。纯 C# 实现意味着**几乎零跨语言翻译成本**。

| 能力 | 上游位置（已核实） | 抄法 | 落到我们哪 | 服务哪个痛点 |
|------|--------------------|------|-----------|--------------|
| 截图 → **MCP ImageContent** + `max_resolution`（默认 640px）+ surround/orbit **contact sheet** | `MCPForUnity/Editor/Tools/Cameras/`（PR [#818](https://github.com/CoplayDev/unity-mcp/pull/818)、[#840](https://github.com/CoplayDev/unity-mcp/pull/840)） | 算法移植 | `LiveUnityBridgeServer.CaptureScreenshotJson`（downscale + atlas 合成）+ `McpServer.ToolsCallResult`（image block） | **A1** / AC-V1/V2/V7 |
| 临时相机定位取景（`view_position`/`view_target`） | 同上 Cameras/ | 算法移植 | 桥 screenshot 分支 | AC-V3 |
| **长操作 job 化**（提交测试→拿 job id→轮询） | `MCPForUnity/Editor/Tools/GetTestJob.cs` + `RunTests.cs` | 模式移植 | testing skill：`tests_run` 返回 jobId，`tests_status` 轮询 | **A5** |
| 客户端检测 + 一键配置（多客户端） | `MCPForUnity/Editor/Clients/`、`Setup/`、`Windows/` | 模式移植（结构+检测逻辑参考） | `packages/com.unitycomdr.mcp/Editor/ClientConfig/`（新） | FR-I1/I2 |
| UI 元素专项操作 | `MCPForUnity/Editor/Tools/ManageUI.cs` | 算法移植（读它管哪些：Canvas/RectTransform/anchor） | 新 skill `ui`（P1；配合像素截图 = 结构+像素双通道） | G6、**A3** |
| 安全默认与危险操作清单 | `MCPForUnity/Editor/Security/` | 模式移植 | escape hatch 策略 + FR-A9 dryRun 设计参考 | **A10** |
| ScriptableObject / Texture / Shader 管理 | `ManageScriptableObject.cs` / `ManageTexture.cs` / `ManageShader.cs` | 算法移植 | 后续 skill 扩展（P2，按需） | 广度 |

### 2.2 IvanMurzak/Unity-MCP（Apache-2.0 · 纯 C# · ~3.8k★）

> 核实：仓库根 = `Unity-MCP-Plugin/`（Unity 工程，工具代码在其 `Packages/` 下）+ `cli/` + `Installer/`。工具面见 [AI-Tools-Reference wiki](https://github.com/IvanMurzak/Unity-MCP/wiki/AI-Tools-Reference)。具体工具文件路径在移植时于 `Unity-MCP-Plugin/Packages/` 内定位。

| 能力 | 上游位置 | 抄法 | 落到我们哪 | 服务哪个痛点 |
|------|----------|------|-----------|--------------|
| **`screenshot-isolated`**：单 GO 隔离渲染（临时相机 + 独立 layer + 可选 2×2 四视角拼图） | Plugin Packages 内 screenshot 套件（wiki 确认 4 个 surface） | 算法移植 | 桥 `screenshot.capture` 的 `isolated` 分支（现在是假的） | **A1** 的 prefab 级问法："这个 prefab 对不对" |
| GameView 含 Overlay UI 的截取路径（`ScreenCapture.CaptureScreenshotAsTexture` 主线程） | 同上 | 算法移植 | `game_view` 分支（现在只找 Camera，丢 Overlay UI = **不准**） | AC-V3 |
| **MainThread dispatcher**（Editor API 主线程编组） | Plugin 基础设施 | 模式移植 | 桥请求处理循环（配合 Coder 的 update-queue） | A5/A6 稳定性 |
| Reflection 子系统（find/call/type schema） | Plugin reflection 工具组 | 算法移植（默认关不变） | escape hatch `reflect_call` 真实现（现 dry-run） | 长尾能力 |
| TestRunnerApi 真集成 | Plugin test 工具 | 算法移植 | testing skill 真跑 EditMode/PlayMode | 三闭环之外的真验证 |
| 环境感知 skill 生成（OS/Unity 版本/已装插件） | `cli/` + Plugin | 模式移植（想法级） | `unity://skills` resource 增加环境元数据 | A8（帮我选对工具） |
| 按功能开关（UserSettings JSON） | Plugin settings | 模式移植 | FR-T2 per-tool disable | 信任面 |

**License 注意：** Apache-2.0 → MIT 工程可包含，但**带署名改编**的文件必须保留 Apache 头 + NOTICE 登记；优先算法移植降低义务。

### 2.3 CoderGamester/mcp-unity（MIT · Node+C# · ~1.9k★）

> 核实：`Editor/Tools/` 25 个工具文件 + `Editor/UnityBridge/` + `Server~/`（Node）。

| 能力 | 上游位置（已核实） | 抄法 | 落到我们哪 | 服务哪个痛点 |
|------|--------------------|------|-----------|--------------|
| **有界序列化 + 显式截断标记**（depth scope、5MB 上限、truncation marker） | `Editor/Tools/GetGameObjectTool.cs`（1.4.0 行为） | 算法移植 | `hierarchy_get` / `gameobject_manage get` 硬规范化 | **A9** |
| **主线程 update 泵队列**（Editor 失焦仍处理请求） | `Editor/UnityBridge/`（1.4.0 WebSocket→update-drained queue） | 模式移植 | `LiveUnityBridgeServer` TCP 接收 → `EditorApplication.update` 泵 | A5、后台可用性 |
| 项目内可提交的相对路径客户端配置 | Editor 配置窗口（1.4.0 auto-config Cursor/Claude/Codex） | 模式移植 | ClientConfig 写 `.cursor/mcp.json` 等（相对路径） | FR-I2 |
| TestRunnerApi 实现（第二参考） | `Editor/Tools/RunTestsTool.cs` | 算法移植 | testing skill | 同 Ivan |
| `send_console_log`（agent 主动写日志做标记） | `Editor/Tools/SendConsoleLogTool.cs` | 算法移植（小） | console 工具组可选 action——配合 **A7 epoch** 做"我自己的时间戳" | **A7** |
| resources/prompts 目录风格 | `Server~/src/resources|prompts` | 已吸收 | — | — |

### 2.4 Unity 官方 MCP（专有 · 只抄模式）

| 能力 | 抄法 | 落点 |
|------|------|------|
| 首次连接审批（pending connection approval） | 模式移植 | FR-T1 |
| per-tool 开关 | 模式移植 | FR-T2 |
| project path / PID 多实例寻址 | 模式移植（设计预留，P2） | 桥握手协议字段预留 |

### 2.5 协议层决策点：官方 MCP C# SDK

[`modelcontextprotocol/csharp-sdk`](https://github.com/modelcontextprotocol/csharp-sdk)（MIT）提供 image content、progress、cancellation、协议版本协商。

**定案建议：** 两步走——
1. **现在（改动最小）：** 手写 `McpServer` 直接加 `type:"image"` content block（≈30 行改动），先关 A1/AC-V1；
2. **P1 评估：** host 协议层迁移到官方 SDK（`ToolResult` 增加 `Images` 字段即可对接），拿 progress/cancellation 反哺 A5。不为迁移而迁移，迁移前后行为测试锁定。

---

## 3. 自研点（没得抄，我们原创）

| ID | 内容 | 依据 |
|----|------|------|
| O1 | **compile epoch 协议**：`editor_compile` 返回递增代数；console 条目带 `epoch` 字段；跨 epoch 的旧错误显式标记 `stale:true` | A7——竞品全都没做成契约 |
| O2 | **reload 代际契约**：桥握手带 `sessionGeneration`；domain reload 后旧 ID 调用返回 `stale_reference` 错误 + path 重查建议 | A6 |
| O3 | **统一结果 envelope**：`{ok, data, hint?, error?{code, suggestion, nextStep}}`；全部 15 core + 9 skills 一次性规范化 | A2/A4 |
| O4 | **mutation 回读**：所有写操作返回改后状态摘要（新 transform / 新层级位置） | A3 |
| O5 | **dryRun 影响清单**：`script_delete` / `assets_manage delete` / `batch_execute` 支持 preview | A10 |
| O6 | **Region crop / element crop**：按屏幕 rect 或 UI 元素 id 原生分辨率截取；`component_manage` 改 RectTransform 后可选返回该元素即时 crop（写侧视觉回读） | A11/G8——竞品只有整屏+缩放，没人做元素级视觉回读 |
| O7 | **Design token 应用**：spacing/字号/色板作为数据一次声明，工具按 token 应用到成组元素——一致性不再依赖 agent 每轮记忆 | A11 根因⑤ |
| O8 | **参考稿对比流程**：agent 侧持参考图（客户端本来就能看用户贴图），配 O6 的 region crop 实现逐区域 diff；MCP 侧只需保证 crop 保真，不需要传参考图进 Unity | A11 根因③——流程设计，成本极低 |

---

## 4. 执行阶段（有序 · 全部 BLOCKED 等解锁）

> 排序原则：**先准确（V/R），后省与顺手（E），再安装与信任（I/T）**。每阶段有验收，不许跳。

| 阶段 | 内容 | 主要抄点 | 验收 |
|------|------|----------|------|
| **V — 看见（最高优先）** | image content block；live 截图三分支修真（game_view 含 Overlay UI / isolated 真渲染 / 无相机显式失败）；640px 默认缩放（**仅整屏**）；contact sheet；**region/element crop 原生分辨率（O6）** | Coplay Cameras + Ivan isolated/ScreenCapture + 自研 O6 | AC-V1–**V10** 全过 + 真实 Cursor 会话人工确认（SC7/SC8）；含一次 UI 精修收敛实测（对照 2026-08-03 落霞案例） |
| **R — 时序准确** | busy 状态机（`editor_reloading` 等）；compile epoch（O1）；reload 代际（O2）；update 泵队列 | Coder UnityBridge + 自研 O1/O2 | SC10 kill-test：reload 中闭环存活 |
| **E — Agent 顺手** | envelope（O3）；mutation 回读（O4）；可行动错误（A4）；截断硬规范（A9）；长操作 job 化（A5）；dryRun（O5） | Coplay GetTestJob + Coder GetGameObjectTool + 自研 | 每条 A# 有对应回归测试；错误消息含 nextStep |
| **I — 首五分钟** | ClientConfig：检测 + deeplink + 项目内相对路径配置写入 | Coplay Clients + Coder auto-config | SC9：干净机器 ≤5 分钟零手改路径 |
| **T — 信任面** | 首连审批（定：**阻塞式，一次批准后记住**）；per-tool 开关；本地调用审计日志 | Unity 官方模式 + Ivan UserSettings | 首连弹窗可拒；禁用工具确不出现在 tools/list |

### 悬而未决 5 问的定案建议（我的意见，可被推翻）

1. **PNG 默认**，JPEG 仅显式 opt-in——UI 文字准确性 > 体积（精准第一）。
2. 首连**阻塞式审批**，批准后持久记住——对齐官方信任门槛，只花首五分钟里的一次点击。
3. Doggy 暂**并入 Cursor/Codex 兼容矩阵**，AC-V6 验证时补一条 Doggy 实测即可。
4. reload 期间**立即返回 busy+retry**，不排队——可预期失败 > 静默等待（PR-5）。
5. 多实例**降 P2**——solo 单 Editor 为主，官方已覆盖 Unity 6；仅在桥握手预留寻址字段（O2 顺带）。

---

## 5. License 卫生

| 上游 | License | 义务 |
|------|---------|------|
| CoplayDev/unity-mcp | MIT | 带署名改编文件头注明来源 + NOTICE |
| IvanMurzak/Unity-MCP | Apache-2.0 | 优先算法移植；逐行改编须保留 Apache 头 + NOTICE 条目 |
| CoderGamester/mcp-unity | MIT | 同 Coplay |
| MCP C# SDK | MIT | NuGet 依赖，正常引用 |
| Unity 官方 | 专有 | **只抄交互模式，禁止碰实现** |

每个移植 PR 必须在 `THIRD_PARTY.md` 增加一行：来源文件 → 我们文件 → 抄法。

---

## 6. 与既有文档的关系

- 优先级锁与验收：[`product-ux-frontier.md`](product-ux-frontier.md)（§0、AC-V、PR-*）
- 需求：[`brainstorms/2026-08-03-ux-frontier-addendum.md`](brainstorms/2026-08-03-ux-frontier-addendum.md)（FR-V/I/R/T）+ 本文 FR-A1–A9（AI-first 新增）
- 债务台账：[`audit.md`](audit.md)（VISION-*；关闭须两处同时满足）
- 本文是**执行时的唯一抄点清单**：执行者不得脱离本表自行决定"抄哪/怎么抄"；发现上游路径变动，先更新本表再动手。
