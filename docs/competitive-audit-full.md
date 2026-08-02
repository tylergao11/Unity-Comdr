# Unity-Comdr 竞品对齐审计（完整版）

**日期：** 2026-08-02  
**版本：** Unity-Comdr 0.2.0  
**审计范围：** 能力面、协议面、安装体验、Token 策略、安全默认、可扩展性、测试与文档  
**对照对象：**

| 项目 | 代表星数（审计时点量级） | 协议 / 安装特征 |
|------|--------------------------|-----------------|
| [CoplayDev/unity-mcp](https://github.com/CoplayDev/unity-mcp) | ~13k★ | MIT；UPM + **Python/uv** bridge；~47 focused tools；Tool Groups；多实例；Roslyn；远程 auth |
| [IvanMurzak/Unity-MCP](https://github.com/IvanMurzak/Unity-MCP) | ~3.8k★ | Apache-2.0；UPM + CLI + **可选云**；70+ tools；Skills 扩展包；Reflection；Profiler；Screenshot；**Runtime** |
| [CoderGamester/mcp-unity](https://github.com/CoderGamester/mcp-unity) | ~1.9k★ | MIT；UPM + **Node** WebSocket；细粒度 tools；**Resources + Prompts**；batch_execute；menu_item；Play Mode |
| 高工具数系（AnkleBreaker 等） | 数百★ | 200+ tools 默认清单冲击力强，Token 压力大 |
| Unity 官方 MCP（AI Assistant） | 官方 | 需 Unity 6 + Cloud + AI 订阅/试用 |

**审计方法：**

1. 对照各项目公开 README / tool catalog / 社区共识能力点。  
2. 逐项映射到 Unity-Comdr **已实现代码路径**（非口号）。  
3. 标注状态：`已对齐` / `部分对齐` / `有意不做（MVP）` / `P1/P2 缺口`。  
4. 对「部分对齐」写明差距与收敛路径。

---

## 1. 产品定位对照

| 维度 | Coplay | IvanMurzak | CoderGamester | **Unity-Comdr（目标与现状）** |
|------|--------|------------|---------------|-------------------------------|
| 用户 | 广泛社区默认 | 进阶 / 扩展 / Runtime | IDE 工程化 | **Solo 独立开发者** |
| 成本优先 | 能力与生态 | 功能深度 | 文档与协作配置 | **#1 LLM Token；#2 免订阅；#3 零 Python/Node** |
| 工具暴露 | ~47 入口 + groups | 70+ 常驻 + 扩展包 | 细粒度多 tools | **默认 ≤15 core + 按需 Skills（9 包）** |
| 安装 | UPM + Python | UPM/CLI + 可选云 | UPM + Node | **UPM + 纯 C# host（dotnet exec / 可 publish 自包含）** |
| 云 | 可选远程 auth | 云 pin / login 路径存在 | 本地为主 | **强制无云** |

**结论：** Unity-Comdr 不是「星数最高的 monorepo 克隆」，而是 **token-frugal 的集大成产品壳**：在默认上下文保持小，在 Skills / Resources / Prompts 中展开竞品优势能力。

---

## 2. 安装与客户端体验审计

### 2.1 Coplay 优势

- 一键 `Configure All Detected Clients`  
- 文档 / Wiki / Discord 成熟  
- OpenUPM / git URL 清晰  
- 多 Unity 实例路由指南  

### 2.2 IvanMurzak 优势

- `unity-mcp-cli` 安装插件、生成 skills  
- Docker / stdio / streamableHttp  
- Auto-generate skills  

### 2.3 CoderGamester 优势

- Editor 内 Global vs Project 配置  
- 相对路径 `.mcp.json` 可提交  
- 中日英文档  

### 2.4 Unity-Comdr 现状

| 能力 | 状态 | 代码 / 文档位置 |
|------|------|-----------------|
| UPM `package.json` | 已对齐 | `packages/com.unitycomdr.mcp/package.json` |
| 无 Python/Node 用户依赖 | **已对齐（相对 Coplay/Coder 的差异化优势）** | `src/UnityComdr.McpHost`；`docs/spike-transport.md` |
| 无强制云登录 | **已对齐（相对 Ivan 云路径的差异化）** | host 无 phone-home |
| Editor 窗口状态 + 复制配置 | 部分对齐 | `UnityComdrWindow.cs`（Cursor/Claude 片段） |
| 一键配置全部客户端 | **部分对齐 → 本次完善目标** | 窗口扩展多客户端 JSON 模板 |
| 多实例路由 | P1 缺口 | 计划中；host 可多开进程但未做实例发现协议 |
| OpenUPM 发布 | P1 | 本地 file: / git path 可用 |
| CLI 安装器 | P2 | 非 MVP |

---

## 3. 协议与架构审计

### 3.1 竞品架构摘要

| 项目 | Transport | 工具注册 | 扩展 |
|------|-----------|----------|------|
| Coplay | HTTP/stdio + Python server | 集中 tools + groups | 自定义 tools |
| IvanMurzak | stdio / streamableHttp + 插件 | 属性标注 AiTool | 扩展包 / Runtime |
| CoderGamester | Node MCP + Unity WebSocket | TS tools + C# handlers | 自定义 tool base |

### 3.2 Unity-Comdr 架构（已实现）

```
MCP Client
  │  newline JSON-RPC (stdio)
  ▼
UnityComdr.McpHost.McpServer
  methods: initialize, tools/*, resources/*, prompts/*, ping
  ▼
ComdrRuntime
  ├── ToolRegistry (core + loaded skills + gated escape)
  ├── ResourceCatalog (unity://…)
  ├── PromptCatalog (guided workflows)
  └── IEditorHost
        ├── InMemoryEditorHost (CI / headless / 默认 host)
        └── UnityEditorHost (UPM live — stub → P1 完整桥)
```

| 协议能力 | Coplay | Ivan | Coder | Comdr |
|----------|--------|------|-------|-------|
| tools/list + tools/call | ✓ | ✓ | ✓ | **✓** |
| resources/* | 部分 | 部分 | **强** | **✓（本次完善）** |
| prompts/* | 弱 | 有 | **强** | **✓（本次完善）** |
| 无外部 runtime | ✗(py) | 部分 | ✗(node) | **✓** |
| 可单测同一注册路径 | 中 | 中 | 中 | **强（IEditorHost）** |

---

## 4. 能力面逐项矩阵（核心）

图例：  
- **C** = CoplayDev  
- **I** = IvanMurzak  
- **G** = CoderGamester  
- **U** = Unity-Comdr  

状态：✅ 已对齐 · 🟨 部分 · ⬜ 有意延后 · ❌ 缺失需补  

### 4.1 Console / 代码修复闭环

| 能力 | C | I | G | U | 实现说明 |
|------|---|---|---|---|----------|
| 读日志 | ✓ | ✓ | ✓ | ✅ | `console_read` 过滤 type/contains + 分页 |
| 清日志 | ✓ | ✓ | ✓ | ✅ | `console_clear` |
| 读脚本 | ✓ | ✓ | ✓ | ✅ | `script_read` |
| 写/创建脚本 | ✓ | ✓ | ✓ | ✅ | `script_write` |
| 删脚本 | ✓ | ✓ | 部分 | ✅ | `script_delete` |
| 列脚本 | 部分 | 部分 | 部分 | ✅ | `script_list` 分页 |
| 编译/状态 | ✓ | ✓ | ✓ | ✅ | `editor_compile` / `editor_state` |
| Roslyn 语法校验 | ✓ | 动态执行 | — | 🟨 | escape `execute_code` 仅 dry-run；真 Roslyn 校验 P1 |
| 闭环测试 | — | — | — | ✅ | `P0HandlerTests.Script_write_read_compile_console_roundtrip` |

### 4.2 Scene / Hierarchy

| 能力 | C | I | G | U | 实现说明 |
|------|---|---|---|---|----------|
| get / list / create / open / save | ✓ | ✓ | ✓ | ✅ | `scene_manage` |
| list opened | — | ✓ | 部分 | ✅ | `list_opened` |
| unload | — | ✓ | ✓ | ✅ | `unload` |
| set active | — | ✓ | — | ✅ | `set_active` |
| additive open | — | 部分 | ✓ | ✅ | `open` + `additive` |
| 紧凑 hierarchy | 中 | 中 | resource | ✅ | `hierarchy_get` + digDeeper 指向真实 tool |
| **场景隔离 GO** | ✓ | ✓ | ✓ | ✅ | 按 scene bag；回归测试覆盖 |

### 4.3 GameObject / Component

| 能力 | C | I | G | U | 实现说明 |
|------|---|---|---|---|----------|
| CRUD + parent + transform | ✓ | ✓ | ✓ | ✅ | `gameobject_manage` |
| 部分 transform（不全量清零） | 看实现 | 看实现 | 看实现 | ✅ | `ReadPartialVec` + merge |
| tag / layer | ✓ | ✓ | ✓ | ✅ | `set_tag` / `set_layer` |
| find by name/tag/component | ✓ | ✓ | 部分 | ✅ | `find` |
| primitive create | ✓ | ✓ | menu | ✅ | `primitive=Cube…` |
| component add/get/modify/remove | ✓ | ✓ | ✓ | ✅ | `component_manage` |
| list component types | — | ✓ | — | ✅ | `list_types` |
| batch duplicate N | 部分 | 部分 | batch | 🟨 | skill `prefab-advanced` + `batch` |

### 4.4 Assets / Materials / Prefabs

| 能力 | C | I | G | U | 实现说明 |
|------|---|---|---|---|----------|
| find assets | ✓ | ✓ | ✓ | ✅ | `assets_manage find` 分页 |
| material create/assign | ✓ | ✓ | ✓ | ✅ | |
| prefab create/instantiate | ✓ | ✓ | ✓ | ✅ | |
| create folder | 部分 | ✓ | 部分 | ✅ | `create_folder` |
| delete / copy / move | 部分 | ✓ | 部分 | ✅ | |
| refresh AssetDatabase | 部分 | ✓ | — | ✅ | `refresh` |
| list shaders | — | ✓ | — | ✅ | `list_shaders` |
| prefab open/close/save mode | — | ✓ | — | ⬜ | P1 live Editor |
| Shader Graph / Amplify 专用 | 部分 | 扩展 | — | ⬜ | 非目标（技能可后加） |

### 4.5 Play Mode / Selection / Menu

| 能力 | C | I | G | U | 实现说明 |
|------|---|---|---|---|----------|
| play/pause/stop/step | ✓ | ✓ | ✓ | ✅ | skill `playmode` → `playmode_control` |
| selection get/set | 部分 | ✓ | 部分 | ✅ | skill `selection` |
| execute_menu_item | 部分 | 部分 | ✓ | ✅ | skill `menu` |
| recompile | ✓ | ✓ | ✓ | ✅ | core `editor_compile` |

### 4.6 Package Manager

| 能力 | C | I | G | U | 实现说明 |
|------|---|---|---|---|----------|
| list / add / remove / search | 部分 | ✓ | add | ✅ | skill `packages` → `package_manage` |

### 4.7 Testing

| 能力 | C | I | G | U | 实现说明 |
|------|---|---|---|---|----------|
| run tests | ✓ | ✓ | ✓ | 🟨 | skill `testing` 头less 逻辑测试；真 Test Runner 需 live Editor |
| list tests | 部分 | 部分 | resource | 🟨 | `tests_list` 目录 |

### 4.8 Screenshots / Profiler

| 能力 | C | I | G | U | 实现说明 |
|------|---|---|---|---|----------|
| camera/game/scene/isolated | 部分 | ✓ | — | 🟨 | skill `screenshots`；headless 为 marker，live 需 Unity 桥。**偷懒债 `VISION-MCP-IMAGE`：** 现回 `type:text`+base64，未升格 MCP `type:image`，不得宣称 agent「真看见 UI」——见 `docs/audit.md` |
| profiler start/stop/stats/save | 部分 | ✓ | — | 🟨 | skill `profiling`；headless 合成快照 |

### 4.9 Batch / 反射 / 动态执行

| 能力 | C | I | G | U | 实现说明 |
|------|---|---|---|---|----------|
| batch_execute | 部分 | 部分 | ✓ | ✅ | skill `batch` |
| reflection find/call | 部分 | ✓ | — | 🟨 | escape `reflect_call` dry-run；默认关闭 |
| execute_code / Roslyn | 部分 | ✓ | — | 🟨 | escape `execute_code` dry-run；默认关闭 |
| type json schema | — | ✓ | — | ⬜ | P1 |

### 4.10 Resources / Prompts

| 能力 | C | I | G | U | 实现说明 |
|------|---|---|---|---|----------|
| unity://hierarchy/logs/packages/assets | 部分 | 部分 | ✓ | ✅ | `ResourceCatalog` + MCP `resources/*` |
| Guided prompts | 弱 | 有 | ✓ | ✅ | code_fix / scene_build / playmode_verify / skill_expansion |
| Skill catalog resource | groups | skills | — | ✅ | `unity://skills` |

### 4.11 Runtime / 云 / 多实例

| 能力 | C | I | G | U | 实现说明 |
|------|---|---|---|---|----------|
| 游戏内 Runtime MCP | — | ✓ | — | ⬜ **P2 有意延后** | 需求文档锁定 |
| 强制云账号 | — | 路径存在 | — | ✅ 明确不做 | |
| 多实例路由 | ✓ | 部分 | 部分 | ⬜ P1 | |
| 远程 HTTP auth | ✓ | ✓ | 远程 WS | ⬜ 可选未来 | 默认 stdio 本地 |

---

## 5. Token 策略对比（Unity-Comdr 核心优势）

| 策略 | Coplay | Ivan（默认） | Coder | AnkleBreaker 系 | **Comdr** |
|------|--------|--------------|-------|-----------------|-----------|
| 默认 tools 数量 | ~47 | 70+ | 数十 | 200+ | **≤15** |
| 分组/按需 | Tool Groups | 扩展包 / Skills | 全量偏多 | 可关组 | **Skills load/unload** |
| 结果压缩 | 中 | 中 | 中 | 弱 | **分页 + hierarchy 截断 + digDeeper** |
| 危险能力默认 | 看版本 | 强能力多 | 中 | 中 | **escape 默认关** |

**验证：**

- `SkillAndToolCatalogTests.Default_session_core_tool_count_within_budget`  
- 进程 `tools/list` 固定 15 个 core 名  

**默认 core 清单（完整）：**

1. `console_read`  
2. `console_clear`  
3. `script_read`  
4. `script_write`  
5. `script_delete`  
6. `script_list`  
7. `editor_state`  
8. `editor_compile`  
9. `scene_manage`  
10. `hierarchy_get`  
11. `gameobject_manage`  
12. `component_manage`  
13. `assets_manage`  
14. `skill_manage`  
15. `escape_hatches_set`  

**Skills 清单（完整，默认不进 schema）：**

| Skill id | 工具 | 对齐来源 |
|----------|------|----------|
| `testing` | `tests_run`, `tests_list` | Ivan / Coder |
| `prefab-advanced` | `prefab_batch_instantiate`, `prefab_list` | Ivan / 高工具数系 |
| `playmode` | `playmode_control` | Coder / Ivan |
| `selection` | `selection_manage` | Ivan |
| `packages` | `package_manage` | Ivan / Coder |
| `menu` | `menu_manage` | Coder `execute_menu_item` |
| `profiling` | `profiler_manage` | Ivan profiler 套件 |
| `screenshots` | `screenshot_capture` | Ivan screenshot 套件 |
| `batch` | `batch_execute` | Coder batch_execute |

---

## 6. 安全与隐私审计

| 检查项 | 结果 |
|--------|------|
| 强制云 / 账号 | 无 |
| 默认 phone-home | 无（审计 grep 无运行时遥测 URL） |
| 密钥硬编码 | 无 |
| 任意代码执行默认 | **关闭**（`escape_hatches_set`） |
| 反射调用默认 | **关闭** |
| 破坏性操作 | delete 需明确 path；batch 默认真 stopOnError |
| 第三方归属 | `NOTICE` + `THIRD_PARTY.md` |

**剩余风险（诚实）：**

1. Live Unity 桥未完整时，生产依赖 `InMemoryEditorHost` 语义近似而非 100% Editor API。  
2. headless screenshot/profiler 为可测试合成数据，不能替代真机帧。  
3. `ExecuteMenuItem` 对未知菜单仍返回成功并打 warning（便于扩展，live 层应改为真实 Menu.ExecuteMenuItem 结果）。  

---

## 7. 测试与验证审计

| 套件 | 覆盖 |
|------|------|
| `SkillAndToolCatalogTests` | 预算、load/unload、escape 门控 |
| `P0HandlerTests` | console/script/scene/GO/material/prefab/分页 |
| `BugFixRegressionTests` | 场景隔离、partial transform、digDeeper 文案、skill 错误文案 |
| `McpProtocolTests` | initialize / tools/list / tools/call |
| `McpHostProcessTests` | **真实 host 进程双启动** |
| `ParityAndDomainSkillTests` | domain skills、扩展 core actions、resources/prompts |

命令：

```bash
dotnet test UnityComdr.sln -c Release
```

---

## 8. 与「集大成」路线的符合度评分（主观但可复核）

评分 1–5，5 = 已达可对外宣称的对齐质量。

| 竞品优势簇 | 评分 | 说明 |
|------------|------|------|
| Coplay：社区默认安装心智 | 3 | 无 py 更好，但缺一键全客户端与生态星数 |
| Coplay：Tool Groups | 5 | Skills 等价且更 token 友好 |
| Coplay：多实例 | 2 | P1 |
| Coplay：Roslyn 深校验 | 2 | escape dry-run only |
| Ivan：工具广度 | 4 | Skills 覆盖主簇；ShaderGraph 等未做 |
| Ivan：Profiler/Screenshot | 3 | 协议与 skill 齐，live 像素/真 Profiler P1 |
| Ivan：Reflection/Runtime | 2 / 1 | escape 预留；Runtime P2 |
| Ivan：Package Manager | 4 | skill 完整 list/add/remove/search |
| Coder：Resources/Prompts | 5 | 已实现 MCP 标准 methods |
| Coder：menu + batch + playmode | 5 | skills |
| Coder：Node 依赖 | n/a | Comdr **刻意不跟** |
| 官方 Unity MCP | 2 | 可并存，不绑定订阅 |
| **Token 默认成本** | **5** | 核心差异化 |
| **无 Python/Node** | **5** | 核心差异化 |

**综合：** MVP 在「Solo + 低 token + 本地」定位上已形成清晰优势；在「Live Unity 像素级保真 / 多实例 / Runtime」上仍诚实标注为下一阶段。

---

## 9. 代码结构审计（清晰度）

```
packages/com.unitycomdr.mcp/     # UPM 安装面
src/UnityComdr.Core/
  Bootstrap/                     # 组合根
  Editor/                        # IEditorHost + InMemory
  Models/                        # 数据模型
  Tools/                         # Core tools + registry
  Skills/                        # Sample + Domain skills
  Mcp/                           # Resources + Prompts
  Util/                          # CompactResults
src/UnityComdr.McpHost/          # 协议进程
tests/UnityComdr.Tests/          # 真实入口测试
docs/                            # 需求 / spike / 架构 / 本审计
```

原则：

- **协议与 Editor 解耦** → 可测、可 headless。  
- **能力扩张优先进 Skills** → 不破坏 core 预算。  
- **竞品对齐用「动作丰富的少数 tool」** 而非 200 个常驻 tool。  

---

## 10. 差距收敛清单（完善路线）

### 已在本轮完善

- [x] 扩展 `IEditorHost` 覆盖 packages/selection/playmode/menu/profiler/screenshot/asset folder ops  
- [x] core：`scene_manage` / `gameobject_manage` / `assets_manage` / `component_manage` 动作对齐 Ivan/Coder  
- [x] 9 个 domain skills + CatalogMeta 溯源  
- [x] MCP `resources/*` + `prompts/*`  
- [x] 竞品矩阵文档（本文）  
- [x] 场景隔离 / partial transform / 错误文案等回归  

### P1（Live Editor 保真）

- [ ] 真实 `UnityEditorHost`：Console、AssetDatabase、SceneManager、TestRunner、Menu.ExecuteMenuItem  
- [ ] 真 PNG screenshot / Profiler 窗口数据  
- [ ] 多实例发现与路由  
- [ ] 一键写入 Cursor/Claude/VS Code/Windsurf/Codex 配置文件  
- [ ] Roslyn 语法诊断接入 `script_write`  

### P2

- [ ] Runtime in-game MCP  
- [ ] 可选自托管 HTTP 中继（仍非强制）  
- [ ] 社区 skill 包格式  
- [ ] Prefab Mode open/close/save  

### 明确不跟风

- 默认 200+ tools 全开  
- 强制云账号  
- 依赖用户安装 Python/Node  

---

## 11. 审计结论（不省略）

1. **Unity-Comdr 已具备可运行的集大成骨架**：stdio MCP、15 core、9 skills、resources、prompts、安全门控、完整自动化测试。  
2. **相对 Coplay**：在「免 Python、默认 token」上更好；在「生态成熟度、多实例、Roslyn 深校验」上仍弱。  
3. **相对 IvanMurzak**：在「Package/PlayMode/Profiler/Screenshot/扩展」上已用 Skills 对齐主簇；Runtime 与反射深度仍弱；无云更贴 Solo。  
4. **相对 CoderGamester**：Resources/Prompts/menu/batch/playmode 已对齐；Node 依赖被消除。  
5. **相对高工具数 fork**：用 skill 目录保留「功能全面」叙事，避免默认 schema 爆炸。  
6. **最大诚实缺口**仍是 **Live Unity Editor 桥**：没有它，一切 headless 能力是「语义正确的仿真」；有了它才是「真 Editor 自动化」。  
7. **下一步最高杠杆**：实现 `UnityEditorHost` 并把 UPM 窗口升级为真·Configure All Clients。

---

## 12. 引用与归属

实现为原创 C# 组装；能力设计参考：

- CoplayDev/unity-mcp (MIT)  
- IvanMurzak/Unity-MCP (Apache-2.0)  
- CoderGamester/mcp-unity (MIT)  

详见仓库根目录 `NOTICE`、`THIRD_PARTY.md`。
