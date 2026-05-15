// Copies the Blazor WASM published wwwroot into ./www so Capacitor can package it as the WebView content.
const fs = require('fs');
const path = require('path');

const src = path.resolve(__dirname, '../../publish/driver-app/wwwroot');
const dst = path.resolve(__dirname, 'www');

function copyDir(s, d) {
    fs.mkdirSync(d, { recursive: true });
    for (const entry of fs.readdirSync(s, { withFileTypes: true })) {
        const sp = path.join(s, entry.name);
        const dp = path.join(d, entry.name);
        if (entry.isDirectory()) copyDir(sp, dp);
        else fs.copyFileSync(sp, dp);
    }
}

if (!fs.existsSync(src)) {
    console.error(`Published Blazor output not found at ${src}. Run 'npm run publish-pwa' first.`);
    process.exit(1);
}

if (fs.existsSync(dst)) fs.rmSync(dst, { recursive: true, force: true });
copyDir(src, dst);

// Patch index.html so the app bootstraps from the file:// origin properly
const indexPath = path.join(dst, 'index.html');
let html = fs.readFileSync(indexPath, 'utf8');
// Capacitor injects native bridge — no change needed for the Blazor side
fs.writeFileSync(indexPath, html);

console.log(`Copied PWA from ${src} → ${dst}`);
