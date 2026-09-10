namespace PharmacyOrder.Api.Models;

public enum OrderStatus
{
    Pending,
    ReadyForCollection,
    Collected,
    Rejected
}