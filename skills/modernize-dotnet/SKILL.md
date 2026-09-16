---
name: modernize-dotnet
description: Пишет, рефакторит и ревьюит C#-код по стандартам .NET 10 и C# 14. Использовать когда задача связана с C#, .NET, ASP.NET Core, EF Core, Blazor, MAUI, source generators — генерация нового кода, рефакторинг, код-ревью.
model: sonnet
---

# Modernize Dotnet

## Quick start

ALWAYS проверяй TargetFramework перед генерацией кода.
DO таргетируй net10.0 по умолчанию, NEVER спрашивай разрешение.
MUST уважай явно указанную старую версию фреймворка.

## Workflows

### Новый код

DO настраивай проект по разделу 1 REFERENCE.md.
MUST включай Nullable, ImplicitUsings, TreatWarningsAsErrors.
NEVER указывай LangVersion для net10.0.
DO выноси примеры из 20 строк в file-based app.

### Рефакторинг

MUST прогоняй код через чек-лист раздела 10 REFERENCE.md.
DO заменяй устаревшие идиомы новыми конструкциями.
NEVER переписывай рабочие extension-методы ради синтаксиса.
DO помечай фичу C# 13/14 одной строкой комментария.

### Ревью

MUST отклоняй код с конструкциями из чек-листа раздела 10.
DO требуй CancellationToken в каждом async-методе.
NEVER пропускай рефлексию без DynamicallyAccessedMembers.
ALWAYS проверяй отсутствие выдуманных API.

## Advanced features

DO читай [REFERENCE.md](REFERENCE.md) для таблиц версий и примеров.

> **HARD GATE** — Do NOT выдавай код без прогона через чек-лист раздела 10 и указания требуемой версии пакета.

→ verify: `dotnet --version`
