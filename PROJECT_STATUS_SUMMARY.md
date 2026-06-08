# ATEQ 泄漏测试系统项目状态总结

## 1. 项目概况

本项目是运行在 Windows 工控机上的 ATEQ 泄漏测试上位机，采用 `Node.js 18 + Express + 浏览器前端` 结构，负责：

- 与 ATEQ 仪器通过 `RS232 + Modbus` 通讯
- 与扫码枪通过 `RS232` 通讯
- 展示主测试页面、设置页面、查询页面、通讯配置页面
- 保存测试记录、曲线样本、产品配置、操作员配置

## 2. 当前运行环境

- 工控机：`100.95.136.69`
- 项目目录：`D:\ATEQ Test\ATEQ-Leak-Test`
- 浏览器访问：`http://127.0.0.1:3000/`
- Node 运行时：项目内置 `runtime18`
- 服务启动：`start-remote-server.cmd`
- 服务停止：`stop-remote-server.cmd`

## 3. 当前设备参数

### ATEQ

- COM：`COM7`
- 波特率：`9600`
- 数据位：`8`
- 校验：`Even`
- 停止位：`1`
- Slave ID：`255`

### 扫码枪

- COM：`COM1`
- 波特率：`115200`
- 数据位：`8`
- 校验：`None`
- 停止位：`1`
- 流控：`None`

## 4. 已完成内容

### 4.1 后端主链

以下核心模块已经落地并联通：

- `server.js`：API 路由与页面静态服务
- `modbusService.js`：ATEQ 串口连接、Modbus 读写、状态读取、程序时间读取
- `scannerService.js`：扫码枪串口接收与扫码缓存
- `testWorkflowService.js`：测试状态机、样本采集、结果保存
- `db.js`：本地数据存储、查询、导出、历史记录兼容处理

### 4.2 页面

#### 主测试页

`public/index.html` 已完成并可用，包含：

- 产品型号选择
- 操作员选择
- 程序号显示
- ATEQ 在线状态显示
- 扫码结果显示
- 实时压力
- 实时/最终漏率
- 测试流程显示
- 启动测试 / 复位 ATEQ
- 最终压力 / 最终漏率
- 今日总数 / 今日合格数
- 最近测试记录
- 双曲线显示

#### 查询页

`public/query.html` 已完成并可用，包含：

- 起止时间筛选
- 产品筛选
- 结果筛选
- 二维码筛选
- 失败原因筛选
- 分页查询
- CSV 导出
- 点击记录查看详情弹层
- 详情曲线回看

#### 设置页 / 通讯配置页

- `public/settings.html` 已有正式页面
- `public/comm-config.html` 已有正式页面

### 4.3 测试逻辑

当前测试链规则已经固定为：

- 真正进入 `stepCode = 4` 才算测试开始
- `stepCode = 6` 最后 1 秒取最终压力
- `stepCode = 65535` 取最终漏率
- 物理按钮启动与页面按钮启动共用同一套曲线 / 记录 / 结果流程
- 扫码结果在测试保存成功后会清空
- 扫码仅允许在允许扫码阶段接收

### 4.4 单位和结果

以下内容已经处理：

- 单位码映射
- 历史 `CODE_xxx` 单位兼容
- 高分辨率 / 低分辨率 / Cal / D 模式缩写统一
- 32 位有符号漏率解码修复
- OK / NG / UNKNOWN 判定修复
- 失败原因 `errorText` 输出到主页面、查询页、导出

## 5. program timings 读取校准结果

### 5.1 结论

`fill / stab / test` 时间已经校准为：

- 通过 ATEQ `parameter-identifiers` 方式读取
- 不再采用 `0x0400` 直读结果作为主来源

### 5.2 为什么这样定

我们对比了两类数据源：

#### A. 参数标识读取

通过：

- 先写 `0x3004` 选中程序
- 再向 `0x0000` 写参数标识列表
- 再从 `0x0000` 回读参数值

当前读取结果：

- 程序 1：`fill=8.0s` `stab=15.1s` `test=5.0s`
- 程序 2：`fill=79.9s` `stab=17.0s` `test=5.0s`
- 程序 3：`fill=3.0s` `stab=20.0s` `test=5.0s`

#### B. 真实历史样本阶段时长

从 `test_records.samples` 反推的阶段时长：

- 程序 1：`step4≈7.5s` `step5≈14.8s` `step6≈4.75s`
- 程序 2：`step4≈52.5s` `step5≈16.7s` `step6≈4.75s`
- 程序 3：`step4≈2.6s` `step5≈19.8s` `step6≈4.78s`

#### C. 对比结论

`parameter-identifiers` 读出来的结果，与真实样本阶段时长在量级和结构上是一致的；  
而 `0x0400` 直读结果会出现类似 `21.072 / 8.261 / 18.758s` 的异常值，并且不同程序可能重复出现，不可信。

因此当前代码已经调整为：

- 主读取来源：`parameter-identifiers`
- `0x0400` 直读仅保留为诊断兜底，不再作为页面主显示来源

## 6. 当前 API 状态

目前核心接口已经可用：

- `GET /api/health`
- `GET /api/status`
- `GET /api/program-timings`
- `POST /api/start`
- `POST /api/reset`
- `GET/POST /api/config/ateq`
- `GET/POST /api/config/scanner`
- `GET/POST /api/settings/products`
- `GET/POST /api/settings/operators`
- `GET /api/scanner/latest`
- `GET /api/tests/latest`
- `GET /api/tests/query`
- `GET /api/tests/export.csv`
- `GET /api/test/active`

## 7. 当前未完成项

### 7.1 ATEQ 设备侧不稳定因素

以下问题属于设备 / 现场状态层，代码侧已尽量兜底，但仍需现场继续观察：

- ATEQ 偶发 `offline`
- 启动命令偶发超时
- 串口通讯稳定性依赖现场设备状态

### 7.2 页面还可继续优化

主测试页已经可用，但仍建议继续优化：

- 曲线区和右侧统计区在低高度浏览器窗口下继续压缩
- 程序时间在页面上更明确展示
- 最近记录和历史详情的视觉统一

### 7.3 运维收尾

还需要继续收口：

- 日志轮转
- 数据库备份
- 服务自动恢复验证
- 升级 / 发布说明

## 8. 当前最重要的已确认规则

后续继续开发时，不应再改坏这些规则：

- 测试开始条件是 `stepCode = 4`，不是点击按钮本身
- 最终压力取 `stepCode = 6` 最后 1 秒
- 最终漏率取 `stepCode = 65535`
- 扫码记录与测试记录绑定后清空
- 程序时间以 ATEQ 实际读取结果为准，不再手工维护
- 查询页、主页面、导出页对失败原因保持一致

## 9. 下一步建议

建议按以下顺序继续：

1. 继续做主页面细节优化，特别是曲线区与流程区布局
2. 把程序时间在主页面显式展示成 `充气 / 稳压 / 测试`
3. 完成日志、备份、服务恢复这三项运维收口
4. 视需要增加单条测试报告导出或打印
