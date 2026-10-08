// Serves a published app with a switchable network: POST /__mode/normal | offline | flaky
// usage: node server.js <wwwroot> <port>
//
// offline: every request is answered by closing the connection.
// flaky: the first request of each file after switching fails with a reset mid-body; retries succeed.
const http = require("http");
const fs = require("fs");
const path = require("path");

const root = path.resolve(process.argv[2]);
const port = parseInt(process.argv[3] || "8002");

const types = {
	".html": "text/html", ".js": "text/javascript", ".css": "text/css", ".json": "application/json",
	".wasm": "application/wasm", ".png": "image/png", ".webmanifest": "application/manifest+json",
};

let mode = "normal";
let seen = new Set();

http.createServer((req, res) => {
	const urlPath = decodeURIComponent(new URL(req.url, "http://localhost").pathname);

	if (urlPath.startsWith("/__mode/")) {
		mode = urlPath.substring("/__mode/".length);
		seen = new Set();
		return res.end(mode);
	}

	if (mode === "offline") {
		return req.socket.destroy();
	}

	// Not an app file: answers slower than the service worker's network timeout
	if (urlPath === "/__api/slow") {
		res.writeHead(200, { "content-type": "application/json" });
		res.write('{"part":1,');
		return setTimeout(() => res.end('"query":' + JSON.stringify(new URL(req.url, "http://localhost").search) + "}"), 5000);
	}

	const fileName = urlPath.endsWith("/") ? urlPath + "index.html" : urlPath;
	const file = path.join(root, fileName);
	if (!file.startsWith(root) || !fs.existsSync(file) || fs.statSync(file).isDirectory()) {
		res.statusCode = 404;
		return res.end();
	}

	const body = fs.readFileSync(file);
	res.writeHead(200, {
		"content-type": types[path.extname(fileName)] || "application/octet-stream",
		"content-length": body.length,
		"cache-control": "no-cache",
	});

	if (mode === "flaky" && !seen.has(urlPath)) {
		seen.add(urlPath);
		res.write(body.subarray(0, Math.floor(body.length / 2)));
		return setTimeout(() => req.socket.destroy(), 50);
	}

	res.end(body);
}).listen(port, () => console.log(`server on http://localhost:${port}/ serving ${root}`));
