

# OpenTangYuan

Cloud-Local Agent Workflow Runtime

AI plans. Local runtime executes.

Files • Email • Browser • Enterprise • Workflows

---


## Typical Workflows

Search file → Open → Screenshot → Email

Read inbox → Download attachments → Organize → Notify

Open enterprise system → Export → Capture → Send message

---

## Core Concepts

OpenTangYuan is built around a simple idea:

> Let AI plan tasks, while execution and sensitive data stay inside a trusted local runtime.

### 1. Skills: everything is a capability

A skill is a reusable unit of capability:
- file operations
- email handling
- browser automation
- screenshots
- enterprise messaging
- local tool execution

Skills are exposed through a unified runtime.

---

### 2. Discover before execution

GetSkillListForAI → GetBuiltinSkillDetail / GetSkillAction → ExecuteSkill

This keeps agents lightweight and extensible.

---

### 3. Workflows

Search → Open → Screenshot → Send email

Workflows can be predefined or dynamic.

---

### 4. Context passing

{{step0}} → {{step1}}

---

### 5. Local execution

All real operations run locally:
- files
- email
- browser
- system tools

---

### 6. Safety layer

- allowlists
- authentication
- logging


<p align="center">
  <strong>Cloud-Local Agent Workflow Runtime for Privacy-Sensitive Office Automation</strong>
</p>

<p align="center">
  Connect browsers, email, file systems, enterprise messaging, local tools, and internal systems through cloud-side planning and trusted local execution.
</p>

<p align="center">
  <a href="#what-is-opentangyuan">What it is</a> ·
  <a href="#what-can-it-do">What it can do</a> ·
  <a href="#quick-start">Quick Start</a> ·
  <a href="#core-concepts">Core Concepts</a> ·
  <a href="#workflow-runtime">Workflow Runtime</a> ·
  <a href="#core-apis">Core APIs</a> ·
  <a href="#security-and-deployment-boundaries">Security</a> ·
  <a href="#research-context">Research Context</a> ·
  <a href="#citation">Citation</a>
</p>

<p align="center">
  <a href="#"><img src="https://img.shields.io/badge/.NET-8.0-purple" alt=".NET 8"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-green" alt="MIT License"></a>
  <a href="#"><img src="https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20Docker-blue" alt="Platform"></a>
  <a href="#"><img src="https://img.shields.io/badge/status-research%20software-orange" alt="Research Software"></a>
  <a href="#"><img src="https://img.shields.io/badge/Agent-Workflow%20Runtime-blueviolet" alt="Agent Workflow Runtime"></a>
</p>

---

## What is OpenTangYuan?

**OpenTangYuan** is an open-source **Agent Workflow Runtime** for privacy-sensitive office automation and institutional workflow automation.

It is designed for a common situation: an external AI agent can understand a user's request and plan a task, but the actual work often needs access to local files, email accounts, browsers, screenshots, enterprise messaging tools, or internal systems. Those resources should not be exposed directly to a cloud-side agent.

OpenTangYuan separates the two sides:

```text
Cloud side: planning and capability metadata
Local runtime: execution and sensitive data processing
Enterprise systems: accessed only through the trusted local runtime
```

In practice, this means an agent can discover available local capabilities, ask for the details it needs, compose a workflow, and submit the job to a trusted runtime. The runtime then performs the real work locally and returns structured results.

OpenTangYuan is not meant to be a chatbot by itself, and it is not a small tool-calling demo. It is a runtime layer that helps external agents safely use local automation capabilities while keeping sensitive data and execution privileges under local control.

The first validation scenarios came from university administrative work, but the design does not depend on any particular institution or business system. It can be adapted to research management, corporate back-office work, laboratory operations, government assistance workflows, and other privacy-sensitive automation settings.

---

## What can it do?

OpenTangYuan is useful when a task needs to cross multiple systems, repeat reliably, and run close to private data. Typical examples include:

- searching, copying, moving, renaming, opening, or organizing local files and folders;
- searching email, reading messages, downloading attachments, replying, sending messages, and inserting screenshots into email bodies;
- automating browsers to open pages, extract content, take screenshots, or download files;
- pushing results to enterprise messaging platforms such as WeChat Work or DingTalk;
- launching whitelisted local tools or executables;
- composing multiple local skills into reusable workflows;
- triggering local tasks from Coze, Dify, GPTs, or a custom agent gateway.

