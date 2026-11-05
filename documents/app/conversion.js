// Loaded by app.js as a local module; actions retain the original draft state.
function initializeConversion({state, exportBytes, host, setSaving, update, report, toast, onSaved}) {
  const button=document.querySelector('#conversion-button');
  const dialog=document.querySelector('#conversion-dialog');
  const select=document.querySelector('#conversion-target');
  let saveAs=false;
  const open=(asNew)=>{saveAs=asNew;
    const s=state();if(!s.current||s.busy||s.saving||s.printing)return;
    const choices={writer:['docx','doc','odt','rtf','txt','pdf'],calc:['xlsx','xls','ods','csv','tsv','pdf'],impress:['pptx','ppt','odp','pdf'],draw:['pdf','odg'],pdf:['pdf']}[s.current.kind]||['pdf'];
    select.replaceChildren(...choices.map(extension=>{const option=document.createElement('option');option.value=extension;option.textContent=extension.toUpperCase();return option;}));
    dialog.showModal();
  };
  button.onclick=()=>open(false);window.glintOpenSaveAs=()=>open(true);
  dialog.querySelector('[data-convert-cancel]').onclick=()=>dialog.close();
  dialog.querySelector('[data-convert-start]').onclick=async()=>{
    const extension=select.value;dialog.close();const s=state();if(!s.current||s.busy||s.saving||s.printing)return;
    try{
      setSaving(true);update();const bytes=await exportBytes(extension);
      let binary='';for(let i=0;i<bytes.length;i+=0x8000)binary+=String.fromCharCode(...bytes.subarray(i,i+0x8000));
      const result=await host('save',{extension,name:s.current.name.replace(/\.[^.]+$/,'')+'-转换.'+extension,base64:btoa(binary),saveAs:true,exportOnly:!saveAs||(extension==='pdf'&&s.current.kind!=='pdf')});
      if(!result?.canceled){if(saveAs&&onSaved&&(extension!=='pdf'||s.current.kind==='pdf'))onSaved(result);toast(saveAs?'文件已另存':'转换文件已保存');}
    }catch(error){report(error);}finally{setSaving(false);update();}
  };
}
