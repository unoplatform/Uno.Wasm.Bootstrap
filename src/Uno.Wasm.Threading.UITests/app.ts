const puppeteer = require('puppeteer');
const path = require("path");

function delay(time) {
	return new Promise(function (resolve) {
		setTimeout(resolve, time)
	});
}

(async () => {
	const browser = await puppeteer.launch({
		"headless": false,
		args: ['--no-sandbox', '--disable-setuid-sandbox'],
		"defaultViewport": { "width": 1280, "height": 1024 }
	});
	const page = await browser.newPage();

	page.on('console', msg => {
		console.log('BROWSER LOG:', msg.text());
	});
	page.on('requestfailed', err => console.error('BROWSER-REQUEST-FAILED:', err))

	await page.goto("http://localhost:8000/");

	let value: string = null;

	console.log(`Init puppeteer`);

	let counter = 10;

	while (value === null && counter-- > 0) {
		await delay(2000);
		try {
			value = await page.$eval('#results', a => a.textContent) as string;

			console.log(`got value= ${value}`);

			if (value.indexOf('results') === -1) {
				value = null;
			}
		}
		catch (e) {
			console.log(`Waiting for results... (${e})`);
		}
	}

	await page.screenshot({ path: 'threads-tests.png' });

	await browser.close();

	if (!value) {
		console.log(`Failed to read the results`);
		process.exit(1);
	} else {
		console.log(`Results: ${value}`);
	}

	const expected = "StartupWorking...Done 10000 results";

	if (value !== expected) {
		console.log(`Invalid results got ${value}, expected ${expected}`);
		process.exit(1);
	}
})();

