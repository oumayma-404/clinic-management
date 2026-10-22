/**
 * The PC's road to its cloud during `relay-roundtrip`'s retire phase: a plain HTTP pass-through that answers 503 to the
 * return (`POST /api/relay/handback…`) while the BLOCK file exists — the line is back, the heartbeats get through, the
 * return keeps failing. That is the one state where the cloud KNOWS its PC holds the cabinet's saves (it locks), which a
 * full cut cannot produce: with manual takeover the cloud never locks on silence.
 *
 *   node proxy.mjs <listen port> <target origin> <block file>
 */
import fs from "node:fs"
import http from "node:http"

const [port, target, block] = process.argv.slice(2)
const to = new URL(target)

http.createServer((req, res) => {
  if (req.method === "POST" && req.url.startsWith("/api/relay/handback") && fs.existsSync(block)) {
    req.resume()
    res.writeHead(503, { "Content-Type": "application/json" })
    res.end(JSON.stringify({ error: "relay-roundtrip: return blocked", code: "rt_blocked" }))
    return
  }
  const up = http.request({ host: to.hostname, port: to.port, method: req.method, path: req.url, headers: req.headers },
    (answer) => { res.writeHead(answer.statusCode, answer.headers); answer.pipe(res) })
  up.on("error", () => { if (!res.headersSent) res.writeHead(502); res.end() })
  req.pipe(up)
}).listen(Number(port), "127.0.0.1")
