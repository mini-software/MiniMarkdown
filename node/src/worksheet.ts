import type { ResolvedConversionOptions } from "./index.js";
import type { SharedStringStore } from "./shared-strings.js";
import type { CellStyleStore } from "./styles.js";
import type { MarkdownWriter } from "./writer.js";
import type { XlsxArchive } from "./zip.js";
import { attribute, parseXml } from "./xml.js";

export interface WorksheetBounds {
  firstRow: number;
  lastRow: number;
  firstColumn: number;
  lastColumn: number;
}

interface Cell {
  row: number;
  column: number;
  type: string;
  style: number;
  value: string;
}

export async function scanWorksheet(
  archive: XlsxArchive,
  path: string,
  options: ResolvedConversionOptions,
): Promise<WorksheetBounds> {
  const bounds: WorksheetBounds = {
    firstRow: Number.MAX_SAFE_INTEGER,
    lastRow: 0,
    firstColumn: Number.MAX_SAFE_INTEGER,
    lastColumn: 0,
  };
  await visitCells(archive, path, options, (cell) => {
    if (cell.value.length === 0) return;
    bounds.firstRow = Math.min(bounds.firstRow, cell.row);
    bounds.lastRow = Math.max(bounds.lastRow, cell.row);
    bounds.firstColumn = Math.min(bounds.firstColumn, cell.column);
    bounds.lastColumn = Math.max(bounds.lastColumn, cell.column);
  });
  return bounds;
}

export async function writeWorksheet(
  archive: XlsxArchive,
  path: string,
  bounds: WorksheetBounds,
  strings: SharedStringStore,
  styles: CellStyleStore,
  writer: MarkdownWriter,
  options: ResolvedConversionOptions,
): Promise<void> {
  const values = new Map<number, string>();
  let currentRow = bounds.firstRow;
  let headerWritten = false;
  await visitCells(
    archive,
    path,
    options,
    (cell) => {
      if (cell.row < bounds.firstRow || cell.row > bounds.lastRow) return;
      while (cell.row > currentRow) {
        writeRow(writer, values, bounds.firstColumn, bounds.lastColumn);
        if (!headerWritten) {
          writeSeparator(writer, bounds.lastColumn - bounds.firstColumn + 1);
          headerWritten = true;
        }
        values.clear();
        currentRow++;
      }
      values.set(cell.column, formatValue(cell, strings, styles));
    },
    writer,
  );
  while (currentRow <= bounds.lastRow) {
    writeRow(writer, values, bounds.firstColumn, bounds.lastColumn);
    if (!headerWritten) writeSeparator(writer, bounds.lastColumn - bounds.firstColumn + 1);
    headerWritten = true;
    values.clear();
    currentRow++;
  }
}

async function visitCells(
  archive: XlsxArchive,
  path: string,
  options: ResolvedConversionOptions,
  visit: (cell: Cell) => void,
  writer?: MarkdownWriter,
): Promise<void> {
  await archive.withEntry(path, async (stream) => {
    let inferredRow = 0;
    let currentRow = 0;
    let inferredColumn = 0;
    let cell: Cell | undefined;
    let capture = false;
    await parseXml(stream, {
      open(tag) {
        if (tag.local === "row") {
          inferredRow++;
          currentRow = positiveInteger(attribute(tag, "r"), inferredRow);
          inferredRow = currentRow;
          inferredColumn = 0;
          if (currentRow > options.maximumRows) throw new Error("The worksheet exceeds the row limit.");
        } else if (tag.local === "c") {
          const reference = attribute(tag, "r");
          const column = reference ? parseColumn(reference) : inferredColumn + 1;
          inferredColumn = column;
          if (column > options.maximumColumns) throw new Error("The worksheet exceeds the column limit.");
          cell = {
            row: currentRow,
            column,
            type: attribute(tag, "t") ?? "",
            style: positiveInteger(attribute(tag, "s"), 0),
            value: "",
          };
        } else if (tag.local === "v" && cell) {
          capture = true;
        } else if (tag.local === "t" && cell?.type === "inlineStr") {
          capture = true;
        }
      },
      text(value) {
        if (capture && cell) cell.value += value;
      },
      close(tag) {
        if (tag.local === "v" || tag.local === "t") capture = false;
        else if (tag.local === "c" && cell) {
          visit(cell);
          cell = undefined;
        }
      },
      afterChunk: () => writer?.drain() ?? Promise.resolve(),
    });
  });
}

function formatValue(cell: Cell, strings: SharedStringStore, styles: CellStyleStore): string {
  if (cell.type === "s") return strings.get(cell.value);
  if (cell.type === "b") return cell.value === "1" ? "TRUE" : "FALSE";
  const number = Number(cell.value);
  if (cell.type === "" && cell.value.length > 0 && Number.isFinite(number)) {
    return styles.format(cell.style, number, cell.value);
  }
  return cell.value;
}

function writeRow(writer: MarkdownWriter, values: Map<number, string>, first: number, last: number): void {
  writer.write("|");
  for (let column = first; column <= last; column++) writer.write(` ${escapeCell(values.get(column) ?? "")} |`);
  writer.write("\r\n");
}

function writeSeparator(writer: MarkdownWriter, columns: number): void {
  writer.write(`|${" --- |".repeat(columns)}\r\n`);
}

function escapeCell(value: string): string {
  return value
    .replaceAll("\\", "\\\\")
    .replaceAll("|", "\\|")
    .replaceAll("\r\n", "<br>")
    .replaceAll("\r", "<br>")
    .replaceAll("\n", "<br>");
}

function positiveInteger(value: string | undefined, fallback: number): number {
  const parsed = Number(value);
  return Number.isInteger(parsed) && parsed > 0 ? parsed : fallback;
}

function parseColumn(reference: string): number {
  let column = 0;
  for (const character of reference) {
    if (character < "A" || character > "Z") break;
    column = column * 26 + character.charCodeAt(0) - 64;
  }
  if (column === 0 || !Number.isSafeInteger(column)) throw new Error("A cell reference is invalid.");
  return column;
}