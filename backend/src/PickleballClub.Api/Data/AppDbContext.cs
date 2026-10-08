using Microsoft.EntityFrameworkCore;
using PickleballClub.Api.Domain;

namespace PickleballClub.Api.Data;

/// <summary>
/// Schema is owned by SQL scripts in /db (not EF migrations) because it relies on
/// generated columns and an exclusion constraint. Keep this mapping in sync.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<ClubSettings> ClubSettings => Set<ClubSettings>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserToken> UserTokens => Set<UserToken>();
    public DbSet<Court> Courts => Set<Court>();
    public DbSet<OperatingHours> OperatingHours => Set<OperatingHours>();
    public DbSet<Holiday> Holidays => Set<Holiday>();
    public DbSet<PriceRule> PriceRules => Set<PriceRule>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<CourtBlock> CourtBlocks => Set<CourtBlock>();
    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<ClubSettings>(e =>
        {
            e.ToTable("club_settings");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
        });

        b.Entity<User>(e =>
        {
            e.ToTable("users");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        });

        b.Entity<UserToken>(e =>
        {
            e.ToTable("user_tokens");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        });

        b.Entity<Court>().ToTable("courts");

        b.Entity<OperatingHours>(e =>
        {
            e.ToTable("operating_hours");
            e.HasKey(x => x.DayOfWeek);
            e.Property(x => x.DayOfWeek).ValueGeneratedNever();
        });

        b.Entity<Holiday>(e =>
        {
            e.ToTable("holidays");
            e.HasKey(x => x.HolidayDate);
        });

        b.Entity<PriceRule>(e =>
        {
            e.ToTable("price_rules");
            e.Property(x => x.PricePerHour).HasPrecision(10, 2);
        });

        b.Entity<Booking>(e =>
        {
            e.ToTable("bookings");
            // "" is the CLR default: tell EF it means "not set" so the DB default generates the code
            // (otherwise the first booking is stored with code '' and the second violates the unique key).
            e.Property(x => x.Code).HasDefaultValueSql("('PB' || nextval('booking_code_seq'))").HasSentinel("").ValueGeneratedOnAdd();
            e.Property(x => x.Total).HasPrecision(10, 2);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
        });

        b.Entity<Payment>(e =>
        {
            e.ToTable("payments");
            e.Property(x => x.Amount).HasPrecision(10, 2);
            e.Property(x => x.RawWebhook).HasColumnType("jsonb");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        });

        b.Entity<CourtBlock>(e =>
        {
            e.ToTable("court_blocks");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        });
    }
}