A simple task may call one built-in skill, such as searching email. A more complex task may run several steps, for example:

```text
search file -> open it -> capture screenshot -> send email
```

---

## Quick Start

### Requirements

Full desktop automation works best on Windows 10, Windows 11, or Windows Server 2016+. You will need:

- .NET 8 SDK or Runtime;
- Visual Studio 2022, JetBrains Rider, or the `dotnet` CLI;
- SQLite;
- optional email account, WeChat Work bot, and local browser environment.

The server components can also run on Linux or in Docker. Features such as desktop file search, document opening, screenshots, and local tool invocation still require Windows desktop resources.

### Clone the repository

```bash
git clone https://github.com/wrnas/OpenTangYuan.git
cd OpenTangYuan
```

Or from Gitee:

```bash
git clone https://gitee.com/l00f/open-tang-yuan.git
cd open-tang-yuan
```

### Restore dependencies

```bash
dotnet restore
```

### Run the service

```bash
dotnet run --urls "http://localhost:54124"
```

### Verify the service

```bash
curl -X POST http://localhost:54124/api/Skills/GetSkillListForAI
```

You should see a list of available workflows and built-in skills.

### Swagger / OpenAPI

![Swagger](readme/images/swagger-1.png)

Once the service is running, visit:

```text
http://localhost:54124/swagger
```

Use Swagger to explore and test the APIs interactively.

---

## 1. Agent workflow runtime

OpenTangYuan is a runtime for agent-driven tasks. It provides:

- capability discovery;
- skill detail queries;
- parameter generation;
- multi-step workflow orchestration;
- context passing between steps;
- trusted local execution;
- structured result packaging;
- debugging and error feedback.

### 2. Manifest-driven skill registry

All built-in skills are described in `skill-manifest.json`. An external agent does not need to remember every parameter upfront. It can follow a discovery cycle:

```text
GetSkillListForAI
        ↓
GetBuiltinSkillDetail / GetSkillAction
        ↓
ExecuteSkill / ExecuteSkillForCoze
```

This keeps prompts shorter, improves call stability, and makes it easier to add new skills later.

### 3. Workflow-first execution

OpenTangYuan distinguishes two kinds of capabilities:

| Type | Description |
|---|---|
| Builtin Skill | Atomic operations such as email, file, browser, screenshot, messaging, and local tool execution. |
| Workflow Skill | Reusable sequences composed of multiple built-in skills. |

The runtime prefers pre-validated workflows from the database when available. For new requests, agents can query built-in skill details and assemble a temporary workflow on the fly.

### 4. Context-aware multi-step execution

Later steps can refer to outputs from earlier steps using simple template variables:

```text
{{step0}}
{{step0.path}}
{{step0.data.path}}
{{step1.result}}
```

This makes pipelines such as `search file -> open it -> take a screenshot -> email it` both easy to describe and easy to execute in a structured way.

### 5. Trusted local execution

All real actions involving files, emails, browsers, screenshots, local tools, or enterprise systems happen inside the local runtime. The external agent receives capability metadata and structured execution results, but it does not directly access sensitive resources.

### 6. Policy-controlled side effects

OpenTangYuan applies restrictions and logging to operations that change state or have external impact, including:

- sending or replying to emails;
- copying, moving, or deleting files;
- downloading attachments;
- launching local programs;
- printing;
- calling external tools.

Combined with API authentication, path allowlists, executable allowlists, and side-effect controls, this provides a safer automation environment.

---

## System Architecture

![Architecture](readme/images/architecture.png)

OpenTangYuan follows a cloud-local collaboration model.

| Layer | Responsibility |
|---|---|
| User Interaction Layer | Web, mobile, chat, API, SDK, Coze, Dify, GPTs, and custom agents. |
| OpenTangYuan Orchestration Layer | Workflow repository, skill registry, capability discovery, planning, and routing. |
| Secure Execution Channel | Secure communication between cloud orchestration and the local runtime. |
| Trusted Local Runtime Layer | Authentication, policy validation, workflow scheduling, skill invocation, context management, and result packaging. |
| Enterprise Integration Layer | Browser, email, file system, WeChat Work, local tools, OA, ERP/CRM, and custom APIs. |
| Governance & Compliance | Privacy protection, access control, trust management, auditing, monitoring, and alerting. |

