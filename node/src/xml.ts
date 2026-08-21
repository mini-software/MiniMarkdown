import type { Readable } from "node:stream";
import { SaxesParser, type SaxesTagNS } from "saxes";

export interface XmlHandlers {
  open?(tag: SaxesTagNS): void;
  close?(tag: SaxesTagNS): void;
  text?(value: string): void;
  afterChunk?(): Promise<void>;
}

export async function parseXml(stream: Readable, handlers: XmlHandlers): Promise<void> {
  const parser = new SaxesParser({ xmlns: true });
  parser.on("opentag", (tag) => handlers.open?.(tag));
  parser.on("closetag", (tag) => handlers.close?.(tag));
  parser.on("text", (value) => handlers.text?.(value));
  parser.on("cdata", (value) => handlers.text?.(value));
  parser.on("doctype", () => {
    throw new Error("XML document type declarations are not allowed.");
  });
  stream.setEncoding("utf8");
  for await (const chunk of stream) {
    parser.write(chunk);
    await handlers.afterChunk?.();
  }
  parser.close();
}

export function attribute(tag: SaxesTagNS, localName: string): string | undefined {
  return Object.values(tag.attributes).find((value) => value.local === localName)?.value;
}