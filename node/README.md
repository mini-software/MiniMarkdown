# MiniMarkdown for Node.js

The Node.js implementation provides a TypeScript API and compiled JavaScript CLI with the same deterministic XLSX-to-Markdown contract as the C# and Rust implementations.

## CLI

```powershell
npm install --prefix node
npm run build --prefix node
node node/dist/cli.js report.xlsx -o report.md
node node/dist/cli.js report.xlsx
Get-Content report.xlsx -AsByteStream | node node/dist/cli.js -
```

## TypeScript

```typescript
import { convertFile, convertStream } from "minimarkdown";

await convertFile("report.xlsx", "report.md");
await convertStream(input, output);
```

The implementation lazily reads ZIP entries, parses XML incrementally, honors Writable backpressure, and stores shared strings in a temporary disk-backed index. Caller-owned streams remain open.

## Validation

```powershell
npm test --prefix node
```