# Contributing to ForgeMap

Thank you for your interest in contributing to ForgeMap! This guide will help you get started.

## How to Contribute

1. **Fork** the repository
2. **Create a branch** from `main` for your change
3. **Make your changes** and add tests
4. **Submit a pull request** using the provided PR template

## Development Setup

```bash
git clone https://github.com/<your-fork>/ForgeMap.git
cd ForgeMap
dotnet build ForgeMap.slnx
dotnet test ForgeMap.slnx
```

Requires the .NET SDK (see `global.json` for the minimum SDK version).

## Code Style

Follow the `.editorconfig` rules included in the repository. No additional linters or formatters are required.

## Source Generator Notes

ForgeMap is a **Roslyn incremental source generator**. The main entry point is `ForgeMapGenerator.cs` in `src/ForgeMap.Generator/`, which constructs and uses `ForgeCodeEmitter`. The `ForgeCodeEmitter*` files in that directory contain the code emission implementation.

Key things to know:

- The generator runs at compile time inside the IDE and the build pipeline
- Changes to the generator affect every project that references ForgeMap
- New diagnostics must be registered in `DiagnosticDescriptors.cs` and tracked in `src/ForgeMap.Generator/AnalyzerReleases.Unshipped.md`

## Running Benchmarks

Benchmarks live in the `benchmarks/` directory:

```bash
dotnet run -c Release --project benchmarks/ForgeMap.Benchmarks
```

## Issues and Pull Requests

- Use the provided issue templates when filing bugs, feature requests, or questions
- Keep pull requests focused — one concern per PR
- Link your PR to a related issue when applicable

## Commit Conventions

This project follows [Conventional Commits](https://www.conventionalcommits.org/):

- `feat:` — new feature
- `fix:` — bug fix
- `docs:` — documentation changes
- `chore:` — maintenance tasks
- `refactor:` — code restructuring without behavior change

Example: `feat: add support for nested object mapping`
