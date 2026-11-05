'use strict';

const $ = selector => document.querySelector(selector);
const $$ = selector => [...document.querySelectorAll(selector)];
const native = Boolean(window.chrome && window.chrome.webview);
const rpc = new Map();
const engineRequests = new Map();
const nativeWebview = native ? window.chrome.webview : null;
const supported = new Set('doc docx odt rtf txt xls xlsx ods csv ppt pptx odp pdf odg'.split(' '));
const defaults = { writer: 'docx', calc: 'xlsx', impress: 'pptx', draw: 'pdf', pdf: 'pdf' };
const kindNames = { writer: '文字文档', calc: '电子表格', impress: '演示文稿', draw: '绘图', pdf: 'PDF 文档' };
let nextId = 0, generation = 0, port, enginePromise, current, busy = false, saving = false;
let dirtyVersion = 0, zoom = 100, fullTools = false, focused = false, toastTimer;
let activeOpen, pendingFile, previousGeneration, composing = false;
let pdfEditor;
let printing = false;
let inputComposition = false, inputCompositionCommitted = false;
const textInput = $('#text-input');

function focusTextInput() {
  if (!current || current.kind === 'pdf' || busy || saving || printing) return;
  textInput.focus({ preventScroll: true });
}

function host(command, data = {}) {
  if (!native) return Promise.reject(new Error('桌面功能未连接'));
  const requestId = ++nextId;
  return new Promise((resolve, reject) => {
    const timer = command === 'print' ? null : setTimeout(() => { rpc.delete(requestId); reject(new Error('操作未完成，请检查文件对话框或重试')); }, 300000);
    rpc.set(requestId, { resolve, reject, timer });
    nativeWebview.postMessage({ command, requestId, ...data });
  });
}

if (native) nativeWebview.addEventListener('message', event => {
  const message = event.data;
  if (message.event === 'save-before-close') { save(false).then(ok => host('closeResult', { ok })); return; }
  if (message.event === 'startup-file') {
    openPacket(message.file).catch(reportError);
    return;
  }
  const pending = rpc.get(message.requestId);
  if (!pending) return;
  clearTimeout(pending.timer); rpc.delete(message.requestId);
  if (message.error) pending.reject(new Error(message.error));
  else pending.resolve(message.result);
});

function tellHostState() {
  if (native) nativeWebview.postMessage({ command: 'state', dirty: Boolean(current?.dirty), busy: busy || saving || printing, name: current?.name || '' });
}

function toast(message, error = false) {
  clearTimeout(toastTimer);
  const element = $('#toast');
  element.textContent = message; element.classList.toggle('error', error); element.hidden = false;
  toastTimer = setTimeout(() => { element.hidden = true; }, error ? 7000 : 3000);
}

function reportError(error) {
  if (error?.name === 'AbortError') return;
  console.error(error);
  const detail=error?.message||String(error);
  toast(/SfxBaseModel|impl_store|file:\/\/\/tmp|0xc10|uno::|storeToURL/.test(detail)?'这个格式暂时无法导出，请选择 DOCX、ODT 或 PDF；当前修改已保留。':detail,true);
}

function setBusy(value, text = '正在打开文件…', detail = '') {
  busy = value;
  $('#loading-title').textContent = text;
  $('#loading-detail').textContent = detail;
  $('#loading').hidden = !value;
  if (value) {
    $('#home').hidden = true; $('#workspace').hidden = false;
    $('#qtcanvas').style.visibility = 'hidden';
  }
  updateControls();
}

