# Email Sender

Send every Umbraco email through an HTTP API (Azure Communication Services, Postmark, GOV.UK Notify) instead of SMTP.

[![Build](https://github.com/PascalA94/Umbraco.Community.EmailSender/actions/workflows/build.yml/badge.svg)](https://github.com/PascalA94/Umbraco.Community.EmailSender/actions/workflows/build.yml)
[![NuGet](https://img.shields.io/nuget/v/Umbraco.Community.EmailSender)](https://www.nuget.org/packages/Umbraco.Community.EmailSender)
[![NuGet downloads](https://img.shields.io/nuget/dt/Umbraco.Community.EmailSender)](https://www.nuget.org/packages/Umbraco.Community.EmailSender)
[![Licence: MIT](https://img.shields.io/badge/licence-MIT-blue)](https://github.com/PascalA94/Umbraco.Community.EmailSender/blob/main/LICENSE)

A community package, not made or supported by Umbraco HQ. Requires Umbraco 17 (17.8.0 or later) or Umbraco 18 (18.3.0 or later), on .NET 10.

<!-- TODO before the first release: add the best screenshot from assets/screenshots/ with an absolute URL, so it also shows on NuGet:
![What it looks like](https://raw.githubusercontent.com/PascalA94/Umbraco.Community.EmailSender/main/assets/screenshots/screenshot-01-<what>.png) -->

## Compatibility

| Package version | Umbraco |
|---|---|
| 17.x | 17 (17.8.0 or later) |
| 18.x | 18 (18.3.0 or later) |

## Install

```bash
dotnet add package Umbraco.Community.EmailSender
```

## Use

TODO: what the editor sees, and the settings (`appsettings.json` section, defaults).

## Develop

See `.github/CONTRIBUTING.md` and `CLAUDE.md` for the commands. In short: `./scripts/test.sh` builds and tests both Umbraco lines (17 and 18) from this one branch; run the demo site under `demo/`.

## Licence

MIT. See `LICENSE`. Security issues: see `SECURITY.md`.
