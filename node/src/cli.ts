#!/usr/bin/env node

import { createWriteStream } from "node:fs";
import process from "node:process";
import type { Writable } from "node:stream";

import { convertPath, convertStream } from "./index.js";

const args = process.argv.slice(2);
if (args.length === 0 || args.includes("--help") || args.includes("-h")) {
	process.stdout.write("Usage: minimarkdown <input.xlsx|-> [-o output.md]\r\n");
	process.exitCode = args.length === 0 ? 2 : 0;
} else {
	const outputFlag = args.indexOf("-o", 1);
	const outputPath = outputFlag >= 0 ? args[outputFlag + 1] : undefined;
	if (outputFlag >= 0 && !outputPath) {
		process.stderr.write("Missing output path after -o.\r\n");
		process.exitCode = 2;
	} else {
		run(args[0]!, outputPath).catch((error: unknown) => {
			const message = error instanceof Error ? error.message : String(error);
			process.stderr.write(`Conversion failed: ${message}\r\n`);
			process.exitCode = 1;
		});
	}
}

async function run(inputPath: string, outputPath: string | undefined): Promise<void> {
	const output: Writable = outputPath ? createWriteStream(outputPath) : process.stdout;
	try {
		if (inputPath === "-") await convertStream(process.stdin, output);
		else await convertPath(inputPath, output);
	} finally {
		if (outputPath) output.end();
	}
}