function updateControls() {
  const usable = Boolean(current) && !busy;
  const disabled = !usable || saving || printing;
  textInput.disabled = busy || saving || printing;
  $('#canvas-container').inert = busy || saving || printing;
  for (const id of ['save-button', 'save-as-button', 'pdf-button', 'conversion-button', 'full-tools-button', 'close-button', 'print-button', 'print-menu-button']) $( '#' + id ).disabled = disabled;
  $('#open-button').disabled = busy || saving || printing;
  for (const button of $$('[data-new]')) button.disabled = busy || saving || printing;
  for (const control of $$('#toolbar button, #toolbar select, #toolbar input')) control.disabled = disabled;
  $('#insert-table-button').hidden = current?.kind !== 'writer';
  const action = $('#kind-action-button');
  action.textContent = current?.kind === 'calc' ? '插入行' : current?.kind === 'impress' ? '新一页' : '分页';
  action.hidden = current?.kind === 'draw';
  $('#font-family').value = $('#font-family').value || 'Microsoft YaHei';
  $('#file-identity').hidden = !current;
  document.body.dataset.kind = current?.kind || '';
  $$('.pdf-tools').forEach(element => { element.hidden = current?.kind !== 'pdf'; });
  if (current) {
    $('#file-name').textContent = current.name; $('#file-name').title = current.name;
    $('#file-type').textContent = extension(current.name).toUpperCase();
    const status = $('#save-status');
    status.textContent = saving ? '保存中…' : current.dirty ? '有修改' : current.saved ? '已保存' : '未保存';
    status.classList.toggle('dirty', current.dirty);
    document.title = (current.dirty ? '● ' : '') + current.name + ' · 开物';
    $('#editor-state').textContent = current.dirty ? '有未保存的修改' : '本地编辑';
  } else document.title = '开物';
  tellHostState();
}

function extension(name) { return String(name).split('.').pop().toLowerCase(); }
function stem(name) { return name.replace(/\.[^.]+$/, ''); }
function closeMenus() { $$('.dropdown[open]').forEach(details => { details.open = false; }); }
function edit(message) {
  if (!current || busy || saving || printing) return;
  if (current.kind === 'pdf') {
    if (message.name === 'Undo') pdfEditor.undo();
    if (message.name === 'Redo') pdfEditor.redo();
    if (message.cmd === 'find') pdfEditor.find(message.text).then(found => toast(found ? '已定位到匹配页面' : '没有找到匹配内容'));
    if (message.cmd === 'zoom') pdfEditor.setZoom(message.value).catch(reportError);
    return;
  }
  port.postMessage({ ...message, generation });
  focusTextInput();
}

async function ensureEngine() {
  if (enginePromise) return enginePromise;
  enginePromise = new Promise(async (resolve, reject) => {
    let timeout;
    try {
      if (!crossOriginIsolated) throw new Error('文档运行环境尚未准备好，请关闭后重新打开应用');
      const fontResponse = await fetch('/system-font.ttf');
      if (!fontResponse.ok) throw new Error('无法读取系统中文字体');
      const font = new Uint8Array(await fontResponse.arrayBuffer());
      const canvas = $('#qtcanvas');
      window.Module = {
        canvas,
        uno_scripts: [location.origin + '/engine/zeta.js', location.origin + '/office-worker.js'],
        locateFile: path => location.origin + '/engine/' + path,
        mainScriptUrlOrBlob: new Blob(["importScripts('" + location.origin + "/engine/soffice.js');"], { type: 'text/javascript' }),
        preRun: [function () {
          FS.mkdirTree('/instdir/share/fonts/truetype');
          FS.writeFile('/instdir/share/fonts/truetype/local-chinese.ttc', font);
        }],
        print: (...args) => console.log(...args),
        printErr: (...args) => console.warn(...args),
        onAbort: reason => reject(new Error('文档内核未能启动：' + reason)),
      };
      const script = document.createElement('script'); script.src = '/engine/soffice.js';
      script.onerror = () => reject(new Error('无法载入本地文档内核，请检查应用文件是否完整'));
      timeout = setTimeout(() => reject(new Error('文档启动超时，请关闭后重新打开应用')), 60000);
      script.onload = () => Module.uno_main.then(enginePort => {
        port = enginePort;
        port.onmessage = event => {
          if (event.data.cmd === 'ready') { clearTimeout(timeout); resolve(); }
          else if (event.data.cmd === 'fatal') { clearTimeout(timeout); reject(new Error(event.data.message)); }
          else onEngineMessage(event.data);
        };
      }).catch(reject);
      document.body.append(script);
    } catch (error) { clearTimeout(timeout); reject(error); }
  });
  return enginePromise;
}

