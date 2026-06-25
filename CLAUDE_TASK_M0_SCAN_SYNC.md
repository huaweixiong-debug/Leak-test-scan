# Claude Code 任务单：扫码与 PLC M0 严格一致校验

## 目标

请你在 **C# 后端项目** 中审查并必要时修正“扫码状态 -> M0 输出”的整条链路，保证以下规则 **严格一致**：

1. 当前选中的产品，如果：
   - `scanConfirmEnabled = true`，或
   - `scanMatchEnabled = true`，或
   - `scanAutoStartEnabled = true`

   那么 **没有有效扫码时，PLC 的 `M0` 必须为 `OFF`**。

2. 当前选中的产品，如果：
   - `scanConfirmEnabled = false`
   - `scanMatchEnabled = false`
   - `scanAutoStartEnabled = false`

   那么这是 **无码产品**，**`M0` 必须为 `ON`**。

3. 保存产品设置后，如果当前选中的产品规则发生变化，**必须立即重算 `M0`**，不能等页面刷新或重新选产品。

4. 如果扫码被消费、清空、失效，`M0` 也必须立即按当前产品规则重新计算。

## 当前已知状态

远端电脑当前已验证：

- `http://127.0.0.1:3000/api/health` 正常
- `http://127.0.0.1:3000/api/plc/read-map` 中 `M0` 当前返回 `False`
- 当前产品已勾选“扫码记录”，且 `latestScan = null` 时，`M0 = OFF` 已经成立

也就是说：

- **“严格一致”主逻辑大概率已经修过**
- 但请你不要假设已经完全正确，仍需做一次完整后端审查和必要补强

## 请先阅读的文件

重点检查这些文件：

- `src/ATEQ.LeakTest.Web/Services/TestWorkflowService.cs`
- `src/ATEQ.LeakTest.Web/Controllers/SettingsController.cs`
- `src/ATEQ.LeakTest.Web/Services/ScannerService.cs`
- `src/ATEQ.LeakTest.Web/Services/PlcCoordinatorService.cs`
- `src/ATEQ.LeakTest.Web/Services/PlcService.cs`
- `src/ATEQ.LeakTest.Web/wwwroot/index.html`

如果前端镜像文件存在并依赖同步，也同步检查：

- `public/index.html`

## 重点审查点

### 1. 当前选中产品是否会使用旧快照

检查 `TestWorkflowService` 是否存在这种问题：

- 系统设置里产品规则已经改了
- 但 `_selectedContext.ProductProfile` 或 `_pendingContext.ProductProfile` 仍然保留旧值
- 导致 `M0` 重算时读到的是旧规则

如果有这个问题，必须修成：

- `ReapplyM0ForCurrentSelectionAsync()` 在计算前使用数据库中的**最新产品配置**
- 不允许继续依赖过期快照

### 2. 保存产品设置后是否会立刻重算 M0

检查 `POST /api/settings/products` 保存完成后，是否立即触发当前选中产品的 `M0` 重算。

如果没有，补上。

### 3. 扫码清空后是否会立刻重算 M0

检查扫码被消费、测试完成、扫码上下文被清空后，是否重新执行 `M0` 计算。

### 4. 手动切换产品时是否会立刻重算 M0

检查 `/api/test/context` 对应的上下文同步逻辑。

要求：

- 切换到无码产品：`M0 = ON`
- 切换到有码产品且没有有效扫码：`M0 = OFF`

### 5. 前端是否错误跳过了上下文同步

检查主测试页是否存在“因为没扫码，所以不调用 `/api/test/context`”之类的老逻辑。

要求：

- 只要用户选择了产品和操作员，前端就应正常同步上下文
- 不要因为“当前没有扫码”而跳过同步

## 允许的修改范围

优先只做 **最小必要修改**，不要顺手做无关重构。

允许修改：

- `src/ATEQ.LeakTest.Web/Services/TestWorkflowService.cs`
- `src/ATEQ.LeakTest.Web/Controllers/SettingsController.cs`
- `src/ATEQ.LeakTest.Web/Services/ScannerService.cs`
- `src/ATEQ.LeakTest.Web/Services/PlcCoordinatorService.cs`
- `src/ATEQ.LeakTest.Web/wwwroot/index.html`
- `public/index.html`（如果需要镜像同步）

## 不要改的内容

- 不要改业务无关 UI
- 不要改 PLC 地址定义
- 不要改 ATEQ 串口配置逻辑
- 不要把这次任务扩展成“大重构”

## 实施要求

1. 先给出一段简短审查结论：
   - 当前问题是否已基本修复
   - 是否还存在边缘不一致点

2. 如果发现风险，做最小修复。

3. 完成后执行最小验证：

```powershell
Invoke-RestMethod http://127.0.0.1:3000/api/scanner/latest | ConvertTo-Json -Depth 6
Invoke-RestMethod http://127.0.0.1:3000/api/settings/products | ConvertTo-Json -Depth 6
(Invoke-RestMethod http://127.0.0.1:3000/api/plc/read-map).data.coils | Where-Object { $_.label -eq 'M0' } | Format-List
```

4. 如果你修改了代码，还要给出远端重启步骤：

```powershell
Set-Location D:\ateq
.\stop-server.cmd
dotnet build .\ATEQ.LeakTest.sln -c Release
powershell -ExecutionPolicy Bypass -File .\start-server-bg.ps1 -NoSchtask
```

## 验收标准

必须满足以下场景：

### 场景 A：有码产品，未扫码

- 产品任一扫码开关为 `true`
- `latestScan = null`
- `M0 = OFF`

### 场景 B：无码产品

- 三个扫码开关全为 `false`
- 无论 `latestScan` 是否为空
- `M0 = ON`

### 场景 C：保存设置即时生效

- 当前选中产品从无码改成有码
- 保存后无需刷新页面
- `M0` 立即从 `ON` 变 `OFF`

### 场景 D：扫码消费后回退

- 有码产品扫码后满足启动条件
- 测试结束或扫码被消费后
- 如果当前不再有有效扫码，`M0` 应回到 `OFF`

## 输出格式

请按下面结构汇报：

### 1. 审查结论

- 当前逻辑哪些已经正确
- 发现了哪些残留问题

### 2. 修改内容

- 改了哪些文件
- 每个文件改了什么

### 3. 验证结果

- `latestScan`
- 当前产品扫码配置
- `M0` 结果
- 是否满足场景 A / B / C / D

### 4. 需要我继续验证的点

- 如果有任何你无法在本机确认、必须依赖远端 PLC/扫码枪/仪器的点，请明确列出来

