using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sitim.Api.Security;
using Sitim.Core.Entities;
using Sitim.Infrastructure.Data;

namespace Sitim.Api.Controllers
{
    /// <summary>
    /// Platform-level management of institutions (tenants).
    /// Only accessible by SuperAdmin users.
    /// </summary>
    [Authorize(Roles = SitimRoles.PlatformAdmin)]
    [ApiController]
    [Route("api/[controller]")]
    public sealed class InstitutionsController : ControllerBase
    {
        private readonly AppDbContext _db;

        public InstitutionsController(AppDbContext db)
        {
            _db = db;
        }

        public sealed record InstitutionDto(
            Guid Id,
            string Name,
            string Slug,
            string OrthancLabel,
            bool IsActive,
            DateTime CreatedAtUtc);

        public sealed record CreateInstitutionRequest(
            string Name,
            string Slug,
            string OrthancLabel);

        public sealed record UpdateInstitutionRequest(
            string Name,
            bool IsActive);

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<InstitutionDto>>> List(CancellationToken ct)
        {
            var items = await _db.Institutions
                .AsNoTracking()
                .OrderBy(i => i.Name)
                .ToListAsync(ct);

            return Ok(items.Select(ToDto).ToList());
        }

        [HttpGet("{id:guid}")]
        public async Task<ActionResult<InstitutionDto>> GetById(Guid id, CancellationToken ct)
        {
            var inst = await _db.Institutions.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);
            return inst is null ? NotFound() : Ok(ToDto(inst));
        }

        [HttpPost]
        public async Task<ActionResult<InstitutionDto>> Create([FromBody] CreateInstitutionRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest("Name is required.");
            if (string.IsNullOrWhiteSpace(req.Slug)) return BadRequest("Slug is required.");
            if (string.IsNullOrWhiteSpace(req.OrthancLabel)) return BadRequest("OrthancLabel is required.");

            if (await _db.Institutions.AnyAsync(i => i.Slug == req.Slug, ct))
                return Conflict($"Slug '{req.Slug}' is already taken.");
            if (await _db.Institutions.AnyAsync(i => i.OrthancLabel == req.OrthancLabel, ct))
                return Conflict($"OrthancLabel '{req.OrthancLabel}' is already taken.");

            var inst = new Institution
            {
                Id = Guid.NewGuid(),
                Name = req.Name.Trim(),
                Slug = req.Slug.Trim().ToLowerInvariant(),
                OrthancLabel = req.OrthancLabel.Trim().ToLowerInvariant(),
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            };

            _db.Institutions.Add(inst);
            await _db.SaveChangesAsync(ct);

            return CreatedAtAction(nameof(GetById), new { id = inst.Id }, ToDto(inst));
        }

        [HttpPut("{id:guid}")]
        public async Task<ActionResult<InstitutionDto>> Update(Guid id, [FromBody] UpdateInstitutionRequest req, CancellationToken ct)
        {
            var inst = await _db.Institutions.FirstOrDefaultAsync(i => i.Id == id, ct);
            if (inst is null) return NotFound();

            inst.Name = req.Name.Trim();
            inst.IsActive = req.IsActive;

            await _db.SaveChangesAsync(ct);
            return Ok(ToDto(inst));
        }

        private static InstitutionDto ToDto(Institution i) =>
            new(i.Id, i.Name, i.Slug, i.OrthancLabel, i.IsActive, i.CreatedAtUtc);
    }
}
