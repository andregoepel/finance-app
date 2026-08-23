namespace AndreGoepel.FinanceApp.Domain.Resources;

/// <summary>
/// Marker for backend user-facing failure messages. Separate from the web project's <c>Strings</c>
/// because Domain cannot reference the web project — the dependency arrow points the other way.
/// Lives in the Resources namespace so its full name matches the embedded resource path, which
/// means no ResourcesPath setting is needed.
/// <para>
/// Shared with the projects that sit between Domain and the web app (Categorization, Connectors):
/// they reference Domain and so can reach this resx, and giving each its own would mean a marker,
/// two resx files and a parity test apiece for a handful of strings. Anything that runs inside the
/// web project has <c>Strings</c> available and should use that instead.
/// </para>
/// <para>
/// Only messages a user can act on belong here. Raw diagnostics — HTTP status codes, exception
/// text, parser internals — deliberately stay English: translating them buys the user nothing and
/// makes support harder. So does anything persisted rather than rendered live, such as the parse
/// errors stored on an <c>ImportBatch</c>: localizing at write time freezes the culture that
/// happened to be active, and no later switch can undo it.
/// </para>
/// </summary>
public sealed class DomainStrings;
