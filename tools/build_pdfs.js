// Imprime chaque manuel HTML en PDF A4. Usage : node build_pdfs.js <dossier docs>
const { chromium } = require('playwright');
const path = require('path');

(async () => {
  const docs = path.resolve(process.argv[2]);
  const browser = await chromium.launch();
  const page = await browser.newPage();
  await page.emulateMedia({ media: 'print', colorScheme: 'light' });

  for (const lang of ['fr', 'en', 'de', 'nl', 'es', 'it']) {
    await page.goto('file://' + path.join(docs, 'manual', lang + '.html'), { waitUntil: 'load' });
    await page.pdf({
      path: path.join(docs, 'manual', 'pdf', `SoundRebornRemote-manual-${lang}.pdf`),
      format: 'A4',
      printBackground: true,
      margin: { top: '16mm', bottom: '16mm', left: '12mm', right: '12mm' },
    });
    console.log(lang, 'ok');
  }
  await browser.close();
})();