function onEngineMessage(message) {
  if (message.cmd === 'opened') {
    if (message.generation !== generation) return;
    current.kind = message.kind;
    if (pdfEditor) { pdfEditor.destroy(false); pdfEditor = null; }
    $('#pdf-container').hidden = true;
    current.dirty = false;
    dirtyVersion = 0;
    zoom = 100; fullTools = false;
    $('#zoom-label').textContent = '100%';
    $('#full-tools-button').textContent = '展开更多排版';
    $('#document-info').textContent = kindNames[message.kind] +
      (message.sheets ? ' · ' + message.sheets + ' 个工作表' : message.pages ? ' · ' + message.pages + ' 页' : '');
    setBusy(false);
    $('#qtcanvas').style.visibility = 'visible';
    requestAnimationFrame(() => window.dispatchEvent(new Event('resize')));
    setTimeout(() => { window.dispatchEvent(new Event('resize')); focusTextInput(); }, 100);
    activeOpen?.resolve(); activeOpen = null;
  } else if (message.cmd === 'modified') {
    if (!current || message.generation !== generation || busy) return;
    dirtyVersion++;
    current.dirty = true;
    updateControls();
  } else if (message.cmd === 'format') {
    if (message.generation !== generation) return;
    const button = $(`[data-command="${message.name}"]`);
    if (button) button.setAttribute('aria-pressed', String(message.active));
  } else if (message.cmd === 'exported') {
    const pending = engineRequests.get(message.requestId);
    if (!pending) return;
    engineRequests.delete(message.requestId);
    if (message.generation !== generation) { pending.reject(new Error('文件已经切换，请重新保存')); return; }
    try {
      const bytes = FS.readFile(message.path);
      FS.unlink(message.path);
      pending.resolve(new Uint8Array(bytes));
    } catch (error) { pending.reject(error); }
  } else if (message.cmd === 'error') {
    const error = new Error(message.message || '文件操作失败');
    const pending = engineRequests.get(message.requestId);
    if (pending) { engineRequests.delete(message.requestId); pending.reject(error); }
    else if (busy && activeOpen) {
      const pendingOpen = activeOpen; activeOpen = null;
      setBusy(false); current = pendingFile; generation = previousGeneration; pendingFile = null; updateControls();
      if (current) $('#qtcanvas').style.visibility = 'visible'; else showHome();
      pendingOpen.reject(error);
    } else reportError(error);
  } else if (message.cmd === 'found') toast(message.found ? '已定位到匹配内容' : '没有找到匹配内容');
  else if (message.cmd === 'copied') {
    if (native) host('clipboardWrite', { text: message.text }).catch(reportError);
    else navigator.clipboard.writeText(message.text).catch(reportError);
  }
  else if (message.cmd === 'tools') {
    fullTools = message.visible;
    $('#full-tools-button').textContent = fullTools ? '收起更多排版' : '展开更多排版';
    window.dispatchEvent(new Event('resize'));
  } else if (message.cmd === 'inspection') {
    const pending = engineRequests.get(message.requestId);
    if (pending) { engineRequests.delete(message.requestId); pending.resolve(message); }
  }
}

function waitOpen(message) {
  return new Promise((resolve, reject) => {
    activeOpen = { resolve, reject };
    port.postMessage(message);
  });
}

async function guardUnsaved() {
  if (busy || saving || printing) return false;
  if (!current?.dirty) return true;
  const dialog = $('#unsaved-dialog');
  const result = await new Promise(resolve => {
    dialog.onclose = () => resolve(dialog.returnValue || 'cancel');
    dialog.returnValue = 'cancel'; dialog.showModal();
  });
  if (result === 'cancel') return false;
  return result === 'save' ? save(false) : true;
}

async function create(kind) {
  closeMenus();
  if (!await guardUnsaved()) return;
  pendingFile = current;
  previousGeneration = generation;
  setBusy(true, '正在准备' + kindNames[kind] + '…', enginePromise ? '' : '首次打开需要几秒钟');
  try {
    await ensureEngine();
    generation++;
    current = { kind, name: '未命名' + kindNames[kind] + '.' + defaults[kind], saved: false, dirty: false };
    updateControls();
    await waitOpen({ cmd: 'new', kind, generation });
    pendingFile = null;
  } catch (error) {
    if (busy) { setBusy(false); current = pendingFile; generation = previousGeneration; pendingFile = null; if (!current) showHome(); }
    reportError(error);
  }
}

