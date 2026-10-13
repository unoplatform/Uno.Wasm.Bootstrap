// Serves a published app, slowly enough for the loader to be observed: GET /__mode/normal | failwasm | slow | keep | custom
// usage: node server.js <wwwroot> <port>
//
// Responses are delayed by 150 ms, and dotnet.native.*.wasm by 2 s (20 s in slow mode, past the loader's 15 s slow
// threshold). In failwasm mode, dotnet.native.*.wasm is a 404.
// keep: index.html marks the loader uno-keep-loader as soon as it's set up, like Uno Platform does, so the test can
// remove it the way Uno Platform does on its first frame.
// custom: index.html has an app-provided loader (data-uno-loader="custom") with its own styles instead of the built-in one.
// csp: index.html is sent with a Content-Security-Policy header that blocks inline styles.
const http = require("http");
const fs = require("fs");
const path = require("path");

const root = path.resolve(process.argv[2]);
const port = parseInt(process.argv[3] || "8005");

const types = {
	".html": "text/html", ".js": "text/javascript", ".css": "text/css", ".json": "application/json",
	".wasm": "application/wasm", ".png": "image/png", ".webmanifest": "application/manifest+json",
};

let mode = "normal";

const keepLoaderScript = `<script>(function keep() {
	const loader = document.querySelector(".uno-persistent-loader");
	loader ? loader.classList.add("uno-keep-loader") : requestAnimationFrame(keep);
})();</script>`;

// In <head>: the app can replace #uno-body's content, and the loader's exit transition must survive that
const customLoaderStyle = `<style>
	.uno-loader { position: fixed; inset: 0; background: #123; color: #fff; transition: opacity 400ms; }
	.uno-loader.uno-leaving { opacity: 0; }
</style>`;

const customLoader = `<div id="uno-body" class="container-fluid uno-body">
	<div class="uno-loader" data-uno-loader="custom"><span class="mine">Custom</span><progress max="100"></progress></div>
</div>
<noscript>`;

function rewriteIndexHtml(html) {
	if (mode === "keep") {
		return html.replace("</head>", keepLoaderScript + "</head>");
	}
	if (mode === "custom") {
		return html
			.replace(/<style id="uno-bootstrap-css"[^>]*>[\s\S]*?<\/style>/, "")
			.replace("</head>", customLoaderStyle + "</head>")
			.replace(/<div id="uno-body"[\s\S]*?<noscript>/, customLoader);
	}
	return html;
}

http.createServer((req, res) => {
	const urlPath = decodeURIComponent(new URL(req.url, "http://localhost").pathname);

	if (urlPath.startsWith("/__mode/")) {
		mode = urlPath.substring("/__mode/".length);
		return res.end(mode);
	}

	const isNativeWasm = /\/dotnet\.native\.[^/]*\.wasm$/.test(urlPath);
	setTimeout(() => {
		const fileName = urlPath.endsWith("/") ? urlPath + "index.html" : urlPath;
		const file = path.join(root, fileName);
		if ((isNativeWasm && mode === "failwasm") || path.relative(root, file).startsWith("..") || path.isAbsolute(path.relative(root, file)) || !fs.existsSync(file) || fs.statSync(file).isDirectory()) {
			res.statusCode = 404;
			return res.end();
		}

		const body = path.extname(fileName) === ".html"
			? Buffer.from(rewriteIndexHtml(fs.readFileSync(file, "utf8")))
			: fs.readFileSync(file);
		res.writeHead(200, {
			"content-type": types[path.extname(fileName)] || "application/octet-stream",
			"content-length": body.length,
			"cache-control": "no-store",
			...(mode === "csp" && path.extname(fileName) === ".html" ? { "content-security-policy": "style-src 'self'" } : {}),
		});
		res.end(body);
	}, isNativeWasm ? (mode === "slow" ? 20000 : 2000) : 150);
}).listen(port, () => console.log(`server on http://localhost:${port}/ serving ${root}`));
