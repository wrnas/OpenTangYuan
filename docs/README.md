[中文版](README_zh-CN.md) · [Back to Project Home](../README.md)

# OpenTangYuan Documentation

This directory contains the detailed OpenTangYuan documentation. The project homepage introduces the software, quick start, and core concepts; the documents in this directory cover architecture, APIs, configuration, deployment, agent integration, and troubleshooting.

## Documentation Map

| Document | Intended readers | Main topics |
|---|---|---|
| [Architecture and Runtime Model](architecture.md) | Architects, developers, integrators | Cloud-local boundaries, capability discovery, workflow runtime, context propagation, and extension mechanisms. |
| [Core API Reference](api.md) | Agent developers, client developers | Capability discovery, workflow retrieval, unified execution, response formats, and calling principles. |
| [Built-in Skills Reference](builtin-skills.md) | Integration developers, skill developers | Actions and examples for email, file, browser, messaging, and local-tool skills. |
| [Configuration and Security Controls](configuration-security.md) | Operators, administrators | Email configuration, path allowlists, executable allowlists, credential handling, and production security guidance. |
| [Agent Integration Guide](agent-integration.md) | Agent-platform developers | Standard call flow, tool design, stop rules, parameter handling, and side-effect control. |
| [Coze System Prompt](coze-system-prompt.md) | Coze users | A reusable system prompt that should be adapted to the current deployment. |
| [Deployment and Platform Support](deployment-platform.md) | Operators, developers | Windows, Linux, and Docker scope; build, runtime, and release guidance. |
| [Troubleshooting](troubleshooting.md) | Users, operators, developers | Diagnostics for email, files, browsers, workflow context, and common failures. |

## Suggested Reading Paths

- **First-time visitors:** start with the root `README.md`.
- **Client or agent-plugin developers:** read `api.md` and `agent-integration.md`.
- **Real office-environment configuration:** read `configuration-security.md`.
- **Skill or workflow extension:** read `architecture.md` and `builtin-skills.md`.
- **Deployment preparation:** read `deployment-platform.md`.

## Documentation Conventions

- API paths, field names, `SkillCode` values, and template variables retain their original English identifiers.
- Email addresses, paths, domains, and credentials in examples are placeholders.
- The actual port is determined by startup logs, `launchSettings.json`, or deployment configuration.
- APIs with side effects should be tested in an isolated environment first.
- Examples should remain consistent with the actual response structure of the current release.
