import { posix } from "node:path";

import type { XlsxArchive } from "./zip.js";
import { attribute, parseXml } from "./xml.js";

interface SheetInfo {
  name: string;
  path: string;
}

export interface WorkbookInfo {
  sheets: SheetInfo[];
  uses1904DateSystem: boolean;
}

export async function readWorkbook(archive: XlsxArchive): Promise<WorkbookInfo> {
  if (!archive.has("xl/workbook.xml") || !archive.has("xl/_rels/workbook.xml.rels")) {
    throw new Error("The file is not a valid XLSX workbook.");
  }
  const relationships = new Map<string, string>();
  await archive.withEntry("xl/_rels/workbook.xml.rels", async (stream) => {
    await parseXml(stream, {
      open(tag) {
        if (tag.local !== "Relationship") return;
        if (attribute(tag, "TargetMode")?.toLowerCase() === "external") return;
        const id = attribute(tag, "Id");
        const target = attribute(tag, "Target");
        if (id && target) relationships.set(id, resolvePartPath(target));
      },
    });
  });

  const result: WorkbookInfo = { sheets: [], uses1904DateSystem: false };
  await archive.withEntry("xl/workbook.xml", async (stream) => {
    await parseXml(stream, {
      open(tag) {
        if (tag.local === "workbookPr") {
          const value = attribute(tag, "date1904") ?? "";
          result.uses1904DateSystem = value === "1" || value.toLowerCase() === "true";
        }
        if (tag.local !== "sheet") return;
        const id = attribute(tag, "id");
        const path = id ? relationships.get(id) : undefined;
        if (!path) throw new Error("A worksheet relationship is missing.");
        result.sheets.push({ name: attribute(tag, "name") ?? "Sheet", path });
      },
    });
  });
  return result;
}

function resolvePartPath(target: string): string {
  const normalized = target.replaceAll("\\", "/");
  const resolved = normalized.startsWith("/")
    ? posix.normalize(normalized).slice(1)
    : posix.normalize(posix.join("xl", normalized));
  if (resolved === ".." || resolved.startsWith("../")) {
    throw new Error("A package relationship escapes the XLSX package.");
  }
  return resolved;
}