The execution boundary is intentionally clear:

```text
Cloud side: planning and capability metadata only
Local runtime: execution and sensitive data processing
Enterprise systems: accessed only through the trusted local runtime
```

---

## Capability Discovery

![Capability Discovery](readme/images/capability-discovery.png)

An external agent discovers capabilities through a simple sequence:

1. Call `GetSkillListForAI` to get a summary of available workflows and built-in skills.
2. If more detail is needed, call `GetBuiltinSkillDetail` for built-ins or `GetSkillAction` for workflows.
3. Use the returned parameter specifications to construct a call.
4. Submit the execution via `ExecuteSkill` or `ExecuteSkillForCoze`.

### Design benefits

| Mechanism | Benefit |
|---|---|
| Summary-only initial fetch | Reduces prompt size and token usage. |
| On-demand detail queries | Keeps the agent context light. |
| Workflow prioritization | Reuses proven sequences for consistency. |
| Built-in skills as fallback | Handles ad-hoc tasks flexibly. |
| Manifest-driven design | Makes extension and maintenance easier. |

---

## Workflow Runtime

OpenTangYuan includes a built-in workflow runtime that handles both predefined and temporary multi-step jobs.

It supports:

- step scheduling;
- context propagation;
- template variable resolution;
- runtime execution;
- compact result packaging;
- debug logging;
- failure reporting.

### Execution flow

```text
1. Receive workflow steps
2. Initialize execution context
3. Execute each step sequentially
4. Resolve template variables
5. Invoke the corresponding built-in skill
6. Store the result as stepN
7. Let later steps reference previous outputs
8. Return the final result
```

### Template variable examples

```text
{{step0}}
{{step0.path}}
{{step0.data.path}}
{{step1.result}}
```

### Example workflow: screenshot and email

```json
{
  "SkillCode": "temp_task",
  "Arguments": {},
  "Steps": [
    {
      "Action": "screenshot_task",
      "Args": {
        "action": "capture_full_screen"
      }
    },
    {
      "Action": "email_task",
      "Args": {
        "action": "send",
        "to": "someone@example.com",
        "subject": "Screen capture",
        "body": "The automatically captured screenshot is attached below.",
        "insertImagePaths": [
          "{{step0.data.path}}"
        ]
      }
    }
  ]
}
```

### Example workflow: search, open, screenshot, and email

```json
{
  "SkillCode": "temp_task",
  "Arguments": {},
  "Steps": [
    {
      "Action": "file_task",
      "Args": {
        "action": "search",
        "keyword": "target_document",
        "ext": "docx"
      }
    },
    {
      "Action": "open_task",
      "Args": {
        "path": "{{step0.data.firstPath}}"
      }
    },
    {
      "Action": "screenshot_task",
      "Args": {
        "action": "capture_full_screen"
      }
    },
    {
      "Action": "email_task",
      "Args": {
        "action": "send",
        "to": "someone@example.com",
        "subject": "Document screenshot and attachment",
        "body": "The document screenshot is inserted below, and the original file is attached.",
        "insertImagePaths": [
          "{{step2.data.path}}"
        ],
        "attachments": [
          "{{step0.data.firstPath}}"
        ]
      }
    }
  ]
}
```

---

## Core APIs

This README lists the most important APIs for agent integration. For full parameter details, see [`docs/api.md`](docs/api.md).

| API | Method | Endpoint | Purpose |
|---|---|---|---|
| GetSkillListForAI | POST | `/api/Skills/GetSkillListForAI` | Get workflow and built-in skill summaries. |
| GetBuiltinSkillDetail | POST | `/api/Skills/GetBuiltinSkillDetail` | Get the detailed definition of a built-in skill. |
| GetBuiltinSkillManifest | POST | `/api/Skills/GetBuiltinSkillManifest` | Get the full manifest of a built-in skill. |
| GetSkillAction | POST | `/api/Skills/GetSkillAction` | Get step definitions of a workflow. |
| ExecuteSkill | POST | `/api/Skills/ExecuteSkill` | Unified execution for built-ins, workflows, or temporary tasks. |
| ExecuteSkillForCoze | POST | `/api/Skills/ExecuteSkillForCoze` | Coze-compatible execution wrapper. |

