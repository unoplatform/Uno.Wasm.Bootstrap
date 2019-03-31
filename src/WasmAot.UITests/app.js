var __awaiter = (this && this.__awaiter) || function (thisArg, _arguments, P, generator) {
    return new (P || (P = Promise))(function (resolve, reject) {
        function fulfilled(value) { try { step(generator.next(value)); } catch (e) { reject(e); } }
        function rejected(value) { try { step(generator["throw"](value)); } catch (e) { reject(e); } }
        function step(result) { result.done ? resolve(result.value) : new P(function (resolve) { resolve(result.value); }).then(fulfilled, rejected); }
        step((generator = generator.apply(thisArg, _arguments || [])).next());
    });
};
const puppeteer = require('puppeteer');
const path = require("path");
(() => __awaiter(this, void 0, void 0, function* () {
    console.log(`Init puppeteer`);
    const browser = yield puppeteer.launch({ "headless": true, "defaultViewport": { "width": 1280, "height": 1024 } });
    const page = yield browser.newPage();
    yield page.goto("http://localhost:8000/");
    var value = null;
    var counter = 3;
    console.log(`Reading results...`);
    while (value == null && counter-- > 0) {
        yield delay(5000);
        try {
            value = yield page.$eval('#results', a => a.textContent);
            console.log(`got value= ${value}`);
        }
        catch (e) {
            console.log(`Waiting for results... (${e})`);
        }
    }
    yield page.screenshot({ path: 'aotTests.png' });
    console.log(`Results: ${value}`);
    yield browser.close();
}))();
function delay(time) {
    return new Promise(function (resolve) {
        setTimeout(resolve, time);
    });
}
//# sourceMappingURL=app.js.map
