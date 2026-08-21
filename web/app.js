import { dotnet } from './_framework/dotnet.js';
import initRust, { convert_xlsx as convertWithRust } from './rust/minimarkdown.js';

const maximumFileBytes = 16 * 1024 * 1024;
const state = {
  engine: 'both',
  file: null,
  outputs: { csharp: '', rust: '' },
  selectedOutput: 'csharp',
  ready: false,
};

const elements = {
  runtimeStatus: document.querySelector('#runtime-status'),
  fileInput: document.querySelector('#file-input'),
  fileTitle: document.querySelector('#file-title'),
  fileDetail: document.querySelector('#file-detail'),
  dropZone: document.querySelector('#drop-zone'),
  convertButton: document.querySelector('#convert-button'),
  resultTitle: document.querySelector('#result-title'),
  resultDetail: document.querySelector('#result-detail'),
  outputTabs: document.querySelector('#output-tabs'),
  output: document.querySelector('#output'),
  copyButton: document.querySelector('#copy-button'),
  downloadButton: document.querySelector('#download-button'),
};

let convertWithCSharp;

async function loadEngines() {
  const [{ getAssemblyExports, getConfig }] = await Promise.all([
    dotnet.withDiagnosticTracing(false).create(),
    initRust(),
  ]);
  const config = getConfig();
  const exports = await getAssemblyExports(config.mainAssemblyName);
  convertWithCSharp = exports.MiniMarkdown.WebAssembly.Program.ConvertXlsx;
  state.ready = true;
  elements.runtimeStatus.classList.add('ready');
  elements.runtimeStatus.lastElementChild.textContent = 'Both AOT engines ready';
  updateConvertButton();
}

function selectFile(file) {
  if (!file) return;
  if (!file.name.toLowerCase().endsWith('.xlsx')) {
    showError('Choose an .xlsx workbook.');
    return;
  }
  if (file.size > maximumFileBytes) {
    showError('The browser demo accepts workbooks up to 16 MiB.');
    return;
  }
  state.file = file;
  state.outputs = { csharp: '', rust: '' };
  elements.fileTitle.textContent = file.name;
  elements.fileDetail.textContent = `${formatBytes(file.size)} · Ready to convert locally`;
  elements.resultTitle.textContent = 'Workbook ready';
  elements.resultDetail.textContent = 'Choose an engine or compare both implementations.';
  elements.output.value = '';
  elements.outputTabs.hidden = true;
  setResultActions(false);
  updateConvertButton();
}

async function convert() {
  if (!state.file || !state.ready) return;
  setBusy(true);
  try {
    const bytes = new Uint8Array(await state.file.arrayBuffer());
    const started = performance.now();
    if (state.engine === 'csharp' || state.engine === 'both') {
      state.outputs.csharp = String(convertWithCSharp(bytes));
    }
    if (state.engine === 'rust' || state.engine === 'both') {
      state.outputs.rust = String(convertWithRust(bytes));
    }
    const elapsed = performance.now() - started;
    state.selectedOutput = state.engine === 'rust' ? 'rust' : 'csharp';
    renderResult(elapsed);
  } catch (error) {
    showError(error instanceof Error ? error.message : String(error));
  } finally {
    setBusy(false);
  }
}

function renderResult(elapsed) {
  const compared = state.engine === 'both';
  const identical = compared && state.outputs.csharp === state.outputs.rust;
  elements.resultTitle.textContent = compared
    ? (identical ? 'Byte-identical output' : 'Outputs differ')
    : `${state.engine === 'csharp' ? 'C# AOT' : 'Rust WASM'} conversion complete`;
  elements.resultTitle.className = compared ? (identical ? 'match' : 'mismatch') : '';
  elements.resultDetail.textContent = `${elapsed.toFixed(1)} ms · ${formatBytes(activeOutput().length)} Markdown`;
  elements.outputTabs.hidden = !compared;
  elements.output.value = activeOutput();
  updateTabs();
  setResultActions(true);
}

function activeOutput() {
  return state.outputs[state.selectedOutput] || '';
}

function setBusy(busy) {
  elements.convertButton.disabled = busy || !state.ready || !state.file;
  elements.convertButton.textContent = busy ? 'Converting…' : 'Convert';
  elements.dropZone.classList.toggle('busy', busy);
}

function showError(message) {
  elements.resultTitle.textContent = 'Conversion failed';
  elements.resultTitle.className = 'mismatch';
  elements.resultDetail.textContent = message;
  elements.output.value = '';
  setResultActions(false);
}

function updateConvertButton() {
  elements.convertButton.disabled = !state.ready || !state.file;
}

function setResultActions(enabled) {
  elements.copyButton.disabled = !enabled;
  elements.downloadButton.disabled = !enabled;
}

function updateTabs() {
  elements.outputTabs.querySelectorAll('button').forEach((button) => {
    button.setAttribute('aria-selected', String(button.dataset.output === state.selectedOutput));
  });
}

function formatBytes(bytes) {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KiB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MiB`;
}

document.querySelectorAll('.engine-option').forEach((button) => {
  button.addEventListener('click', () => {
    state.engine = button.dataset.engine;
    document.querySelectorAll('.engine-option').forEach((option) => {
      option.setAttribute('aria-checked', String(option === button));
    });
  });
});

elements.fileInput.addEventListener('change', () => selectFile(elements.fileInput.files[0]));
elements.convertButton.addEventListener('click', convert);
elements.dropZone.addEventListener('dragover', (event) => {
  event.preventDefault();
  elements.dropZone.classList.add('dragging');
});
elements.dropZone.addEventListener('dragleave', () => elements.dropZone.classList.remove('dragging'));
elements.dropZone.addEventListener('drop', (event) => {
  event.preventDefault();
  elements.dropZone.classList.remove('dragging');
  selectFile(event.dataTransfer.files[0]);
});
elements.outputTabs.addEventListener('click', (event) => {
  const button = event.target.closest('button[data-output]');
  if (!button) return;
  state.selectedOutput = button.dataset.output;
  elements.output.value = activeOutput();
  updateTabs();
});
elements.copyButton.addEventListener('click', async () => {
  await navigator.clipboard.writeText(activeOutput());
  elements.copyButton.textContent = 'Copied';
  setTimeout(() => { elements.copyButton.textContent = 'Copy'; }, 1200);
});
elements.downloadButton.addEventListener('click', () => {
  const blob = new Blob([activeOutput()], { type: 'text/markdown;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = `${state.file.name.replace(/\.xlsx$/i, '')}.md`;
  anchor.click();
  URL.revokeObjectURL(url);
});

loadEngines().catch((error) => {
  elements.runtimeStatus.classList.add('failed');
  elements.runtimeStatus.lastElementChild.textContent = 'Engine loading failed';
  showError(error instanceof Error ? error.message : String(error));
});