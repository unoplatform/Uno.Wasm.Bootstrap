const puppeteer = require('puppeteer');
const path = require("path");

(async () => {
	const browser = await puppeteer.launch({
		"headless": true,
		args: ['--no-sandbox', '--disable-setuid-sandbox'],
		"defaultViewport": { "width": 1280, "height": 1024 }
	});
	const page = await browser.newPage();
	await page.goto("http://localhost:8001/");

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

	await browser.close();

	if (!value) {
		console.log(`Failed to read the results`);
		process.exit(1);
	} else {
		console.log(`Results: ${value}`);
	}

    var expected = "FullAOT;42;42.3;42.7;e42";

	if (value != expected) {
		console.log(`Invalid results got ${value}, expected ${expected}`);
		process.exit(1);
	}
})();


function delay(time) {
	return new Promise(function (resolve) {
		setTimeout(resolve, time)
	});
}
