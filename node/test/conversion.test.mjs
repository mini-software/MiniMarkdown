import assert from "node:assert/strict";
import { createReadStream, createWriteStream, promises as fs } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { PassThrough, Writable } from "node:stream";
import { pipeline } from "node:stream/promises";
import { spawn } from "node:child_process";
import test from "node:test";

import yazl from "yazl";

import { convertPath, convertStream } from "../dist/index.js";

test("converts common cell types and sparse rows exactly", async () => {
  await withWorkbook(commonWorkbook(), async (path) => {
    const output = collectOutput();
    await convertPath(path, output.stream);
    assert.equal(
      output.text(),
      "## Data\r\n\r\n" +
        "| Name | Note |  | Active | Date |\r\n" +
        "| --- | --- | --- | --- | --- |\r\n" +
        "| Alice | A\\|B<br>C |  | TRUE | 2023-03-15 |\r\n" +
        "|  |  | 42.5 |  |  |\r\n",
    );
  });
});

test("converts stream input without ending caller-owned output", async () => {
  await withWorkbook(commonWorkbook(), async (path) => {
    const output = collectOutput();
    await convertStream(createReadStream(path), output.stream);
    assert.match(output.text(), /\| Alice \|/);
    assert.equal(output.stream.writableEnded, false);
  });
});

test("rejects packages over resource limits", async () => {
  await withWorkbook(commonWorkbook(), async (path) => {
    await assert.rejects(
      convertPath(path, new PassThrough(), { maximumPackageBytes: 1 }),
      /compressed size limit/,
    );
  });
});

test("rejects malformed packages", async () => {
  const directory = await fs.mkdtemp(join(tmpdir(), "minimarkdown-node-test-"));
  const path = join(directory, "malformed.xlsx");
  try {
    await fs.writeFile(path, "not an xlsx package");
    await assert.rejects(convertPath(path, new PassThrough()));
  } finally {
    await fs.rm(directory, { recursive: true, force: true });
  }
});

test("writes a large worksheet incrementally", async () => {
  const rows = 20_000;
  await withWorkbook(largeWorkbook(rows), async (path) => {
    const output = new CountingWriter();
    await convertPath(path, output);
    assert.equal(output.lineCount, rows + 3);
    assert.ok(output.maximumWriteLength < 64, `maximum write was ${output.maximumWriteLength} bytes`);
  });
});

test("implements the shared CLI contract", async () => {
  const help = await runCli(["--help"]);
  assert.equal(help.code, 0);
  assert.match(help.stdout, /^Usage: minimarkdown/);

  const missingOutput = await runCli(["input.xlsx", "-o"]);
  assert.equal(missingOutput.code, 2);
  assert.match(missingOutput.stderr, /Missing output path/);

  await withWorkbook(commonWorkbook(), async (path) => {
    const converted = await runCli([path]);
    assert.equal(converted.code, 0);
    assert.match(converted.stdout, /^## Data\r\n/);
  });
});

function commonWorkbook() {
  return {
    "[Content_Types].xml": "<?xml version=\"1.0\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"/>",
    "xl/workbook.xml": "<?xml version=\"1.0\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><workbookPr date1904=\"0\"/><sheets><sheet name=\"Data\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>",
    "xl/_rels/workbook.xml.rels": "<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Target=\"worksheets/sheet1.xml\"/></Relationships>",
    "xl/sharedStrings.xml": "<?xml version=\"1.0\"?><sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><si><t>Name</t></si><si><t>Alice</t></si><si><r><t>A|B</t></r><r><t>\nC</t></r></si></sst>",
    "xl/styles.xml": "<?xml version=\"1.0\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><cellXfs count=\"2\"><xf numFmtId=\"0\"/><xf numFmtId=\"14\"/></cellXfs></styleSheet>",
    "xl/worksheets/sheet1.xml": "<?xml version=\"1.0\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c><c r=\"B1\" t=\"inlineStr\"><is><t>Note</t></is></c><c r=\"D1\" t=\"inlineStr\"><is><t>Active</t></is></c><c r=\"E1\" t=\"inlineStr\"><is><t>Date</t></is></c></row><row r=\"2\"><c r=\"A2\" t=\"s\"><v>1</v></c><c r=\"B2\" t=\"s\"><v>2</v></c><c r=\"D2\" t=\"b\"><v>1</v></c><c r=\"E2\" s=\"1\"><v>45000</v></c></row><row r=\"3\"><c r=\"C3\"><v>42.5</v></c></row></sheetData></worksheet>",
  };
}

function largeWorkbook(rows) {
  let sheet = "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>";
  for (let row = 1; row <= rows; row++) {
    sheet += `<row r=\"${row}\"><c r=\"A${row}\"><v>${row}</v></c></row>`;
  }
  sheet += "</sheetData></worksheet>";
  return {
    "xl/workbook.xml": "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Large\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>",
    "xl/_rels/workbook.xml.rels": "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Target=\"worksheets/sheet1.xml\"/></Relationships>",
    "xl/worksheets/sheet1.xml": sheet,
  };
}

async function withWorkbook(entries, action) {
  const directory = await fs.mkdtemp(join(tmpdir(), "minimarkdown-node-test-"));
  const path = join(directory, "fixture.xlsx");
  try {
    const zip = new yazl.ZipFile();
    for (const [name, content] of Object.entries(entries)) {
      zip.addBuffer(Buffer.from(content), name, { compress: false });
    }
    zip.end();
    await pipeline(zip.outputStream, createWriteStream(path));
    await action(path);
  } finally {
    await fs.rm(directory, { recursive: true, force: true });
  }
}

function collectOutput() {
  const chunks = [];
  const stream = new Writable({
    write(chunk, _encoding, callback) {
      chunks.push(Buffer.from(chunk));
      callback();
    },
  });
  return { stream, text: () => Buffer.concat(chunks).toString("utf8") };
}

class CountingWriter extends Writable {
  lineCount = 0;
  maximumWriteLength = 0;

  _write(chunk, _encoding, callback) {
    this.maximumWriteLength = Math.max(this.maximumWriteLength, chunk.length);
    for (const byte of chunk) if (byte === 10) this.lineCount++;
    callback();
  }
}

function runCli(args) {
  return new Promise((resolve, reject) => {
    const child = spawn(process.execPath, ["dist/cli.js", ...args], {
      cwd: new URL("..", import.meta.url),
      stdio: ["ignore", "pipe", "pipe"],
    });
    let stdout = "";
    let stderr = "";
    child.stdout.setEncoding("utf8").on("data", (value) => (stdout += value));
    child.stderr.setEncoding("utf8").on("data", (value) => (stderr += value));
    child.once("error", reject);
    child.once("close", (code) => resolve({ code, stdout, stderr }));
  });
}