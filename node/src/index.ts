import { createWriteStream, promises as fs } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import type { Readable, Writable } from "node:stream";
import { pipeline } from "node:stream/promises";

import { SharedStringStore } from "./shared-strings.js";
import { CellStyleStore } from "./styles.js";
import { readWorkbook } from "./workbook.js";
import { scanWorksheet, writeWorksheet } from "./worksheet.js";
import { MarkdownWriter } from "./writer.js";
import { XlsxArchive } from "./zip.js";

export interface ConversionOptions {
  maximumColumns?: number;
  maximumRows?: number;
  maximumUncompressedBytes?: number;
  maximumPackageBytes?: number;
  maximumZipEntries?: number;
  maximumCompressionRatio?: number;
}

export interface ResolvedConversionOptions {
  maximumColumns: number;
  maximumRows: number;
  maximumUncompressedBytes: number;
  maximumPackageBytes: number;
  maximumZipEntries: number;
  maximumCompressionRatio: number;
}

const defaults: ResolvedConversionOptions = {
  maximumColumns: 16_384,
  maximumRows: 1_048_576,
  maximumUncompressedBytes: 512 * 1024 * 1024,
  maximumPackageBytes: 256 * 1024 * 1024,
  maximumZipEntries: 10_000,
  maximumCompressionRatio: 1_000,
};

export async function convertFile(
  inputPath: string,
  outputPath: string,
  options: ConversionOptions = {},
): Promise<void> {
  const output = createWriteStream(outputPath, { encoding: "utf8" });
  try {
    await convertPath(inputPath, output, options);
    output.end();
    await new Promise<void>((resolve, reject) => {
      output.once("finish", resolve);
      output.once("error", reject);
    });
  } catch (error) {
    output.destroy();
    throw error;
  }
}

export async function convertStream(
  input: Readable,
  output: Writable,
  options: ConversionOptions = {},
): Promise<void> {
  const resolved = resolveOptions(options);
  const directory = await fs.mkdtemp(join(tmpdir(), "minimarkdown-node-"));
  const packagePath = join(directory, "package.xlsx");
  let total = 0;
  input.on("data", (chunk: Buffer | string) => {
    total += Buffer.byteLength(chunk);
    if (total > resolved.maximumPackageBytes) {
      input.destroy(new Error("The XLSX package exceeds the compressed size limit."));
    }
  });
  try {
    await pipeline(input, createWriteStream(packagePath));
    await convertPath(packagePath, output, resolved);
  } finally {
    await fs.rm(directory, { recursive: true, force: true });
  }
}

export async function convertPath(
  inputPath: string,
  output: Writable,
  options: ConversionOptions = {},
): Promise<void> {
  const resolved = resolveOptions(options);
  const stat = await fs.stat(inputPath);
  if (stat.size > resolved.maximumPackageBytes) {
    throw new Error("The XLSX package exceeds the compressed size limit.");
  }

  const archive = await XlsxArchive.open(inputPath, resolved);
  const workbook = await readWorkbook(archive);
  const styles = await CellStyleStore.load(archive, workbook.uses1904DateSystem);
  const strings = await SharedStringStore.load(archive);
  const writer = new MarkdownWriter(output);
  let wroteSheet = false;
  try {
    for (const sheet of workbook.sheets) {
      const bounds = await scanWorksheet(archive, sheet.path, resolved);
      if (bounds.lastRow === 0) continue;
      if (wroteSheet) writer.write("\r\n");
      writer.write(`## ${escapeHeading(sheet.name)}\r\n\r\n`);
      await writer.drain();
      await writeWorksheet(archive, sheet.path, bounds, strings, styles, writer, resolved);
      wroteSheet = true;
    }
    await writer.drain();
  } finally {
    strings.close();
  }
}

function resolveOptions(options: ConversionOptions): ResolvedConversionOptions {
  const resolved = { ...defaults, ...options };
  for (const value of Object.values(resolved)) {
    if (!Number.isFinite(value) || value < 1) {
      throw new RangeError("All conversion limits must be positive.");
    }
  }
  return resolved;
}

function escapeHeading(value: string): string {
  return value.replaceAll("\\", "\\\\").replaceAll("#", "\\#");
}