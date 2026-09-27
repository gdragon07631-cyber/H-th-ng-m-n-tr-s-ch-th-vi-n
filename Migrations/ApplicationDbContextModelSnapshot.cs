using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Project.Data;

#nullable disable

namespace Project.Migrations;

[DbContext(typeof(ApplicationDbContext))]
public sealed class ApplicationDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder
            .HasAnnotation("ProductVersion", "10.0.11")
            .HasAnnotation("Relational:MaxIdentifierLength", 128);

        SqlServerModelBuilderExtensions.UseIdentityColumns(modelBuilder);

        modelBuilder.Entity("Project.Models.AdminAccount", entity =>
        {
            entity.Property<int>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("int");

            SqlServerPropertyBuilderExtensions.UseIdentityColumn(entity.Property<int>("Id"));

            entity.Property<string>("Email")
                .IsRequired()
                .HasMaxLength(256)
                .HasColumnType("nvarchar(256)");

            entity.Property<bool>("IsActive")
                .ValueGeneratedOnAdd()
                .HasColumnType("bit")
                .HasDefaultValue(true);

            entity.Property<int>("FailedLoginAttempts")
                .ValueGeneratedOnAdd()
                .HasColumnType("int")
                .HasDefaultValue(0);

            entity.Property<DateTime?>("LockoutEndUtc")
                .HasColumnType("datetime2");

            entity.Property<DateTime?>("LockoutStartUtc")
                .HasColumnType("datetime2");

            entity.Property<string>("PasswordHash")
                .IsRequired()
                .HasMaxLength(512)
                .HasColumnType("nvarchar(512)");

            entity.HasKey("Id");
            entity.HasIndex("Email").IsUnique();
            entity.ToTable("AdminAccounts", table => table.HasCheckConstraint(
                "CK_AdminAccounts_FailedLoginAttempts_NonNegative", "[FailedLoginAttempts] >= 0"));
        });

        modelBuilder.Entity("Project.Models.LoginLog", entity =>
        {
            entity.Property<long>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("bigint");

            SqlServerPropertyBuilderExtensions.UseIdentityColumn(entity.Property<long>("Id"));

            entity.Property<DateTime>("AttemptedAtUtc").HasColumnType("datetime2");
            entity.Property<string>("Email").IsRequired().HasMaxLength(256).HasColumnType("nvarchar(256)");
            entity.Property<int?>("AdminAccountId").HasColumnType("int");
            entity.Property<string>("IpAddress").HasMaxLength(45).HasColumnType("nvarchar(45)");
            entity.Property<string>("Status").IsRequired().HasMaxLength(32).HasColumnType("nvarchar(32)");
            entity.HasKey("Id");
            entity.HasIndex("AdminAccountId");
            entity.HasIndex("AttemptedAtUtc");
            entity.HasIndex("Email");
            entity.ToTable("LoginLogs");
        });

        modelBuilder.Entity("Project.Models.RefreshToken", entity =>
        {
            entity.Property<long>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("bigint");
            SqlServerPropertyBuilderExtensions.UseIdentityColumn(entity.Property<long>("Id"));
            entity.Property<int>("AdminAccountId").HasColumnType("int");
            entity.Property<DateTime>("CreatedAtUtc").HasColumnType("datetime2");
            entity.Property<DateTime>("ExpiresAtUtc").HasColumnType("datetime2");
            entity.Property<DateTime?>("RevokedAtUtc").HasColumnType("datetime2");
            entity.Property<string>("ReplacedByTokenHash").HasMaxLength(64).HasColumnType("nvarchar(64)");
            entity.Property<string>("TokenHash").IsRequired().HasMaxLength(64).HasColumnType("nvarchar(64)");
            entity.HasKey("Id");
            entity.HasIndex("TokenHash").IsUnique();
            entity.HasIndex("AdminAccountId", "ExpiresAtUtc");
            entity.ToTable("RefreshTokens");
        });

        modelBuilder.Entity("Project.Models.RefreshToken", entity =>
        {
            entity.HasOne("Project.Models.AdminAccount", "AdminAccount")
                .WithMany()
                .HasForeignKey("AdminAccountId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            entity.Navigation("AdminAccount");
        });

        modelBuilder.Entity("Project.Models.LoginLog", entity =>
        {
            entity.HasOne("Project.Models.AdminAccount", "AdminAccount")
                .WithMany()
                .HasForeignKey("AdminAccountId")
                .OnDelete(DeleteBehavior.SetNull);
            entity.Navigation("AdminAccount");
        });

        modelBuilder.Entity("Project.Models.ReaderAccount", entity =>
        {
            entity.Property<int>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("int");

            SqlServerPropertyBuilderExtensions.UseIdentityColumn(entity.Property<int>("Id"));

            entity.Property<string>("FullName")
                .IsRequired()
                .HasMaxLength(100)
                .HasColumnType("nvarchar(100)");

            entity.Property<DateOnly>("DateOfBirth")
                .HasColumnType("date");

            entity.Property<string>("Email")
                .IsRequired()
                .HasMaxLength(256)
                .HasColumnType("nvarchar(256)");

            entity.Property<string>("PhoneNumber")
                .IsRequired()
                .HasMaxLength(20)
                .HasColumnType("nvarchar(20)");

            entity.Property<string>("StudentOrStaffCode")
                .IsRequired()
                .HasMaxLength(50)
                .HasColumnType("nvarchar(50)");

            entity.Property<string>("PasswordHash")
                .IsRequired()
                .HasMaxLength(512)
                .HasColumnType("nvarchar(512)");

            entity.Property<string>("Status")
                .IsRequired()
                .ValueGeneratedOnAdd()
                .HasMaxLength(50)
                .HasColumnType("nvarchar(50)")
                .HasDefaultValue("Chờ duyệt");

            entity.Property<DateTime>("CreatedAtUtc")
                .HasColumnType("datetime2");

            entity.HasKey("Id");

            entity.HasIndex("Email");

            entity.HasIndex("StudentOrStaffCode");

            entity.ToTable("ReaderAccounts");
        });

        modelBuilder.Entity("Project.Models.Author", entity =>
        {
            entity.Property<int>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("int");

            SqlServerPropertyBuilderExtensions.UseIdentityColumn(entity.Property<int>("Id"));

            entity.Property<string>("Name")
                .IsRequired()
                .HasMaxLength(150)
                .HasColumnType("nvarchar(150)");

            entity.Property<string>("Note")
                .HasMaxLength(500)
                .HasColumnType("nvarchar(500)");

            entity.Property<string>("Status")
                .IsRequired()
                .ValueGeneratedOnAdd()
                .HasMaxLength(50)
                .HasColumnType("nvarchar(50)")
                .HasDefaultValue("Hoạt động");

            entity.Property<DateTime>("CreatedAtUtc")
                .HasColumnType("datetime2");

            entity.HasKey("Id");

            entity.HasIndex("Name")
                .IsUnique();

            entity.ToTable("Authors");
        });
#pragma warning restore 612, 618
    }
}