async function openPacket(packet) {
  closeMenus();
  if (!packet || packet.canceled) return;
  const name = packet.name || packet.file?.name;
  const ext = extension(name);
  if (!supported.has(ext)) { toast('暂不支持这个格式，请选择 Word、表格、PPT 或 PDF 文件', true); return; }
  if (!await guardUnsaved()) return;
  pendingFile = current;
  previousGeneration = generation;
  setBusy(true, '正在打开 ' + name, enginePromise ? '' : '首次打开需要几秒钟');
  try {
    const bytes = packet.bytes || (packet.base64 ? fromBase64(packet.base64) : new Uint8Array(await packet.file.arrayBuffer()));
    if (bytes.byteLength > 64 * 1024 * 1024) throw new Error('这份文件超过 64 MB，请先使用较小的文件');
    if (ext === 'pdf') {
      const { PdfEditor } = await import('/pdf-editor.js');
      const container = $('#pdf-container'); container.hidden = false;
      const nextPdf = new PdfEditor(container, () => {
        if (current?.kind === 'pdf') { current.dirty = true; dirtyVersion++; updateControls(); }
      }, (number, count) => { $('#pdf-page-number').textContent = number + ' / ' + count; });
      try { await nextPdf.open(bytes); }
      catch (error) {
        nextPdf.destroy(false);
        if (pdfEditor) await pdfEditor.render(); else container.hidden = true;
        throw error;
      }
      if (current && current.kind !== 'pdf') port?.postMessage({ cmd: 'close', generation });
      if (pdfEditor) pdfEditor.destroy(false);
      pdfEditor = nextPdf;
      generation++; dirtyVersion = 0; fullTools = false;
      current = { name, kind: 'pdf', dirty: false, saved: Boolean(packet.token || packet.handle), token: packet.token, handle: packet.handle };
      $('#document-info').textContent = 'PDF · ' + pdfEditor.document.numPages + ' 页';
      zoom = Math.round(pdfEditor.scale * 100); $('#zoom-label').textContent = zoom + '%';
      setBusy(false); $('#qtcanvas').style.visibility = 'hidden';
      pendingFile = null;
      if (native) refreshRecents();
      return;
    }
    await ensureEngine();
    const path = '/tmp/qing-input-' + (++nextId) + '.' + ext;
    FS.writeFile(path, bytes);
    generation++;
    current = { name, kind: '', dirty: false, saved: Boolean(packet.token || packet.handle),
      token: packet.token, handle: packet.handle, source: path };
    updateControls();
    try { await waitOpen({ cmd: 'open', path, generation }); }
    finally { try { FS.unlink(path); } catch (_) {} }
    pendingFile = null;
    if (native) refreshRecents();
  } catch (error) {
    if (busy) { setBusy(false); current = pendingFile; generation = previousGeneration; pendingFile = null; if (!current) showHome(); }
    throw error;
  }
}

async function open() {
  if (busy || saving || printing) return;
  closeMenus();
  if (native) {
    try { await openPacket(await host('open')); } catch (error) { reportError(error); }
  } else if ('showOpenFilePicker' in window) {
    try {
      const [handle] = await showOpenFilePicker({ types: [{ description: '文档', accept: { 'application/octet-stream': [...supported].map(ext => '.' + ext) } }] });
      await openPacket({ handle, file: await handle.getFile() });
    } catch (error) { reportError(error); }
  } else $('#file-input').click();
}

async function exportBytes(ext) {
  if (current?.kind === 'pdf') {
    if (ext !== 'pdf') throw new Error('PDF 批注请保存为 PDF 格式');
    return pdfEditor.export();
  }
  const requestId = ++nextId;
  return new Promise((resolve, reject) => {
    engineRequests.set(requestId, { resolve, reject });
    port.postMessage({ cmd: 'export', requestId, extension: ext, generation });
  });
}

