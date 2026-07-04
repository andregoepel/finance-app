using AndreGoepel.FinanceApp.Domain.Accounts;

namespace AndreGoepel.FinanceApp.Domain.Tests.Accounts;

public class AccountOwnersTests
{
    private static readonly Guid UserA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid UserB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void Validate_NonSharedSingleOwner_Succeeds()
    {
        // Act
        var result = AccountOwners.Validate(isShared: false, [UserA]);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal([UserA], result.Value!);
    }

    [Fact]
    public void Validate_SharedMultipleOwners_Succeeds()
    {
        // Act
        var result = AccountOwners.Validate(isShared: true, [UserA, UserB]);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal([UserA, UserB], result.Value!);
    }

    [Fact]
    public void Validate_SharedSingleOwner_Succeeds()
    {
        // Act
        var result = AccountOwners.Validate(isShared: true, [UserA]);

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_NonSharedMultipleOwners_Fails()
    {
        // Act
        var result = AccountOwners.Validate(isShared: false, [UserA, UserB]);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("non-shared", result.Error);
    }

    [Fact]
    public void Validate_NoOwners_Fails()
    {
        // Act
        var result = AccountOwners.Validate(isShared: true, []);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("at least one owner", result.Error);
    }

    [Fact]
    public void Validate_OnlyEmptyGuids_Fails()
    {
        // Act
        var result = AccountOwners.Validate(isShared: false, [Guid.Empty]);

        // Assert
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Validate_DuplicatesAndEmptyGuids_AreDroppedBeforeCounting()
    {
        // A non-shared account with the same user listed twice (plus an empty id)
        // collapses to one distinct owner rather than tripping the >1 rule.

        // Act
        var result = AccountOwners.Validate(isShared: false, [UserA, UserA, Guid.Empty]);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal([UserA], result.Value!);
    }
}
