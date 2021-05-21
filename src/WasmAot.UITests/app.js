"use strict";
var __awaiter = (this && this.__awaiter) || function (thisArg, _arguments, P, generator) {
    function adopt(value) { return value instanceof P ? value : new P(function (resolve) { resolve(value); }); }
    return new (P || (P = Promise))(function (resolve, reject) {
        function fulfilled(value) { try { step(generator.next(value)); } catch (e) { reject(e); } }
        function rejected(value) { try { step(generator["throw"](value)); } catch (e) { reject(e); } }
        function step(result) { result.done ? resolve(result.value) : adopt(result.value).then(fulfilled, rejected); }
        step((generator = generator.apply(thisArg, _arguments || [])).next());
    });
};
Object.defineProperty(exports, "__esModule", { value: true });
const puppeteer = require('puppeteer');
const path = require("path");
(() => __awaiter(void 0, void 0, void 0, function* () {
    const browser = yield puppeteer.launch({
        "headless": true,
        args: ['--no-sandbox', '--disable-setuid-sandbox'],
        "defaultViewport": { "width": 1280, "height": 1024 }
    });
    const page = yield browser.newPage();
    page.on('console', msg => {
        console.log('BROWSER LOG:', msg.text());
    });
    page.on('requestfailed', err => console.error('BROWSER-REQUEST-FAILED:', err));
    yield page.goto(process.env.BOOTSTRAP_TEST_RUNNER_URL);
    var value = null;
    console.log(`Init puppeteer`);
    var counter = 3;
    while (value == null && counter-- > 0) {
        yield delay(2000);
        try {
            value = yield page.$eval('#results', a => a.textContent);
            console.log(`got value= ${value}`);
        }
        catch (e) {
            console.log(`Waiting for results... (${e})`);
        }
    }
    yield page.screenshot({ path: `${process.env.BUILD_ARTIFACTSTAGINGDIRECTORY}/aotTests.png` });
    yield browser.close();
    if (!value) {
        console.log(`Failed to read the results`);
        process.exit(1);
    }
    else {
        console.log(`Results: ${value}`);
    }
}))();
function delay(time) {
    return new Promise(function (resolve) {
        setTimeout(resolve, time);
    });
}
function keepAlive() {
    setTimeout(keepAlive, 1000);
}
//# sourceMappingURL=app.js.map