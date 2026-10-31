using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace StardewCompanion.Core;

public sealed class ModelSettings
{
    public string Endpoint { get; set; } = "https://api.deepseek.com/chat/completions";
    public string Model { get; set; } = "deepseek-chat";
    public string ApiKeyDpapi { get; set; } = "";
    public string GamePath { get; set; } = @"F:\SteamLibrary\steamapps\common\Stardew Valley";
    public static ModelSettings Load(string path) => File.Exists(path) ? JsonSerializer.Deserialize<ModelSettings>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })! : new();
    public void Save(string path) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
    public void SetKey(string key)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows DPAPI required");
        ApiKeyDpapi = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser));
    }
    public string GetKey()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows DPAPI required");
        if (string.IsNullOrEmpty(ApiKeyDpapi)) throw new InvalidOperationException("尚未设置模型密钥");
        // Also read the PowerShell SecureString DPAPI format used by the initial developer setup.
        var bytes = ApiKeyDpapi.StartsWith("01000000", StringComparison.Ordinal) ? Convert.FromHexString(ApiKeyDpapi) : Convert.FromBase64String(ApiKeyDpapi);
        var clear = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
        try { return ApiKeyDpapi.StartsWith("01000000", StringComparison.Ordinal) ? Encoding.Unicode.GetString(clear) : Encoding.UTF8.GetString(clear); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
    public Uri ValidateEndpoint()
    {
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback))) throw new InvalidOperationException("模型地址必须是 HTTPS；本地假服务可使用回环 HTTP");
        if (string.IsNullOrWhiteSpace(Model)) throw new InvalidOperationException("模型名称不能为空");
        return uri;
    }
}

public sealed record MemoryEntry(long Id, string Kind, string Text, string State, string Source, int Day)
{ public string Display=>Kind=="project"?TryTitle(Text):Text; private static string TryTitle(string text){try{return JsonDocument.Parse(text).RootElement.GetProperty("Goal").GetString()??text;}catch{return text;}} }
public sealed record ProjectNote(string Id,string Goal,string[] VerifiedResults,string[] Remaining,string NextStep,string Status,string LastCheckedSession)
{
    public string[] UnverifiedResults { get; init; } = Array.Empty<string>();
}
public sealed record ProjectTaskReceipt(string TaskId,string Skill,string Status,int Completed,string Reason,int Day,string SessionId);
public sealed record ChestBinding(string Name, string Id, string Location, int X, int Y, string[] AllowedIds, string[] ProtectedIds);
