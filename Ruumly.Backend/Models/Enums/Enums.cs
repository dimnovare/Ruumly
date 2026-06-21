namespace Ruumly.Backend.Models.Enums;

public enum UserRole { Guest, Customer, Provider, Admin }
public enum UserStatus { Active, Blocked, Deleted }
public enum IntegrationType { Api, Email, Manual }
public enum IntegrationHealth { Healthy, Degraded, Offline }
public enum ApprovalMode { Auto, Admin, Provider }
public enum PostingMode { Api, Email, Manual }
public enum BookingStatus { Pending, Reserved, Confirmed, Active, Completed, Cancelled }
// Failed is appended last to keep existing persisted int values stable. It is a
// terminal state set when dispatch to the partner permanently fails after retries.
public enum OrderStatus { Created, Sending, Sent, Confirmed, Rejected, Active, Completed, Cancelled, Failed }
public enum FulfillmentStatus { AwaitingApproval, Approved, Rejected, Posting, Posted, Confirmed, Failed, Completed }
public enum InvoiceStatus { Pending, AwaitingPayment, Paid, Overdue, PendingRefund, Refunded }
public enum ListingType { Warehouse, Moving, Trailer }
public enum MessageSender { Customer, Provider, Admin }
public enum NotificationType { Booking, Alert, System, Order, Review, Payment }
public enum ListingBadge { Cheapest, Closest, BestValue, Promoted }
public enum NotificationChannel { InApp, Email }
public enum SupplierTier { Starter = 0, Standard = 1, Premium = 2 }
public enum PayoutStatus { Pending, Paid, Disputed, Cancelled }
public enum BillingModel { Marketplace = 0, Rebate = 1 }
public enum RebateInvoiceStatus { Draft, Sent, Paid, Overdue }
public enum LeadStatus { New, Contacted, Won, Lost }
public enum PriorityLevel { Standard = 0, High = 1, Critical = 2 }