### Get skill list

```http
POST /api/Skills/GetSkillListForAI
```

Example:

```bash
curl -X POST http://localhost:54124/api/Skills/GetSkillListForAI
```

Example response:

```json
{
  "success": true,
  "data": {
    "workflows": [
      {
        "skillCode": "capture_and_send_email",
        "AIDesc": "Capture a screenshot and send it by email.",
        "sourceType": "workflow",
        "needDetail": true
      }
    ],
    "builtins": [
      {
        "skillCode": "email_task",
        "AIDesc": "Email operations such as search, read, send, reply and attachment download.",
        "sourceType": "builtin",
        "needDetail": true
      }
    ]
  }
}
```

### Get built-in skill detail

```http
POST /api/Skills/GetBuiltinSkillDetail
```

Request body:

```json
{
  "skillCode": "email_task"
}
```

### Get workflow definition

```http
POST /api/Skills/GetSkillAction
```

Request body:

```json
{
  "skillCode": "capture_and_send_email"
}
```

### Execute a skill

```http
POST /api/Skills/ExecuteSkill
```

Request fields:

| Field | Type | Required | Description |
|---|---|---|---|
| `SkillCode` | string | Yes | Skill identifier, such as `email_task` or `temp_task`. |
| `Arguments` | object | No | Parameters for a single-step task. |
| `Steps` | array | No | Steps for a temporary workflow. |

### Coze-compatible execution

```http
POST /api/Skills/ExecuteSkillForCoze
```

Request body:

```json
{
  "Json": "{\"skillCode\":\"email_task\",\"arguments\":{\"action\":\"search\",\"subjectKeyword\":\"notification\",\"maxCount\":10}}"
}
```

### Response format

Success:

```json
{
  "success": true,
  "message": "Execution succeeded.",
  "data": {}
}
```

Failure:

```json
{
  "success": false,
  "message": "Invalid arguments.",
  "errorCode": "INVALID_ARGUMENTS",
  "data": null
}
```
---

## Technology Stack

| Technology | Purpose |
|---|---|
| .NET 8 | Backend framework. |
| C# 10+ | Main implementation language. |
| ASP.NET WebAPI | Local runtime API. |
| SQLite | Workflow storage. |
| Dapper | Data access. |
| MailKit | SMTP / IMAP email handling. |
| Playwright | Browser automation. |
| Everything SDK / Windows Search | File search on Windows. |
| WeChat Work Webhook / API | Enterprise messaging. |
| REST API | Skill query and execution interface. |
| JSON Manifest | Skill metadata description. |
| Docker | Optional containerized deployment. |

---

## Docker Deployment

Docker is useful for running server-side components and checking APIs. Desktop-intensive features, such as screenshots, opening documents, and Windows file search, still require a Windows runtime with desktop access.

### Build the image

```bash
docker build -t opentangyuan .
```

### Run the container

```bash
docker run -d \
  --name opentangyuan \
  -p 54124:54124 \
  opentangyuan
```

### Sample `docker-compose.yml`

```yaml
version: '3.8'

services:
  tangyuan-app:
    build: .
    container_name: tangyuan-app
    restart: always
    ports:
      - "54124:54124"
    volumes:
      - ./sqlite-data:/app/data
    environment:
      - TZ=Asia/Shanghai
      - ASPNETCORE_URLS=http://*:54124
```

---

## Configuration

Never commit email passwords, authorization codes, Webhook keys, API tokens, database secrets, or internal system credentials to the repository. Use environment variables, user secrets, Docker secrets, CI/CD variables, or a separate production configuration file.

### Email settings example

```json
{
  "EmailSettings": {
    "SmtpServer": "smtp.example.com",
    "SmtpPort": 465,
    "SmtpUseSsl": true,
    "SenderEmail": "your-email@example.com",
    "SenderPassword": "your-authorization-code",
    "ImapServer": "imap.example.com",
    "ImapPort": 993,
    "ImapUseSsl": true
  }
}
```

