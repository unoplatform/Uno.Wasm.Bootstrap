﻿const puppeteer = require('puppeteer');
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
	await page.goto("http://localhost:8000/");

	let value = null;

	console.log(`Init puppeteer`);

	let counter = 10;

	while (counter-- > 0) {
		await delay(2000);
		try {
			value = await page.$eval('#results', a => a.innerText);
			console.log(`got value= [${value}]`);

			if (value && value.length > 0) {
				break;
			}
		}
		catch (e) {
			console.log(`Waiting for results... (${e})`);
		}
	}

	await page.screenshot({ path: `${process.env.BUILD_ARTIFACTSTAGINGDIRECTORY}/linking_test.png` });

	await browser.close();

	if (!value) {
		console.log(`Failed to read the results`);
		process.exit(1);
	} else {
		console.log(`Results: ${value}`);
	}

	let expected = process.platform === 'darwin' ? "Interpreter;" : "InterpreterAndAOT;";
	expected += "42;42.30;42.7;e42;True;true;True;1.2;1.4;3.1;0;42;requireJs:true;jsInterop:Invoked;gl:true;ex:true;sat:True;la:True;";

	if (value !== expected) {
		console.log(`Invalid results got ${value}, expected ${expected}`);
		process.exit(1);
	}
})();


function delay(time) {
	return new Promise(function (resolve) {
		setTimeout(resolve, time)
	});
}
