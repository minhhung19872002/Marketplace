using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopHub.Domain.Iam;
using ShopHub.Domain.SystemConfig;

namespace ShopHub.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users", "iam");
        b.HasKey(u => u.Id);
        b.Property(u => u.Phone).HasMaxLength(15);
        b.Property(u => u.Email).HasMaxLength(254);
        b.Property(u => u.Username).HasMaxLength(30);
        b.Property(u => u.PasswordHash).HasMaxLength(100).IsRequired();
        b.Property(u => u.FullName).HasMaxLength(100).IsRequired();
        b.Property(u => u.AvatarUrl).HasMaxLength(500);
        b.Property(u => u.Gender).HasConversion<string>().HasMaxLength(10);
        b.Property(u => u.Status).HasConversion<string>().HasMaxLength(10);
        b.Property(u => u.LockReason).HasMaxLength(500);
        b.Property(u => u.Version).IsRowVersion();

        // Uniqueness only among live rows (soft-deleted / anonymised accounts free their identifiers)
        b.HasIndex(u => u.Phone).IsUnique().HasFilter("deleted_at IS NULL AND phone IS NOT NULL").HasDatabaseName("ux_users_phone");
        b.HasIndex(u => u.Email).IsUnique().HasFilter("deleted_at IS NULL AND email IS NOT NULL").HasDatabaseName("ux_users_email");
        b.HasIndex(u => u.Username).IsUnique().HasFilter("deleted_at IS NULL AND username IS NOT NULL").HasDatabaseName("ux_users_username");
        b.HasIndex(u => new { u.CreatedAt, u.Id }).HasDatabaseName("ix_users_created");
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("refresh_tokens", "iam");
        b.HasKey(t => t.Id);
        b.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
        b.Property(t => t.Device).HasMaxLength(200);
        b.Property(t => t.Ip).HasMaxLength(64);
        b.Property(t => t.RevokeReason).HasMaxLength(30);
        b.HasIndex(t => t.TokenHash).IsUnique().HasDatabaseName("ux_refresh_tokens_hash");
        b.HasIndex(t => new { t.UserId, t.FamilyId }).HasDatabaseName("ix_refresh_tokens_user_family");
        b.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class OtpCodeConfiguration : IEntityTypeConfiguration<OtpCode>
{
    public void Configure(EntityTypeBuilder<OtpCode> b)
    {
        b.ToTable("otp_codes", "iam");
        b.HasKey(o => o.Id);
        b.Property(o => o.Target).HasMaxLength(254).IsRequired();
        b.Property(o => o.Purpose).HasConversion<string>().HasMaxLength(20);
        b.Property(o => o.CodeHash).HasMaxLength(64).IsRequired();
        b.Property(o => o.VerificationTokenHash).HasMaxLength(64);
        b.Property(o => o.Ip).HasMaxLength(64);
        b.HasIndex(o => new { o.Target, o.CreatedAt }).HasDatabaseName("ix_otp_codes_target_created");
        b.ToTable(t => t.HasCheckConstraint("ck_otp_codes_attempts", "attempts >= 0"));
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.ToTable("roles", "iam");
        b.HasKey(r => r.Id);
        b.Property(r => r.Code).HasMaxLength(50).IsRequired();
        b.Property(r => r.Name).HasMaxLength(100).IsRequired();
        b.Property(r => r.Description).HasMaxLength(500);
        b.HasIndex(r => r.Code).IsUnique().HasFilter("deleted_at IS NULL").HasDatabaseName("ux_roles_code");
        b.HasMany(r => r.Permissions).WithOne().HasForeignKey(p => p.RoleId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(r => r.Permissions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> b)
    {
        b.ToTable("role_permissions", "iam");
        b.HasKey(rp => new { rp.RoleId, rp.PermissionCode });
        b.Property(rp => rp.PermissionCode).HasMaxLength(100);
    }
}

internal sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> b)
    {
        b.ToTable("permissions", "iam");
        b.HasKey(p => p.Code);
        b.Property(p => p.Code).HasMaxLength(100);
        b.Property(p => p.Module).HasMaxLength(50).IsRequired();
        b.Property(p => p.Name).HasMaxLength(200).IsRequired();
    }
}

internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> b)
    {
        b.ToTable("user_roles", "iam");
        b.HasKey(ur => new { ur.UserId, ur.RoleId });
        b.HasOne<User>().WithMany().HasForeignKey(ur => ur.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Role>().WithMany().HasForeignKey(ur => ur.RoleId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AddressConfiguration : IEntityTypeConfiguration<Address>
{
    public void Configure(EntityTypeBuilder<Address> b)
    {
        b.ToTable("addresses", "iam");
        b.HasKey(a => a.Id);
        b.Property(a => a.ReceiverName).HasMaxLength(100).IsRequired();
        b.Property(a => a.Phone).HasMaxLength(15).IsRequired();
        b.Property(a => a.ProvinceCode).HasMaxLength(10).IsRequired();
        b.Property(a => a.DistrictCode).HasMaxLength(10).IsRequired();
        b.Property(a => a.WardCode).HasMaxLength(10).IsRequired();
        b.Property(a => a.Street).HasMaxLength(255).IsRequired();
        b.Property(a => a.Type).HasConversion<string>().HasMaxLength(10);
        b.HasIndex(a => a.UserId).HasDatabaseName("ix_addresses_user");
        // At most one default address per user, guaranteed by the database
        b.HasIndex(a => a.UserId).IsUnique().HasFilter("is_default AND deleted_at IS NULL").HasDatabaseName("ux_addresses_default");
        b.HasOne<User>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AdminDivision>().WithMany().HasForeignKey(a => a.ProvinceCode).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AdminDivision>().WithMany().HasForeignKey(a => a.DistrictCode).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AdminDivision>().WithMany().HasForeignKey(a => a.WardCode).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AdminDivisionConfiguration : IEntityTypeConfiguration<AdminDivision>
{
    public void Configure(EntityTypeBuilder<AdminDivision> b)
    {
        b.ToTable("admin_divisions", "iam");
        b.HasKey(d => d.Code);
        b.Property(d => d.Code).HasMaxLength(10);
        b.Property(d => d.Name).HasMaxLength(100).IsRequired();
        b.Property(d => d.Level).HasConversion<short>();
        b.Property(d => d.ParentCode).HasMaxLength(10);
        b.HasIndex(d => d.ParentCode).HasDatabaseName("ix_admin_divisions_parent");
        b.HasOne<AdminDivision>().WithMany().HasForeignKey(d => d.ParentCode).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SimulatedSmsConfiguration : IEntityTypeConfiguration<SimulatedSms>
{
    public void Configure(EntityTypeBuilder<SimulatedSms> b)
    {
        b.ToTable("simulated_sms", "sys");
        b.HasKey(s => s.Id);
        b.Property(s => s.To).HasMaxLength(20).IsRequired();
        b.Property(s => s.Content).HasMaxLength(1000).IsRequired();
        b.HasIndex(s => new { s.To, s.CreatedAt }).HasDatabaseName("ix_simulated_sms_to");
    }
}

internal sealed class UserIdentityConfiguration : IEntityTypeConfiguration<UserIdentity>
{
    public void Configure(EntityTypeBuilder<UserIdentity> b)
    {
        b.ToTable("user_identities", "iam");
        b.HasKey(i => i.Id);
        b.Property(i => i.Provider).HasConversion<string>().HasMaxLength(20);
        b.Property(i => i.ProviderKey).HasMaxLength(255).IsRequired();
        b.Property(i => i.Email).HasMaxLength(255);
        b.HasOne<User>().WithMany().HasForeignKey(i => i.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(i => new { i.Provider, i.ProviderKey }).IsUnique().HasDatabaseName("ux_user_identities_provider_key");
        b.HasIndex(i => i.UserId).HasDatabaseName("ix_user_identities_user");
    }
}
