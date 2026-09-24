const {chromium, expect}=require('../src/Sonda.Web/node_modules/@playwright/test');
const fs=require('node:fs');
(async()=>{
 const browser=await chromium.launch({channel:'chrome',headless:true});
 try {
  const context=await browser.newContext({ignoreHTTPSErrors:true,viewport:{width:1366,height:736},timezoneId:'Asia/Tokyo'});
  const page=await context.newPage();const errors=[];page.on('pageerror',e=>errors.push(e.message));
  await page.goto(process.env.SONDA_BROWSER_URL);
  await page.getByLabel('Login',{exact:true}).fill(process.env.SONDA_BROWSER_LOGIN);
  await page.getByLabel('Password',{exact:true}).fill(process.env.SONDA_BROWSER_PASSWORD);
  await page.getByRole('button',{name:'Sign in',exact:true}).click();
  await expect(page.locator('.greeting')).toContainText(process.env.SONDA_BROWSER_LOGIN);
  const api=async path=>{const r=await context.request.get(process.env.SONDA_BROWSER_URL+'/api/v1'+path);expect(r.ok()).toBeTruthy();return r.json();};
  const session=await api('/session'),dashboard=await api('/dashboard');
  const hour=Number(new Intl.DateTimeFormat('en-US',{timeZone:session.reportingTimeZoneIanaId,hour:'numeric',hourCycle:'h23'}).format(new Date(session.asOf)));
  await expect(page.locator('.greeting h1')).toHaveText(`${hour<12?'Good Morning':hour<18?'Good Afternoon':'Good Evening'}, ${session.displayName}`);
  await expect(page.locator('.metric-card').nth(0).locator('.card-badge')).toHaveText(dashboard.currentSystemBadge.businessHealth);
  await expect(page.locator('.metric-card').nth(2).locator('.card-badge')).toHaveText(dashboard.activeIncidentsBadge);
  const info=page.getByRole('button',{name:'Info',exact:true}).first();await info.click();
  const incident=page.getByRole('dialog',{name:'Incident information',exact:true});await expect(incident).toBeVisible();
  await expect(incident.getByText('CAM',{exact:true})).toBeVisible();
  await incident.getByRole('button',{name:'Order / Run',exact:true}).click();
  await expect(incident.getByRole('button',{name:'Open run details',exact:true}).first()).toBeVisible();
  await incident.getByRole('button',{name:'Open run details',exact:true}).first().click();
  await expect(page.getByRole('dialog',{name:'Run details',exact:true})).toBeVisible();
  await page.getByRole('button',{name:'Close Run details',exact:true}).click();
  await incident.getByRole('button',{name:'Evidence',exact:true}).click();await expect(incident.locator('pre').first()).toBeVisible();
  await incident.getByRole('button',{name:'History',exact:true}).click();await expect(incident.getByText('Active',{exact:true}).first()).toBeVisible();
  await incident.getByLabel('Reason',{exact:true}).fill('Browser investigation');
  // Lose only the response after the real server commits. Reconciliation must use the original durable ID.
  let operationId;await page.route('**/api/v1/incidents/*/status',async route=>{operationId=route.request().postDataJSON().operationId;const r=await route.fetch();expect(r.ok()).toBeTruthy();await route.abort('failed');},{times:1});
  await incident.getByRole('button',{name:'Investigating',exact:true}).click();await expect(incident.getByText('Outcome unknown.',{exact:false})).toBeVisible();
  await incident.getByRole('button',{name:'Close Incident information'}).click();await info.click();
  await expect(incident.getByText(`Operation ID: ${operationId}`)).toBeVisible();await incident.getByRole('button',{name:'Check operation',exact:true}).click();
  await expect(incident.getByText('Operation Committed.',{exact:true})).toBeVisible();
  await incident.getByRole('button',{name:'History',exact:true}).click();await expect(incident.getByText('Browser investigation',{exact:true})).toBeVisible();
  await page.keyboard.press('Escape');await expect(incident).toHaveCount(0);await expect(info).toBeFocused();expect(new URL(page.url()).pathname).toBe('/');
  await page.getByRole('link',{name:'Monitoring',exact:true}).click();await expect(page.getByRole('complementary',{name:'Logs Monitored'})).toHaveCount(0);
  await page.locator('.monitor-row').filter({hasText:'CAM'}).click();
  const app=page.getByRole('dialog',{name:'CAM',exact:true});await expect(app.getByText('Availability',{exact:true})).toBeVisible();
  await app.getByRole('button',{name:'Incidents',exact:true}).click();await expect(app.getByRole('heading',{name:'Investigating incidents',exact:true})).toBeVisible();await expect(app.getByText('Investigating',{exact:false}).first()).toBeVisible();await app.getByRole('button',{name:'Runs',exact:true}).click();await expect(app.getByRole('heading',{name:'Recent failed Order Runs',exact:true})).toBeVisible();
  await expect(app).not.toContainText('UNITELLER');await page.keyboard.press('Escape');
  await page.evaluate(()=>{document.documentElement.requestFullscreen=()=>Promise.reject(new Error('Test unavailable'));});
  await page.getByRole('button',{name:'Fullscreen monitoring',exact:true}).click();await expect(page.getByRole('navigation',{name:'Primary'})).toHaveCount(0);
  await expect(page.locator('.monitor-row')).toHaveCount(2);await page.getByRole('button',{name:'Exit fullscreen',exact:true}).click();
  await page.getByRole('link',{name:'Search',exact:true}).click();await expect(page.locator('.order-result')).toHaveCount(8);await page.locator('.order-result').first().click();await expect(page.getByRole('dialog',{name:'Run details',exact:true})).toBeVisible();await page.getByRole('button',{name:'Close Run details',exact:true}).click();
  await expect(page.getByRole('heading',{name:'Unassigned evidence',exact:true}).first()).toBeVisible();
  await expect(page.getByRole('button',{name:'10:01:04 Outside cycle unmatched evidence',exact:true})).toHaveCount(1);
  const shared=page.getByRole('button',{name:'10:01:03 CAM process completed',exact:true});await expect(shared).toHaveCount(1);
  await expect(page.getByText('Shared evidence →',{exact:false}).first()).toBeVisible();await shared.click();
  const evidence=page.getByRole('dialog',{name:'Evidence',exact:true});await expect(evidence.locator('ul button')).toHaveCount(3);
  await evidence.locator('ul button').first().click();await expect(page.getByRole('dialog',{name:'Run details',exact:true})).toBeVisible();
  await page.getByRole('button',{name:'Close Run details',exact:true}).click();await page.getByRole('button',{name:'Close Evidence',exact:true}).click();
  expect(errors).toEqual([]);
  fs.writeFileSync('artifacts/phase6/screen-integration-results.json',JSON.stringify({passed:true,transport:'real loopback HTTPS',database:'disposable PostgreSQL',apiResponsesMocked:false,checks:['server-timezone authenticated greeting with Tokyo browser','authoritative dashboard badges','Home Incident modal tabs and nested runs','lost acknowledgment then close/reopen and committed receipt reconciliation','history reflects real manual workflow','Home focus restoration without navigation','application-scoped popup','fullscreen permission failure fallback and exit','eight grouped Order Runs','neutral unmatched evidence','shared cycle-end evidence appears once with all three linked runs']},null,2));
 } finally {await browser.close();}
})().catch(e=>{console.error(e.message);process.exitCode=1;});


