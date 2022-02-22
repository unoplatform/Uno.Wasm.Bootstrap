var __awaiter = (this && this.__awaiter) || function (thisArg, _arguments, P, generator) {
    function adopt(value) { return value instanceof P ? value : new P(function (resolve) { resolve(value); }); }
    return new (P || (P = Promise))(function (resolve, reject) {
        function fulfilled(value) { try { step(generator.next(value)); } catch (e) { reject(e); } }
        function rejected(value) { try { step(generator["throw"](value)); } catch (e) { reject(e); } }
        function step(result) { result.done ? resolve(result.value) : adopt(result.value).then(fulfilled, rejected); }
        step((generator = generator.apply(thisArg, _arguments || [])).next());
    });
};
const puppeteer = require('puppeteer');
const path = require("path");
(() => __awaiter(this, void 0, void 0, function* () {
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
    yield page.goto("http://localhost:8000/");
    let value = null;
    console.log(`Init puppeteer`);
    let counter = 3;
    while (value === null && counter-- > 0) {
        yield delay(2000);
        try {
            value = yield page.$eval('#results', a => a.textContent);
            console.log(`got value= ${value}`);
        }
        catch (e) {
            console.log(`Waiting for results... (${e})`);
        }
    }
    yield page.screenshot({ path: `${process.env.BUILD_ARTIFACTSTAGINGDIRECTORY}/linking_test.png` });
    yield browser.close();
    if (!value) {
        console.log(`Failed to read the results`);
        process.exit(1);
    }
    else {
        console.log(`Results: ${value}`);
    }
    const expected = "InterpreterAndAOT;42;42.30;42.7;e42;True;true;True;1.3;1.4;3.0;0;42;requireJs:true;";
    if (value !== expected) {
        console.log(`Invalid results got ${value}, expected ${expected}`);
        process.exit(1);
    }
}))();
function delay(time) {
    return new Promise(function (resolve) {
        setTimeout(resolve, time);
    });
}
//# sourceMappingURL=app.js.map
