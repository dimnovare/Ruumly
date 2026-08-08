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
    // ── DO NOT DELETE: Packing and Insurance ─────────────────────────────────
    // As of the 2026-08 founder decision neither is sold as a consumer-facing
    // service any more (packing is an add-on of a moving request; insurance is
    // dropped — see Constants/ServiceCategories.RetainedNotSoldSlugs for the
    // market evidence). That is a PRESENTATION change, not a data deletion:
    // production DemandLead rows are already stored with these categories, and
    // this column persists the enum NAME — removing a member makes those rows
    // unreadable and 500s the admin lead views. They also remain valid supplier
    // metadata in Supplier.ServiceTypesJson. Leave them here.
    Packing,
    VanRental,
    Insurance,
}
