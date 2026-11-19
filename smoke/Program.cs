using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

internal static class Program
{
    [DllImport("tdjson", CallingConvention = CallingConvention.Cdecl)] private static extern int td_create_client_id();
    [DllImport("tdjson", CallingConvention = CallingConvention.Cdecl)] private static extern void td_send(int clientId, IntPtr request);
    [DllImport("tdjson", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr td_receive(double timeout);
    [DllImport("tdjson", CallingConvention = CallingConvention.Cdecl)] private static extern void td_set_log_verbosity_level(int level);

    private static void Send(int clientId, string json)
    {
        IntPtr p = Marshal.StringToCoTaskMemUTF8(json);
        try { td_send(clientId, p); } finally { Marshal.FreeCoTaskMem(p); }
    }

    private static void Main()
    {
        td_set_log_verbosity_level(0);
        int client = td_create_client_id();
        Send(client, "{\"@type\":\"getOption\",\"name\":\"version\"}");
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            IntPtr ptr = td_receive(1.0);
            if (ptr == IntPtr.Zero) continue;
            string json = Marshal.PtrToStringUTF8(ptr)!;
            JsonNode n = JsonNode.Parse(json)!;
            string type = n["@type"]!.GetValue<string>();
            if (type == "updateAuthorizationState")
                Console.WriteLine("auth state: " + n["authorization_state"]!["@type"]!.GetValue<string>());
            else if (type == "optionValueString")
            { Console.WriteLine("TDLib version (C#): " + n["value"]!.GetValue<string>()); return; }
            else if (type == "error")
            { Console.WriteLine("error: " + json); return; }
        }
        Console.WriteLine("timeout: version not received");
    }
}