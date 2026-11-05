/* Local document engine. Integration inspired by allotropia/zetajs (MIT). */
'use strict';

Module.zetajs.then(function (zeta) {
  const css = zeta.uno.com.sun.star;
  const context = zeta.getUnoComponentContext();
  const desktop = css.frame.Desktop.create(context);
  const dispatcher = css.frame.DispatchHelper.create(context);
  let model, controller, kind = 'writer', generation = 0, fullTools = false;
  let retainedListeners = [];

  function property(name, value) {
    return new css.beans.PropertyValue({ Name: name, Value: value });
  }

  function emit(data) { zeta.mainPort.postMessage(data); }

  function exceptionText(error) {
    try { return zeta.catchUnoException(error).Message || String(error); }
    catch (_) { return String(error); }
  }

  function dispatch(command, args) {
    if (!controller) throw new Error('请先打开文件');
    return dispatcher.executeDispatch(controller.getFrame(), '.uno:' + command, '', 0,
      Object.entries(args || {}).map(([name, value]) => property(name, value)));
  }

  function setupConfiguration() {
    const config = css.configuration.ReadWriteAccess.create(context, 'en-US');
    for (const moduleName of ['Writer', 'Calc', 'Impress', 'Draw']) {
      try {
        const elements = config.getByHierarchicalName(
          '/org.openoffice.Office.UI.' + moduleName + 'WindowState/UIElements/States');
        for (const name of elements.getElementNames()) {
          const element = elements.getByName(name);
          if (element.getByName('Visible')) element.setPropertyValue('Visible', false);
        }
      } catch (_) { /* A build may omit a module-specific toolbar configuration. */ }
    }
    try {
      config.setPropertyValue('/org.openoffice.Office.Impress/Misc/StartWithTemplate', false);
    } catch (_) {}
    try {
      const schemes=config.getByHierarchicalName('/org.openoffice.Office.UI/ColorScheme/ColorSchemes');
      for(const name of schemes.getElementNames()){
        const scheme=schemes.getByName(name);
        for(const [key,color] of [['AppBackground',0xf0f3f1],['DocBoundaries',0xdde5df]]){
          try{scheme.getByName(key).setPropertyValue('Color',color);}catch(_){}
        }
      }
    }catch(_){}
    config.commitChanges();
  }

  function getKind(doc) {
    if (doc.supportsService('com.sun.star.sheet.SpreadsheetDocument')) return 'calc';
    if (doc.supportsService('com.sun.star.presentation.PresentationDocument')) return 'impress';
    if (doc.supportsService('com.sun.star.text.TextDocument')) return 'writer';
    if (doc.supportsService('com.sun.star.drawing.DrawingDocument')) return 'draw';
    throw new Error('这个文件类型暂不支持编辑');
  }

  function showTools(show) {
    const layout = controller.getFrame().LayoutManager;
    fullTools = Boolean(show);
    for (const item of layout.getElements()) {
      const url = item.ResourceURL;
      if (url.includes('/toolbar/')) {
        if (show && !/\/(standardbar|findbar|toolbar)$/.test(url)) layout.showElement(url);
        else layout.hideElement(url);
      }
    }
    const menu = 'private:resource/menubar/menubar';
    layout.hideElement(menu);
    try { layout.hideElement('private:resource/dockingwindow/Sidebar'); } catch (_) {}
    try {
      const sidebar = controller.getSidebar();
      if (sidebar) sidebar.setVisible(false);
    } catch (_) {}
    try { layout.hideElement('private:resource/statusbar/statusbar'); } catch (_) {}
    try{layout.destroyElement('private:resource/statusbar/statusbar');}catch(_){}
    try{const view=controller.getViewSettings();for(const key of ['ShowRulers','ShowHoriRuler','ShowVertRuler','ShowTextBoundaries']){try{view.setPropertyValue(key,false);}catch(_){}}}catch(_){}
    if (kind === 'calc') {
      try { layout.showElement('private:resource/toolbar/inputbar'); } catch (_) {}
    }
  }

  function listen() {
    retainedListeners = [];
    const thisGeneration = generation;
    const modified = zeta.unoObject([css.util.XModifyListener], {
      disposing() {},
      modified() {
        if (generation === thisGeneration && model && model.isModified()) emit({ cmd: 'modified', generation });
      },
    });
    model.addModifyListener(modified);
    retainedListeners.push(modified);
    for (const name of ['Bold', 'Italic', 'Underline']) {
      try {
        const urlParam = { val: new css.util.URL({ Complete: '.uno:' + name }) };
        css.util.URLTransformer.create(context).parseStrict(urlParam);
        const url = urlParam.val;
        const dispatchObject = controller.queryDispatch(url, '_self', 0);
        const listener = zeta.unoObject([css.frame.XStatusListener], {
          disposing() {},
          statusChanged(event) {
            let state = zeta.fromAny(event.State);
            emit({ cmd: 'format', name, active: state === true, generation: thisGeneration });
          },
        });
        dispatchObject.addStatusListener(listener, url);
        retainedListeners.push(listener);
      } catch (_) {}
    }
  }

  function description() {
    if (kind === 'writer') {
      const info = { kind, characters: model.getText().getString().length };
      try { info.pages = controller.PageCount; } catch (_) {}
      return info;
    }
    if (kind === 'calc') return { kind, sheets: model.getSheets().getCount() };
    return { kind, pages: model.getDrawPages().getCount() };
  }

  function attach(doc, nextGeneration, isNew) {
    const previous = model;
    const nextKind = getKind(doc);
    generation = nextGeneration;
    model = doc;
    controller = model.getCurrentController();
    kind = nextKind;
    if (previous && previous !== model) {
      try { previous.setModified(false); previous.close(true); } catch (_) {}
    }
    const frame = controller.getFrame();
    frame.getContainerWindow().FullScreen = true;
    showTools(false);setTimeout(()=>{try{showTools(false);}catch(_){}},600);
    if (isNew && kind === 'writer') {
      const cursor = model.getText().createTextCursor();
      cursor.CharFontName = 'Microsoft YaHei';
      cursor.CharFontNameAsian = 'Microsoft YaHei';
      cursor.CharHeight = 12;
      cursor.CharHeightAsian = 12;
    }
    try { dispatch('Zoom', { 'Zoom.Value': 100 }); } catch (_) {}
    model.setModified(false);
    listen();
    emit({ cmd: 'opened', generation, ...description() });
  }

  const filters = {
    docx: 'Office Open XML Text', doc: 'MS Word 97', odt: 'writer8',
    rtf: 'Rich Text Format', txt: 'Text (encoded)',
    xlsx: 'Calc MS Excel 2007 XML', xls: 'MS Excel 97', ods: 'calc8',
    csv: 'Text - txt - csv (StarCalc)',
    pptx: 'Impress MS PowerPoint 2007 XML', ppt: 'MS PowerPoint 97', odp: 'impress8',
    odg: 'draw8', epub: 'EPUB', html: 'HTML (StarWriter)', tsv: 'Text - txt - csv (StarCalc)',
  };

  function exportFile(extension, requestId) {
    const ext = String(extension).toLowerCase();
    const allowed = {
      writer: ['docx', 'doc', 'odt', 'rtf', 'txt', 'pdf', 'epub', 'html'],
      calc: ['xlsx', 'xls', 'ods', 'csv', 'pdf', 'tsv'],
      impress: ['pptx', 'ppt', 'odp', 'pdf'], draw: ['pdf', 'odg'],
    };
    if (!allowed[kind].includes(ext)) throw new Error('当前文件不能保存为这个格式');
    const filter = ext === 'pdf' ? {
      writer: 'writer_pdf_Export', calc: 'calc_pdf_Export',
      impress: 'impress_pdf_Export', draw: 'draw_pdf_Export',
    }[kind] : filters[ext];
    const path = '/tmp/qing-export-' + requestId + '.' + ext;
    const props = [property('FilterName', filter), property('Overwrite', true)];
    if (ext === 'txt') props.push(property('FilterOptions', 'UTF8,LF,,,'));
    if (ext === 'html') props.push(property('FilterOptions','EmbedImages'));
    if (ext === 'tsv') props.push(property('FilterOptions','9,34,76,1'));
    if (ext === 'csv') props.push(property('FilterOptions', '44,34,76,1'));
    model.storeToURL('file://' + path, props);
    emit({ cmd: 'exported', requestId, generation, path, extension: ext });
  }

  function execute(message) {
    if (message.cmd === 'new') {
      const factory = { writer: 'swriter', calc: 'scalc', impress: 'simpress' }[message.kind];
      if (!factory) throw new Error('未知的新建类型');
      const doc = desktop.loadComponentFromURL('private:factory/' + factory, '_blank', 0,
        [property('Hidden', false), property('Silent', true)]);
      attach(doc, message.generation, true);
      return;
    }
    if (message.cmd === 'open') {
      if (!/^\/tmp\/qing-input-[a-zA-Z0-9.-]+$/.test(message.path)) throw new Error('无效的文件');
      const props = [property('Hidden', false), property('ReadOnly', false),
        property('MacroExecutionMode', css.document.MacroExecMode.NEVER_EXECUTE),
        property('UpdateDocMode', css.document.UpdateDocMode.NO_UPDATE), property('Silent', true)];
      const doc = desktop.loadComponentFromURL('file://' + message.path, '_blank', 0, props);
      if (!doc) throw new Error('无法打开文件，文件可能已损坏或需要密码');
      attach(doc, message.generation, false);
      return;
    }
    if (!model) throw new Error('请先打开文件');
    if (message.generation !== undefined && message.generation !== generation) return;
    switch (message.cmd) {
      case 'export': exportFile(message.extension, message.requestId); break;
      case 'saved': model.setModified(false); break;
      case 'command': {
        const allowed = ['Undo', 'Redo', 'Bold', 'Italic', 'Underline', 'LeftPara',
          'CenterPara', 'RightPara', 'InsertPagebreak', 'InsertRows', 'InsertColumns',
          'InsertPage', 'DeletePage', 'DefaultBullet', 'NumberFormatCurrency', 'AutoSum'];
        if (!allowed.includes(message.name)) throw new Error('未知的编辑命令');
        let name = message.name;
        if (kind === 'calc') name = { LeftPara: 'AlignLeft', CenterPara: 'AlignHorizontalCenter', RightPara: 'AlignRight' }[name] || name;
        dispatch(name);
        break;
      }
      case 'font':
        dispatch('CharFontName', { CharFontName: String(message.family) });
        break;
      case 'fontSize':
        dispatch('FontHeight', { FontHeight: Math.max(6, Math.min(150, Number(message.size))) });
        break;
      case 'color': dispatch('Color', { Color: parseInt(message.color.replace('#', ''), 16) }); break;
      case 'insertText': {
        const text = String(message.text || '').replace(/\r\n/g, '\n').slice(0, 2000000);
        if (!text) break;
        if (kind === 'writer') {
          const viewCursor = controller.getViewCursor();
          const ownerText = viewCursor.getText();
          const undo = model.getUndoManager();
          undo.enterUndoContext('输入文字');
          try { ownerText.insertString(viewCursor, text, true); viewCursor.collapseToEnd(); }
          finally { undo.leaveUndoContext(); }
        }
        else if (kind === 'calc') {
          const selection = controller.getSelection();
          const address = selection.getRangeAddress();
          const sheet = model.getSheets().getByIndex(address.Sheet);
          const rows = text.split('\n');
          if (rows[rows.length - 1] === '') rows.pop();
          for (let r = 0; r < rows.length; r++) for (const [c, value] of rows[r].split('\t').entries()) {
            const cell = sheet.getCellByPosition(address.StartColumn + c, address.StartRow + r);
            if (value.startsWith('=')) cell.setFormula(value);
            else if (value.trim() !== '' && Number.isFinite(Number(value))) cell.setValue(Number(value));
            else cell.setString(value);
          }
        } else {
          const selection = controller.getSelection();
          let range;
          try { if (selection.getText) range = selection; } catch (_) {}
          if (!range) {
            try { range = selection.getByIndex(0); } catch (_) {}
          }
          if (range && range.getText) {
            const textObject = range.getText();
            const cursor = textObject.createTextCursor(); cursor.gotoEnd(false);
            textObject.insertString(cursor, text, false);
          } else throw new Error('请先双击文字框，再输入或粘贴文字');
        }
        break;
      }
      case 'copy': {
        let text = '';
        if (kind === 'writer') text = controller.getViewCursor().getString();
        else if (kind === 'calc') text = controller.getSelection().getDataArray().map(row => row.join('\t')).join('\n');
        else {
          const selection = controller.getSelection();
          try { text = selection.getString(); } catch (_) {
            try { text = selection.getByIndex(0).getString(); } catch (_) {}
          }
        }
        emit({ cmd: 'copied', text, cut: Boolean(message.cut), generation });
        if (message.cut && text) dispatch('Delete');
        break;
      }
      case 'key': {
        const key = message.key, select = Boolean(message.shift);
        if (message.ctrl && ['z','y','b','i','u'].includes(key)) {
          dispatch({z:select?'Redo':'Undo',y:'Redo',b:'Bold',i:'Italic',u:'Underline'}[key]); break;
        }
        if (kind === 'writer') {
          const cursor = controller.getViewCursor();
          if (message.ctrl && key === 'a') { cursor.gotoStart(false); cursor.gotoEnd(true); }
          else if (key === 'ArrowLeft') cursor.goLeft(1,select);
          else if (key === 'ArrowRight') cursor.goRight(1,select);
          else if (key === 'ArrowUp') cursor.goUp(1,select);
          else if (key === 'ArrowDown') cursor.goDown(1,select);
          else if (key === 'Home') { if(message.ctrl) cursor.gotoRange(model.getText().getStart(),select); else cursor.gotoStartOfLine(select); }
          else if (key === 'End') { if(message.ctrl) cursor.gotoRange(model.getText().getEnd(),select); else cursor.gotoEndOfLine(select); }
          else if (key === 'Backspace' || key === 'Delete') {
            if(cursor.isCollapsed()) key==='Backspace'?cursor.goLeft(1,true):cursor.goRight(1,true);
            cursor.getText().insertString(cursor,'',true);
          }
          else if (key === 'Enter') {cursor.getText().insertControlCharacter(cursor,css.text.ControlCharacter.PARAGRAPH_BREAK,true);cursor.collapseToEnd();}
          else if (key === 'Tab') {cursor.getText().insertString(cursor,'\t',true);cursor.collapseToEnd();}
          else if (key === 'PageUp') cursor.goUp(20,select);
          else if (key === 'PageDown') cursor.goDown(20,select);
        } else if (kind === 'calc') {
          const selection=controller.getSelection(),address=selection.getRangeAddress();
          const sheet=model.getSheets().getByIndex(address.Sheet);
          let col=address.StartColumn,row=address.StartRow;
          if(key==='ArrowLeft') col--;
          if(key==='ArrowRight'||key==='Tab') col+=message.shift?-1:1;
          if(key==='ArrowUp') row--;
          if(key==='ArrowDown'||key==='Enter') row+=message.shift?-1:1;
          if(key==='Home') {col=0;if(message.ctrl)row=0;}
          if(key==='PageDown')row+=20;
          if(key==='PageUp')row-=20;
          if(key==='Backspace'||key==='Delete') selection.clearContents(23);
          else if(message.ctrl&&key==='a') dispatch('SelectAll');
          else controller.select(sheet.getCellByPosition(Math.max(0,col),Math.max(0,row)));
        } else {
          if(key==='Enter') execute({cmd:'insertText',text:'\n'});
          else if(key==='Delete'||key==='Backspace') dispatch('Delete');
          else if(message.ctrl&&key==='a') dispatch('SelectAll');
        }
        break;
      }
      case 'table': {
        if (kind !== 'writer') throw new Error('表格插入用于文字文档');
        const rows = Math.max(1, Math.min(100, Number(message.rows) | 0));
        const columns = Math.max(1, Math.min(30, Number(message.columns) | 0));
        const table = model.createInstance('com.sun.star.text.TextTable');
        table.initialize(rows, columns);
        model.getText().insertTextContent(controller.getViewCursor(), table, false);
        break;
      }
      case 'image':
        if (!/^\/tmp\/qing-image-[a-zA-Z0-9.-]+$/.test(message.path)) throw new Error('图片路径无效');
        dispatch('InsertGraphic', { FileName: 'file://' + message.path });
        break;
      case 'zoom': {
        const value = Math.max(25, Math.min(250, Number(message.value)));
        if (kind === 'writer') controller.ViewSettings.ZoomValue = value;
        else if (kind === 'calc') controller.ZoomValue = value;
        else dispatch('Zoom', { 'Zoom.Value': value });
        break;
      }
      case 'tools': showTools(message.visible); emit({ cmd: 'tools', visible: fullTools }); break;
      case 'find': {
        const descriptor = model.createSearchDescriptor();
        descriptor.SearchString = String(message.text);
        const found = model.findFirst(descriptor);
        if (found) { controller.select(found); emit({ cmd: 'found', found: true }); }
        else emit({ cmd: 'found', found: false });
        break;
      }
      case 'readOnly':
        if (kind === 'writer') controller.ViewSettings.ShowRulers = !message.value;
        // View mode is applied by the main UI; it must never silently discard edits.
        break;
      case 'close':
        generation = 0;
        model.setModified(false);
        model.close(true);
        model = undefined;
        controller = undefined;
        retainedListeners = [];
        emit({ cmd: 'closed' });
        break;
      case 'inspect': emit({ cmd: 'inspection', requestId: message.requestId, ...description() }); break;
      default: throw new Error('未知的文档操作');
    }
  }

  try {
    setupConfiguration();
    zeta.mainPort.onmessage = function (event) {
      try { execute(event.data); }
      catch (error) {
        const text = exceptionText(error);
        console.error('Document operation failed', event.data.cmd, text);
        emit({ cmd: 'error', operation: event.data.cmd, requestId: event.data.requestId,
          generation: event.data.generation, message: text });
      }
    };
    emit({ cmd: 'ready' });
  } catch (error) {
    emit({ cmd: 'fatal', message: exceptionText(error) });
  }
});
