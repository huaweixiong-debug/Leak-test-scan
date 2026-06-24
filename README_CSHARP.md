# ATEQ Leak Test System — C# 版本

## 项目概述

气密性泄漏测试上位机系统，通过 Modbus RTU 控制 ATEQ 检漏仪，通过 RS232 串口读取扫码枪，实现产品绑定、扫码自动启动、手动启动、测试记录查询与 CSV 导出。

本版本使用 **C# / ASP.NET Core 8.0** 开发，替代原有的 Node.js 版本。

## 运行环境

| 要求 | 说明 |
|------|------|
| 操作系统 | Windows 10/11 (x64) |
| .NET Runtime | 8.0（自包含发布包无需额外安装） |
| 串口 | 至少 2 个 RS232 COM 口（ATEQ + 扫码枪） |
| 浏览器 | Chrome / Edge（访问 `http://127.0.0.1:3000`） |

## 快速开始

### 方式一：自包含发布包（推荐给客户）

1. 将 `publish/` 文件夹完整复制到目标电脑，如 `D:\ATEQ`
2. 双击 `ATEQ.LeakTest.Web.exe` 启动服务
3. 打开浏览器访问 `http://127.0.0.1:3000`

### 方式二：源码构建

```bat
cd D:\ATEQ
build.cmd        # 构建项目
run-server.cmd   # 启动服务
```

前置要求：安装 [.NET 8.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)

## 配置真实设备

### 1. 通讯配置

打开 `http://127.0.0.1:3000/comm-config`，设置：

**ATEQ 检漏仪**（现场参数以设备管理器为准）：

| 参数 | 建议值 |
|------|--------|
| COM 口 | COM7 |
| 波特率 | 9600 |
| 数据位 | 8 |
| 校验 | even |
| 停止位 | 1 |
| 从站 ID | 255 |

**扫码枪**：

| 参数 | 建议值 |
|------|--------|
| COM 口 | COM1 |
| 波特率 | 115200 |
| 数据位 | 8 |
| 校验 | none |
| 停止位 | 1 |

### 2. 系统设置

打开 `http://127.0.0.1:3000/settings`，添加：

- **产品型号**：填写产品名称、ATEQ 程序号、QR 码关键词
- **操作员**：添加产线操作员

### 3. 开始测试

打开 `http://127.0.0.1:3000/index`，选择产品和操作员，点击"开始测试"或直接扫码。

## Mock 模式（无硬件调试）

在 `appsettings.json` 中设置 `FeatureFlags.EnableMockMode = true`，重启服务后可以通过 API 模拟完整测试流程，无需连接真实设备。

```
POST /api/config/ateq     → comPort = "MOCK_ATEQ"
POST /api/config/scanner  → comPort = "MOCK_SCANNER"
```

详细操作见项目内的 `RUNBOOK.md`。

## API 端点参考

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/health` | 服务健康检查 |
| GET | `/api/status` | ATEQ 实时状态 |
| GET | `/api/test/active` | 当前测试状态 |
| GET | `/api/config/ateq` | 读取 ATEQ 配置 |
| POST | `/api/config/ateq` | 保存 ATEQ 配置 |
| GET | `/api/config/scanner` | 读取扫码枪配置 |
| POST | `/api/config/scanner` | 保存扫码枪配置 |
| GET | `/api/settings/products` | 产品列表 |
| POST | `/api/settings/products` | 保存产品 |
| GET | `/api/settings/operators` | 操作员列表 |
| POST | `/api/settings/operators` | 保存操作员 |
| POST | `/api/start` | 启动测试 |
| POST | `/api/reset` | 复位 ATEQ |
| POST | `/api/test/context` | 同步产品/操作员 |
| GET | `/api/tests/latest` | 最近测试记录 |
| GET | `/api/tests/query?...` | 查询测试记录 |
| GET | `/api/tests/export.csv?...` | 导出 CSV |
| GET | `/api/program-timings?programNumber=N` | ATEQ 程序时序 |
| GET | `/api/scanner/latest` | 最新扫码 |
| GET | `/api/scanner/debug` | 扫码枪调试 |

## 项目结构

```
D:\ATEQ\
├── ATEQ.LeakTest.Web.exe     # 自包含可执行文件（publish 目录）
├── appsettings.json           # 配置文件
├── data\                      # SQLite 数据库（自动创建）
├── wwwroot\                   # 前端页面
│   ├── index.html             # 主测试页
│   ├── comm-config.html       # 通讯配置
│   ├── settings.html          # 系统设置
│   └── query.html             # 历史查询
├── build.cmd                  # 构建脚本
├── run-server.cmd             # 启动脚本
├── RUNBOOK.md                 # 运维手册
└── README_CSHARP.md           # 本文档
```

## 常见问题

**Q: 服务启动后页面打不开？**
A: 检查防火墙是否允许端口 3000，或用 `netstat -ano | findstr ":3000"` 确认服务是否在监听。

**Q: ATEQ 显示离线？**
A: 检查 COM 口是否被其他程序占用，确认设备管理器中 COM 口编号与配置一致。

**Q: 扫码枪无反应？**
A: 打开 `/scanner-test` 页面查看原始串口数据，检查波特率是否匹配扫码枪设置。

**Q: 测试记录不保存？**
A: 检查 `data/` 目录是否有写入权限，查看 `server.log` 中的错误信息。

## 技术支持

- 项目运维手册：`RUNBOOK.md`
- Mock 模式调试：设置 `FeatureFlags.EnableMockMode = true`
