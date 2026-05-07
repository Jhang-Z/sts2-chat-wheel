# StS2 Chat Wheel — 视觉设计规范

参考自《Slay the Spire 2》本体的视觉语言（牛皮纸 + 木纹 + 金边的奇幻冒险风格），目的是让 mod 的 UI 与游戏本体融合，不显得格格不入。

颜色值来自 **`MegaCrit.Sts2.Core.Helpers.StsColors`**（运行时反射读出，权威来源）。

---

## 0. 重要：StS2 有两套色板

游戏本体根据**场景类型**用不同色板，mod 也照办：

| 场景 | 色板 | 应用 |
|---|---|---|
| **战斗 / 地下城 / 卡牌** | **暖棕黑**（path/wood）| 卡牌弹窗、宝箱、地图节点 |
| **菜单 / 设置 / 弹窗** | **冷海军蓝 + 青色高亮** | 设置页、暂停菜单、确认对话框 |

mod 的对应：
- **轮盘 (`WheelUI`) / 气泡 (`BubbleOverlay`)**：战斗场景叠加 → 用 **暖棕** 色板（`PanelBg/PanelBgAlt/PanelBgHover`）
- **设置页 (`SettingsScreen`)**：菜单语义 → 用 **冷蓝** 色板（`MenuBg/MenuBgAlt/MenuBgHover`）

菜单的关键标志色：

| Token | 值 | 来源 |
|---|---|---|
| `MenuBg` | `#0F1A2A` | 深海军蓝（手调）|
| `MenuBgSlate` | `#2A3848` | tab 石板灰 |
| `MenuAccentCyan` | `#B1F8FF` | **`StsColors.settingTabsButtonOutline` 原值**——active tab 描边的青白光 |
| `Gold` | `#EFC851` | ◀▶ 三角、勾选、装饰宝石（保持 gold）|

---

## 1. 调色板（Color Tokens）

### 1.1 主色（语义色）

| Token | 十六进制 | 用途 |
|---|---|---|
| `cream` | `#FFF6E2` | **主文本色** — 标题、正文 |
| `gold` | `#EFC851` | **强调色** — 标题点缀、选中边框、标签银 |
| `aqua` | `#2AEBBE` | 互动元素 / 能量 / 链接 |
| `screenBackdrop` | `#000000CC` | 模态遮罩（80% 黑） |
| `pathDotTraveled` | `#241F1A` | 深棕，面板底色 |
| `bossNodeUntraveled` | `#7D6A55D8` | 木棕，次级面板底色 |
| `legendText` | `#2B3152` | 深海军蓝，备用文本底 |

### 1.2 状态色

| Token | 十六进制 | 用途 |
|---|---|---|
| `red` | `#FF5555` | 危险 / 错误 |
| `orange` | `#FFA518` | 警告 |
| `green` | `#7FFF00` | 成功（用得少，建议用 aqua 替代） |
| `purple` | `#EE82EE` | 稀有 / 特殊 |
| `pink` | `#FF78A0` | 罕见 |

### 1.3 透明度阶梯

| Token | 透明度 |
|---|---|
| `transparentBlack` | 0% |
| `quarterTransparentBlack` | 25% |
| `halfTransparentBlack` | 50% |
| `ninetyPercentBlack` | 90% |
| `screenBackdrop` | 80% |
| `transparentWhite` | 0% |
| `quarterTransparentWhite` | 25% |
| `halfTransparentWhite` | 50% |

### 1.4 灰阶

| Token | 十六进制 | 用途 |
|---|---|---|
| `lightGray` | `#BFBFBF` | 次级文本 |
| `gray` | `#7F7F7F` | 禁用文本 |
| `disabledTextForPotionPopup` | `#5E5E5E` | 禁用提示 |
| `exhaustGray` | `#191919` | 几乎黑（消耗效果） |

### 1.5 卡牌稀有度（用作分类色板灵感）

| 稀有度 | 描边色 |
|---|---|
| 普通 | `#4D4B40` 棕 |
| 罕见 | `#005C75` 深青 |
| 稀有 | `#6B4B00` 深金 |
| 诅咒 | `#550B9E` 紫 |
| 任务 | `#7E3E15` 橙棕 |
| 状态 | `#4F522F` 橄榄绿 |
| 特殊 | `#1B6131` 深绿 |

> 我们将这套色板**借用作语音情感色**：开心=金、愤怒=红、悲伤=深蓝、委屈=紫、正常=cream

---

## 2. 字体与字号

| 角色 | 大小 | 颜色 | 备注 |
|---|---|---|---|
| H1（页面标题） | 24-28 | `cream` + `gold` 描边 | 模态对话框最上方 |
| H2（区块标题） | 16-18 | `gold` | "当前轮盘"、"语音库" |
| Body | 14 | `cream` | 卡片/按钮内文字 |
| Caption | 12 | `lightGray` | 提示、副信息 |
| Tiny | 10-11 | `gray` | 极小辅助说明 |

> Godot 默认字体（系统字体）即可。如未来要更接近 StS2，可后续换 Mantinia/类似衬线字体。

**关键原则**：标题用 `gold`，正文用 `cream`，**绝不用纯白**——纯白显得 UI 冷且与游戏不搭。

