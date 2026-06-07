package billing

import "context"

// Invoice is a billing document.
type Invoice struct {
	ID     string
	Amount int64
}

// Repository reads and writes invoices.
type Repository interface {
	Get(ctx context.Context, id string) (*Invoice, error)
	Save(ctx context.Context, inv *Invoice) error
}

// NewInvoice constructs an Invoice.
func NewInvoice(id string, amount int64) *Invoice {
	return &Invoice{ID: id, Amount: amount}
}

// Total returns the amount.
func Total(inv *Invoice) int64 {
	return inv.Amount
}

func unexportedHelper() {}
