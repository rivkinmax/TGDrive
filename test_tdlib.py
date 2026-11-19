import ctypes, json

td = ctypes.CDLL(r"C:\tdlib\td\tdlib\bin\tdjson.dll")
td.td_execute.argtypes = [ctypes.c_char_p]
td.td_execute.restype = ctypes.c_void_p

req = {"@type": "getTextEntities",
       "text": "@telegram /test_command https://telegram.org"}

ptr = td.td_execute(json.dumps(req).encode("utf-8"))
print(ctypes.string_at(ptr).decode("utf-8"))