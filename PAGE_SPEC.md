# ATEQ 泄漏测试页面 — 前端说明文档

## 文件位置

`ATEQ-Leak-Test/public/index.html` — 单文件，纯 HTML/CSS/JS，无框架。

## 运行时环境

- 工控机 IP: `100.95.136.69` (Tailscale)
- 后端: Node.js 18 + Express, 端口 3000
- 页面入口: `http://localhost:3000/` 或 `http://localhost:3000/index.html`
- 浏览器: 工控机上的 Chrome/Edge

---

## API 端点（全部 GET，除非标注）

| 端点 | 用途 | 频率 |
|------|------|------|
| `GET /api/status` | ATEQ 实时遥测 | 1s |
| `GET /api/scanner/latest` | 最新扫码结果 | 5s |
| `GET /api/tests/latest` | 最近测试记录列表 | 5s |
| `GET /api/settings/products` | 产品型号列表（含 fillTime/stabTime/testTime） | 页面加载时 |
| `GET /api/settings/operators` | 操作员列表 | 页面加载时 |
| `POST /api/start` | 启动测试 `{startMode, productModel, operatorName}` | 用户点击 |
| `POST /api/reset` | 复位 ATEQ | 用户点击 |
| `GET /api/test/active` | 当前测试的样点数据（150条，100ms粒度） | 200ms（测试期间） |

### `/api/status` 返回结构

```json
{
  "connected": true,
  "running": false,
  "currentStep": 65535,
  "currentJob": 1,
  "resultCode": "OK",
  "errorCode": null,
  "telemetry": {
    "stepCode": 65535,
    "pressure": 0,
    "pressureUnit": "Bar",
    "leak": 0.005,
    "leakUnit": "mL/min",
    "statusWord": 32800
  }
}
```

### `/api/test/active` 返回结构

```json
{
  "success": true,
  "activeTest": {
    "running": true,
    "stage": "monitoring",
    "samples": [
      {
        "sampledAt": "2026-06-03T12:00:00.000Z",
        "stepCode": 5,
        "pressure": 20.1,
        "pressureUnit": "Bar",
        "leak": 0,
        "leakUnit": "mL/min"
      }
    ],
    "matchedProduct": { "productModel": "HB11", "ateqProgramNo": 1 }
  }
}
```

### `/api/scanner/latest` 返回结构

```json
{
  "connected": true,
  "latestScan": {
    "rawText": "435200616419761",
    "scannedAt": "2026-06-03T12:00:00.000Z"
  }
}
```

### `/api/tests/latest` 返回结构

```json
{
  "success": true,
  "total": 30,
  "records": [
    {
      "sequenceCode": "0030",
      "productModel": "HB11",
      "resultCode": "OK",
      "testPressure": 20.07,
      "finalPressure": 0,
      "pressureUnit": "Bar",
      "finalLeak": 0.005,
      "leakUnit": "mL/min",
      "startedAt": "2026-06-03T12:00:00.000Z",
      "finishedAt": "2026-06-03T12:00:24.000Z"
    }
  ]
}
```

---

## 页面功能模块

### 1. 顶栏
- 页面标题 "ATEQ 泄漏测试"
- 产品型号下拉 `<select id="sel-product">` — 从 `/api/settings/products` 加载
- 操作员下拉 `<select id="sel-operator">` — 从 `/api/settings/operators` 加载
- 连接状态指示灯（绿/红圆点 + 文字）

### 2. 扫码区（左侧上）
- 扫码结果显示框，实时更新
- 去重逻辑：同一 rawText+scannedAt 不重复计数

### 3. 测试指标（左侧中）
- 两个大数字卡片：
  - **测试压力** — 显示当前压力值 + 单位（动态从 ATEQ 读取：Bar/kPa/...）
  - **最终漏率** — 显示当前漏率值 + 单位（动态：mL/min/Pa/...）

### 4. 图表区（左侧下）
- 压力曲线 + 漏率曲线并排（50%/50%）
- Canvas 绘制，深色背景 `#0f1419`
- 蓝色折线 + 半透明填充
- Y 轴自适应（最新值占 90% 高度）
- X 轴按程序时间参数缩放（fillTime+stabTime 用于压力，testTime 用于漏率）
- 测试期间 200ms 粒度（从 `/api/test/active` 读取样点）
- 压力曲线采集 Step 4+5 的数据，漏率曲线采集 Step 6 的数据

### 5. 测试流程（右侧上）
- 5 步流程卡片横排：`4 充气 → 5 稳压 → 6 排气/漏率 → 7 漏率 → OK? 结果`
- 每步显示当前值（压力/漏率）+ 单位
- 已完成步骤绿色，当前步骤蓝色高亮，未开始灰色
- Step 6 同时显示压力和漏率两行