### File access whitelist example

```json
{
  "FileSystem": {
    "AllowedRoots": [
      "C:\\Users\\Public\\Documents",
      "D:\\Work",
      "D:\\Temp"
    ]
  }
}
```

### Executable whitelist example

```json
{
  "AllowedExeNames": [
    "pandoc.exe",
    "custom-tool.exe"
  ]
}
```

### Configuration reference

| Key | Required? | Description |
|---|---|---|
| `EmailSettings:SmtpServer` | Optional | SMTP server address. |
| `EmailSettings:SmtpPort` | Optional | SMTP port. |
| `EmailSettings:SenderEmail` | Optional | Sender email address. |
| `EmailSettings:SenderPassword` | Optional | Email authorization code. |
| `EmailSettings:ImapServer` | Optional | IMAP server address. |
| `ConnectionStrings:Sqlite` | Yes | SQLite connection string. |
| `FileSystem:AllowedRoots` | Recommended | Directories the runtime may access. |
| `AllowedExeNames` | Recommended | Executables that can be launched. |
| `DebugMode` | Optional | Enable verbose logging. |

---

## Built-in Skills

| SkillCode | Description |
|---|---|
| `email_task` | Send, search, read, download attachments, reply, mark read, and save as `.eml`. |
| `wechat_task` | Send text, markdown, or card messages to WeChat Work. |
| `browser_task` | Browse, capture screenshots, extract content, and download files. |
| `file_task` | Search, copy, move, rename, create directories, and run batch operations. |
| `open_task` | Open local files, directories, or programs. |
| `print_task` | Print local files. |
| `tool_task` | Invoke whitelisted local tools or executables. |
| `screenshot_task` | Capture the full screen or active window. |
| `folder_task` | Organize files by extension. |
| `lock_task` | Lock the local workstation. |

---

## Demo Screenshots

### Agent execution example

![Agent Execution Example](readme/images/demo-1.png)

These screenshots were taken in a Chinese office automation environment. English captions or annotations can be added in the paper and supplementary materials to explain the key UI elements, workflow steps, and results. The runtime APIs and workflow definitions are language-agnostic.

### Dynamic composite task execution

![Dynamic Composite Task Execution](readme/images/demo-2.png)

### Coze debug trace

![Coze Debug Trace](readme/images/coze-trace.png)

---

## External Agent Integration

OpenTangYuan can serve as an external execution runtime for Coze, Dify, GPTs, or custom agent platforms. The external agent is responsible for understanding user intent, selecting skills, and constructing parameters, while the local runtime handles the actual execution.

For integrating OpenTangYuan with external agents, we recommend the following approach:

### 1. Create Plugins for the Agent

We suggest creating **four plugins**, each corresponding to one of the core OpenTangYuan APIs:

| Plugin Name | Endpoint | Description |
|---|---|---|
| GetSkillListForAI | `Skills/GetSkillListForAI` | Retrieves an overview of currently available skills. Use this first to let the agent determine whether a reusable workflow already exists. Returns two categories: (1) `workflows`: pre-saved workflows from the database (prefer these for direct execution); (2) `builtins`: atomic built-in skills such as `browser_task`, `file_task`, `tool_task`, `email_task`, and `wechat_task`. **Usage rules:** (a) always call this tool first when a user request arrives; (b) if a suitable workflow is found, continue with `GetSkillAction` to inspect details, then call `ExecuteSkill`; (c) if no suitable workflow exists, consider composing a temporary workflow via `ExecuteSkill` with `Steps`, or call `AiBrowser` for low-level browser automation. |
| GetSkillAction | `Skills/GetSkillAction` | Retrieves the full definition of a saved workflow by its `SkillCode`. **Use when:** (a) you have found a potentially usable workflow via `GetSkillListForAI`; (b) you need to inspect its purpose, steps, and parameters; (c) you want to confirm it fits the current task before execution. **Input rules:** pass only the `SkillCode`, which must come from the `workflows` list returned by `GetSkillListForAI`. Output includes `skillCode`, `remark`, `skillType`, `updateTime`, `steps`, and `skillActionsRaw`. **Recommendation:** always read the details first before deciding whether to call `ExecuteSkill` — never skip this step and blindly execute a workflow. |
| ExecuteSkill | `Skills/ExecuteSkillForCoze` | Unified execution entry point for built-in skills, temporary workflows, and saved workflows. Parameters are passed as a JSON string. |
| GetBuiltinSkillDetail | `Skills/GetBuiltinSkillDetail` | Retrieves detailed information about a built-in skill, including its usage, parameters, and examples. |

