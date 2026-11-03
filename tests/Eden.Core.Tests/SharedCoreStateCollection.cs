using Xunit;

namespace EdenClient.NetTests;

/// <summary>Les classes marquées mutuent les statiques partagées de
/// Core (PlayerVitals, EntityBuffs, TextResolver/UiTextEn, caches de
/// tables ini) — la collection les sérialise : xUnit parallélise les
/// classes entre elles par défaut et leurs mutations se marchaient
/// dessus (IndexOutOfRange sur Snapshot, language/ResolveFile perdus).</summary>
[CollectionDefinition("SharedCoreState")]
public sealed class SharedCoreStateCollection;
