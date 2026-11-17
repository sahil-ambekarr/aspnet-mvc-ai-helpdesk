using Helpdesk.Web.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Web.Data;

public class HelpdeskDbContext : IdentityDbContext<ApplicationUser>
{
    public HelpdeskDbContext(DbContextOptions<HelpdeskDbContext> options) : base(options)
    {
    }

    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketComment> TicketComments => Set<TicketComment>();
    public DbSet<KbArticle> KbArticles => Set<KbArticle>();
    public DbSet<AiInteraction> AiInteractions => Set<AiInteraction>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Ticket>(t =>
        {
            t.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            t.Property(x => x.Priority).HasConversion<string>().HasMaxLength(20);
            t.Property(x => x.Category).HasConversion<string>().HasMaxLength(20);
            t.HasIndex(x => x.Status);
            t.HasIndex(x => x.RequesterId);
            t.HasIndex(x => x.AssigneeId);
            t.HasOne(x => x.Requester).WithMany().HasForeignKey(x => x.RequesterId).OnDelete(DeleteBehavior.Restrict);
            t.HasOne(x => x.Assignee).WithMany().HasForeignKey(x => x.AssigneeId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<TicketComment>(c =>
        {
            c.HasOne(x => x.Ticket).WithMany(t => t.Comments).HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);
            c.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<KbArticle>(a =>
        {
            a.Property(x => x.Category).HasConversion<string>().HasMaxLength(20);
            a.HasIndex(x => x.Slug).IsUnique();
        });

        builder.Entity<AiInteraction>(i =>
        {
            i.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            i.HasIndex(x => x.CreatedAt);
            i.HasIndex(x => x.TicketId);
        });
    }
}
