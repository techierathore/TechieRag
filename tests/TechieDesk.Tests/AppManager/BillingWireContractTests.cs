using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using TechieDesk.Services.AppManager;
using TechieDesk.Tests.Support;
using Xunit;

namespace TechieDesk.Tests.AppManager;

/// <summary>
/// REQ-UI-029 / REQ-UI-030 / REQ-UI-031 / REQ-FN-026: the LicenseSvc catalogue and PaymentSvc
/// billing endpoints — URLs, the v1.4 a-prefixed parameter names, request bodies, response
/// shapes, and the binary invoice-PDF path.
/// </summary>
public sealed class BillingWireContractTests
{
    private static StubHttpMessageHandler Responder(Func<HttpRequestMessage, string?, HttpResponseMessage> responder)
        => new(responder);

    private static HttpResponseMessage Ok(string json)
        => StubHttpMessageHandler.Json(HttpStatusCode.OK, json);

    private static string Envelope(object data)
        => JsonSerializer.Serialize(new { success = true, data, message = "ok" });

    /// <summary>
    /// The pricing catalogue is fetched anonymously with the v1.4 aApplicationId parameter and the
    /// requested currency filter, and the per-currency price list is parsed.
    /// </summary>
    [Fact]
    public async Task LicenseTypesSendCurrencyAndApplicationId()
    {
        var handler = Responder((request, body) => Ok(Envelope(new[]
        {
            new
            {
                licenseTypeId = 1,
                typeName = "Professional",
                typeCode = "PRO",
                licenseModel = "Subscription",
                maxDevices = 3,
                durationDays = 365,
                pricing = new[]
                {
                    new { currencyCode = "USD", amount = 99.99m, formattedPrice = "$99.99" },
                    new { currencyCode = "INR", amount = 7999.00m, formattedPrice = "Rs.7999.00" }
                }
            }
        })));
        var client = TestFactory.Client(handler);

        var types = await client.GetLicenseTypesAsync("USD");

        Assert.Equal("/LicenseSvc/types?aApplicationId=7&aCurrency=USD", handler.Calls.Single().PathAndQuery);
        var professional = Assert.Single(types);
        Assert.Equal("Professional", professional.TypeName);
        Assert.Equal(365, professional.DurationDays);
        Assert.Equal(2, professional.Pricing.Count);
        Assert.Equal("$99.99", professional.Pricing[0].FormattedPrice);
        Assert.Equal(99.99m, professional.Pricing[0].Amount);
    }

    /// <summary>
    /// An application that sells nothing yet answers with no data payload; that is an empty
    /// catalogue, not an EMPTY_RESPONSE protocol failure.
    /// </summary>
    [Fact]
    public async Task LicenseTypesTreatNoDataAsEmptyCatalogue()
    {
        var handler = Responder((request, body) => Ok("{\"success\":true,\"message\":\"none\"}"));
        var client = TestFactory.Client(handler);

        Assert.Empty(await client.GetLicenseTypesAsync());
    }

    /// <summary>The user's licences are read under bearer auth, scoped to the application.</summary>
    [Fact]
    public async Task LicensesUseBearerAndApplicationScope()
    {
        var handler = Responder((request, body) => Ok(Envelope(new[]
        {
            new
            {
                licenseId = 1,
                licenseKey = "LIC-ABC123-XYZ789",
                licenseName = "Professional",
                status = "Active",
                expiryDate = "2027-01-26T00:00:00Z",
                daysRemaining = 193,
                maxDevices = 3,
                activatedDevices = 2
            }
        })));
        var client = TestFactory.Client(handler);

        var licenses = await client.GetLicensesAsync("access-token-1");

        var call = handler.Calls.Single();
        Assert.Equal("/LicenseSvc?aApplicationId=7", call.PathAndQuery);
        Assert.Equal("Bearer access-token-1", call.Headers["Authorization"]);
        var license = Assert.Single(licenses);
        Assert.Equal("LIC-ABC123-XYZ789", license.LicenseKey);
        Assert.Equal(2, license.ActivatedDevices);
        Assert.Equal(3, license.MaxDevices);
    }

    /// <summary>Device deactivation is a DELETE against the licence's device sub-resource.</summary>
    [Fact]
    public async Task DeactivateDeviceUsesDeleteRoute()
    {
        var handler = Responder((request, body) => Ok("{\"success\":true,\"message\":\"released\"}"));
        var client = TestFactory.Client(handler);

        await client.DeactivateDeviceAsync("access-token-1", 11, 42);

        var call = handler.Calls.Single();
        Assert.Equal(HttpMethod.Delete, call.Method);
        Assert.Equal("/LicenseSvc/11/devices/42", call.PathAndQuery);
    }

