using System.Net;
using System.Text;
using AndreGoepel.FinanceApp.Connectors.Providers.Wise;
using AndreGoepel.FinanceApp.Domain.Providers;
using NSubstitute;

namespace AndreGoepel.FinanceApp.Connectors.Tests.Providers;

/// <summary>Activity-feed parsing (the token-only Wise transaction source).</summary>
public class WiseActivitiesTests
{
    // Real sandbox shape (an outgoing transfer) plus a credit with Wise's
    // <positive> markup and a non-monetary entry that must be skipped.
    private const string ActivitiesJson = """
        {
          "cursor": null,
          "activities": [
            {
              "id": "TU9ORVRBUllfQUNUSVZJVFk6OjI5MzA1NTUzOjpUUkFOU0ZFUjo6MjE0NzczMTA5OQ==",
              "type": "TRANSFER",
              "resource": { "type": "TRANSFER", "id": "2147731099" },
              "title": "<strong>André Example</strong>",
              "description": "Sending",
              "primaryAmount": "666 EUR",
              "secondaryAmount": "",
              "status": "COMPLETED",
              "createdOn": "2026-07-04T13:34:55.705Z",
              "updatedOn": "2026-07-04T13:36:09.452Z"
            },
            {
              "id": "credit-activity-id",
              "type": "BALANCE_DEPOSIT",
              "title": "<strong>Erika Beispiel</strong>",
              "description": "Received",
              "primaryAmount": "<positive>+ 1,500.25 EUR</positive>",
              "status": "COMPLETED",
              "createdOn": "2026-07-01T08:00:00.000Z"
            },
            {
              "id": "non-monetary",
              "type": "PROFILE_UPDATE",
              "title": "Profile updated",
              "primaryAmount": "",
              "status": "COMPLETED",
              "createdOn": "2026-07-02T08:00:00.000Z"
            }
          ]
        }
        """;

    [Fact]
    public void ParseActivitiesPage_MapsDebitCreditAndSkipsNonMonetary()
    {
        // Act
        var activities = new List<WiseActivity>();
        var cursor = WiseApiClient.ParseActivitiesPage(ActivitiesJson, activities);

        // Assert — the plain amount is money out, the marked-up one is a credit.
        Assert.Null(cursor);
        Assert.Collection(
            activities,
            debit =>
            {
                Assert.Equal(-666m, debit.Amount);
                Assert.Equal("EUR", debit.Currency);
                Assert.Equal("André Example", debit.Title);
                Assert.Equal("COMPLETED", debit.Status);
                Assert.Equal(new DateOnly(2026, 7, 4), debit.Date);
            },
            credit =>
            {
                Assert.Equal(1500.25m, credit.Amount);
                Assert.Equal("Erika Beispiel", credit.Title);
                Assert.Equal("credit-activity-id", credit.Id);
            }
        );
    }

    [Theory]
    [InlineData("666 EUR", -666, "EUR")]
    [InlineData("<positive>+ 3.53 GBP</positive>", 3.53, "GBP")]
    [InlineData("- 1,000.66 USD", -1000.66, "USD")]
    [InlineData("<positive>1,500 EUR</positive>", 1500, "EUR")]
    public void TryParseDisplayAmount_HandlesSignsMarkupAndSeparators(
        string display,
        decimal expectedAmount,
        string expectedCurrency
    )
    {
        // Act
        var parsed = WiseApiClient.TryParseDisplayAmount(display, out var amount, out var currency);

        // Assert
        Assert.True(parsed);
        Assert.Equal(expectedAmount, amount);
        Assert.Equal(expectedCurrency, currency);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-currency")]
    [InlineData("EUR")]
    public void TryParseDisplayAmount_Garbage_ReturnsFalse(string display)
    {
        // Act / Assert
        Assert.False(WiseApiClient.TryParseDisplayAmount(display, out _, out _));
    }

    [Fact]
    public async Task GetActivitiesAsync_FollowsCursorPagination()
    {
        // Arrange — first page hands a cursor, second page ends the walk.
        const string page1 = """
            {"cursor":"next-1","activities":[{"id":"a1","type":"TRANSFER","title":"x",
            "primaryAmount":"10 EUR","status":"COMPLETED","createdOn":"2026-07-04T13:34:55.705Z"}]}
            """;
        const string page2 = """
            {"cursor":null,"activities":[{"id":"a2","type":"TRANSFER","title":"y",
            "primaryAmount":"20 EUR","status":"COMPLETED","createdOn":"2026-07-03T13:34:55.705Z"}]}
            """;
        var handler = new SequenceHandler(page1, page2);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(new HttpClient(handler));
        var client = new WiseApiClient(factory);

        // Act
        var result = await client.GetActivitiesAsync(
            "token",
            ProviderEnvironment.Sandbox,
            29305553,
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 7, 13)
        );

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        Assert.Contains("nextCursor=next-1", handler.SecondUri!.ToString());
        Assert.Contains("since=2026-06-01", handler.FirstUri!.ToString());
    }

    private sealed class SequenceHandler(params string[] bodies) : HttpMessageHandler
    {
        private int _call;
        public Uri? FirstUri { get; private set; }
        public Uri? SecondUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (_call == 0)
            {
                FirstUri = request.RequestUri;
            }
            else if (_call == 1)
            {
                SecondUri = request.RequestUri;
            }
            var body = bodies[Math.Min(_call, bodies.Length - 1)];
            _call++;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                }
            );
        }
    }
}