### 6. 操作区（右侧中）
- **▶ 启动测试** 按钮 — POST `/api/start`
- **↺ 复位** 按钮 — POST `/api/reset`
- 实时显示：步进码、压力值、漏率值

### 7. 结果横幅
- 测试完成时显示 OK（绿色）或 NG（红色）
- 10 秒后自动消失
- 测试中断时显示诊断信息

### 8. 最近测试记录（底部）
- 表格：序号、产品、结果、压力、漏率、时间
- 最新在前
- 每 5 秒刷新

---

## 关键状态变量

```javascript
let connected = false;        // ATEQ 是否连接
let activeTest = false;       // 是否有正在运行的测试
let lastStep = null;          // 上次步进码（检测步进码变化）
let presHistory = [];         // 压力曲线数据 [{t: timestamp, v: value}]
let leakHistory = [];         // 漏率曲线数据
let presUnit = 'Bar';         // 压力单位（动态）
let leakUnit = 'mL/min';      // 漏率单位（动态）
let progParams = {            // 程序时间参数（秒）
  fillTime: {sec: 7},
  stabTime: {sec: 10.7},
  testTime: {sec: 5}
};
```

---

## 主要 JS 函数

| 函数 | 作用 |
|------|------|
| `pollStatus()` | 1s 轮询 `/api/status`，更新状态/流程/指标 |
| `pollSamples()` | 200ms 轮询 `/api/test/active`，更新图表数据 |
| `pollScan()` | 5s 轮询 `/api/scanner/latest`，更新扫码显示 |
| `pollRecords()` | 5s 轮询 `/api/tests/latest`，更新记录表 |
| `drawChart(id, data, unit, totalSec)` | Canvas 绘制曲线 |
| `updateFlowValue(step, p, l)` | 更新流程步骤卡片的值 |
| `updateFlowHighlight(step, rc)` | 更新流程步骤卡片的配色 |
| `doStart()` | 启动测试 |
| `doReset()` | 复位 ATEQ |
| `startFastPoll()` / `stopFastPoll()` | 启停 200ms 快速图表轮询 |
| `resetCharts()` | 清空图表 |
| `fetchParams()` | 加载产品时间参数 |

---

## CSS 设计系统

```css
:root {
  --bg: #0f1419;        /* 页面背景 */
  --panel: #1a2332;     /* 卡片背景 */
  --border: #2a3a50;    /* 边框 */
  --text: #e6edf3;      /* 主文字 */
  --muted: #8b9bb5;     /* 次要文字 */
  --accent: #58a6ff;    /* 强调色/按钮 */
  --good: #3fb950;      /* OK/成功 */
  --bad: #f85149;       /* NG/失败 */
  --warn: #d29922;      /* 警告 */
}
```

- 深色主题
- 字体: `"Segoe UI", "Noto Sans SC", sans-serif`
- 等宽字体: `Consolas, monospace`
- 圆角: 6-12px
- 网格布局: `grid-template-columns`

---

## 当前布局结构

```
┌─ topbar ──────────────────────────────────────┐
│ ATEQ 泄漏测试  [产品▼] [操作员▼]  ●已连接        │
├──────────────────────┬────────────────────────┤
│ 扫码面板              │ 测试流程 (5步横排)       │
│ 指标卡片 (压力/漏率)   │ ▶启动  ↺复位            │
│ 图表 (压力 | 漏率)     │ 步骤:xxx 压:xxx 漏:xxx  │
│                      │ 结果横幅 (OK/NG)        │
├──────────────────────┴────────────────────────┤
│ 最近测试记录表格                                │
└───────────────────────────────────────────────┘
```

---

## 已知问题 / 注意事项

1. systemat 代理 (`http_proxy=100.82.136.106:17890`) 会拦截浏览器 API 请求 → 需在工控机 localhost 打开或用代理例外
2. modbus 偶发重连（不关前端的事，是 ATEQ 串口稳定性问题）
3. `fetchParams()` 在产品加载前调用，需等产品列表返回后才能读到产品参数
4. 图表采样 200ms 来自 `/api/test/active`，而非直接 modbus 读取
5. MongoDB 样式系统中无 ID 前缀冲突的，所有元素用 `id` 匹配 JS

---

## 需要 Redesign 的方向

- 整体布局重新设计（目前两栏不够紧凑）
- 配色可调整
- 流程步骤可改用竖向时间线
- 图表可更大
- 扫码结果可更醒目
- 记录表可折叠
- 响应式适配 1366×768 工控机屏幕
