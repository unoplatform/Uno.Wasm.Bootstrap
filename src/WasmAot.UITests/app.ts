const puppeteer = require('puppeteer');
const path = require("path");

(async () => {
	const browser = await puppeteer.launch({ "headless": true, "defaultViewport": { "width": 1280, "height": 1024}});
	const page = await browser.newPage();
	await page.goto("http://localhost:8000/");

	var value = null;

	console.log(`Init puppeteer`);

	var counter = 3;

	while (value == null && counter-- > 0) {
		await delay(2000);
		try {
			value = await page.$eval('#results', a => a.textContent);
			console.log(`got value= ${value}`);
		}
		catch (e) {
			console.log(`Waiting for results... (${e})`);
		}
	}

	await page.screenshot({ path: 'aotTests.png' });

	console.log(`Results: ${value}`);

	await browser.close();
})();


function delay(time) {
	return new Promise(function (resolve) {
		setTimeout(resolve, time)
	});
}