async function save(asNew = false, toPdf = false) {
  if(asNew&&!toPdf&&window.glintOpenSaveAs){window.glintOpenSaveAs();return false;}
  closeMenus();
  if (!current || busy || saving || printing) return false;
  const ext = toPdf ? 'pdf' : extension(current.name) || defaults[current.kind];
  const name = stem(current.name) + '.' + ext;
  let browserHandle;
  try {
    // File picker is called before asynchronous export so the user gesture remains valid.
    if (!native && 'showSaveFilePicker' in window) {
      browserHandle = !asNew && !toPdf ? current.handle : null;
      if (!browserHandle) browserHandle = await showSaveFilePicker({ suggestedName: name,
        types: [{ description: ext.toUpperCase() + ' 文件', accept: { 'application/octet-stream': ['.' + ext] } }] });
    }
    saving = true;
    const startGeneration = generation;
    updateControls();
    const bytes = await exportBytes(ext);
    let result;
    if (native) result = await host('save', { token: current.token || null, name, extension: ext,
      saveAs: asNew || toPdf, exportOnly: toPdf, base64: toBase64(bytes) });
    else if (browserHandle) {
      const writable = await browserHandle.createWritable();
      try { await writable.write(bytes); await writable.close(); }
      catch (error) { try { await writable.abort(); } catch (_) {} throw error; }
      result = { name: browserHandle.name, handle: browserHandle };
    } else {
      download(bytes, name);
      result = { downloaded: true, name };
    }
    if (result?.canceled) return false;
    if (!toPdf && generation === startGeneration) {
      current.name = result.name || name;
      current.token = result.token || current.token;
      current.handle = result.handle || current.handle;
      current.dirty = false; current.saved = !result.downloaded;
      if (current.kind !== 'pdf') port.postMessage({ cmd: 'saved', generation });
    }
    toast(toPdf ? 'PDF 已导出' : result.downloaded ? '已下载文件副本' : '文件已保存');
    if (native) refreshRecents();
    return true;
  } catch (error) { reportError(error); return false; }
  finally { saving = false; updateControls(); }
}

async function printDocument() {
  closeMenus();
  if (!current || busy || saving || printing) return false;
  if (!native) { toast('请在开物 Windows 安装版中使用打印'); return false; }
  if (inputComposition || composing) { toast('请先确认正在输入的文字，再打印'); return false; }
  const priorDirty = current.dirty;
  const priorGeneration = generation;
  const priorName = current.name;
  const priorSaved = current.saved;
  printing = true;
  updateControls();
  toast('正在准备打印预览…');
  try {
    const bytes = await exportBytes('pdf');
    await host('print', { name: priorName, base64: toBase64(bytes) });
    return true;
  } catch (error) { reportError(error); return false; }
  finally {
    // Exporting a print snapshot never saves or renames the source document.
    if (generation === priorGeneration && current) {
      current.dirty = priorDirty; current.saved = priorSaved; current.name = priorName;
    }
    printing = false;
    updateControls();
    focusTextInput();
  }
}

function fromBase64(value) {
  const binary = atob(value), bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  return bytes;
}

function toBase64(bytes) {
  let result = '';
  for (let i = 0; i < bytes.length; i += 32768) result += String.fromCharCode(...bytes.subarray(i, i + 32768));
  return btoa(result);
}

function download(bytes, name) {
  const url = URL.createObjectURL(new Blob([bytes]));
  const link = document.createElement('a'); link.href = url; link.download = name; link.click();
  setTimeout(() => URL.revokeObjectURL(url), 5000);
}

async function showHome() {
  $('#workspace').hidden = true; $('#home').hidden = false;
  document.body.classList.remove('reading');
  updateControls();
  if (native) refreshRecents();
}

async function closeDocument() {
  closeMenus();
  if (!await guardUnsaved()) return;
  if (current && current.kind !== 'pdf') port.postMessage({ cmd: 'close', generation });
  if (current?.kind === 'pdf') { pdfEditor.destroy(); pdfEditor = null; }
  current = null; focused = false; showHome();
}