---

## 3. 几何（Geometry）

| 项 | 值 | 说明 |
|---|---|---|
| 圆角（小） | 6px | 按钮、输入框 |
| 圆角（中） | 10px | 子面板（编辑器、卡片） |
| 圆角（大） | 14-16px | 主模态对话框 |
| 边框（普通） | 1px | 卡片、输入 |
| 边框（高亮） | 2-3px | 选中态 |
| 内边距（紧凑） | 8/4 | 行内按钮 |
| 内边距（标准） | 14/8 | 卡片内 |
| 内边距（宽松） | 24/16 | 模态主体 |
| 阴影 | `screenBackdrop` 18px ↓ | 主面板浮起感 |

---

## 4. 组件规范

### 4.1 模态对话框
- 全屏 `screenBackdrop` 黑遮罩
- 居中 Panel，宽 1080×720（chat-wheel 设置页规格）
- 底色：`pathDotTraveled` 加少量 `legendText` 提亮
- 边框：`gold` 2px
- 圆角 14px + 阴影 18px

### 4.2 按钮（标准）
- 底色 `pathDotTraveled` 微亮（如 `#221C2E`）
- 边框 `bossNodeUntraveled` 1px
- 文字 `cream` 14px
- Hover：底色变 `bossNodeUntraveled` 30% 亮、边框 `gold`
- 圆角 6px、内边距 12/6

### 4.3 按钮（主操作 / Save / Confirm）
- 底色 `red` 暗化（如 `#A4332C`）
- 边框 `gold`
- 文字 `cream`
- Hover：变亮 `#C84A40`

### 4.4 输入框
- 底色 `#11101A`（极暗紫黑）
- 边框 1px `bossNodeUntraveled`
- Focus：边框变 `gold`
- 文字 `cream`，placeholder `gray`
- 圆角 6px

### 4.5 卡片（Slot row、Library item）
- 底色：`pathDotTraveled` + 5% legendText（如 `#221C2E`）
- 边框：默认 `bossNodeUntraveled` 1px；选中 `gold` 2px
- 内边距 14/8
- 文字左对齐
- Hover：底色变亮一档（如 `#3A3052`）

### 4.6 Tab 标签
- 未激活：底色与卡片一致，边框 `bossNodeUntraveled`
- 激活：底色亮 `#3A3052`，**底边 2px gold 边框**（像便签贴上去）
- 文字：未激活 `cream`，激活 `#FFD76A`（gold 提亮）

### 4.7 状态指示
- 🔊（有语音）：`gold` 色文字图标，紧贴文本左侧
- 选中行：左侧 `gold` 高亮条 + 整行底色变亮

---

## 5. 文案与图标

### 5.1 文案风格
- 中文为主，简洁直接（"打精英怪！" 而非 "建议优先攻击精英敌人"）
- 操作动词放前面（"重置默认" "试听 #1"）
- 状态用括号或冒号（"装备中：#1 好牌！"）

### 5.2 图标策略
- **不用**精美 PNG 图标（mod 不能引用游戏内资源）
- 用 Unicode 字符代替：
  - `🔊` 有语音
  - `🎮` 操作
  - `⚠` 警告
  - `▶` 试听 / 播放
  - `▾` 下拉
  - `✕` 关闭
  - `①②③④⑤⑥⑦⑧` 圆圈数字
- 颜色一律用 token，禁止裸 hex

---

## 6. 动效（先简后繁）

| 动效 | 时长 | 缓动 |
|---|---|---|
| 模态淡入 | 150ms | ease-out |
| 模态淡出 | 250ms | ease-in |
| 按钮 hover | 即时 | — |
| 选中态切换 | 即时 | — |
| 试听/合成 loading | 转圈 | 持续 |

> 轮盘打开动画可后续考虑：**从中心 scale 0.8→1 + alpha 0→1**，250ms ease-out。

---

## 7. 反例（不要做）

- ❌ 用纯白 `#FFFFFF` 当主文本色（生硬、不和谐）
- ❌ 用饱和度极高的色（比如纯绿 `#00FF00`）
- ❌ 圆角 0 的硬直角（除非刻意模仿便签贴边）
- ❌ 多种字体混用
- ❌ 图标 + 文字 + 背景三种饱和色叠加（视觉乱）
- ❌ 透明度 < 30% 的元素当主信息（看不清）

---

## 8. 当前 mod 的颜色映射

把代码里现用的颜色 token 对齐到游戏本体：

| 旧（自定）| 新（StsColors）|
|---|---|
| `OverlayBg #00000099` | `screenBackdrop #000000CC` |
| `PanelBg #17141f` | 取 `#1A1410` 类 `pathDotTraveled` 微调 |
| `TitleColor #ece4d4` | `cream #FFF6E2` |
| `AccentOn #d4a937` | `gold #EFC851` |
| `BtnPrimaryBg #a4332c` | 保留（已接近 `red` 暗化） |
| `WarningColor #e88a4a` | `orange #FFA518` |
| `SubtleColor #8a8298` | `lightGray #BFBFBF` 或 `gray` |

代码里集中替换为新 token，所有按钮/卡片/标签自动看起来更"StS2"。
