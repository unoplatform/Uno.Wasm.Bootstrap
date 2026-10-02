// Serves a published app, slowly enough for the loader to be observed: GET /__mode/normal | failwasm
// usage: node server.js <wwwroot> <port>
//
// Responses are delayed by 150 ms, and dotnet.native.*.wasm by 2 s. In failwasm mode, dotnet.native.*.wasm is a 404.
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

		const body = fs.readFileSync(file);
		res.writeHead(200, {
			"content-type": types[path.extname(fileName)] || "application/octet-stream",
			"content-length": body.length,
			"cache-control": "no-store",
		});
		res.end(body);
	}, isNativeWasm ? 2000 : 150);
}).listen(port, () => console.log(`server on http://localhost:${port}/ serving ${root}`));
