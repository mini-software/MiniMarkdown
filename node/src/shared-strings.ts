import { closeSync, mkdtempSync, openSync, readSync, rmSync, writeSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

import type { XlsxArchive } from "./zip.js";
import { parseXml } from "./xml.js";

export class SharedStringStore {
  readonly #directory: string;
  readonly #fd: number;
  readonly #offsets: number[] = [];
  #position = 0;

  private constructor() {
    this.#directory = mkdtempSync(join(tmpdir(), "minimarkdown-strings-"));
    this.#fd = openSync(join(this.#directory, "strings.bin"), "w+");
  }

  static async load(archive: XlsxArchive): Promise<SharedStringStore> {
    const store = new SharedStringStore();
    if (!archive.has("xl/sharedStrings.xml")) return store;
    let current: string | undefined;
    let inText = false;
    await archive.withEntry("xl/sharedStrings.xml", async (stream) => {
      await parseXml(stream, {
        open(tag) {
          if (tag.local === "si") current = "";
          else if (tag.local === "t" && current !== undefined) inText = true;
        },
        text(value) {
          if (inText && current !== undefined) current += value;
        },
        close(tag) {
          if (tag.local === "t") inText = false;
          else if (tag.local === "si") {
            store.#write(current ?? "");
            current = undefined;
          }
        },
      });
    });
    return store;
  }

  get(indexText: string): string {
    const index = Number(indexText);
    const offset = Number.isInteger(index) && index >= 0 ? this.#offsets[index] : undefined;
    if (offset === undefined) throw new Error("A shared string index is invalid.");
    const lengthBytes = Buffer.allocUnsafe(4);
    readSync(this.#fd, lengthBytes, 0, 4, offset);
    const value = Buffer.allocUnsafe(lengthBytes.readUInt32LE());
    readSync(this.#fd, value, 0, value.length, offset + 4);
    return value.toString("utf8");
  }

  close(): void {
    closeSync(this.#fd);
    rmSync(this.#directory, { recursive: true, force: true });
  }

  #write(value: string): void {
    const bytes = Buffer.from(value, "utf8");
    const length = Buffer.allocUnsafe(4);
    length.writeUInt32LE(bytes.length);
    this.#offsets.push(this.#position);
    writeSync(this.#fd, length, 0, length.length, this.#position);
    writeSync(this.#fd, bytes, 0, bytes.length, this.#position + 4);
    this.#position += 4 + bytes.length;
  }
}