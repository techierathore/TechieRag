using System.Net;
using System.Text.Json;
using TechieDesk.Services.AppManager;
using TechieDesk.Services.AppManager.Models;
using TechieDesk.Tests.Support;
using Xunit;

namespace TechieDesk.Tests.SupportIssues;

/// <summary>
/// REQ-UI-032 / REQ-UI-033 / REQ-FN-027: the IssueSvc half of the AppManager wire contract v1.4 —
/// which URL each call hits, what it puts in the body, and how the documented error codes surface.
/// </summary>
/// <remarks>
/// Every assertion is made against the recorded HTTP call rather than against the client's return
/// value alone, because the failure this guards is a call that succeeds locally while hitting the
/// wrong path or omitting the ApplicationId the endpoint requires.
/// </remarks>
public sealed class SupportWireContractTests
{
    private const string IssueListJson = """
    {"success":true,"data":[
      {"issueId":7,"issueNumber":"ISS-2026-0007","title":"Cannot export thread to JSON",
       "issueType":"Bug","priority":"High","status":"Open","applicationId":7,
       "createdDate":"2026-07-20T09:00:00Z","updatedDate":"2026-07-26T09:00:00Z"}
    ],"message":"Retrieved 1 issue"}
    """;

    private const string IssueDetailJson = """
    {"success":true,"data":{
      "issueId":5,"issueNumber":"ISS-2026-0005","title":"Add Marathi locale",
      "description":"Marathi is missing from the language picker.",
      "issueType":"Feature","priority":"Low","status":"InProgress","applicationId":7,
      "createdDate":"2026-07-12T09:14:00Z","updatedDate":"2026-07-15T11:40:00Z",
      "comments":[
        {"commentId":1,"comment":"Thanks — adding to the Phase 4 batch.","isInternal":false,
         "createdByName":"Support Team","createdDate":"2026-07-15T11:02:00Z"}
      ]}}
    """;