### 2. Set Up the System Prompt

Using Coze as an example, the system prompt is quite long. You can find the full version here:

➡️ **[`readme/docs/agent-prompt.md`](readme/docs/agent-prompt.md)**

### 3. Core Call Flow

1. Call `GetSkillListForAI` to see what capabilities are available.
2. If `needDetail` is `true`, fetch further details:
   - For workflows: `GetSkillAction`
   - For built-in skills: `GetBuiltinSkillDetail`
3. After confirming the parameters, call `ExecuteSkill` or `ExecuteSkillForCoze`.
4. Stop immediately after success — **do not repeat** side-effect operations.
5. If a list is returned, display the list and **stop**. Only proceed further if the user explicitly requests details, such as "read the first one".
6. If required parameters are missing, ask the user. **Never guess** paths, email addresses, filenames, or credentials.
7. If a skill fails, you may retry **once** with corrected parameters.

### Coze Agent Configuration Example

![Coze Agent Configuration](readme/images/coze-agent-config.png)

---

## Security and Deployment Boundaries

OpenTangYuan can send emails, modify files, control browsers, take screenshots, print, and run local executables. It should therefore run in a trusted environment with proper access controls.

### Cloud-local boundary

| Component | Role |
|---|---|
| External agent | Task understanding, workflow planning, and skill routing. |
| Trusted Local Runtime | Execution, authentication, policy validation, context management, and result packaging. |
| Enterprise systems | Accessed only by the local runtime and never directly exposed to the cloud agent. |

### Security recommendations

- Never commit secrets such as passwords, tokens, and webhook keys to the repository.
- Restrict API access in production, for example by IP allowlist or VPN.
- Avoid exposing the local runtime directly to the public internet.
- Enable audit logging for all side-effect actions, including sending email, deleting files, and printing.
- Add human confirmation or approval for high-risk operations.
- Use path allowlists to limit filesystem access.
- Use executable allowlists to restrict which programs can be launched.
- Rotate email authorization codes, webhook keys, and API tokens regularly.
- Allowlist internal system endpoints and log access attempts.
- Keep execution logs for troubleshooting and compliance.

---

## Platform and Reproducibility

Full desktop automation is Windows-first because file search, document opening, screenshots, and local tool invocation rely on Windows desktop resources.

To make evaluation easier, OpenTangYuan offers several validation paths:

| Approach | Platform | What you can verify |
|---|---|---|
| Browse source and docs | Any | Architecture, API design, workflow model, and security design. |
| Start service and Swagger | Windows / Linux / Docker | API endpoints and capability discovery. |
| Windows self-contained release | Windows | Run the local runtime without installing .NET 8. |
| Full external agent integration | Windows Runtime + agent platform | End-to-end automation with Coze, Dify, GPTs, or custom agents. |

Real-world pilot logs may contain sensitive information such as email content, file paths, screenshots, and internal system data, so they are not published. The code, documentation, sample workflows, and deployment guides are provided to reproduce the software structure and execution paths.

---

## Extension Mechanisms

OpenTangYuan can be extended in several ways:

1. **Add a new Builtin Skill**  
   Implement the logic in the WebAPI and register it in `skill-manifest.json`.

2. **Add a new Workflow**  
   Store it in the database or via management APIs to compose multiple built-in skills into a reusable sequence.

3. **Integrate new enterprise systems**  
   Use browser automation, REST APIs, local tools, or custom plugins to connect OA, ERP, CRM, file servers, mail servers, and other systems.

4. **Extend security policies**  
   Add path allowlists, API access controls, role-based permissions, approval workflows, audit logs, or alerting rules.

---

## Development and Contributions

