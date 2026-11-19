import ctypes, json, time

td = ctypes.CDLL(r"c:\work\TGDrive\native\tdjson.dll")
td.td_create_client_id.restype = ctypes.c_int
td.td_send.argtypes = [ctypes.c_int, ctypes.c_char_p]
td.td_send.restype = None
td.td_receive.argtypes = [ctypes.c_double]
td.td_receive.restype = ctypes.c_void_p

client = td.td_create_client_id()
td.td_send(client, json.dumps({"@type": "getOption", "name": "version"}).encode("utf-8"))

deadline = time.time() + 10
while time.time() < deadline:
    ptr = td.td_receive(1.0)
    if not ptr:
        continue
    msg = json.loads(ctypes.string_at(ptr).decode("utf-8"))
    t = msg.get("@type")
    if t == "updateAuthorizationState":
        print("auth state:", msg["authorization_state"]["@type"])
    elif t == "optionValueString" and msg.get("name") == "version":
        print("TDLib version:", msg["value"])
        break
    elif t == "error":
        print("error:", msg)
        break