    private static StubHttpMessageHandler Responder(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        return new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(status, json));
    }

    /// <summary>The list call hits /IssueSvc and carries the v1.4 a-prefixed ApplicationId.</summary>
    [Fact]
    public async Task ListIssuesUsesAPrefixedApplicationId()
    {
        var handler = Responder(IssueListJson);
        var client = TestFactory.Client(handler);

        var issues = await client.ListIssuesAsync("token-1");

        var call = handler.Calls.Single();
        Assert.Equal(HttpMethod.Get, call.Method);
        Assert.Equal("/IssueSvc?aApplicationId=7", call.PathAndQuery);
        Assert.Equal("Bearer token-1", call.Headers["Authorization"]);
        Assert.Equal("ISS-2026-0007", issues.Single().IssueNumber);
    }

    /// <summary>A status filter travels as the v1.4 aStatus parameter, not as "status".</summary>
    [Fact]
    public async Task ListIssuesSendsStatusFilterAsAPrefixedParam()
    {
        var handler = Responder(IssueListJson);
        var client = TestFactory.Client(handler);

        await client.ListIssuesAsync("token-1", "InProgress");

        Assert.Equal("/IssueSvc?aApplicationId=7&aStatus=InProgress", handler.Calls.Single().PathAndQuery);
    }

    /// <summary>
    /// A user with no issues is an empty list, not an EMPTY_RESPONSE failure. The screen shows a
    /// different thing for "you have none" than for "support could not be reached", so the client
    /// must not collapse the two.
    /// </summary>
    [Fact]
    public async Task ListIssuesReturnsEmptyRatherThanThrowingWhenThereAreNone()
    {
        var handler = Responder("""{"success":true,"data":[],"message":"Retrieved 0 issues"}""");
        var client = TestFactory.Client(handler);

        Assert.Empty(await client.ListIssuesAsync("token-1"));
    }

    /// <summary>The detail call reads the comment thread from /IssueSvc/{aIssueId}.</summary>
    [Fact]
    public async Task GetIssueReadsCommentThread()
    {
        var handler = Responder(IssueDetailJson);
        var client = TestFactory.Client(handler);

        var issue = await client.GetIssueAsync("token-1", 5);

        Assert.Equal("/IssueSvc/5", handler.Calls.Single().PathAndQuery);
        var comment = Assert.Single(issue.Comments);
        Assert.Equal("Support Team", comment.CreatedByName);
        Assert.False(comment.IsInternal);
    }

    /// <summary>
    /// Creation posts the documented body — including the create-only "type" field name, which the
    /// read payload calls "issueType".
    /// </summary>
    [Fact]
    public async Task CreateIssuePostsDocumentedBody()
    {
        var handler = Responder("""
        {"success":true,"data":{"issueId":9,"issueNumber":"ISS-2026-0009","status":"Open"}}
        """);
        var client = TestFactory.Client(handler);

        var created = await client.CreateIssueAsync("token-1", new CreateIssueRequest
        {
            ApplicationId = 7,
            Title = "Cannot export thread to JSON",
            Description = "Export spins forever.",
            Type = "Bug",
            Priority = "High"
        });

        var call = handler.Calls.Single();
        Assert.Equal(HttpMethod.Post, call.Method);
        Assert.Equal("/IssueSvc", call.PathAndQuery);

        using var body = JsonDocument.Parse(call.Body!);
        var root = body.RootElement;
        Assert.Equal(7, root.GetProperty("applicationId").GetInt32());
        Assert.Equal("Bug", root.GetProperty("type").GetString());
        Assert.Equal("High", root.GetProperty("priority").GetString());
        Assert.Equal("ISS-2026-0009", created.IssueNumber);
    }

    /// <summary>A comment posts {"comment": …} to the issue's comments collection.</summary>
    [Fact]
    public async Task AddCommentPostsToCommentsCollection()
    {
        var handler = Responder("""{"success":true,"data":{"commentId":3}}""");
        var client = TestFactory.Client(handler);

        await client.AddIssueCommentAsync("token-1", 5, "Screenshot attached.");

        var call = handler.Calls.Single();
        Assert.Equal("/IssueSvc/5/comments", call.PathAndQuery);
        using var body = JsonDocument.Parse(call.Body!);
        Assert.Equal("Screenshot attached.", body.RootElement.GetProperty("comment").GetString());
    }

    /// <summary>Closing an issue posts to /close with no body, exactly as documented.</summary>
    [Fact]
    public async Task CloseIssuePostsNoBody()
    {
        var handler = Responder("""{"success":true,"data":{"status":"Closed"}}""");
        var client = TestFactory.Client(handler);

        await client.CloseIssueAsync("token-1", 5);

        var call = handler.Calls.Single();
        Assert.Equal(HttpMethod.Post, call.Method);
        Assert.Equal("/IssueSvc/5/close", call.PathAndQuery);
        Assert.True(string.IsNullOrEmpty(call.Body));
    }

    /// <summary>
    /// REQ-FN-027: ALREADY_CLOSED arrives as a typed error the screen can answer specifically,
    /// rather than as an unknown failure.
    /// </summary>
    [Fact]
    public async Task CloseIssueSurfacesAlreadyClosedAsTypedError()
    {
        var handler = Responder(
            """{"success":false,"error":"ALREADY_CLOSED","message":"Issue is already closed"}""",
            HttpStatusCode.BadRequest);
        var client = TestFactory.Client(handler);

        var exception = await Assert.ThrowsAsync<AppManagerException>(
            () => client.CloseIssueAsync("token-1", 5));

        Assert.Equal(AppManagerError.AlreadyClosed, exception.Error);
    }

    /// <summary>ISSUE_NOT_FOUND from the detail endpoint maps to its typed member too.</summary>
    [Fact]
    public async Task GetIssueSurfacesIssueNotFoundAsTypedError()
    {
        var handler = Responder(
            """{"success":false,"error":"ISSUE_NOT_FOUND","message":"No issue with that ID"}""",
            HttpStatusCode.NotFound);
        var client = TestFactory.Client(handler);

        var exception = await Assert.ThrowsAsync<AppManagerException>(
            () => client.GetIssueAsync("token-1", 404));

        Assert.Equal(AppManagerError.IssueNotFound, exception.Error);
    }

    /// <summary>
    /// BRD-129: with no AppManager configured no issue call ever reaches the network. The screen
    /// depends on this to tell "no support backend" apart from "no issues".
    /// </summary>
    [Fact]
    public async Task OfflineInstallMakesNoIssueCallAtAll()
    {
        var handler = Responder(IssueListJson);
        var client = TestFactory.Client(handler, new AppManagerOptions { BaseUrl = string.Empty });

        var exception = await Assert.ThrowsAsync<AppManagerException>(
            () => client.ListIssuesAsync("token-1"));

        Assert.Equal(AppManagerError.NotConfigured, exception.Error);
        Assert.Empty(handler.Calls);
    }
}
