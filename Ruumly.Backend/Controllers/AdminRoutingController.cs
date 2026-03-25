using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Controllers;

[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin")]
public class AdminRoutingController(RuumlyDbContext db) : AdminBaseController(db)
{
    [HttpGet("routing-rules")]
    public async Task<IActionResult> GetRoutingRules()
    {
        var rules = await Db.OrderRoutingRules
            .OrderByDescending(r => r.Priority)
            .ToListAsync();
        return Ok(rules.Select(AdminMappers.MapRoutingRule));
    }

    [HttpPost("routing-rules")]
    public async Task<IActionResult> CreateRoutingRule([FromBody] CreateRoutingRuleRequest body)
    {
        ListingType? serviceType = null;
        if (!string.IsNullOrWhiteSpace(body.ServiceType) &&
            Enum.TryParse<ListingType>(body.ServiceType, ignoreCase: true, out var st))
            serviceType = st;

        if (!Enum.TryParse<PostingMode>(body.PostingChannel, ignoreCase: true, out var pc))
            return BadRequest(Error($"Invalid postingChannel '{body.PostingChannel}'"));

        var rule = new OrderRoutingRule
        {
            Id               = Guid.NewGuid(),
            Name             = body.Name,
            SupplierId       = body.SupplierId,
            ServiceType      = serviceType,
            OrderType        = body.OrderType,
            PriceThreshold   = body.PriceThreshold,
            CustomerType     = body.CustomerType,
            RequiresApproval = body.RequiresApproval,
            ApproverRole     = body.ApproverRole,
            PostingChannel   = pc,
            Priority         = body.Priority,
            IsActive         = body.IsActive,
            CreatedAt        = DateTime.UtcNow,
            UpdatedAt        = DateTime.UtcNow,
        };

        Db.OrderRoutingRules.Add(rule);
        await Audit("routing_rule.created", User.GetUserEmail(), body.Name, null);
        await Db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetRoutingRules), new { }, AdminMappers.MapRoutingRule(rule));
    }

    [HttpPatch("routing-rules/{id:guid}")]
    public async Task<IActionResult> PatchRoutingRule(Guid id, [FromBody] PatchRoutingRuleRequest body)
    {
        var rule = await Db.OrderRoutingRules.FindAsync(id);
        if (rule is null) return NotFound(Error("Routing rule not found"));

        if (body.Name is not null) rule.Name = body.Name;
        if (body.OrderType is not null) rule.OrderType = body.OrderType;
        if (body.CustomerType is not null) rule.CustomerType = body.CustomerType;
        if (body.PriceThreshold.HasValue) rule.PriceThreshold = body.PriceThreshold;
        if (body.RequiresApproval.HasValue) rule.RequiresApproval = body.RequiresApproval.Value;
        if (body.ApproverRole is not null) rule.ApproverRole = body.ApproverRole;
        if (body.Priority.HasValue) rule.Priority = body.Priority.Value;
        if (body.IsActive.HasValue) rule.IsActive = body.IsActive.Value;

        if (body.ServiceType is not null)
            rule.ServiceType = Enum.TryParse<ListingType>(body.ServiceType, ignoreCase: true, out var st)
                ? st : (ListingType?)null;

        if (body.PostingChannel is not null &&
            Enum.TryParse<PostingMode>(body.PostingChannel, ignoreCase: true, out var pc))
            rule.PostingChannel = pc;

        rule.UpdatedAt = DateTime.UtcNow;
        await Audit("routing_rule.updated", User.GetUserEmail(), rule.Name, null);
        await Db.SaveChangesAsync();

        return Ok(AdminMappers.MapRoutingRule(rule));
    }

    [HttpDelete("routing-rules/{id:guid}")]
    public async Task<IActionResult> DeleteRoutingRule(Guid id)
    {
        var rule = await Db.OrderRoutingRules.FindAsync(id);
        if (rule is null) return NotFound(Error("Routing rule not found"));

        Db.OrderRoutingRules.Remove(rule);
        await Audit("routing_rule.deleted", User.GetUserEmail(), rule.Name, null);
        await Db.SaveChangesAsync();

        return NoContent();
    }
}
