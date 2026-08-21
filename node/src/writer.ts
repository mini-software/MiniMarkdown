import { once } from "node:events";
import type { Writable } from "node:stream";

export class MarkdownWriter {
  readonly #output: Writable;
  #needsDrain = false;

  constructor(output: Writable) {
    this.#output = output;
  }

  write(value: string): void {
    if (!this.#output.write(value, "utf8")) this.#needsDrain = true;
  }

  async drain(): Promise<void> {
    if (!this.#needsDrain) return;
    this.#needsDrain = false;
    await once(this.#output, "drain");
  }
}