# OpenTangYuan 本地工作流 Demo

本 Demo 用于演示 **OpenTangYuan Runtime** 如何从数据库加载并执行一个本地工作流。

工作流会依次完成文件查找、复制和打开操作。

> [!IMPORTANT]
> **无需安装 .NET 8。**
>
> Release 包已包含运行所需的 .NET Runtime。请完整解压后，通过根目录中的 `Start-Demo.bat` 启动。

## 截图

![OpenTangYuan Demo](demo.png)

## 快速开始

### 系统要求

- Windows 10 / 11（x64）
- 无需安装 .NET SDK
- 无需安装 .NET Runtime
- 无需管理员权限

### 运行步骤

1. 下载 GitHub Release 中的 Demo ZIP。
2. 将 ZIP 完整解压到本地目录，例如：

   ```text
   C:\OpenTangYuan-Demo
   ```

3. 双击根目录中的：

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

> [!WARNING]
> 请勿直接双击 `WinForm\TangYuan.Demo.exe`。
>
> Demo 使用自带的私有 .NET 8 运行时，必须通过 `Start-Demo.bat` 加载运行环境。

## 演示内容

本 Demo 使用的工作流已预先保存在数据库中，并通过以下 `SkillCode` 加载：

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

- 从数据库读取持久化工作流
- 根据 `SkillCode` 执行工作流
- 注入运行时参数
- 在工作流步骤之间传递数据
- 调用本地文件查询、复制和打开能力
- 独立验证输出文件是否成功生成

## 目录结构

```text
DemoExe
├── Start-Demo.bat       # Demo 启动入口
├── runtime              # 自带的 .NET 运行时
├── serverExe            # OpenTangYuan Runtime
│   └── TangYuan.exe
└── WinForm              # 桌面演示程序
    ├── TangYuan.Demo.exe
    └── sample-report.txt
```

请保持目录结构不变。

请勿：

- 单独移动 `TangYuan.Demo.exe`
- 单独移动 `TangYuan.exe`
- 删除或移动 `runtime`、`serverExe`、`WinForm`
- 在 ZIP 压缩包中直接运行
- 删除配置文件、依赖 DLL、`.runtimeconfig.json` 或 `.deps.json`

## 运行结果

执行 **Run Demo** 后，程序将：

1. 检查 Runtime 是否正常运行。
2. 从数据库读取工作流。
3. 执行文件查找、复制和打开操作。
4. 生成以下文件：

   ```text
   WinForm\demo-output\sample-report-copy.txt
   ```

5. 使用系统默认程序打开复制后的文件。

看到复制后的文件被成功打开，即表示 Demo 执行完成。

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

### Runtime 无法启动

请确认：

- `serverExe\TangYuan.exe` 存在
- 端口 `54124` 未被其他程序占用

### 找不到 sample-report.txt

请确认文件位于：

```text
WinForm\sample-report.txt
```

### 无法覆盖输出文件

请关闭已经打开的：

```text
sample-report-copy.txt
```

然后重新运行 Demo。

## 更多说明

本 README 仅介绍 Demo 的运行方式。

项目介绍、工作流定义、API、Runtime 架构及实现细节，请查看项目根目录中的主 `README.md` 和相关文档。
