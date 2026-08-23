namespace AndreGoepel.FinanceApp.Domain.Resources;

/// <summary>
/// Marker for the domain layer's own user-facing failure messages. Separate from the web project's
/// <c>Strings</c> because Domain cannot reference the web project — the dependency arrow points the
/// other way. Lives in the Resources namespace so its full name matches the embedded resource path,
/// which means no ResourcesPath setting is needed.
/// <para>
/// Only messages a user can act on belong here. Raw diagnostics — HTTP status codes, exception
/// text, parser internals — deliberately stay English: translating them buys the user nothing and
/// makes support harder.
/// </para>
/// </summary>
public sealed class DomainStrings;
