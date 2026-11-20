using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace STS2_MCP;

internal sealed class LocalApiAccess
{
    internal const string TokenFileName = "STS2_MCP.token";
    private const string BearerPrefix = "Bearer ";
    private const int TokenLength = 64;
    private readonly byte[] _token;

    private LocalApiAccess(string token) => _token = Encoding.ASCII.GetBytes(token);

    internal static LocalApiAccess LoadOrCreate(string userDataDirectory)
    {
        if (!Path.IsPathFullyQualified(userDataDirectory))
            throw new InvalidDataException("The game did not provide an absolute user data directory.");

        string tokenPath = Path.Combine(userDataDirectory, TokenFileName);
        if (!File.Exists(tokenPath))
        {
            Directory.CreateDirectory(userDataDirectory);
            CreateTokenFile(tokenPath);
        }

        string token = File.ReadAllText(tokenPath).Trim();
        if (token.Length != TokenLength || !token.All(Uri.IsHexDigit))
            throw new InvalidDataException($"Invalid token file at {tokenPath}.");

        return new LocalApiAccess(token);
    }

    private static void CreateTokenFile(string tokenPath)
    {
        string temporaryPath = tokenPath + "." + Guid.NewGuid().ToString("N");
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        try
        {
            using (var stream = new FileStream(temporaryPath, options))
                stream.Write(Encoding.ASCII.GetBytes(RandomNumberGenerator.GetHexString(TokenLength)));

            // Another game instance may have initialized the same token first.
            try { File.Move(temporaryPath, tokenPath); }
            catch (IOException) when (File.Exists(tokenPath)) { }
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    internal int? GetRejectionStatus(
        IPAddress? peer, bool isBrowserRequest, string method, string path, string? authorization)
    {
        if (peer?.IsIPv4MappedToIPv6 == true)
            peer = peer.MapToIPv4();
        if (peer == null || !IPAddress.IsLoopback(peer) || isBrowserRequest)
            return 403;

        if (method == "GET" && path == "/")
            return null;

        if (authorization == null || authorization.Length != BearerPrefix.Length + TokenLength
            || !authorization.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase)
            || !CryptographicOperations.FixedTimeEquals(
                _token, Encoding.UTF8.GetBytes(authorization[BearerPrefix.Length..])))
            return 401;

        return null;
    }
}
