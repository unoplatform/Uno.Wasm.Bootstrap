// Serves a published app, failing the first request of every boot resource.
// usage: node fault-server.js <wwwroot> <port>
//
// Faults rotate between a connection reset mid-body, a stall (headers and part of the body, then silence)
// and a 503. Retries succeed. Entry points loaded by index.html itself (index.html, require.js,
// uno-bootstrap.js, stylesheets) are not faulted: nothing can retry them.
// /late.html is index.html with uno-bootstrap.js imported after the load event.
// With the stale=1 cookie, /uno-config.js (without a query) names another package, as a copy cached from an earlier
// deployment would.
const http = require("http");
const fs = require("fs");
const path = require("path");

const root = path.resolve(process.argv[2]);
const port = parseInt(process.argv[3] || "8001");

const types = {
	".html": "text/html", ".js": "text/javascript", ".mjs": "text/javascript", ".css": "text/css",
	".json": "application/json", ".wasm": "application/wasm", ".png": "image/png", ".dat": "application/octet-stream",
};

const seen = new Set();
const faults = ["reset", "stall", "503"];
let faultIndex = 0;
const log = { requests: 0, faults: {} };

function isFaultable(urlPath) {
	const name = path.basename(urlPath);
	return !["index.html", "late.html", "require.js", "uno-bootstrap.js", "service-worker.js"].includes(name)
		&& !name.endsWith(".css")
		&& (urlPath.includes("/_framework/") || urlPath.includes("/package_") || name === "uno-config.js");
}

function lateHtml() {
	return fs.readFileSync(path.join(root, "index.html"), "utf8").replace(
		/<script type="module" src="([^"]+)"><\/script>/,
		(_, src) => `<script>addEventListener("load", () => { const s = document.createElement("script"); s.type = "module"; s.src = "${src}"; document.head.appendChild(s); });</script>`);
}

http.createServer((req, res) => {
	log.requests++;
	const urlPath = decodeURIComponent(new URL(req.url, "http://localhost").pathname);

	if (urlPath === "/__log") {
		res.setHeader("content-type", "application/json");
		return res.end(JSON.stringify(log));
	}

	let body;
	let fileName = urlPath;
	if (urlPath === "/late.html") {
		body = Buffer.from(lateHtml());
	} else {
		fileName = urlPath === "/" ? "/index.html" : urlPath;
		const file = path.join(root, fileName);
		const relative = path.relative(root, file);
		if (relative.startsWith("..") || path.isAbsolute(relative) || !fs.existsSync(file) || fs.statSync(file).isDirectory()) {
			res.statusCode = 404;
			return res.end();
		}
		body = fs.readFileSync(file);

		if (urlPath === "/uno-config.js" && !new URL(req.url, "http://localhost").search && /(^|;\s*)stale=1/.test(req.headers.cookie || "")) {
			body = Buffer.from(body.toString("utf8").replace(/package_[0-9a-f]+/g, "package_0000000000000000000000000000000000000000"));
		}
	}

	let fault = null;
	if (isFaultable(urlPath) && !seen.has(urlPath)) {
		seen.add(urlPath);
		fault = faults[faultIndex++ % faults.length];
		log.faults[urlPath] = fault;
	}

	if (fault === "503") {
		res.statusCode = 503;
		return res.end("unavailable");
	}

	res.writeHead(200, {
		"content-type": types[path.extname(fileName)] || "application/octet-stream",
		"content-length": body.length,
		"cache-control": "no-store",
	});

	if (fault === null) {
		return res.end(body);
	}

	// Part of the body, then a reset or silence
	res.write(body.subarray(0, Math.floor(body.length / 2)));
	if (fault === "reset") {
		setTimeout(() => req.socket.destroy(), 50);
	}
}).listen(port, () => console.log(`fault server on http://localhost:${port}/ serving ${root}`));
