[中文版](architecture_zh-CN.md) · [Back to Documentation](README.md) · [Back to Project Home](../README.md)

# Architecture and Runtime Model

## 1. Design Goals

OpenTangYuan is designed for tasks in which an external AI agent can understand a request and plan steps, while real execution must access local files, email, browsers, desktop applications, enterprise messaging platforms, or internal systems.

The system separates task understanding from execution privileges:

```text
External agent
  ├─ Understand user intent
  ├─ Query available capabilities
  ├─ Select workflows or built-in skills
  └─ Generate structured parameters
        │
        ▼
OpenTangYuan trusted local runtime
  ├─ Validate requests and parameters
  ├─ Execute workflows
  ├─ Invoke local skills
  ├─ Manage context
  └─ Return structured results
        │
        ▼
Local and enterprise resources
```

The external agent does not directly hold local execution privileges. Files, email content, screenshots, and internal-system data are processed by the local runtime.

## 2. Main Components

| Component | Responsibility |
|---|---|
| Capability Catalog | Aggregates database workflows and built-in skills from `skill-manifest.json`. |
| Skill Manifest | Describes skill names, purposes, actions, parameters, examples, constraints, and side effects. |
| Workflow Repository | Stores reusable multi-step task definitions. |
| Workflow Runtime | Handles step scheduling, template resolution, context propagation, execution, and failure reporting. |
| Built-in Skill Executors | Invoke email, file, browser, desktop, local-program, and enterprise-messaging capabilities. |
| Unified API | Exposes discovery, query, and execution interfaces to agents, desktop clients, and other systems. |
| Security Controls | Restrict execution through authentication, path allowlists, executable allowlists, policy validation, and logging. |

## 3. Capability Discovery

The recommended discovery sequence is:

```text
1. GetSkillListForAI
   Retrieve summaries of workflows and built-in skills

2. GetSkillAction or GetBuiltinSkillDetail
   Retrieve the selected workflow steps or skill parameters

3. ExecuteSkill or ExecuteSkillForCoze
   Submit and execute the task
```

This model avoids loading the complete skill dictionary into every agent conversation and allows new skills to be added without changing the agent's core logic.

### Workflow First

When the capability catalog already contains a workflow that fully covers the task, the agent should prefer that workflow. Reuse reduces repeated planning and makes common task sequences more consistent.

### Built-in Skills as Composition Units

When no matching workflow exists, the agent can query the detailed definitions of one or more built-in skills and construct a temporary workflow.

## 4. Execution Modes

The unified execution endpoint supports three modes:

| Mode | Detection | Purpose |
|---|---|---|
| Built-in skill | No `Steps`, and no matching database workflow | Execute one atomic capability. |
| Database workflow | A workflow with the same `SkillCode` exists in the database | Execute a predefined multi-step task. |
| Temporary workflow | The request contains a non-empty `Steps` array | Execute a task generated dynamically by an agent or client. |

## 5. Workflow Runtime

The workflow runtime executes steps sequentially:

```text
Receive workflow steps
      ↓
Initialize execution context
      ↓
Resolve template variables in the current step
      ↓
Invoke the corresponding built-in skill
      ↓
Store the result as stepN
      ↓
Allow later steps to reference earlier results
      ↓
Return the final result and execution status
```

The runtime is responsible for:

- step scheduling;
- parameter template resolution;
- context management;
- built-in skill invocation;
- result packaging;
- debug logging;
- error and failure reporting.

## 6. Context Propagation

Each step result is written into the context in sequence:

```text
step0
step1
step2
...
```

Later steps can reference prior outputs through template variables:

```text
{{step0}}
{{step0.path}}
{{step0.data.path}}
{{step0.data.firstPath}}
{{step1.result}}
```

The runtime resolves templates against the actual response structure of the previous step. Agents should not guess intermediate file paths or hard-code values generated at runtime.

## 7. Cloud-Local Boundary

| Area | May process | Should not be responsible for |
|---|---|---|
| External agent | User intent, capability summaries, parameter definitions, and task planning | Directly reading local files, email, or internal-system data |
| Local runtime | Execution, sensitive-data processing, permission validation, and context management | Replacing the agent's natural-language understanding |
| Enterprise systems | Business data and operational capabilities | Direct exposure to an uncontrolled external agent |

In a real deployment, communication between the external agent and local runtime should still be protected by HTTPS, a gateway, VPN, IP restrictions, or other access controls.

## 8. Side-Effect Control

The following operations change external state:

- sending or replying to email;
- downloading, copying, moving, renaming, or deleting files;
- printing files;
- launching programs;
- sending content to enterprise messaging platforms;
- invoking write operations in internal systems.

These operations should use the following controls:

1. validate required parameters before execution;
2. restrict accessible paths and executable programs;
3. stop immediately after success and do not repeat the action;
4. record key execution events;
5. require human confirmation for high-risk tasks;
6. limit retries after failure.

## 9. Extension Mechanisms

### Add a Built-in Skill

Implement the execution logic and register its capabilities, parameters, actions, examples, and constraints in `skill-manifest.json`.

### Add a Workflow

Compose multiple built-in skills into a sequence and store it in the database or create it through the management APIs.

### Integrate a New Enterprise System

Integration options include:

- REST APIs;
- browser automation;
- local command-line tools;
- custom plugins;
- enterprise messaging webhooks;
- internal service gateways.

### Extend Security Policies

Deployments can add role-based permissions, approval processes, path policies, executable policies, operational auditing, and alerting rules.

## 10. Related Documentation

- [Core API Reference](api.md)
- [Built-in Skills Reference](builtin-skills.md)
- [Agent Integration Guide](agent-integration.md)
- [Configuration and Security Controls](configuration-security.md)
