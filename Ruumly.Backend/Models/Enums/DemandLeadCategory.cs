namespace Ruumly.Backend.Models.Enums;

// New categories are APPENDED after Any to keep existing persisted int values
// stable (the column stores enum names, but appended-only is still the rule).
public enum DemandLeadCategory
{
    Warehouse,
    Moving,
    Trailer,
    Any,
    Cleaning,
    Packing,
    VanRental,
    Insurance,
}
