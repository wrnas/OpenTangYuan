[English](README.md)

# OpenTangYuan 本地工作流 Demo

本 Demo 用于演示 **OpenTangYuan Runtime** 如何从随包提供的数据库中加载并执行一个本地工作流。

该工作流会依次查找示例文件、将其复制到输出目录，并打开复制后的文件。

> [!IMPORTANT]
> **无需安装 .NET 8。**
>
> Release 包已经包含运行所需的 .NET Runtime。请先完整解压整个 ZIP 文件，再通过根目录中的 `Start-Demo.bat` 启动。

## 截图

![OpenTangYuan Demo](demo.png)

## Demo 范围

本 Demo 完全在本机运行，只使用随包提供的示例文件和预定义工作流。

运行时不需要：

- 邮箱账号；
- 外部 AI Agent；
- Coze、Dify 或其他 Agent 平台；
- 企业系统凭据；
- 访问用户的私人文件；
- 管理员权限。

该 Demo 用于提供一个小型、独立的验证环境，主要验证 Runtime 启动、数据库工作流加载、步骤间数据传递以及本地文件操作。

## 快速开始

### 系统要求

- Windows 10 / 11（x64）
- 无需安装 .NET SDK
- 无需安装 .NET Runtime
- 无需管理员权限

### 运行步骤

1. 从 OpenTangYuan 官方 GitHub Releases 页面下载 Demo ZIP。
2. 将 ZIP 完整解压到本地目录，例如：

   ```text
   C:\OpenTangYuan-Demo
   ```

3. 双击解压后根目录中的：

   ```text
   Start-Demo.bat
   ```

4. 在 Demo 窗口中依次点击：

   ```text
   Start Runtime
        ↓
   Quick Check
        ↓
   Run Demo
   ```

`Quick Check` 用于确认本地 Runtime 可以访问，并且 Demo 所需的预定义工作流已经存在。只有检查成功后，再继续执行 `Run Demo`。

> [!WARNING]
> 请勿直接双击 `WinForm\TangYuan.Demo.exe`。
>
> Demo 使用随包提供的私有 .NET 8 运行时，必须通过 `Start-Demo.bat` 设置运行环境后启动。

## 演示内容

本 Demo 使用的工作流已经预先保存在随 Runtime 提供的数据库中，并通过以下 `SkillCode` 加载：

```text
demo_local_file_workflow
```

工作流执行过程：

```text
查找 sample-report.txt
        ↓
复制到 demo-output
        ↓
打开复制后的文件
```

该流程主要验证：

- 从数据库读取持久化工作流；
- 根据 `SkillCode` 执行工作流；
- 向工作流注入运行时参数；
- 在工作流步骤之间传递数据；
- 调用本地文件查询、复制和打开能力；
- 独立验证输出文件是否成功生成。

## 目录结构

```text
<解压目录>
├── Start-Demo.bat       # Demo 启动入口
├── runtime              # 随包提供的私有 .NET 运行时
├── serverExe            # OpenTangYuan Runtime、配置文件和预置数据库
│   └── TangYuan.exe
└── WinForm              # 桌面演示程序和示例文件
    ├── TangYuan.Demo.exe
    └── sample-report.txt
```

请保持目录结构不变。

请勿：

- 单独移动 `TangYuan.Demo.exe`；
- 单独移动 `TangYuan.exe`；
- 删除或移动 `runtime`、`serverExe` 或 `WinForm` 文件夹；
- 在 ZIP 压缩包内直接运行；
- 删除配置文件、数据库文件、依赖 DLL、`.runtimeconfig.json` 或 `.deps.json` 文件。

## 运行结果

执行 **Run Demo** 后，程序将：

1. 检查 Runtime 是否正常运行。
2. 从随包数据库读取预定义工作流。
3. 执行文件查找、复制和打开操作。
4. 生成以下文件：

   ```text
   WinForm\demo-output\sample-report-copy.txt
   ```

5. 使用系统默认程序打开复制后的文件。

如果输出文件成功生成并被打开，即表示 Demo 已按预期完成。

## 常见问题

### Demo 无法启动

请确认 ZIP 已完整解压，并通过以下文件启动：

```text
Start-Demo.bat
```

不要直接运行：

```text
WinForm\TangYuan.Demo.exe
```

### Windows 显示安全警告

如果 Demo 二进制文件尚未进行代码签名，Windows SmartScreen 可能会在首次启动时显示警告。

继续操作前，请确认 ZIP 文件来自 OpenTangYuan 官方 GitHub Releases 页面。不要为了运行 Demo 而全局关闭 Windows 安全功能。

### Runtime 无法启动

请确认：

- `serverExe\TangYuan.exe` 存在；
- 端口 `54124` 未被其他程序占用；
- `serverExe` 文件夹中随 ZIP 提供的配置、数据库和依赖文件仍然完整。

### Quick Check 失败

请确认：

- Runtime 已成功启动；
- 本机可以访问端口 `54124`；
- 随包数据库和配置文件没有被移动或删除；
- 工作流 `demo_local_file_workflow` 仍然可用。

### 找不到 sample-report.txt

请确认文件位于：

```text
WinForm\sample-report.txt
```

### 无法覆盖输出文件

请先关闭已经打开的：

```text
sample-report-copy.txt
```

然后重新运行 Demo。

## 更多说明

本 README 仅介绍打包版 Demo 的运行方式。

项目介绍、架构、工作流模型、API 参考、配置说明和源代码，请访问：

- [OpenTangYuan 项目仓库](https://github.com/wrnas/OpenTangYuan)
- [项目文档](https://github.com/wrnas/OpenTangYuan/tree/master/docs)
- [GitHub Releases](https://github.com/wrnas/OpenTangYuan/releases)
