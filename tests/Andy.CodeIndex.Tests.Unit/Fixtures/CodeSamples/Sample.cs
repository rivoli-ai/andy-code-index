using System;
using System.Threading.Tasks;

namespace Acme.Billing;

/// <summary>A monetary amount in a currency.</summary>
public record Money(decimal Amount, string Currency);

public interface IInvoiceService
{
    Task<Money> GetTotalAsync(Guid invoiceId);
    int Count { get; }
}

/// <summary>Issues and totals invoices.</summary>
public class InvoiceService : ServiceBase, IInvoiceService
{
    public required string Region { get; set; }
    private int _issued;

    public InvoiceService(string region) => Region = region;

    /// <summary>Returns the invoice total.</summary>
    public async Task<Money> GetTotalAsync(Guid invoiceId)
    {
        await Task.Delay(1);
        return new Money(0m, "USD");
    }

    public int Count => _issued;

    public static InvoiceService CreateDefault() => new("US");

    private void Touch() => _issued++;

    public class LineItem
    {
        public string Sku { get; set; } = "";
        public decimal Price { get; set; }
    }
}

public enum InvoiceStatus
{
    Draft,
    Issued,
    Paid
}
