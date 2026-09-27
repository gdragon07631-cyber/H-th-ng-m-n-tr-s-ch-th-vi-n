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

            entity.Property<string>("RejectionReason")
                .HasMaxLength(1000)
                .HasColumnType("nvarchar(1000)");

            entity.Property<string>("Status")
                .IsRequired()
                .ValueGeneratedOnAdd()
                .HasMaxLength(50)
                .HasColumnType("nvarchar(50)")
                .HasDefaultValue("Chờ duyệt");

            entity.Property<DateTime>("CreatedAtUtc")
                .HasColumnType("datetime2");

            entity.Property<DateTime>("UpdatedAtUtc")
                .HasColumnType("datetime2");

            entity.HasKey("Id");

            entity.HasIndex("Email");

            entity.HasIndex("StudentOrStaffCode");

            entity.ToTable("ReaderAccounts");
        });

        modelBuilder.Entity("Project.Models.LibraryCardType", entity =>
        {
            entity.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("int");
            SqlServerPropertyBuilderExtensions.UseIdentityColumn(entity.Property<int>("Id"));
            entity.Property<bool>("IsActive").ValueGeneratedOnAdd().HasColumnType("bit").HasDefaultValue(true);
            entity.Property<string>("Name").IsRequired().HasMaxLength(100).HasColumnType("nvarchar(100)");
            entity.HasKey("Id");
            entity.HasIndex("Name").IsUnique();
            entity.ToTable("LibraryCardTypes");
        });

        modelBuilder.Entity("Project.Models.LibraryCard", entity =>
        {
            entity.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("int");
            SqlServerPropertyBuilderExtensions.UseIdentityColumn(entity.Property<int>("Id"));
            entity.Property<string>("CardCode").IsRequired().HasMaxLength(32).HasColumnType("nvarchar(32)");
            entity.Property<DateOnly>("ExpiresOn").HasColumnType("date");
            entity.Property<DateOnly>("IssuedOn").HasColumnType("date");
            entity.Property<int>("LibraryCardTypeId").HasColumnType("int");
            entity.Property<int>("ReaderAccountId").HasColumnType("int");
            entity.Property<string>("Status").IsRequired().ValueGeneratedOnAdd().HasMaxLength(50).HasColumnType("nvarchar(50)").HasDefaultValue("Đang hoạt động");
            entity.HasKey("Id");
            entity.HasIndex("CardCode").IsUnique();
            entity.HasIndex("LibraryCardTypeId");
            entity.HasIndex("ReaderAccountId").IsUnique();
            entity.ToTable("LibraryCards");
        });

        modelBuilder.Entity("Project.Models.BookHold", entity =>
        {
            entity.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("bigint");
            SqlServerPropertyBuilderExtensions.UseIdentityColumn(entity.Property<long>("Id"));
            entity.Property<int>("BookId").HasColumnType("int");
            entity.Property<DateTime>("HeldAtUtc").HasColumnType("datetime2");
            entity.Property<int>("ReaderAccountId").HasColumnType("int");
            entity.HasKey("Id");
            entity.HasIndex("BookId");
            entity.HasIndex("ReaderAccountId", "BookId").IsUnique();
            entity.ToTable("BookHolds");
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

        modelBuilder.Entity("Project.Models.Book", entity =>
        {
            entity.Property<int>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("int");

            SqlServerPropertyBuilderExtensions.UseIdentityColumn(entity.Property<int>("Id"));

            entity.Property<string>("Title")
                .IsRequired()
                .HasMaxLength(250)
                .HasColumnType("nvarchar(250)");

            entity.Property<string>("Isbn")
                .HasMaxLength(50)
                .HasColumnType("nvarchar(50)");

            entity.Property<int>("AuthorId")
                .HasColumnType("int");

            entity.Property<int?>("CategoryId")
                .HasColumnType("int");

            entity.Property<string>("Description")
                .HasMaxLength(500)
                .HasColumnType("nvarchar(500)");

            entity.Property<DateTime>("CreatedAtUtc")
                .HasColumnType("datetime2");

            entity.HasKey("Id");

            entity.HasIndex("AuthorId");

            entity.HasIndex("CategoryId");

            entity.ToTable("Books");

            entity.HasOne("Project.Models.Author", "Author")
                .WithMany("Books")
                .HasForeignKey("AuthorId")
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();

            entity.Navigation("Author");
            entity.HasOne("Project.Models.Category", "Category")
                .WithMany("Books")
                .HasForeignKey("CategoryId")
                .OnDelete(DeleteBehavior.Restrict);
            entity.Navigation("Category");
        });

        modelBuilder.Entity("Project.Models.Category", entity =>
        {
            entity.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("int");
            SqlServerPropertyBuilderExtensions.UseIdentityColumn(entity.Property<int>("Id"));
            entity.Property<string>("Name").IsRequired().HasMaxLength(150).HasColumnType("nvarchar(150)");
            entity.Property<string>("Status").IsRequired().ValueGeneratedOnAdd().HasMaxLength(50).HasColumnType("nvarchar(50)").HasDefaultValue("Hoạt động");
            entity.Property<DateTime>("CreatedAtUtc").HasColumnType("datetime2");
            entity.Property<int?>("ParentId").HasColumnType("int");
            entity.HasKey("Id");
            entity.HasIndex("Name").IsUnique();
            entity.HasIndex("ParentId");
            entity.ToTable("Categories");
        });

        modelBuilder.Entity("Project.Models.Category", entity =>
        {
            entity.HasMany("Project.Models.Book", "Books").WithOne("Category").HasForeignKey("CategoryId");
            entity.HasMany("Project.Models.Category", "Children").WithOne("Parent").HasForeignKey("ParentId");
            entity.HasOne("Project.Models.Category", "Parent").WithMany("Children").HasForeignKey("ParentId").OnDelete(DeleteBehavior.Restrict);
            entity.Navigation("Books");
            entity.Navigation("Children");
            entity.Navigation("Parent");
        });

        modelBuilder.Entity("Project.Models.LibraryCard", entity =>
        {
            entity.HasOne("Project.Models.LibraryCardType", "LibraryCardType").WithMany("LibraryCards").HasForeignKey("LibraryCardTypeId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            entity.HasOne("Project.Models.ReaderAccount", "ReaderAccount").WithOne("LibraryCard").HasForeignKey("Project.Models.LibraryCard", "ReaderAccountId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            entity.Navigation("LibraryCardType");
            entity.Navigation("ReaderAccount");
        });

        modelBuilder.Entity("Project.Models.BookHold", entity =>
        {
            entity.HasOne("Project.Models.Book", "Book").WithMany().HasForeignKey("BookId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            entity.HasOne("Project.Models.ReaderAccount", "ReaderAccount").WithMany("BookHolds").HasForeignKey("ReaderAccountId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            entity.Navigation("Book");
            entity.Navigation("ReaderAccount");
        });
#pragma warning restore 612, 618
    }
}
