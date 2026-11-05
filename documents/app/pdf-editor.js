// PDF reading and annotations remain local. Export preserves original pages.
let pdfjsPromise, pdfLibPromise;
function script(url) {
  return new Promise((resolve,reject)=>{const element=document.createElement('script');element.src=url;element.onload=resolve;element.onerror=()=>reject(new Error('PDF 组件不完整'));document.body.append(element);});
}
async function pdfjs() {
  if(!pdfjsPromise)pdfjsPromise=import('/pdf/pdf.min.mjs').then(module=>{
    module.GlobalWorkerOptions.workerSrc='/pdf/pdf.worker.min.mjs';return module;
  });
  return pdfjsPromise;
}
async function pdfLib() {
  if(!pdfLibPromise)pdfLibPromise=Promise.all([script('/pdf/pdf-lib.min.js'),script('/pdf/fontkit.min.js')]);
  await pdfLibPromise;return window.PDFLib;
}

export class PdfEditor {
  constructor(container,onChange,onPage) {
    this.container=container;this.onChange=onChange;this.onPage=onPage;
    this.notes=[];this.redoNotes=[];this.scale=1.2;this.tool='select';this.selected=null;this.renderToken=0;
    this.container.addEventListener('scroll',()=>this.updateCurrentPage(),{passive:true});
  }
  async open(bytes) {
    const module=await pdfjs();
    this.original=new Uint8Array(bytes);this.notes=[];this.redoNotes=[];this.selected=null;
    this.document=await module.getDocument({data:bytes.slice(),isEvalSupported:false,
      cMapUrl:'/pdf/cmaps/',cMapPacked:true,standardFontDataUrl:'/pdf/standard_fonts/',wasmUrl:'/pdf/wasm/'}).promise;
    this.scale=Math.min(1.2,Math.max(.4,(this.container.clientWidth-64)/595));
    await this.render();
  }
  async render() {
    const token=++this.renderToken;
    this.container.replaceChildren();this.pages=[];
    const module=await pdfjs();
    for(let index=0;index<this.document.numPages;index++){
      const page=await this.document.getPage(index+1);
      if(token!==this.renderToken)return;
      const viewport=page.getViewport({scale:this.scale});
      const wrapper=document.createElement('div');wrapper.className='pdf-page';wrapper.dataset.page=index;
      wrapper.style.width=viewport.width+'px';wrapper.style.height=viewport.height+'px';
      wrapper.style.setProperty('--scale-factor',this.scale);wrapper.style.setProperty('--total-scale-factor',this.scale);
      const canvas=document.createElement('canvas');const ratio=window.devicePixelRatio||1;
      canvas.width=Math.floor(viewport.width*ratio);canvas.height=Math.floor(viewport.height*ratio);
      canvas.style.width='100%';canvas.style.height='100%';
      const text=document.createElement('div');text.className='textLayer';
      const annotations=document.createElement('div');annotations.className='pdf-notes';
      wrapper.append(canvas,text,annotations);this.container.append(wrapper);
      const state={page,wrapper,canvas,text,annotations,viewport,index,rendered:false};this.pages.push(state);
      wrapper.addEventListener('pointerdown',event=>this.pointerDown(event,state));
    }
    this.observer?.disconnect();
    this.observer=new IntersectionObserver(entries=>{
      for(const entry of entries)if(entry.isIntersecting){const page=this.pages[Number(entry.target.dataset.page)];this.renderPage(page,module).catch(error=>console.error(error));}
    },{root:this.container,rootMargin:'600px'});
    for(const page of this.pages)this.observer.observe(page.wrapper);
    this.redrawNotes();this.updateCurrentPage();
    if(this.pages[0])await this.renderPage(this.pages[0],module);
  }
  async renderPage(state,module) {
    if(!state||state.rendered)return;state.rendered=true;
    const ratio=window.devicePixelRatio||1;
    await state.page.render({canvasContext:state.canvas.getContext('2d'),viewport:state.viewport,transform:ratio!==1?[ratio,0,0,ratio,0,0]:undefined}).promise;
    const layer=new module.TextLayer({textContentSource:state.page.streamTextContent(),container:state.text,viewport:state.viewport});
    await layer.render();
  }
  updateCurrentPage() {
    if(!this.pages?.length)return;
    let number=1;const top=this.container.getBoundingClientRect().top;
    for(const page of this.pages){if(page.wrapper.getBoundingClientRect().top<top+120)number=page.index+1;}
    this.currentPage=number;this.onPage(number,this.document.numPages);
  }
  go(number) {const page=this.pages[Math.max(0,Math.min(this.pages.length-1,number-1))];page?.wrapper.scrollIntoView({block:'start',behavior:'smooth'});}
  setTool(tool) {
    this.tool=tool;this.container.dataset.tool=tool;this.selected=null;this.redrawNotes();
  }
  pointerDown(event,page) {
    if(event.button!==0||this.tool==='select')return;
    event.preventDefault();
    const bounds=page.wrapper.getBoundingClientRect();
    const start={x:(event.clientX-bounds.left)/this.scale,y:(event.clientY-bounds.top)/this.scale};
    if(this.tool==='text'){
      const dialog=document.querySelector('#pdf-text-dialog');
      document.querySelector('#pdf-note-text').value='';
      dialog.returnValue='cancel';dialog.showModal();
      dialog.onclose=()=>{
        const value=document.querySelector('#pdf-note-text').value.trim();
        if(dialog.returnValue==='insert'&&value)this.addNote({type:'text',text:value,x:start.x,y:start.y,page:page.index,size:Number(document.querySelector('#pdf-note-size').value)||14});
      };return;
    }
    if(this.tool==='highlight'){
      const temporary=document.createElement('div');temporary.className='pdf-note highlight';page.annotations.append(temporary);
      const move=e=>{
        const end={x:(e.clientX-bounds.left)/this.scale,y:(e.clientY-bounds.top)/this.scale};
        const x=Math.min(start.x,end.x),y=Math.min(start.y,end.y),width=Math.abs(end.x-start.x),height=Math.abs(end.y-start.y);
        Object.assign(temporary.style,{left:x*this.scale+'px',top:y*this.scale+'px',width:width*this.scale+'px',height:height*this.scale+'px'});
        temporary.dataset.rect=JSON.stringify({x,y,width,height});
      };
      const up=e=>{
        page.wrapper.releasePointerCapture(e.pointerId);page.wrapper.removeEventListener('pointermove',move);page.wrapper.removeEventListener('pointerup',up);
        const rect=temporary.dataset.rect?JSON.parse(temporary.dataset.rect):null;temporary.remove();
        if(rect&&rect.width>3&&rect.height>3)this.addNote({type:'highlight',page:page.index,...rect});
      };
      page.wrapper.setPointerCapture(event.pointerId);page.wrapper.addEventListener('pointermove',move);page.wrapper.addEventListener('pointerup',up);
    }
  }
  addNote(note) {note.id=crypto.randomUUID();this.notes.push(note);this.redoNotes=[];this.selected=note.id;this.redrawNotes();this.onChange();}
  redrawNotes() {
    if(!this.pages)return;
    for(const page of this.pages)page.annotations.replaceChildren();
    for(const note of this.notes){
      const page=this.pages[note.page];if(!page)continue;
      const element=document.createElement('button');element.className='pdf-note '+note.type+(this.selected===note.id?' selected':'');
      element.style.left=note.x*this.scale+'px';element.style.top=note.y*this.scale+'px';
      if(note.type==='text'){element.textContent=note.text;element.style.fontSize=note.size*this.scale+'px';element.style.lineHeight='1';}
      else {element.style.width=note.width*this.scale+'px';element.style.height=note.height*this.scale+'px';}
      element.title='点击选中，按 Delete 删除这条标注';element.onclick=event=>{event.stopPropagation();this.selected=note.id;this.redrawNotes();};
      element.onpointerdown=event=>event.stopPropagation();page.annotations.append(element);
    }
  }
  undo(){if(this.notes.length){this.redoNotes.push(this.notes.pop());this.redrawNotes();this.onChange();}}
  redo(){if(this.redoNotes.length){this.notes.push(this.redoNotes.pop());this.redrawNotes();this.onChange();}}
  removeSelected(){const index=this.notes.findIndex(note=>note.id===this.selected);if(index>=0){this.notes.splice(index,1);this.redrawNotes();this.onChange();}}
  async setZoom(value){this.scale=value/100;await this.render();}
  async find(text){
    for(let index=0;index<this.document.numPages;index++){
      const page=await this.document.getPage(index+1);const content=await page.getTextContent();
      if(content.items.map(item=>item.str).join('').includes(text)){this.go(index+1);return true;}
    }return false;
  }
  async export(){
    if(!this.notes.length)return this.original.slice();
    const {PDFDocument,rgb}=await pdfLib();const doc=await PDFDocument.load(this.original);
    let font;
    if(this.notes.some(note=>note.type==='text')){
      doc.registerFontkit(window.fontkit);
      const bytes=await fetch('/pdf-font.ttf').then(response=>{if(!response.ok)throw new Error('无法读取 PDF 中文字体');return response.arrayBuffer();});
      font=await doc.embedFont(bytes,{subset:true});
    }
    for(const note of this.notes){
      const page=doc.getPage(note.page);const {height}=page.getSize();
      if(note.type==='text')page.drawText(note.text,{x:note.x,y:height-note.y-note.size,size:note.size,font,color:rgb(.15,.22,.18),lineHeight:note.size*1.3});
      else page.drawRectangle({x:note.x,y:height-note.y-note.height,width:note.width,height:note.height,color:rgb(1,.85,.15),opacity:.3});
    }
    return doc.save();
  }
  destroy(clear=true){this.renderToken++;this.observer?.disconnect();this.document?.destroy();if(clear)this.container.replaceChildren();}
}