async function refreshRecents() {
  try {
    const records = await host('recents');
    const list = $('#recent-list'); list.replaceChildren();
    $('#recent-section').hidden = !records.length;
    for (const record of records.slice(0, 8)) {
      const row = document.createElement('button'); row.className = 'recent-row';
      const badge = document.createElement('span'); badge.className = 'small-type ' +
        (/^xls|ods|csv/.test(extension(record.name)) ? 'sheet' : /^ppt|odp/.test(extension(record.name)) ? 'slide' : 'word');
      badge.textContent = extension(record.name).slice(0, 1).toUpperCase();
      const title = document.createElement('span'); title.className = 'recent-name'; title.textContent = record.name;
      const location = document.createElement('span'); location.className = 'recent-location'; location.textContent = record.folder; title.append(location);
      const time = document.createElement('time'); time.textContent = record.date;
      row.append(badge, title, time); row.title = record.folder + '\\' + record.name;
      row.onclick = async () => { try { await openPacket(await host('openRecent', { id: record.id })); } catch (error) { reportError(error); } };
      list.append(row);
    }
  } catch (error) { reportError(error); }
}

$$('[data-new]').forEach(button => { button.onclick = () => create(button.dataset.new); });
$$('[data-command]').forEach(button => { button.onclick = () => edit({ cmd: 'command', name: button.dataset.command }); });
$('#open-button').onclick = $('#drop-zone').onclick = open;
$('#save-button').onclick = () => save();
$('#save-as-button').onclick = () => save(true);
$('#pdf-button').onclick = () => save(true, true);
$('#print-button').onclick = $('#print-menu-button').onclick = printDocument;
$('#close-button').onclick = closeDocument;
$('#home-button').onclick = () => host('switchHome');
$('#font-family').onchange = event => edit({ cmd: 'font', family: event.target.value });
$('#font-size').onchange = event => edit({ cmd: 'fontSize', size: event.target.value });
$('#font-color').oninput = event => edit({ cmd: 'color', color: event.target.value });
$('#full-tools-button').onclick = () => { closeMenus(); edit({ cmd: 'tools', visible: !fullTools }); };
$('#kind-action-button').onclick = () => edit({ cmd: 'command', name: current.kind === 'calc' ? 'InsertRows' : current.kind === 'impress' ? 'InsertPage' : 'InsertPagebreak' });
$('#insert-table-button').onclick = () => { $('#table-dialog').returnValue = 'cancel'; $('#table-dialog').showModal(); };
$('#table-dialog').onclose = () => {
  if ($('#table-dialog').returnValue === 'insert') edit({ cmd: 'table', rows: Number($('#table-rows').value), columns: Number($('#table-columns').value) });
};
$('#insert-image-button').onclick = () => $('#image-input').click();
$('#image-input').onchange = async event => {
  const file = event.target.files[0]; if (!file) return;
  try {
    const ext = extension(file.name);
    const path = '/tmp/qing-image-' + (++nextId) + '.' + (/^(png|jpe?g|webp|gif|bmp)$/.test(ext) ? ext : 'png');
    FS.writeFile(path, new Uint8Array(await file.arrayBuffer()));
    edit({ cmd: 'image', path });
  } catch (error) { reportError(error); }
  finally { event.target.value = ''; }
};
$('#find-button').onclick = () => { const text = prompt('查找内容'); if (text) edit({ cmd: 'find', text }); };
$('#reading-button').onclick = () => {
  focused = !focused;
  document.body.classList.toggle('reading', focused);
  $('#reading-button').textContent = focused ? '编辑' : '专注';
  toast(focused ? '已收起工具栏，按 Esc 返回' : '已展开工具栏');
  edit({ cmd: 'readOnly', value: focused });
  window.dispatchEvent(new Event('resize'));
};
function setZoom(value) {
  zoom = Math.max(25, Math.min(250, value)); $('#zoom-label').textContent = zoom + '%'; edit({ cmd: 'zoom', value: zoom });
}
$('#zoom-in').onclick = () => setZoom(zoom + 10);
$('#zoom-out').onclick = () => setZoom(zoom - 10);
$('#zoom-label').onclick = () => setZoom(100);
$('#about-button').onclick = () => { closeMenus(); $('#about-dialog').showModal(); };
$('#about-close').onclick = () => $('#about-dialog').close();
$('#clear-recents').onclick = async () => { await host('clearRecents'); refreshRecents(); };
$$('[data-pdf-tool]').forEach(button => {
  button.onclick = () => {
    pdfEditor.setTool(button.dataset.pdfTool);
    $$('[data-pdf-tool]').forEach(item => item.setAttribute('aria-pressed', String(item === button)));
  };
});
$('#pdf-previous').onclick = () => pdfEditor.go(pdfEditor.currentPage - 1);
$('#pdf-next').onclick = () => pdfEditor.go(pdfEditor.currentPage + 1);
$('#pdf-delete-note').onclick = () => pdfEditor.removeSelected();
$$('#unsaved-dialog [data-result]').forEach(button => { button.onclick = () => $('#unsaved-dialog').close(button.dataset.result); });
$('#file-input').onchange = event => { const file = event.target.files[0]; event.target.value = ''; if (file) openPacket({ file }).catch(reportError); };
document.addEventListener('click', event => { if (!event.target.closest('.dropdown')) closeMenus(); });
window.addEventListener('keydown', event => {
  const ctrl = event.ctrlKey || event.metaKey;
  if (ctrl && event.key.toLowerCase() === 'p') {
    event.preventDefault(); event.stopImmediatePropagation(); printDocument(); return;
  }
  const onCanvas = event.target === $('#qtcanvas') || event.target === textInput;
  if (event.target === textInput) {
    if (event.isComposing || inputComposition || event.keyCode === 229) { event.stopImmediatePropagation(); return; }
    if (ctrl && ['s','o','f','c','v','x'].includes(event.key.toLowerCase())) {
      // These use the normal application / clipboard route below.
    } else if (ctrl && ['a','z','y','b','i','u'].includes(event.key.toLowerCase())) {
      event.preventDefault(); event.stopImmediatePropagation();
      edit({cmd:'key',key:event.key.toLowerCase(),ctrl:true,shift:event.shiftKey});return;
    } else if (['ArrowLeft','ArrowRight','ArrowUp','ArrowDown','Home','End','PageUp','PageDown','Enter','Tab','Backspace','Delete'].includes(event.key)) {
      event.preventDefault();event.stopImmediatePropagation();
      if (current?.kind === 'calc') textInput.value='';
      edit({cmd:'key',key:event.key,ctrl,shift:event.shiftKey});return;
    } else { event.stopImmediatePropagation(); return; }
  }
  if (current?.kind === 'pdf' && !event.target.matches('input,textarea')) {
    if (ctrl && event.key.toLowerCase() === 'z') { event.preventDefault(); event.shiftKey ? pdfEditor.redo() : pdfEditor.undo(); }
    if (event.key === 'Delete') { event.preventDefault(); pdfEditor.removeSelected(); }
  }
  if (onCanvas && (event.isComposing || event.keyCode === 229)) {
    event.stopImmediatePropagation(); return;
  }
  if (ctrl && onCanvas && event.key.toLowerCase() === 'v') {
    event.preventDefault(); event.stopImmediatePropagation();
    const text = native ? host('clipboardRead') : navigator.clipboard.readText();
    Promise.resolve(text).then(text => edit({ cmd: 'insertText', text })).catch(reportError);
  }
  if (ctrl && onCanvas && ['c', 'x'].includes(event.key.toLowerCase())) {
    event.preventDefault(); event.stopImmediatePropagation();
    edit({ cmd: 'copy', cut: event.key.toLowerCase() === 'x' });
  }
  if (ctrl && event.key.toLowerCase() === 's') { event.preventDefault(); event.stopImmediatePropagation(); save(event.shiftKey); }
  if (ctrl && event.key.toLowerCase() === 'o') { event.preventDefault(); event.stopImmediatePropagation(); open(); }
  if (ctrl && event.key.toLowerCase() === 'f' && current) { event.preventDefault(); event.stopImmediatePropagation(); $('#find-button').click(); }
  if (event.key === 'Escape') {
    closeMenus();
    if (focused) { focused = false; document.body.classList.remove('reading'); edit({ cmd: 'readOnly', value: false }); window.dispatchEvent(new Event('resize')); }
  }
}, true);
$('#qtcanvas').addEventListener('contextmenu', event => event.preventDefault());
$('#qtcanvas').addEventListener('pointerup', event => {
  const bounds=$('#canvas-container').getBoundingClientRect();
  textInput.style.left=Math.max(0,event.clientX-bounds.left)+'px';
  textInput.style.top=Math.max(0,event.clientY-bounds.top)+'px';
  textInput.value='';
  setTimeout(focusTextInput,0);
});
textInput.addEventListener('compositionstart',()=>{inputComposition=true;inputCompositionCommitted=false;});
textInput.addEventListener('compositionend',event=>{
  inputComposition=false;inputCompositionCommitted=true;
  const value=current?.kind==='calc'?textInput.value:event.data;
  if(value)edit({cmd:'insertText',text:value});
  if(current?.kind!=='calc')textInput.value='';
  setTimeout(()=>{inputCompositionCommitted=false;},0);
});
textInput.addEventListener('input',event=>{
  event.stopPropagation();
  if(inputComposition||event.isComposing||inputCompositionCommitted)return;
  const value=current?.kind==='calc'?textInput.value:(event.data||textInput.value);
  if(value)edit({cmd:'insertText',text:value});
  if(current?.kind!=='calc')textInput.value='';
});
$('#qtcanvas').addEventListener('keydown', event => { if (!event.isComposing && event.keyCode !== 229) event.preventDefault(); });
$('#qtcanvas').addEventListener('compositionstart', () => { composing = true; });
$('#qtcanvas').addEventListener('compositionend', event => {
  composing = false;
  if (event.data) edit({ cmd: 'insertText', text: event.data });
  $('#qtcanvas').textContent = '';
});
$('#qtcanvas').addEventListener('beforeinput', event => {
  if (composing || event.isComposing || event.inputType === 'insertCompositionText') return;
  if (event.data && /[^\x00-\x7F]/.test(event.data)) {
    event.preventDefault(); event.stopImmediatePropagation();
    edit({ cmd: 'insertText', text: event.data });
  }
});
$('#qtcanvas').addEventListener('wheel', event => event.preventDefault(), { passive: false });
document.addEventListener('dragover', event => { event.preventDefault(); $('#drop-zone').classList.add('drag-over'); });
document.addEventListener('dragleave', event => { if (!event.relatedTarget) $('#drop-zone').classList.remove('drag-over'); });
document.addEventListener('drop', async event => {
  event.preventDefault(); $('#drop-zone').classList.remove('drag-over');
  if (busy || saving || printing) return;
  const file = event.dataTransfer?.files[0]; if (!file) return;
  let handle;
  try { handle = await event.dataTransfer.items[0]?.getAsFileSystemHandle?.(); } catch (_) {}
  openPacket({ file, handle }).catch(reportError);
});
window.addEventListener('beforeunload', event => {
  if (!native && (current?.dirty || saving)) { event.preventDefault(); event.returnValue = ''; }
});
window.addEventListener('resize', () => { if (current && current.kind !== 'pdf' && !busy) $('#qtcanvas').style.visibility = 'visible'; });

