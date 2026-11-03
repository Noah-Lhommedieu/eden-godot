using System.Runtime.CompilerServices;

namespace EdenClient.NetTests;

/// <summary>Les tests réseau/TLS bloquent des threads pendant que xUnit en exécute des centaines en parallèle :
/// sans seuil minimal, le pool démarre ses threads au compte-gouttes et les handshakes dépassent 10 s (flaky).</summary>
internal static class TestThreadPool
{
    [ModuleInitializer]
    internal static void Init() => ThreadPool.SetMinThreads(64, 64);
}
