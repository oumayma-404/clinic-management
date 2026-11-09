namespace ClinicManagement.API.Middleware;

/// <summary>What every write gate agrees on: the API's path prefix, and which methods are reads.</summary>
public static class WriteGate
{
    public const string ApiPrefix = "/api";

    public static bool IsRead(string method) =>
        HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);
}
