using System;
namespace Glint {
partial class Player {
    void ConvertCurrent(){
        if(!documentMode||document==null||!document.Ready){Notify("请先打开文档");return;}
        document.EvaluateAsync("document.querySelector('#conversion-button').click()");
    }
}
}
