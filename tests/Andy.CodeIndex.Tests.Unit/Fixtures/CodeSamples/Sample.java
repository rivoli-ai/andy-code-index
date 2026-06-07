package com.acme.billing;

public interface InvoiceService {
    Money getTotal(String invoiceId);
}

public class HttpInvoiceService extends ServiceBase implements InvoiceService {
    private final String region;

    public HttpInvoiceService(String region) {
        this.region = region;
    }

    public Money getTotal(String invoiceId) {
        return new Money(0, "USD");
    }
}

public enum InvoiceStatus {
    DRAFT,
    ISSUED,
    PAID
}
