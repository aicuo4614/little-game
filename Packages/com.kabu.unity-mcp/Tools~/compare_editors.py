"""Read-only capability comparison for two explicitly supplied MCP endpoints/editors.

No project paths, ports, battle types or credentials are embedded in this utility.
Only extension contracts are compared; this does not prove equal runtime playback.
"""
import argparse
import json
import urllib.error
import urllib.request


class McpClient:
    def __init__(self, endpoint):
        self.endpoint = endpoint
        self.headers = {"Content-Type": "application/json", "Accept": "application/json, text/event-stream"}
        self.opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
        self.counter = 0
        self.rpc("initialize", {"protocolVersion": "2024-11-05", "capabilities": {},
                                "clientInfo": {"name": "kabu-capability-check", "version": "1.0"}})
        self.rpc("notifications/initialized", notification=True)

    def rpc(self, method, params=None, notification=False):
        self.counter += 1
        body = {"jsonrpc": "2.0", "method": method, "params": params or {}}
        if not notification:
            body["id"] = self.counter
        request = urllib.request.Request(self.endpoint, data=json.dumps(body).encode(), headers=self.headers)
        with self.opener.open(request, timeout=60) as response:
            if response.headers.get("Mcp-Session-Id"):
                self.headers["Mcp-Session-Id"] = response.headers["Mcp-Session-Id"]
            raw = response.read().decode("utf-8")
        if notification:
            return None
        if raw.lstrip().startswith("{"):
            messages = [json.loads(raw)]
        else:
            messages = []
            data = []
            for line in (raw.replace("\r\n", "\n") + "\n\n").splitlines():
                if line.startswith("data:"):
                    data.append(line[5:].lstrip())
                elif not line and data:
                    messages.append(json.loads("\n".join(data)))
                    data = []
        for message in messages:
            if message.get("id") == self.counter:
                if "error" in message:
                    raise RuntimeError(message["error"])
                return message["result"]
        raise RuntimeError("No matching JSON-RPC response")

    def call(self, name, arguments=None):
        result = self.rpc("tools/call", {"name": name, "arguments": arguments or {}})
        text = "\n".join(item["text"] for item in result.get("content", []) if item.get("type") == "text")
        if result.get("isError"):
            raise RuntimeError(text)
        try:
            return json.loads(text)
        except json.JSONDecodeError:
            return text

    def close(self):
        if "Mcp-Session-Id" in self.headers:
            try:
                with self.opener.open(urllib.request.Request(self.endpoint, headers=self.headers, method="DELETE"), timeout=5):
                    pass
            except urllib.error.HTTPError as error:
                if error.code not in (404, 405):
                    raise


def compare(left, right):
    """Report exact contract differences; do not guess compatibility from version strings."""
    differences = []
    for field in ("packageVersion", "unityVersion", "hosts"):
        if left.get(field) != right.get(field):
            differences.append({"field": field, "left": left.get(field), "right": right.get(field)})
    a = {item["id"]: item for item in left["extensions"]}
    b = {item["id"]: item for item in right["extensions"]}
    for extension_id in sorted(a.keys() | b.keys()):
        for field in ("version", "available", "operations"):
            value_a = a.get(extension_id, {}).get(field)
            value_b = b.get(extension_id, {}).get(field)
            if value_a != value_b:
                differences.append({"extensionId": extension_id, "field": field, "left": value_a, "right": value_b})
    return {"schema": "kabu.extensions.compare/v1", "contractsEqual": not differences,
            "differences": differences, "left": left, "right": right,
            "note": "Contract equality only; asset resolution and runtime behavior require project adapters/tests."}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("left", help="First MCP HTTP endpoint")
    parser.add_argument("right", help="Second MCP HTTP endpoint")
    parser.add_argument("--left-editor", help="Explicit connectionId when the first endpoint has multiple editors")
    parser.add_argument("--right-editor", help="Explicit connectionId when the second endpoint has multiple editors")
    args = parser.parse_args()
    clients = []
    try:
        catalogs = []
        for endpoint, editor in ((args.left, args.left_editor), (args.right, args.right_editor)):
            client = McpClient(endpoint)
            clients.append(client)
            if editor:
                selection = client.call("unity_select_editor", {"connectionId": editor})
                if not isinstance(selection, str) or not selection.startswith("已选择编辑器：" + editor.strip() + "（"):
                    raise RuntimeError("Editor selection failed: " + str(selection))
            catalogs.append(client.call("unity_extension_catalog"))
        print(json.dumps(compare(*catalogs), ensure_ascii=False, indent=2))
    finally:
        for client in clients:
            client.close()


if __name__ == "__main__":
    main()
