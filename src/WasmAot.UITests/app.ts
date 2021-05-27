import { settings } from "cluster";

const puppeteer = require('puppeteer');
const path = require("path");

(async () => {
	const browser = await puppeteer.launch({
		"headless": true,
		args: ['--no-sandbox', '--disable-setuid-sandbox'],
		"defaultViewport": { "width": 1280, "height": 1024 }
	});
	const page = await browser.newPage();
	page.on('console', msg => {
		console.log('BROWSER LOG:', msg.text());
	});
	page.on('requestfailed', err => console.error('BROWSER-REQUEST-FAILED:', err))

	await page.goto(process.env.BOOTSTRAP_TEST_RUNNER_URL);

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

	await page.screenshot({ path: `${process.env.BUILD_ARTIFACTSTAGINGDIRECTORY}/aotTests.png` });

	await browser.close();

	if (!value) {
		console.log(`Failed to read the results`);
		process.exit(1);
	} else {
		console.log(`Results: ${value}`);
	}
})();


function delay(time) {
	return new Promise(function (resolve) {
		setTimeout(resolve, time)
	});
}

function keepAlive() {
	setTimeout(keepAlive, 1000);
}
