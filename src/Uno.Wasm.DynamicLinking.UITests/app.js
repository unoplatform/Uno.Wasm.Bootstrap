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
    yield page.goto("http://localhost:8001/");
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
    yield page.screenshot({ path: 'aotTests.png' });
    yield browser.close();
    if (!value) {
        console.log(`Failed to read the results`);
        process.exit(1);
    }
    else {
        console.log(`Results: ${value}`);
    }
    var expected = "FullAOT;42;42.3;42.7;e42";
    if (value != expected) {
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