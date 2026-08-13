[English](README.md)

# OpenTangYuan 本地工作流演示

本独立演示提供一个可复现示例，用于展示 **OpenTangYuan Runtime** 如何加载并执行已存储的本地工作流。

演示中包含的工作流会搜索一个示例文件，将其复制到输出目录，然后打开复制后的文件。该演示主要用于展示 Runtime 启动、已存储工作流加载、步骤间数据传递以及本地技能执行。

## Demo 软件包

本演示对应 **OpenTangYuan V1.1.4**，即 SoftwareX 稿件中描述的软件版本。

- **Release：** [OpenTangYuan V1.1.4](https://github.com/wrnas/OpenTangYuan/releases/tag/V1.1.4)
- **平台：** Windows 10/11（x64）
- **许可证：** MIT

> [!IMPORTANT]
> **无需单独安装 .NET 8。**
>
> Release 软件包中已经包含所需的 .NET Runtime。请先完整解压 ZIP 文件，然后从解压后的根目录运行 `Start-Demo.bat` 启动演示。

## 截图



## Demo 范围

本演示完全在本地计算机上运行，仅使用软件包内置的示例文件和预定义工作流。

运行演示不需要：

- 邮箱账号；
- 外部 AI Agent；
- Coze、Dify 或其他 Agent 平台；
- 企业系统凭据；
- 访问用户的私有文件；
- 管理员权限。

本演示用于提供一个小型、自包含的 Runtime 运行示例，展示数据库工作流加载、步骤间数据传递和本地文件操作。

该演示与论文中报告的高校行政办公场景试运行相互独立，也不用于作为性能、安全性或隐私保护能力的基准测试。

## 快速开始

### 运行要求

- Windows 10 或 11（x64）
- 无需安装 .NET SDK
- 无需单独安装 .NET Runtime
- 无需管理员权限

### 运行方法

1. 从 [OpenTangYuan V1.1.4 Release](https://github.com/wrnas/OpenTangYuan/releases/tag/V1.1.4) 下载独立 Demo ZIP 软件包。
2. 将整个 ZIP 文件完整解压到本地目录，例如：

   ```text
   C:\OpenTangYuan-Demo
   ```

3. 双击解压后根目录中的：

   ```text
   Start-Demo.bat
   ```

4. 在 Demo 窗口中按以下顺序点击按钮：

   ```text
   Start Runtime
        ↓
   Quick Check
        ↓
   Run Demo
   ```

`Quick Check` 会检查本地 Runtime 是否可以访问，以及演示所需的工作流是否可用。只有在检查成功后再继续运行 Demo。

> [!WARNING]
> 请不要直接双击 `WinForm\TangYuan.Demo.exe` 启动演示。
>
> Demo 使用软件包内附带的私有 .NET 8 Runtime。`Start-Demo.bat` 会先配置所需的运行环境，然后再启动演示程序。

## Demo 执行内容

本演示使用的工作流已预先存储在软件包内置的 Runtime 数据库中，并通过以下 `SkillCode` 加载：

```text
demo_local_file_workflow
```

该工作流依次执行：

```text
查找 sample-report.txt
        ↓
复制到 demo-output
        ↓
打开复制后的文件
```

该流程可以验证 OpenTangYuan 是否能够：

- 从数据库加载已保存的工作流；
- 通过 `SkillCode` 运行工作流；
- 将运行参数传入工作流；
- 在工作流步骤之间传递数据；
- 执行本地文件搜索、复制和打开操作；
- 确认输出文件已实际生成。

## 目录结构

```text
<解压目录>
├── Start-Demo.bat       # Demo 启动器
├── runtime              # 内置的私有 .NET Runtime
├── serverExe            # OpenTangYuan Runtime、配置文件和内置数据库
│   └── TangYuan.exe
└── WinForm              # 桌面演示程序和示例文件
    ├── TangYuan.Demo.exe
    └── sample-report.txt
```

请保持上述目录结构不变。

请勿：

- 单独移动 `TangYuan.Demo.exe`；
- 单独移动 `TangYuan.exe`；
- 删除或移动 `runtime`、`serverExe` 或 `WinForm` 文件夹；
- 直接在 ZIP 压缩包内部运行 Demo；
- 删除配置文件、数据库文件、依赖 DLL、`.runtimeconfig.json` 或 `.deps.json` 文件。

## 预期结果

点击 **Run Demo** 后，程序会：

1. 检查 Runtime 是否正在运行；
2. 从内置数据库加载预定义工作流；
3. 搜索、复制并打开示例文件；
4. 创建以下文件：

   ```text
   WinForm\demo-output\sample-report-copy.txt
   ```

5. 使用系统默认应用程序打开复制后的文件。

如果复制后的文件成功创建并被打开，则说明 Demo 已按预期完成。

## 故障排查

### Demo 无法启动

请确认 ZIP 文件已经完整解压，然后从解压后的根目录运行：

```text
Start-Demo.bat
```

不要直接运行：

```text
WinForm\TangYuan.Demo.exe
```

### Windows 显示安全警告

Demo 二进制文件可能未进行代码签名，因此首次启动时 Windows SmartScreen 可能会显示安全警告。

继续操作前，请确认 ZIP 文件是从 OpenTangYuan 官方 GitHub Release 页面下载的。不要为了运行 Demo 而全局关闭 Windows 安全功能。

### Runtime 无法启动

请检查：

- `serverExe\TangYuan.exe` 是否存在；
- 端口 `54124` 是否已被其他程序占用；
- `serverExe` 文件夹中是否仍保留 ZIP 软件包内提供的全部配置文件、数据库文件和依赖文件。

### Quick Check 失败

请检查：

- Runtime 是否已成功启动；
- 本地是否可以访问端口 `54124`；
- 内置数据库和配置文件是否被移动或删除；
- 工作流 `demo_local_file_workflow` 是否存在。

### 找不到 sample-report.txt

请确认文件位于：

```text
WinForm\sample-report.txt
```

### 无法替换输出文件

请先关闭之前已经打开的文件：

```text
sample-report-copy.txt
```

然后重新运行 Demo。

## 更多信息

本 README 仅说明如何运行打包后的 Demo。

如需查看完整项目介绍、系统架构、工作流模型、API 参考、配置说明和源代码，请访问：
- [OpenTangYuan 项目主页](https://gitee.com/l00f/open-tang-yuan/)
- [项目文档](https://gitee.com/l00f/open-tang-yuan/tree/master/docs)
- [OpenTangYuan V1.1.4 Release](https://github.com/wrnas/OpenTangYuan/releases/tag/V1.1.4)
