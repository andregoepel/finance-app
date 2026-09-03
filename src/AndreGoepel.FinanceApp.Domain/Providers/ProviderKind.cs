namespace AndreGoepel.FinanceApp.Domain.Providers;

public enum ProviderKind
{
    Wise,
    Revolut,
    Dkb,
    EasyBank,

    /// <summary>Not a provider at all: the household's own cash, maintained by hand. Appended last: Marten stores enums as integers.</summary>
    Cash,
}