// Small, read-only diagnostics for automated acceptance. Document actions use the same handlers.
window.qing = {
  create, openPacket, exportBytes,
  inspect: () => current?.kind === 'pdf' ? Promise.resolve({kind:'pdf',pages:pdfEditor.document.numPages,notes:pdfEditor.notes.length}) : new Promise((resolve, reject) => {
    const requestId = ++nextId; engineRequests.set(requestId, { resolve, reject });
    port.postMessage({ cmd: 'inspect', requestId, generation });
  }),
  state: () => ({ current: current && { name: current.name, kind: current.kind, dirty: current.dirty, saved: current.saved }, busy, saving, printing, generation }),
};
updateControls();
if (native) host('ready').then(result => { refreshRecents(); if (result?.file) openPacket(result.file).catch(reportError); }).catch(reportError);

initializeConversion({state:()=>qing.state(),exportBytes,host,setSaving:value=>{saving=value;},update:updateControls,report:reportError,toast,onSaved:result=>{current.name=result.name;current.token=result.token;current.dirty=false;current.saved=true;if(current.kind!=='pdf')port.postMessage({cmd:'saved',generation});updateControls();}});

document.addEventListener('keydown',event=>{if(event.ctrlKey&&event.shiftKey&&event.key.toLowerCase()==='e'){event.preventDefault();document.querySelector('#conversion-button').click();}},true);

document.querySelector('#switch-media').onclick=()=>host('switchMedia');document.querySelector('#switch-tools').onclick=()=>host('switchTools');
