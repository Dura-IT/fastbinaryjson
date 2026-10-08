# Security Policy

## Supported Versions

| Version | Supported |
|---------|-----------|
| 0.1.x   | Yes       |

## Reporting a Vulnerability

**Please do not open a public GitHub issue for security vulnerabilities.**

Report security issues by email to **DuraITSolutions@pm.me**. Include:

- A description of the vulnerability and its potential impact
- Steps to reproduce or a minimal proof-of-concept
- The affected version(s)
- Any suggested fix, if you have one

You can expect an acknowledgement within **72 hours** and a status update within **7 days**. If the issue is confirmed, a patched release will be published and you will be credited in the release notes unless you prefer to remain anonymous.

## Scope

Malformed or malicious input that crashes the process, exhausts memory, or throws anything other than `BjsonException` from a reading entry point is in scope. Bytes from an untrusted source should not be deserialized into a type graph that you do not control, because the format carries type names.
