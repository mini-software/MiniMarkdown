import type { XlsxArchive } from "./zip.js";
import { attribute, parseXml } from "./xml.js";

type CellNumberKind = "number" | "date" | "datetime" | "time" | "duration";

export class CellStyleStore {
  readonly #styles: CellNumberKind[];
  readonly #uses1904: boolean;

  private constructor(styles: CellNumberKind[], uses1904: boolean) {
    this.#styles = styles;
    this.#uses1904 = uses1904;
  }

  static async load(archive: XlsxArchive, uses1904: boolean): Promise<CellStyleStore> {
    if (!archive.has("xl/styles.xml")) return new CellStyleStore(["number"], uses1904);
    const custom = new Map<number, string>();
    const styles: CellNumberKind[] = [];
    let inCellFormats = false;
    await archive.withEntry("xl/styles.xml", async (stream) => {
      await parseXml(stream, {
        open(tag) {
          if (tag.local === "numFmt") {
            const id = Number(attribute(tag, "numFmtId"));
            if (Number.isInteger(id)) custom.set(id, attribute(tag, "formatCode") ?? "");
          } else if (tag.local === "cellXfs") {
            inCellFormats = true;
          } else if (tag.local === "xf" && inCellFormats) {
            const id = Number(attribute(tag, "numFmtId") ?? 0);
            styles.push(classify(id, custom.get(id)));
          }
        },
        close(tag) {
          if (tag.local === "cellXfs") inCellFormats = false;
        },
      });
    });
    return new CellStyleStore(styles.length > 0 ? styles : ["number"], uses1904);
  }

  format(style: number, value: number, original: string): string {
    const kind = this.#styles[style] ?? "number";
    if (kind === "number") return original;
    if (kind === "duration") {
      const total = Math.round(value * 86_400);
      const sign = total < 0 ? "-" : "";
      const absolute = Math.abs(total);
      return `${sign}${Math.floor(absolute / 3600)}:${pad(Math.floor(absolute / 60) % 60)}:${pad(absolute % 60)}`;
    }
    const epoch = this.#uses1904 ? Date.UTC(1904, 0, 1) : Date.UTC(1899, 11, 30);
    const date = new Date(epoch + Math.round(value * 86_400_000));
    const datePart = `${date.getUTCFullYear().toString().padStart(4, "0")}-${pad(date.getUTCMonth() + 1)}-${pad(date.getUTCDate())}`;
    const timePart = `${pad(date.getUTCHours())}:${pad(date.getUTCMinutes())}:${pad(date.getUTCSeconds())}`;
    return kind === "date" ? datePart : kind === "time" ? timePart : `${datePart} ${timePart}`;
  }
}

function classify(id: number, format = ""): CellNumberKind {
  if (id >= 14 && id <= 17) return "date";
  if (id === 46) return "duration";
  if ([18, 19, 20, 21, 45, 47].includes(id)) return "time";
  if (id === 22) return "datetime";
  const code = format.replace(/"[^"]*"/g, "").toLowerCase();
  if (code.includes("[h]") || code.includes("[m]") || code.includes("[s]")) return "duration";
  const hasDate = code.includes("y") || code.includes("d");
  const hasTime = code.includes("h") || code.includes("s");
  return hasDate && hasTime ? "datetime" : hasDate ? "date" : hasTime ? "time" : "number";
}

function pad(value: number): string {
  return value.toString().padStart(2, "0");
}