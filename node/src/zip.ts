import type { Readable } from "node:stream";
import yauzl, { type Entry, type ZipFile } from "yauzl";

import type { ResolvedConversionOptions } from "./index.js";

export class XlsxArchive {
  readonly #path: string;
  readonly #entries: Set<string>;

  private constructor(path: string, entries: Set<string>) {
    this.#path = path;
    this.#entries = entries;
  }

  static async open(path: string, options: ResolvedConversionOptions): Promise<XlsxArchive> {
    const zip = await openZip(path);
    const entries = new Set<string>();
    let total = 0;
    try {
      if (zip.entryCount > options.maximumZipEntries) {
        throw new Error("The XLSX package exceeds the ZIP entry limit.");
      }
      await eachEntry(zip, (entry) => {
        total += entry.uncompressedSize;
        if (total > options.maximumUncompressedBytes) {
          throw new Error("The XLSX package exceeds the uncompressed size limit.");
        }
        if (
          entry.uncompressedSize > 0 &&
          (entry.compressedSize === 0 ||
            entry.uncompressedSize / entry.compressedSize > options.maximumCompressionRatio)
        ) {
          throw new Error("An XLSX entry exceeds the compression ratio limit.");
        }
        entries.add(entry.fileName);
      });
    } finally {
      zip.close();
    }
    return new XlsxArchive(path, entries);
  }

  has(path: string): boolean {
    return this.#entries.has(path);
  }

  async withEntry<T>(path: string, use: (stream: Readable) => Promise<T>): Promise<T> {
    if (!this.has(path)) throw new Error(`Worksheet part was not found: ${path}`);
    const zip = await openZip(this.#path);
    try {
      const entry = await findEntry(zip, path);
      const stream = await openEntryStream(zip, entry);
      return await use(stream);
    } finally {
      zip.close();
    }
  }
}

function openZip(path: string): Promise<ZipFile> {
  return new Promise((resolve, reject) => {
    yauzl.open(path, { lazyEntries: true, autoClose: false, validateEntrySizes: true }, (error, zip) => {
      if (error || !zip) reject(error ?? new Error("The file is not a valid XLSX workbook."));
      else resolve(zip);
    });
  });
}

function eachEntry(zip: ZipFile, visit: (entry: Entry) => void): Promise<void> {
  return new Promise((resolve, reject) => {
    zip.on("error", reject);
    zip.on("entry", (entry: Entry) => {
      try {
        visit(entry);
        zip.readEntry();
      } catch (error) {
        reject(error);
      }
    });
    zip.on("end", resolve);
    zip.readEntry();
  });
}

function findEntry(zip: ZipFile, path: string): Promise<Entry> {
  return new Promise((resolve, reject) => {
    zip.on("error", reject);
    zip.on("entry", (entry: Entry) => {
      if (entry.fileName === path) resolve(entry);
      else zip.readEntry();
    });
    zip.on("end", () => reject(new Error(`Worksheet part was not found: ${path}`)));
    zip.readEntry();
  });
}

function openEntryStream(zip: ZipFile, entry: Entry): Promise<Readable> {
  return new Promise((resolve, reject) => {
    zip.openReadStream(entry, (error, stream) => {
      if (error) reject(error);
      else resolve(stream);
    });
  });
}