    /// <summary>Subscriptions are listed under bearer auth and the payload is parsed in full.</summary>
    [Fact]
    public async Task SubscriptionsParseBillingFields()
    {
        var handler = Responder((request, body) => Ok(Envelope(new[]
        {
            new
            {
                subscriptionId = 5,
                planName = "Professional Monthly",
                status = "Active",
                billingCycle = "Monthly",
                amount = 9.99m,
                currencyCode = "USD",
                startDate = "2026-01-01T00:00:00Z",
                currentPeriodEnd = "2026-08-01T00:00:00Z",
                cancelAtPeriodEnd = false,
                nextBillingDate = "2026-08-01T00:00:00Z"
            }
        })));
        var client = TestFactory.Client(handler);

        var subscriptions = await client.GetSubscriptionsAsync("access-token-1");

        Assert.Equal("/PaymentSvc/subscriptions?aApplicationId=7", handler.Calls.Single().PathAndQuery);
        var subscription = Assert.Single(subscriptions);
        Assert.Equal(5, subscription.SubscriptionId);
        Assert.Equal("Professional Monthly", subscription.PlanName);
        Assert.Equal(9.99m, subscription.Amount);
        Assert.False(subscription.CancelAtPeriodEnd);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), subscription.NextBillingDate);
    }

    /// <summary>Having no subscription is an empty list, never an error the screen must explain.</summary>
    [Fact]
    public async Task SubscriptionsTreatNoDataAsEmptyList()
    {
        var handler = Responder((request, body) => Ok("{\"success\":true,\"message\":\"none\"}"));
        var client = TestFactory.Client(handler);

        Assert.Empty(await client.GetSubscriptionsAsync("access-token-1"));
    }

    /// <summary>
    /// Cancelling at period end posts cancelImmediately=false — the flag the server uses to decide
    /// whether access survives the rest of the paid period.
    /// </summary>
    [Fact]
    public async Task CancelAtPeriodEndPostsFalseFlag()
    {
        var handler = Responder((request, body) => Ok("{\"success\":true,\"message\":\"cancelled\"}"));
        var client = TestFactory.Client(handler);

        await client.CancelSubscriptionAsync("access-token-1", 5);

        var call = handler.Calls.Single();
        Assert.Equal(HttpMethod.Post, call.Method);
        Assert.Equal("/PaymentSvc/subscriptions/5/cancel", call.PathAndQuery);
        Assert.Contains("\"cancelImmediately\":false", call.Body);
    }

    /// <summary>Cancelling immediately posts the true flag and the supplied reason.</summary>
    [Fact]
    public async Task CancelImmediatelyPostsTrueFlagAndReason()
    {
        var handler = Responder((request, body) => Ok("{\"success\":true,\"message\":\"cancelled\"}"));
        var client = TestFactory.Client(handler);

        await client.CancelSubscriptionAsync("access-token-1", 5, cancelImmediately: true, reason: "No longer needed");

        var call = handler.Calls.Single();
        Assert.Contains("\"cancelImmediately\":true", call.Body);
        Assert.Contains("No longer needed", call.Body);
    }

    /// <summary>ALREADY_CANCELLED surfaces as a typed error so the screen can refresh rather than alarm.</summary>
    [Fact]
    public async Task CancelSurfacesAlreadyCancelled()
    {
        var handler = Responder((request, body) => StubHttpMessageHandler.Json(
            HttpStatusCode.BadRequest,
            TestFactory.ErrorResponse("ALREADY_CANCELLED", "Subscription is already cancelled", 400)));
        var client = TestFactory.Client(handler);

        var exception = await Assert.ThrowsAsync<AppManagerException>(
            () => client.CancelSubscriptionAsync("access-token-1", 5));

        Assert.Equal(AppManagerError.AlreadyCancelled, exception.Error);
    }

    /// <summary>
    /// Transactions are paged with the v1.4 a-prefixed aPage/aPageSize parameters and the paging
    /// metadata is carried back so the screen can offer the next page.
    /// </summary>
    [Fact]
    public async Task TransactionsSendPagingAndParseEnvelope()
    {
        var handler = Responder((request, body) => Ok(Envelope(new
        {
            items = new[]
            {
                new
                {
                    transactionId = 1,
                    transactionNumber = "TXN-2026-0142",
                    transactionType = "Purchase",
                    amount = 99.99m,
                    currencyCode = "USD",
                    status = "Completed",
                    transactionDate = "2026-01-26T10:00:00Z"
                }
            },
            totalCount = 3,
            page = 2,
            pageSize = 20,
            totalPages = 1
        })));
        var client = TestFactory.Client(handler);

        var result = await client.GetTransactionsAsync("access-token-1", page: 2, pageSize: 20);

        Assert.Equal(
            "/PaymentSvc/transactions?aApplicationId=7&aPage=2&aPageSize=20",
            handler.Calls.Single().PathAndQuery);
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(2, result.Page);
        var transaction = Assert.Single(result.Items);
        Assert.Equal("TXN-2026-0142", transaction.TransactionNumber);
        Assert.Equal(99.99m, transaction.Amount);
    }

    /// <summary>Invoices are paged the same way and parse their totals and status.</summary>
    [Fact]
    public async Task InvoicesSendPagingAndParseEnvelope()
    {
        var handler = Responder((request, body) => Ok(Envelope(new
        {
            items = new[]
            {
                new
                {
                    invoiceId = 9,
                    invoiceNumber = "INV-2026-0142",
                    invoiceDate = "2026-01-26T00:00:00Z",
                    subTotal = 84.74m,
                    taxAmount = 15.25m,
                    totalAmount = 99.99m,
                    currencyCode = "USD",
                    status = "Paid"
                }
            },
            totalCount = 1,
            page = 1,
            pageSize = 20,
            totalPages = 1
        })));
        var client = TestFactory.Client(handler);

        var result = await client.GetInvoicesAsync("access-token-1");

        Assert.Equal(
            "/PaymentSvc/invoices?aApplicationId=7&aPage=1&aPageSize=20",
            handler.Calls.Single().PathAndQuery);
        var invoice = Assert.Single(result.Items);
        Assert.Equal("INV-2026-0142", invoice.InvoiceNumber);
        Assert.Equal(99.99m, invoice.TotalAmount);
        Assert.Equal("Paid", invoice.Status);
    }

    /// <summary>
    /// The invoice PDF comes back as raw bytes with a Content-Disposition file name; TechieDesk
    /// passes both through unchanged rather than rendering a document of its own.
    /// </summary>
    [Fact]
    public async Task InvoiceDownloadReturnsPdfBytesAndFileName()
    {
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37 };
        var handler = Responder((request, body) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(pdfBytes)
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            response.Content.Headers.ContentDisposition =
                new ContentDispositionHeaderValue("attachment") { FileName = "INV-2026-0142.pdf" };
            return response;
        });
        var client = TestFactory.Client(handler);

        var document = await client.DownloadInvoiceAsync("access-token-1", 9);

        Assert.Equal("/PaymentSvc/invoices/9/download", handler.Calls.Single().PathAndQuery);
        Assert.Equal("INV-2026-0142.pdf", document.FileName);
        Assert.Equal("application/pdf", document.ContentType);
        Assert.Equal(pdfBytes, document.Content);
    }

    /// <summary>
    /// A server-supplied file name that tries to escape the download folder is reduced to its leaf
    /// before it can ever reach Path.Combine.
    /// </summary>
    [Fact]
    public async Task InvoiceDownloadStripsPathFromFileName()
    {
        var handler = Responder((request, body) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[] { 0x25, 0x50, 0x44, 0x46 })
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            response.Content.Headers.ContentDisposition =
                new ContentDispositionHeaderValue("attachment") { FileName = "\"../../etc/passwd.pdf\"" };
            return response;
        });
        var client = TestFactory.Client(handler);

        var document = await client.DownloadInvoiceAsync("access-token-1", 9);

        Assert.Equal("passwd.pdf", document.FileName);
    }

    /// <summary>
    /// PDF_GENERATION_FAILED arrives as the normal JSON error envelope on the binary endpoint and
    /// must surface as a typed error, not as a saved file full of JSON.
    /// </summary>
    [Fact]
    public async Task InvoiceDownloadSurfacesGenerationFailure()
    {
        var handler = Responder((request, body) => StubHttpMessageHandler.Json(
            HttpStatusCode.InternalServerError,
            TestFactory.ErrorResponse("PDF_GENERATION_FAILED", "Renderer unavailable", 500)));
        var client = TestFactory.Client(handler);

        var exception = await Assert.ThrowsAsync<AppManagerException>(
            () => client.DownloadInvoiceAsync("access-token-1", 9));

        Assert.Equal(AppManagerError.PdfGenerationFailed, exception.Error);
    }

    /// <summary>
    /// A 200 that is not a PDF (a proxy's HTML sign-in page, say) is rejected rather than written
    /// to disk with a .pdf extension.
    /// </summary>
    [Fact]
    public async Task InvoiceDownloadRejectsNonPdfSuccess()
    {
        var handler = Responder((request, body) => StubHttpMessageHandler.Json(
            HttpStatusCode.OK, TestFactory.ErrorResponse("INVOICE_NOT_FOUND", "No such invoice", 404)));
        var client = TestFactory.Client(handler);

        var exception = await Assert.ThrowsAsync<AppManagerException>(
            () => client.DownloadInvoiceAsync("access-token-1", 9));

        Assert.Equal(AppManagerError.InvoiceNotFound, exception.Error);
    }

    /// <summary>
    /// Promo-code validation posts the bare code to the anonymous endpoint and still carries the
    /// API key headers, which is how the server resolves the application scope.
    /// </summary>
    [Fact]
    public async Task PromoCodeValidationPostsCodeWithApiKey()
    {
        var handler = Responder((request, body) => Ok(Envelope(new
        {
            code = "SAVE20",
            discountType = "Percentage",
            discountValue = 20,
            description = "20% off your first purchase",
            expiryDate = "2026-12-31T23:59:59Z"
        })));
        var client = TestFactory.Client(handler);

        var promo = await client.ValidatePromoCodeAsync("SAVE20");

        var call = handler.Calls.Single();
        Assert.Equal("/PaymentSvc/promo-codes/validate", call.PathAndQuery);
        Assert.Equal("{\"code\":\"SAVE20\"}", call.Body);
        Assert.Equal("ak_test_key", call.Headers["X-Api-Key"]);
        Assert.False(call.Headers.ContainsKey("Authorization"));
        Assert.Equal("Percentage", promo.DiscountType);
        Assert.Equal(20m, promo.DiscountValue);
    }

    /// <summary>
    /// Each documented promo-code rejection maps to its own typed error, so the pricing screen can
    /// tell expiry from exhaustion from wrong-application instead of saying "invalid" five ways.
    /// </summary>
    [Theory]
    [InlineData("PROMO_CODE_NOT_FOUND", AppManagerError.PromoCodeNotFound)]
    [InlineData("PROMO_CODE_INACTIVE", AppManagerError.PromoCodeInactive)]
    [InlineData("PROMO_CODE_EXPIRED", AppManagerError.PromoCodeExpired)]
    [InlineData("PROMO_CODE_EXHAUSTED", AppManagerError.PromoCodeExhausted)]
    [InlineData("PROMO_CODE_NOT_VALID_FOR_APPLICATION", AppManagerError.PromoCodeNotValidForApplication)]
    public async Task PromoCodeRejectionsMapToDistinctErrors(string wireCode, AppManagerError expected)
    {
        var handler = Responder((request, body) => StubHttpMessageHandler.Json(
            HttpStatusCode.BadRequest, TestFactory.ErrorResponse(wireCode, "rejected", 400)));
        var client = TestFactory.Client(handler);

        var exception = await Assert.ThrowsAsync<AppManagerException>(
            () => client.ValidatePromoCodeAsync("SAVE20"));

        Assert.Equal(expected, exception.Error);
    }

    /// <summary>
    /// With no AppManager configured every billing call refuses locally rather than dialling an
    /// empty base address — the offline single-user state (BRD-129) must never look like a network
    /// failure.
    /// </summary>
    [Fact]
    public async Task OfflineInstanceRefusesBillingCallsLocally()
    {
        var handler = Responder((request, body) => Ok("{\"success\":true}"));
        var client = TestFactory.Client(
            handler, new TechieDesk.Services.AppManager.AppManagerOptions { BaseUrl = string.Empty });

        var subscriptions = await Assert.ThrowsAsync<AppManagerException>(
            () => client.GetSubscriptionsAsync("access-token-1"));
        var promo = await Assert.ThrowsAsync<AppManagerException>(
            () => client.ValidatePromoCodeAsync("SAVE20"));
        var invoice = await Assert.ThrowsAsync<AppManagerException>(
            () => client.DownloadInvoiceAsync("access-token-1", 9));

        Assert.Equal(AppManagerError.NotConfigured, subscriptions.Error);
        Assert.Equal(AppManagerError.NotConfigured, promo.Error);
        Assert.Equal(AppManagerError.NotConfigured, invoice.Error);
        Assert.Empty(handler.Calls);
    }
}
