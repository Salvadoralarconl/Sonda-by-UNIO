import { defineConfig } from '@playwright/test';
export default defineConfig({testDir:'./browser-tests',use:{baseURL:'http://127.0.0.1:5173',channel:'chrome',viewport:{width:1366,height:736}},webServer:{command:'npm run dev',url:'http://127.0.0.1:5173',reuseExistingServer:false},reporter:[['list'],['json',{outputFile:'../../artifacts/phase6/browser-results.json'}]]});
