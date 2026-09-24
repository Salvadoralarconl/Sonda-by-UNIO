const {chromium}=require('../src/Sonda.Web/node_modules/@playwright/test');
const {pathToFileURL}=require('node:url');
const path=require('node:path');
const fs=require('node:fs');
const crypto=require('node:crypto');
(async()=>{
 const root=path.resolve('artifacts/phase6/screens-review');const browser=await chromium.launch({channel:'chrome',headless:true});
 try {const page=await browser.newPage({viewport:{width:1450,height:1100}});
  const hashes=[];
  for(const name of ['Home','Monitoring','Search']){
   await page.goto(pathToFileURL(path.join(root,'comparison.html')).href+'#'+name.toLowerCase());
   await page.getByRole('button',{name:new RegExp('· '+name+'$')}).click();
   await page.evaluate(async()=>Promise.all([...document.images].map(i=>i.decode())));
   await page.locator('.stage').screenshot({path:path.join(root,name.toLowerCase()+'-overlay.png')});
   const reference=path.resolve('docs/reference/canva-phase6-export',name+' Page.png');
   const implementation=path.join(root,name.toLowerCase()+'.png');
   hashes.push({page:name,referenceSha256:crypto.createHash('sha256').update(fs.readFileSync(reference)).digest('hex'),implementationSha256:crypto.createHash('sha256').update(fs.readFileSync(implementation)).digest('hex')});
  }
  fs.writeFileSync(path.join(root,'comparison-provenance.json'),JSON.stringify({status:'Awaiting review; no frozen visual baseline',overlay:'50% browser-rendered implementation over unchanged reference at 1366x736',pages:hashes},null,2));
 } finally {await browser.close();}
})().catch(e=>{console.error(e.message);process.exitCode=1;});
