const {chromium,expect}=require('../src/Sonda.Web/node_modules/@playwright/test');
const fs=require('node:fs');
(async()=>{
 const browser=await chromium.launch({channel:'chrome',headless:false,args:['--start-maximized']});
 try {
  const context=await browser.newContext({ignoreHTTPSErrors:true,viewport:null});
  const page=await context.newPage();
  await page.goto(process.env.SONDA_BROWSER_URL);
  await page.getByLabel('Login',{exact:true}).fill(process.env.SONDA_BROWSER_LOGIN);
  await page.getByLabel('Password',{exact:true}).fill(process.env.SONDA_BROWSER_PASSWORD);
  await page.getByRole('button',{name:'Sign in',exact:true}).click();
  await expect(page.locator('.greeting')).toContainText(process.env.SONDA_BROWSER_LOGIN);
  await expect(page.locator('.monitor-row').first()).toBeVisible();
  fs.writeFileSync('.tools/local-preview-status.json',JSON.stringify({state:'ready',url:process.env.SONDA_BROWSER_URL,hostPid:Number(process.env.SONDA_PREVIEW_HOST_PID),startedAt:new Date().toISOString(),note:'Isolated sample-data preview; close this Chrome window to stop and discard the preview database.'},null,2));
  console.log('Preview browser is signed in and ready.');
  await new Promise(resolve=>{browser.on('disconnected',resolve);context.on('close',resolve);page.on('close',()=>{if(context.pages().length===0)resolve();});});
 } finally {await browser.close();}
})().catch(e=>{console.error(e.message);process.exitCode=1;});
