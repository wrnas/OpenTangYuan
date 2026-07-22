[返回文档导航](README_zh-CN.md) · [返回项目首页](../README_zh-CN.md)

# 部署与平台支持

## 1. 平台定位

OpenTangYuan 是 **Windows-first** 的本地自动化运行时。

不同部署方式能够验证和使用的能力不同：

| 方式 | 平台 | 适用能力 |
|---|---|---|
| 浏览源码和文档 | 任意平台 | 架构、API、技能清单和工作流模型。 |
| 启动 Web API 与 Swagger | Windows / Linux / Docker | 能力发现、接口调试和不依赖桌面的服务端逻辑。 |
| 完整本地运行时 | Windows | 文件搜索、打开文档、桌面截图、本地工具调用等。 |
| 外部 Agent 集成 | Windows 运行时 + Agent 平台 | 端到端任务发现、规划和执行。 |

Linux 和 Docker 可以用于服务端 API，但不能自动提供 Windows 桌面资源。

## 2. 从源码运行

在仓库根目录执行：

```bash
dotnet restore TangYuan.sln
dotnet build TangYuan.sln
```

启动项目：

```bash
dotnet run --project src/OpenTangYuan --urls "http://localhost:54124"
```

验证：

```bash
curl -X POST http://localhost:54124/api/Skills/GetSkillListForAI
```

访问 Swagger：

```text
http://localhost:54124/swagger
```

## 3. Visual Studio 2022

建议将解决方案文件保留在仓库根目录：

```text
TangYuan.sln
```

核心项目位于：

```text
src/OpenTangYuan/
```

解决方案还可以包含：

```text
samples/OpenTangYuan.WinFormsDemo/
tests/
```

移动项目目录后，应确保 `.sln` 中的项目引用指向新的 `.csproj` 路径。

## 4. Docker

Docker 适合运行 API、查看 Swagger 和验证能力发现等服务端功能。

构建：

```bash
docker build -t opentangyuan .
```

运行：

```bash
docker run -d \
  --name opentangyuan \
  -p 54124:54124 \
  opentangyuan
```

示例 Compose：

```yaml
services:
  opentangyuan:
    build: .
    container_name: opentangyuan
    restart: unless-stopped
    ports:
      - "54124:54124"
    volumes:
      - ./sqlite-data:/app/data
    environment:
      - TZ=Asia/Shanghai
      - ASPNETCORE_URLS=http://*:54124
```

### Docker 限制

容器通常不能直接完成：

- Windows 桌面截图；
- 打开本地 Office 文档；
- 调用需要交互桌面的程序；
- 使用依赖 Windows 桌面的文件搜索能力。

因此 Docker 不应被描述为完整桌面功能的替代方案。

## 5. 发布 Windows 版本

可以生成 Windows x64 自包含发布包：

```powershell
dotnet publish .\src\OpenTangYuan `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=false `
  -o .\publish\OpenTangYuan-win-x64
```

使用文件夹发布而不是单文件发布，便于保留和检查：

- `appsettings.json`；
- SQLite 数据文件或初始化脚本；
- `skill-manifest.json`；
- `wwwroot`；
- 浏览器或其他运行资源。

发布包中不得包含真实凭据、私人数据、内部日志或敏感数据库。

## 6. 数据与静态资源

建议区分：

- **源码中的初始化资源**：可随项目发布；
- **程序运行产生的数据**：放在可配置的数据目录并加入 `.gitignore`；
- **README 演示图片**：放在 `docs/images/`；
- **网站必须使用的静态资源**：放在项目的 `wwwroot/`。

运行时截图、下载文件和日志不应长期提交到源码仓库。

## 7. 生产部署

生产环境建议：

- 使用 HTTPS；
- 通过反向代理、VPN 或内网网关保护服务；
- 启用认证与来源限制；
- 关闭或保护 Swagger；
- 使用非管理员账户运行；
- 收紧文件和程序白名单；
- 对副作用操作记录日志；
- 将配置和数据目录纳入备份；
- 定期轮换邮箱授权码、Webhook Key 和 API Token。

完整安全检查见 [配置与安全控制](configuration-security_zh-CN.md)。

## 8. 验证建议

每次发布前至少验证：

```bash
dotnet restore TangYuan.sln
dotnet build TangYuan.sln -c Release
```

并检查：

- 服务能够启动；
- Swagger 可访问；
- `GetSkillListForAI` 返回能力目录；
- SQLite 数据库能够初始化或读取；
- `skill-manifest.json` 能够加载；
- 配置文件和静态资源路径正确；
- Windows 桌面技能在目标机器上可以执行；
- Docker 构建与文档描述一致。
