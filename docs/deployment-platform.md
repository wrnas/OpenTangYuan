[中文版](deployment-platform_zh-CN.md) · [Back to Documentation](README.md) · [Back to Project Home](../README.md)

# Deployment and Platform Support

## 1. Platform Positioning

OpenTangYuan is a **Windows-first** local automation runtime.

Different deployment methods support different capabilities:

| Approach | Platform | Supported scope |
|---|---|---|
| Browse source and documentation | Any | Architecture, APIs, skill manifests, and workflow model. |
| Run Web API and Swagger | Windows / Linux / Docker | Capability discovery, API testing, and server-side logic that does not require desktop access. |
| Full local runtime | Windows | File search, document opening, desktop screenshots, local-tool invocation, and other desktop capabilities. |
| External agent integration | Windows runtime + agent platform | End-to-end task discovery, planning, and execution. |

Linux and Docker can host server-side APIs, but they do not automatically provide access to Windows desktop resources.

## 2. Run from Source

From the repository root:

```bash
dotnet restore TangYuan.sln
dotnet build TangYuan.sln
```

Start the project:

```bash
dotnet run --project src/OpenTangYuan --urls "http://localhost:54124"
```

Verify the service:

```bash
curl -X POST http://localhost:54124/api/Skills/GetSkillListForAI
```

Open Swagger:

```text
http://localhost:54124/swagger
```

## 3. Visual Studio 2022

Keep the solution file at the repository root:

```text
TangYuan.sln
```

Place the core project under:

```text
src/OpenTangYuan/
```

The solution may also include:

```text
samples/OpenTangYuan.WinFormsDemo/
tests/
```

After moving project directories, confirm that the `.sln` references the new `.csproj` location.

## 4. Docker

Docker is suitable for running the API, inspecting Swagger, and validating server-side capability discovery.

Build:

```bash
docker build -t opentangyuan .
```

Run:

```bash
docker run -d \
  --name opentangyuan \
  -p 54124:54124 \
  opentangyuan
```

Example Compose file:

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

### Docker Limitations

A container normally cannot directly perform:

- Windows desktop screenshots;
- opening local Office documents through an interactive desktop;
- invoking programs that require an interactive desktop session;
- file-search capabilities that depend on Windows desktop services.

Docker should therefore not be described as a complete replacement for the Windows desktop runtime.

## 5. Publish a Windows Build

A self-contained Windows x64 package can be generated with:

```powershell
dotnet publish .\src\OpenTangYuan `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=false `
  -o .\publish\OpenTangYuan-win-x64
```

A folder-based publish is preferable to a single-file build when the runtime needs visible and editable resources such as:

- `appsettings.json`;
- SQLite data files or initialization scripts;
- `skill-manifest.json`;
- `wwwroot`;
- browser or other runtime assets.

Release packages must not contain real credentials, private data, internal logs, or sensitive databases.

## 6. Data and Static Resources

Keep the following categories separate:

- **Initialization resources stored with the source:** may be included in the application project or release.
- **Data generated at runtime:** place in a configurable data directory and exclude from Git.
- **README and documentation images:** place under `docs/images/`.
- **Static assets required by the web application:** place under the project's `wwwroot/`.

Runtime screenshots, downloads, and logs should not be committed to the source repository.

## 7. Production Deployment

Recommended production practices:

- use HTTPS;
- protect the service through a reverse proxy, VPN, or internal gateway;
- enable authentication and source restrictions;
- disable or protect Swagger;
- run under a non-administrator account;
- narrow file and executable allowlists;
- log side-effect operations;
- include configuration and data directories in backup procedures;
- rotate email authorization codes, webhook keys, and API tokens regularly.

See [Configuration and Security Controls](configuration-security.md) for the complete checklist.

## 8. Release Validation

Before each release, run at least:

```bash
dotnet restore TangYuan.sln
dotnet build TangYuan.sln -c Release
```

Also verify that:

- the service starts successfully;
- Swagger is reachable in the intended environment;
- `GetSkillListForAI` returns the capability catalog;
- the SQLite database can be initialized or opened;
- `skill-manifest.json` loads correctly;
- configuration and static-resource paths are correct;
- Windows desktop skills work on the target machine;
- the Docker build matches the documentation.