Issues and pull requests are welcome. Recommended development environments include:

- Visual Studio 2022;
- JetBrains Rider;
- VS Code + C# Dev Kit;
- `dotnet` CLI.

Common dependencies:

```bash
dotnet add package MailKit
dotnet add package Microsoft.Playwright
dotnet add package Microsoft.Data.Sqlite
dotnet add package Dapper
```

Commit message format:

```text
<type>: <subject>
```

Examples:

```text
feat: add enterprise message notification
fix: handle missing email attachment path
docs: update workflow examples
```

---

## Roadmap

- visual workflow designer;
- web admin dashboard;
- plugin-based skill extension;
- permission management and operation auditing;
- improved Docker Compose setup;
- GitHub Release with CI/CD;
- Zenodo DOI;
- MCP support;
- more office software automation capabilities;
- automated test suite and benchmark tasks;
- distributed runtime node management.

---

## FAQ

### Why can I not send email?

Check your SMTP configuration, SSL settings, authorization code, whether your email provider allows SMTP, and your network connectivity.

### Why can I not read email?

Check your IMAP configuration, authorization code, whether IMAP is enabled, and any third-party client restrictions from your provider.

### Why can I not open or print a file?

Verify that the file exists, the runtime has permission to access it, a default application is installed for that file type, and the path is inside `AllowedRoots`.

### Why does a browser task fail?

Make sure Playwright is fully installed, the target page does not require login, your selectors are correct, and the task is not blocked by CAPTCHA or multi-factor authentication.

### Why can a later step not reference a previous result?

Check the step numbering, such as `step0` and `step1`, the exact field path, such as `{{step0.data.path}}`, and confirm that the previous step succeeded. Do not guess paths; use the actual returned structure.

---

## Research Context

OpenTangYuan was developed as research software, but its README is written primarily for users and developers who want to run, inspect, extend, or integrate the runtime. This section summarizes the research-oriented ideas behind the project.

Compared with typical tool-calling architectures, OpenTangYuan adds a practical runtime layer for safer and more reusable agent-based office automation:

1. **Manifest-driven skill registry**  
   Each local skill is described in a structured `skill-manifest.json`, including its capabilities, parameters, supported actions, usage examples, constraints, and side effects. Agents can discover and query skills on demand instead of loading all tool descriptions at once.

2. **Workflow-based multi-step execution**  
   OpenTangYuan supports both reusable workflows stored in a database and ad-hoc temporary workflows generated at runtime. Multiple built-in skills can be orchestrated into traceable automation pipelines.

3. **Trusted local runtime**  
   Sensitive operations such as file access, email, browser control, screenshots, local tools, and enterprise system interactions stay inside the local trusted environment. The cloud-side agent handles understanding, planning, and parameter generation, but not direct access to private resources.

4. **Cloud-local hybrid architecture**  
   By decoupling AI decision-making from actual execution privileges, OpenTangYuan keeps sensitive data and permissions on the user's side while still allowing cloud agents to assist with task decomposition and workflow planning.

5. **Policy-controlled side effects**  
   State-changing actions are explicitly controlled and logged. These include sending or replying to emails, downloading attachments, copying, moving, or deleting files, printing, launching programs, and calling external tools. Authentication, allowlists, policy checks, and execution logs add further safeguards.

6. **Reusable capability discovery APIs**  
   A small set of REST endpoints makes it possible to integrate OpenTangYuan with Coze, Dify, GPTs, or custom agent frameworks. The same API pattern supports skill discovery, detail lookup, workflow retrieval, and unified execution.

---

## Citation

If you use OpenTangYuan in your research or project, please cite it. Once the SoftwareX paper is published, this entry will be updated.

### BibTeX

```bibtex
@software{opentangyuan,
  title        = {OpenTangYuan: A cloud-local agent workflow runtime for privacy-sensitive office automation},
  author       = {Liu, Mingyang and Contributors},
  year         = {2026},
  url          = {https://github.com/wrnas/OpenTangYuan},
  license      = {MIT},
  version      = {v1.1.2}
}
```

---

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE) for details.

---

## Acknowledgements

Thank you to everyone who contributed to the design, development, testing, and feedback of OpenTangYuan.
