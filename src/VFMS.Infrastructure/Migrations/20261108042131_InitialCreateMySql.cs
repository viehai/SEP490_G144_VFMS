using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VFMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreateMySql : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ApplicationRoles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    RoleName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationRoles", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CatalogCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CategoryName = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogCategories", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Exercises",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MuscleGroup = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DifficultyLevel = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ImageUrl = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    VideoUrl = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Exercises", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Gyms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    GymName = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Address = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PhoneNumber = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Email = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OpenTime = table.Column<TimeOnly>(type: "time(6)", nullable: true),
                    CloseTime = table.Column<TimeOnly>(type: "time(6)", nullable: true),
                    IsAcceptingMembers = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Gyms", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LockerPricingPolicies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    PolicyName = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RentalType = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RentalPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DepositAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PolicyStatus = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LockerPricingPolicies", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Packages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PackageStatus = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Packages", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SupplementProducts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ProductName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CatalogCategory = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ImageUrl = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ProductStatus = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplementProducts", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "YogaClassTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TypeName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YogaClassTypes", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ApplicationUsers",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GymId = table.Column<int>(type: "int", nullable: false),
                    FullName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Email = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PhoneNumber = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PasswordHash = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ResetTokenHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ResetTokenExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    AvatarUrl = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AccountStatus = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LastLoginAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    IsBanned = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Id = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationUsers", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_ApplicationUsers_Gyms_GymId",
                        column: x => x.GymId,
                        principalTable: "Gyms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Equipments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    GymId = table.Column<int>(type: "int", nullable: false),
                    CatalogCategoryId = table.Column<int>(type: "int", nullable: true),
                    EquipmentName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EquipmentCode = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EquipmentModel = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EquipmentBrand = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EquipmentStatus = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EquipmentDescriptionUrl = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PurchaseDate = table.Column<DateOnly>(type: "date", nullable: true),
                    PurchasePrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Note = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Equipments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Equipments_CatalogCategories_CatalogCategoryId",
                        column: x => x.CatalogCategoryId,
                        principalTable: "CatalogCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Equipments_Gyms_GymId",
                        column: x => x.GymId,
                        principalTable: "Gyms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LockerZones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    GymId = table.Column<int>(type: "int", nullable: false),
                    ZoneName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FloorNumber = table.Column<int>(type: "int", nullable: false),
                    GenderRestriction = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TotalLockers = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LockerZones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LockerZones_Gyms_GymId",
                        column: x => x.GymId,
                        principalTable: "Gyms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "YogaStudios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    GymId = table.Column<int>(type: "int", nullable: false),
                    StudioName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FloorNumber = table.Column<int>(type: "int", nullable: false),
                    Capacity = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YogaStudios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_YogaStudios_Gyms_GymId",
                        column: x => x.GymId,
                        principalTable: "Gyms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PackageComponents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    PackageId = table.Column<int>(type: "int", nullable: false),
                    ComponentType = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DurationDays = table.Column<int>(type: "int", nullable: true),
                    DurationMonths = table.Column<int>(type: "int", nullable: true),
                    SessionCount = table.Column<int>(type: "int", nullable: true),
                    YogaClassTypeId = table.Column<int>(type: "int", nullable: true),
                    AllowFreeze = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    MaxFreezeDaysPerRequest = table.Column<int>(type: "int", nullable: true),
                    MaxFreezeCount = table.Column<int>(type: "int", nullable: true),
                    AccessStartTime = table.Column<TimeOnly>(type: "time(6)", nullable: true),
                    AccessEndTime = table.Column<TimeOnly>(type: "time(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackageComponents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackageComponents_Packages_PackageId",
                        column: x => x.PackageId,
                        principalTable: "Packages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PackageComponents_YogaClassTypes_YogaClassTypeId",
                        column: x => x.YogaClassTypeId,
                        principalTable: "YogaClassTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ApplicationUserRoles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<string>(type: "varchar(128)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationUserRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApplicationUserRoles_ApplicationRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "ApplicationRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApplicationUserRoles_ApplicationUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ActorUserId = table.Column<string>(type: "varchar(128)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ActionType = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AuditEntity = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EntityId = table.Column<int>(type: "int", nullable: true),
                    OldValue = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NewValue = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OccurredAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IpAddress = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditLogs_ApplicationUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "BannedWordLists",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Word = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "varchar(128)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BannedWordLists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BannedWordLists_ApplicationUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "BusinessReportSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ReportName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    GeneratedByUserId = table.Column<string>(type: "varchar(128)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GeneratedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessReportSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BusinessReportSnapshots_ApplicationUsers_GeneratedByUserId",
                        column: x => x.GeneratedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Competitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CompetitionName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Rules = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RegistrationStart = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    RegistrationEnd = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    MaxParticipants = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ImageUrl = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedByUserId = table.Column<string>(type: "varchar(128)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Competitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Competitions_ApplicationUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "FaceData",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<string>(type: "varchar(128)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FaceNetEmbedding = table.Column<byte[]>(type: "LONGBLOB", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    RegisteredAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ConsentGrantedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FaceData", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FaceData_ApplicationUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "FreezePolicies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    PolicyName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EligibilityPaidDays = table.Column<int>(type: "int", nullable: false),
                    MinimumRequestDays = table.Column<int>(type: "int", nullable: false),
                    DefaultMaxFreezeDays = table.Column<int>(type: "int", nullable: false),
                    DefaultMaxFreezeCount = table.Column<int>(type: "int", nullable: false),
                    PolicyStatus = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsGlobal = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "varchar(128)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FreezePolicies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FreezePolicies_ApplicationUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "GymAnnouncements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    GymId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Content = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedByUserId = table.Column<string>(type: "varchar(128)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsPinned = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GymAnnouncements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GymAnnouncements_ApplicationUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GymAnnouncements_Gyms_GymId",
                        column: x => x.GymId,
                        principalTable: "Gyms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    RecipientUserId = table.Column<string>(type: "varchar(128)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SenderUserId = table.Column<string>(type: "varchar(128)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NotificationType = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Title = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Message = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsRead = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    NotifiedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    EntityType = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EntityId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_ApplicationUsers_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Notifications_ApplicationUsers_SenderUserId",
                        column: x => x.SenderUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    PurchaserUserId = table.Column<string>(type: "varchar(128)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OrderNumber = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Channel = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OrderStatus = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CompletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Orders_ApplicationUsers_PurchaserUserId",
                        column: x => x.PurchaserUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PhysicalProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    MemberUserId = table.Column<string>(type: "varchar(128)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    WeightKg = table.Column<decimal>(type: "decimal(5,2)", nullable: true),
                    HeightCm = table.Column<decimal>(type: "decimal(5,2)", nullable: true),
                    FitnessGoal = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FitnessLevel = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HealthConditions = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    WorkoutFrequencyPerWeek = table.Column<int>(type: "int", nullable: true),
                    EmergencyContactName = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EmergencyContactPhone = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Gender = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhysicalProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhysicalProfiles_ApplicationUsers_MemberUserId",
                        column: x => x.MemberUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PurchaseProposals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ProposedByUserId = table.Column<string>(type: "varchar(128)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ProposalNumber = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ProposalStatus = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SubmittedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseProposals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseProposals_ApplicationUsers_ProposedByUserId",
                        column: x => x.ProposedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "QRCodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<string>(type: "varchar(128)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    QRToken = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GeneratedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QRCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QRCodes_ApplicationUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<string>(type: "varchar(128)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Token = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ReplacedByToken = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RememberMe = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedByIp = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_ApplicationUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SupplementOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CustomerUserId = table.Column<string>(type: "varchar(128)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OrderNumber = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OrderStatus = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DeliveryType = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RecipientName = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DeliveryAddress = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DeliveryNote = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplementOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplementOrders_ApplicationUsers_CustomerUserId",
                        column: x => x.CustomerUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "TrainerApplications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ApplicantUserId = table.Column<string>(type: "varchar(128)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FullName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Email = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PhoneNumber = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CitizenId = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ApplicationFileUrl = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ApplicationStatus = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SubmittedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    SubmissionAttempt = table.Column<int>(type: "int", nullable: false),
                    ReviewedByUserId = table.Column<string>(type: "varchar(128)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReviewedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ReviewNote = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainerApplications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrainerApplications_ApplicationUsers_ApplicantUserId",
                        column: x => x.ApplicantUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrainerApplications_ApplicationUsers_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "TrainerProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    PTUserId = table.Column<string>(type: "varchar(128)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Bio = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AvatarUrl = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CertificateUrl = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Level = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CommissionTier = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainerProfiles", x => x.Id);
                    table.UniqueConstraint("AK_TrainerProfiles_PTUserId", x => x.PTUserId);
                    table.ForeignKey(
                        name: "FK_TrainerProfiles_ApplicationUsers_PTUserId",
                        column: x => x.PTUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Vouchers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Code = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DiscountType = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DiscountValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    StartAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    EndAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    MaxRedemptions = table.Column<int>(type: "int", nullable: true),
                    MaxRedemptionsPerUser = table.Column<int>(type: "int", nullable: true),
                    MinimumSpend = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    MaxDiscountAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    VoucherStatus = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedByUserId = table.Column<string>(type: "varchar(128)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vouchers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Vouchers_ApplicationUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EquipmentIssueReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    EquipmentId = table.Column<int>(type: "int", nullable: false),
                    ReportedByUserId = table.Column<string>(type: "varchar(128)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HandledByUserId = table.Column<string>(type: "varchar(128)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IncidentType = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ImageUrl = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IssueStatus = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReportedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ResolutionNote = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentIssueReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipmentIssueReports_ApplicationUsers_HandledByUserId",
                        column: x => x.HandledByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EquipmentIssueReports_ApplicationUsers_ReportedByUserId",
                        column: x => x.ReportedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EquipmentIssueReports_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Lockers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    LockerZoneId = table.Column<int>(type: "int", nullable: false),
                    LockerPricingPolicyId = table.Column<int>(type: "int", nullable: true),
                    LockerNumber = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LockerType = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LockerStatus = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AssignedPinCode = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    KeyOrPinType = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lockers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Lockers_LockerPricingPolicies_LockerPricingPolicyId",
                        column: x => x.LockerPricingPolicyId,
                        principalTable: "LockerPricingPolicies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Lockers_LockerZones_LockerZoneId",
                        column: x => x.LockerZoneId,
                        principalTable: "LockerZones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ReportMetrics",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    BusinessReportSnapshotId = table.Column<int>(type: "int", nullable: false),
                    MetricName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MetricType = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MetricValue = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    ValueType = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportMetrics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportMetrics_BusinessReportSnapshots_BusinessReportSnapshot~",
                        column: x => x.BusinessReportSnapshotId,
                        principalTable: "BusinessReportSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CompetitionJoins",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CompetitionId = table.Column<int>(type: "int", nullable: false),
                    MemberUserId = table.Column<string>(type: "varchar(128)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RegisteredAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Status = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Score = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    Rank = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConfirmedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ConfirmedByUserId = table.Column<string>(type: "varchar(128)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetitionJoins", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompetitionJoins_ApplicationUsers_ConfirmedByUserId",
                        column: x => x.ConfirmedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompetitionJoins_ApplicationUsers_MemberUserId",
                        column: x => x.MemberUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompetitionJoins_Competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "OrderItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    PackageId = table.Column<int>(type: "int", nullable: false),
                    BeneficiaryUserId = table.Column<string>(type: "varchar(128)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UnitPriceSnapshot = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PackageNameSnapshot = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ActivationStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderItems_ApplicationUsers_BeneficiaryUserId",
                        column: x => x.BeneficiaryUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderItems_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderItems_Packages_PackageId",
                        column: x => x.PackageId,
                        principalTable: "Packages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PaymentTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<string>(type: "varchar(128)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    GatewayName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GatewayTransactionReference = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PaymentMethod = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AmountDue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AmountReceived = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PaymentStatus = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PaidAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    RawGatewayStatus = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentTransactions_ApplicationUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentTransactions_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "WorkoutPlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    MemberUserId = table.Column<string>(type: "varchar(128)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PhysicalProfileId = table.Column<int>(type: "int", nullable: true),
                    Name = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PlanStatus = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsTemplate = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsFallback = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    FitnessGoal = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GeneratedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    GeneratedByAiModel = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkoutPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkoutPlans_ApplicationUsers_MemberUserId",
                        column: x => x.MemberUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkoutPlans_PhysicalProfiles_PhysicalProfileId",
                        column: x => x.PhysicalProfileId,
                        principalTable: "PhysicalProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PurchaseProposalItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    PurchaseProposalId = table.Column<int>(type: "int", nullable: false),
                    EquipmentId = table.Column<int>(type: "int", nullable: true),
                    ItemName = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    EstimatedUnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Reason = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseProposalItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseProposalItems_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseProposalItems_PurchaseProposals_PurchaseProposalId",
                        column: x => x.PurchaseProposalId,
                        principalTable: "PurchaseProposals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PurchaseProposalReviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    PurchaseProposalId = table.Column<int>(type: "int", nullable: false),
                    ReviewedByUserId = table.Column<string>(type: "varchar(128)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Action = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReviewNote = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReviewedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseProposalReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseProposalReviews_ApplicationUsers_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseProposalReviews_PurchaseProposals_PurchaseProposalId",
                        column: x => x.PurchaseProposalId,
                        principalTable: "PurchaseProposals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SupplementOrderDeliveries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SupplementOrderId = table.Column<int>(type: "int", nullable: false),
                    TrackingCode = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CourierName = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RecipientName = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DeliveryAddress = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DeliveredAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Note = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplementOrderDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplementOrderDeliveries_SupplementOrders_SupplementOrderId",
                        column: x => x.SupplementOrderId,
                        principalTable: "SupplementOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SupplementOrderItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SupplementOrderId = table.Column<int>(type: "int", nullable: false),
                    SupplementProductId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    UnitPriceAtPurchase = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplementOrderItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplementOrderItems_SupplementOrders_SupplementOrderId",
                        column: x => x.SupplementOrderId,
                        principalTable: "SupplementOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplementOrderItems_SupplementProducts_SupplementProductId",
                        column: x => x.SupplementProductId,
                        principalTable: "SupplementProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PTAvailabilities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    PTUserId = table.Column<string>(type: "varchar(128)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AvailableDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time(6)", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time(6)", nullable: false),
                    SlotStatus = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TrainerProfileId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PTAvailabilities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PTAvailabilities_ApplicationUsers_PTUserId",
                        column: x => x.PTUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PTAvailabilities_TrainerProfiles_TrainerProfileId",
                        column: x => x.TrainerProfileId,
                        principalTable: "TrainerProfiles",
                        principalColumn: "Id");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "TrainerReviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TrainerUserId = table.Column<string>(type: "varchar(128)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MemberUserId = table.Column<string>(type: "varchar(128)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Rating = table.Column<int>(type: "int", nullable: false),
                    Comment = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReviewStatus = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ManagerReply = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReviewedByUserId = table.Column<string>(type: "varchar(128)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReviewedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    IsVisible = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    TrainerProfileId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainerReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrainerReviews_ApplicationUsers_MemberUserId",
                        column: x => x.MemberUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrainerReviews_ApplicationUsers_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrainerReviews_ApplicationUsers_TrainerUserId",
                        column: x => x.TrainerUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrainerReviews_TrainerProfiles_TrainerProfileId",
                        column: x => x.TrainerProfileId,
                        principalTable: "TrainerProfiles",
                        principalColumn: "Id");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "YogaClasses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    YogaClassTypeId = table.Column<int>(type: "int", nullable: false),
                    ClassName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    InstructorUserId = table.Column<string>(type: "varchar(128)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MinParticipants = table.Column<int>(type: "int", nullable: false),
                    MaxParticipants = table.Column<int>(type: "int", nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    ClassStatus = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsTemplate = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ImageUrl = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YogaClasses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_YogaClasses_ApplicationUsers_InstructorUserId",
                        column: x => x.InstructorUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_YogaClasses_TrainerProfiles_InstructorUserId",
                        column: x => x.InstructorUserId,
                        principalTable: "TrainerProfiles",
                        principalColumn: "PTUserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_YogaClasses_YogaClassTypes_YogaClassTypeId",
                        column: x => x.YogaClassTypeId,
                        principalTable: "YogaClassTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CompetitionPrizes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CompetitionId = table.Column<int>(type: "int", nullable: false),
                    PrizeName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    VoucherId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetitionPrizes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompetitionPrizes_Competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompetitionPrizes_Vouchers_VoucherId",
                        column: x => x.VoucherId,
                        principalTable: "Vouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "VoucherRedemptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    VoucherId = table.Column<int>(type: "int", nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RedeemedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    RedemptionStatus = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoucherRedemptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VoucherRedemptions_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VoucherRedemptions_Vouchers_VoucherId",
                        column: x => x.VoucherId,
                        principalTable: "Vouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EquipmentSafetyConfirmations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    EquipmentId = table.Column<int>(type: "int", nullable: false),
                    EquipmentIssueReportId = table.Column<int>(type: "int", nullable: false),
                    ConfirmedByUserId = table.Column<string>(type: "varchar(128)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConfirmedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Note = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentSafetyConfirmations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipmentSafetyConfirmations_ApplicationUsers_ConfirmedByUse~",
                        column: x => x.ConfirmedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EquipmentSafetyConfirmations_EquipmentIssueReports_Equipment~",
                        column: x => x.EquipmentIssueReportId,
                        principalTable: "EquipmentIssueReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EquipmentSafetyConfirmations_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LockerIncidentLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    LockerId = table.Column<int>(type: "int", nullable: false),
                    MemberUserId = table.Column<string>(type: "varchar(128)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HandledByUserId = table.Column<string>(type: "varchar(128)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IncidentType = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReportedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    FineAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LockerIncidentLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LockerIncidentLogs_ApplicationUsers_HandledByUserId",
                        column: x => x.HandledByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LockerIncidentLogs_ApplicationUsers_MemberUserId",
                        column: x => x.MemberUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LockerIncidentLogs_Lockers_LockerId",
                        column: x => x.LockerId,
                        principalTable: "Lockers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LockerRentalContracts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    LockerId = table.Column<int>(type: "int", nullable: false),
                    LockerPricingPolicyId = table.Column<int>(type: "int", nullable: false),
                    CustomerUserId = table.Column<string>(type: "varchar(128)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ContractNumber = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DepositAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RentalPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ContractStatus = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CancelledAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Note = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FineAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LockerRentalContracts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LockerRentalContracts_ApplicationUsers_CustomerUserId",
                        column: x => x.CustomerUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LockerRentalContracts_LockerPricingPolicies_LockerPricingPol~",
                        column: x => x.LockerPricingPolicyId,
                        principalTable: "LockerPricingPolicies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LockerRentalContracts_Lockers_LockerId",
                        column: x => x.LockerId,
                        principalTable: "Lockers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "MemberMemberships",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    OrderItemId = table.Column<int>(type: "int", nullable: false),
                    PackageComponentId = table.Column<int>(type: "int", nullable: false),
                    MemberUserId = table.Column<string>(type: "varchar(128)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MembershipStatus = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RemainingSessionCount = table.Column<int>(type: "int", nullable: true),
                    TotalFrozenDays = table.Column<int>(type: "int", nullable: false),
                    FreezeCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemberMemberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MemberMemberships_ApplicationUsers_MemberUserId",
                        column: x => x.MemberUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MemberMemberships_OrderItems_OrderItemId",
                        column: x => x.OrderItemId,
                        principalTable: "OrderItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MemberMemberships_PackageComponents_PackageComponentId",
                        column: x => x.PackageComponentId,
                        principalTable: "PackageComponents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    PaymentTransactionId = table.Column<int>(type: "int", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IssuedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    InvoiceStatus = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Invoices_PaymentTransactions_PaymentTransactionId",
                        column: x => x.PaymentTransactionId,
                        principalTable: "PaymentTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PlanItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    PlanId = table.Column<int>(type: "int", nullable: false),
                    ExerciseId = table.Column<int>(type: "int", nullable: false),
                    DayNumber = table.Column<int>(type: "int", nullable: false),
                    OrderInDay = table.Column<int>(type: "int", nullable: false),
                    Sets = table.Column<int>(type: "int", nullable: true),
                    Reps = table.Column<int>(type: "int", nullable: true),
                    DurationMinutes = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanItems_Exercises_ExerciseId",
                        column: x => x.ExerciseId,
                        principalTable: "Exercises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlanItems_WorkoutPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "WorkoutPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "YogaClassSchedules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    YogaClassId = table.Column<int>(type: "int", nullable: false),
                    YogaStudioId = table.Column<int>(type: "int", nullable: false),
                    ScheduledDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time(6)", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time(6)", nullable: false),
                    ScheduleStatus = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RescheduledFromId = table.Column<int>(type: "int", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YogaClassSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_YogaClassSchedules_YogaClassSchedules_RescheduledFromId",
                        column: x => x.RescheduledFromId,
                        principalTable: "YogaClassSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_YogaClassSchedules_YogaClasses_YogaClassId",
                        column: x => x.YogaClassId,
                        principalTable: "YogaClasses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_YogaClassSchedules_YogaStudios_YogaStudioId",
                        column: x => x.YogaStudioId,
                        principalTable: "YogaStudios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CheckIns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    MemberUserId = table.Column<string>(type: "varchar(128)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MemberMembershipId = table.Column<int>(type: "int", nullable: true),
                    CheckinTime = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CheckoutTime = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CheckinMethod = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CheckoutMethod = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HandledByUserId = table.Column<string>(type: "varchar(128)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckIns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckIns_ApplicationUsers_HandledByUserId",
                        column: x => x.HandledByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CheckIns_ApplicationUsers_MemberUserId",
                        column: x => x.MemberUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CheckIns_MemberMemberships_MemberMembershipId",
                        column: x => x.MemberMembershipId,
                        principalTable: "MemberMemberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "MembershipFreezes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    MemberMembershipId = table.Column<int>(type: "int", nullable: false),
                    RequestedByUserId = table.Column<string>(type: "varchar(128)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RequestedStartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RequestedEndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ActualStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ActualEndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    RequestStatus = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReviewNote = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReviewedByUserId = table.Column<string>(type: "varchar(128)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReviewedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    EarlyUnfreezeAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MembershipFreezes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MembershipFreezes_ApplicationUsers_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MembershipFreezes_ApplicationUsers_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MembershipFreezes_MemberMemberships_MemberMembershipId",
                        column: x => x.MemberMembershipId,
                        principalTable: "MemberMemberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PTPurchases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    MemberMembershipId = table.Column<int>(type: "int", nullable: false),
                    MemberUserId = table.Column<string>(type: "varchar(128)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PTUserId = table.Column<string>(type: "varchar(128)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TotalSessions = table.Column<int>(type: "int", nullable: false),
                    RemainingSessions = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PurchasePrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PurchaseDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IsCompleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    TrainerProfileId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PTPurchases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PTPurchases_ApplicationUsers_MemberUserId",
                        column: x => x.MemberUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PTPurchases_ApplicationUsers_PTUserId",
                        column: x => x.PTUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PTPurchases_MemberMemberships_MemberMembershipId",
                        column: x => x.MemberMembershipId,
                        principalTable: "MemberMemberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PTPurchases_TrainerProfiles_TrainerProfileId",
                        column: x => x.TrainerProfileId,
                        principalTable: "TrainerProfiles",
                        principalColumn: "Id");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "YogaBookings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    YogaClassScheduleId = table.Column<int>(type: "int", nullable: false),
                    MemberUserId = table.Column<string>(type: "varchar(128)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BookedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    BookingStatus = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CancelledAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YogaBookings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_YogaBookings_ApplicationUsers_MemberUserId",
                        column: x => x.MemberUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_YogaBookings_YogaClassSchedules_YogaClassScheduleId",
                        column: x => x.YogaClassScheduleId,
                        principalTable: "YogaClassSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "YogaWaitlists",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    YogaClassScheduleId = table.Column<int>(type: "int", nullable: false),
                    YogaClassTypeId = table.Column<int>(type: "int", nullable: true),
                    MemberUserId = table.Column<string>(type: "varchar(128)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Position = table.Column<int>(type: "int", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    NotifiedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    WaitlistStatus = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YogaWaitlists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_YogaWaitlists_ApplicationUsers_MemberUserId",
                        column: x => x.MemberUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_YogaWaitlists_YogaClassSchedules_YogaClassScheduleId",
                        column: x => x.YogaClassScheduleId,
                        principalTable: "YogaClassSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_YogaWaitlists_YogaClassTypes_YogaClassTypeId",
                        column: x => x.YogaClassTypeId,
                        principalTable: "YogaClassTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PTFreezes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    PTPurchaseId = table.Column<int>(type: "int", nullable: false),
                    RequestedByUserId = table.Column<string>(type: "varchar(128)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RequestedStartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RequestedEndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ActualStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ActualEndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    RequestStatus = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReviewNote = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReviewedByUserId = table.Column<string>(type: "varchar(128)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReviewedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PTFreezes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PTFreezes_ApplicationUsers_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PTFreezes_ApplicationUsers_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PTFreezes_PTPurchases_PTPurchaseId",
                        column: x => x.PTPurchaseId,
                        principalTable: "PTPurchases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PTSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    PTPurchaseId = table.Column<int>(type: "int", nullable: false),
                    PTAvailabilityId = table.Column<int>(type: "int", nullable: false),
                    MemberUserId = table.Column<string>(type: "varchar(128)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PTUserId = table.Column<string>(type: "varchar(128)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SessionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time(6)", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time(6)", nullable: false),
                    SessionStatus = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Note = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CancelledAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PTSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PTSessions_ApplicationUsers_MemberUserId",
                        column: x => x.MemberUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PTSessions_ApplicationUsers_PTUserId",
                        column: x => x.PTUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PTSessions_PTAvailabilities_PTAvailabilityId",
                        column: x => x.PTAvailabilityId,
                        principalTable: "PTAvailabilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PTSessions_PTPurchases_PTPurchaseId",
                        column: x => x.PTPurchaseId,
                        principalTable: "PTPurchases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "YogaAttendances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    YogaClassScheduleId = table.Column<int>(type: "int", nullable: false),
                    YogaBookingId = table.Column<int>(type: "int", nullable: false),
                    MemberUserId = table.Column<string>(type: "varchar(128)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    InstructorUserId = table.Column<string>(type: "varchar(128)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AttendanceStatus = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RecordedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YogaAttendances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_YogaAttendances_ApplicationUsers_InstructorUserId",
                        column: x => x.InstructorUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_YogaAttendances_ApplicationUsers_MemberUserId",
                        column: x => x.MemberUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_YogaAttendances_YogaBookings_YogaBookingId",
                        column: x => x.YogaBookingId,
                        principalTable: "YogaBookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_YogaAttendances_YogaClassSchedules_YogaClassScheduleId",
                        column: x => x.YogaClassScheduleId,
                        principalTable: "YogaClassSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationRoles_RoleName",
                table: "ApplicationRoles",
                column: "RoleName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationUserRoles_RoleId",
                table: "ApplicationUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationUserRoles_UserId_RoleId",
                table: "ApplicationUserRoles",
                columns: new[] { "UserId", "RoleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationUsers_Email",
                table: "ApplicationUsers",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationUsers_GymId",
                table: "ApplicationUsers",
                column: "GymId");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationUsers_ResetTokenHash",
                table: "ApplicationUsers",
                column: "ResetTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_ActorUserId",
                table: "AuditLogs",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BannedWordLists_CreatedByUserId",
                table: "BannedWordLists",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BannedWordLists_Word",
                table: "BannedWordLists",
                column: "Word",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessReportSnapshots_GeneratedByUserId",
                table: "BusinessReportSnapshots",
                column: "GeneratedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CheckIns_HandledByUserId",
                table: "CheckIns",
                column: "HandledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CheckIns_MemberMembershipId",
                table: "CheckIns",
                column: "MemberMembershipId");

            migrationBuilder.CreateIndex(
                name: "IX_CheckIns_MemberUserId",
                table: "CheckIns",
                column: "MemberUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionJoins_CompetitionId_MemberUserId",
                table: "CompetitionJoins",
                columns: new[] { "CompetitionId", "MemberUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionJoins_ConfirmedByUserId",
                table: "CompetitionJoins",
                column: "ConfirmedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionJoins_MemberUserId",
                table: "CompetitionJoins",
                column: "MemberUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionPrizes_CompetitionId",
                table: "CompetitionPrizes",
                column: "CompetitionId");

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionPrizes_VoucherId",
                table: "CompetitionPrizes",
                column: "VoucherId");

            migrationBuilder.CreateIndex(
                name: "IX_Competitions_CreatedByUserId",
                table: "Competitions",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentIssueReports_EquipmentId",
                table: "EquipmentIssueReports",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentIssueReports_HandledByUserId",
                table: "EquipmentIssueReports",
                column: "HandledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentIssueReports_ReportedByUserId",
                table: "EquipmentIssueReports",
                column: "ReportedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Equipments_CatalogCategoryId",
                table: "Equipments",
                column: "CatalogCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Equipments_GymId",
                table: "Equipments",
                column: "GymId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentSafetyConfirmations_ConfirmedByUserId",
                table: "EquipmentSafetyConfirmations",
                column: "ConfirmedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentSafetyConfirmations_EquipmentId",
                table: "EquipmentSafetyConfirmations",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentSafetyConfirmations_EquipmentIssueReportId",
                table: "EquipmentSafetyConfirmations",
                column: "EquipmentIssueReportId");

            migrationBuilder.CreateIndex(
                name: "IX_FaceData_UserId",
                table: "FaceData",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FreezePolicies_CreatedByUserId",
                table: "FreezePolicies",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GymAnnouncements_CreatedByUserId",
                table: "GymAnnouncements",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GymAnnouncements_GymId",
                table: "GymAnnouncements",
                column: "GymId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_InvoiceNumber",
                table: "Invoices",
                column: "InvoiceNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_PaymentTransactionId",
                table: "Invoices",
                column: "PaymentTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LockerIncidentLogs_HandledByUserId",
                table: "LockerIncidentLogs",
                column: "HandledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LockerIncidentLogs_LockerId",
                table: "LockerIncidentLogs",
                column: "LockerId");

            migrationBuilder.CreateIndex(
                name: "IX_LockerIncidentLogs_MemberUserId",
                table: "LockerIncidentLogs",
                column: "MemberUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LockerRentalContracts_ContractNumber",
                table: "LockerRentalContracts",
                column: "ContractNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LockerRentalContracts_CustomerUserId",
                table: "LockerRentalContracts",
                column: "CustomerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LockerRentalContracts_LockerId",
                table: "LockerRentalContracts",
                column: "LockerId");

            migrationBuilder.CreateIndex(
                name: "IX_LockerRentalContracts_LockerPricingPolicyId",
                table: "LockerRentalContracts",
                column: "LockerPricingPolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_Lockers_LockerPricingPolicyId",
                table: "Lockers",
                column: "LockerPricingPolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_Lockers_LockerZoneId",
                table: "Lockers",
                column: "LockerZoneId");

            migrationBuilder.CreateIndex(
                name: "IX_LockerZones_GymId",
                table: "LockerZones",
                column: "GymId");

            migrationBuilder.CreateIndex(
                name: "IX_MemberMemberships_MemberUserId",
                table: "MemberMemberships",
                column: "MemberUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MemberMemberships_OrderItemId",
                table: "MemberMemberships",
                column: "OrderItemId");

            migrationBuilder.CreateIndex(
                name: "IX_MemberMemberships_PackageComponentId",
                table: "MemberMemberships",
                column: "PackageComponentId");

            migrationBuilder.CreateIndex(
                name: "IX_MembershipFreezes_MemberMembershipId",
                table: "MembershipFreezes",
                column: "MemberMembershipId");

            migrationBuilder.CreateIndex(
                name: "IX_MembershipFreezes_RequestedByUserId",
                table: "MembershipFreezes",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MembershipFreezes_ReviewedByUserId",
                table: "MembershipFreezes",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RecipientUserId",
                table: "Notifications",
                column: "RecipientUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_SenderUserId",
                table: "Notifications",
                column: "SenderUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_BeneficiaryUserId",
                table: "OrderItems",
                column: "BeneficiaryUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_OrderId",
                table: "OrderItems",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_PackageId",
                table: "OrderItems",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_OrderNumber",
                table: "Orders",
                column: "OrderNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_PurchaserUserId",
                table: "Orders",
                column: "PurchaserUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PackageComponents_PackageId",
                table: "PackageComponents",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_PackageComponents_YogaClassTypeId",
                table: "PackageComponents",
                column: "YogaClassTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_OrderId",
                table: "PaymentTransactions",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_UserId",
                table: "PaymentTransactions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PhysicalProfiles_MemberUserId",
                table: "PhysicalProfiles",
                column: "MemberUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanItems_ExerciseId",
                table: "PlanItems",
                column: "ExerciseId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanItems_PlanId",
                table: "PlanItems",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_PTAvailabilities_PTUserId",
                table: "PTAvailabilities",
                column: "PTUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PTAvailabilities_TrainerProfileId",
                table: "PTAvailabilities",
                column: "TrainerProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_PTFreezes_PTPurchaseId",
                table: "PTFreezes",
                column: "PTPurchaseId");

            migrationBuilder.CreateIndex(
                name: "IX_PTFreezes_RequestedByUserId",
                table: "PTFreezes",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PTFreezes_ReviewedByUserId",
                table: "PTFreezes",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PTPurchases_MemberMembershipId",
                table: "PTPurchases",
                column: "MemberMembershipId");

            migrationBuilder.CreateIndex(
                name: "IX_PTPurchases_MemberUserId",
                table: "PTPurchases",
                column: "MemberUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PTPurchases_PTUserId",
                table: "PTPurchases",
                column: "PTUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PTPurchases_TrainerProfileId",
                table: "PTPurchases",
                column: "TrainerProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_PTSessions_MemberUserId",
                table: "PTSessions",
                column: "MemberUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PTSessions_PTAvailabilityId",
                table: "PTSessions",
                column: "PTAvailabilityId");

            migrationBuilder.CreateIndex(
                name: "IX_PTSessions_PTPurchaseId",
                table: "PTSessions",
                column: "PTPurchaseId");

            migrationBuilder.CreateIndex(
                name: "IX_PTSessions_PTUserId",
                table: "PTSessions",
                column: "PTUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseProposalItems_EquipmentId",
                table: "PurchaseProposalItems",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseProposalItems_PurchaseProposalId",
                table: "PurchaseProposalItems",
                column: "PurchaseProposalId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseProposalReviews_PurchaseProposalId",
                table: "PurchaseProposalReviews",
                column: "PurchaseProposalId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseProposalReviews_ReviewedByUserId",
                table: "PurchaseProposalReviews",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseProposals_ProposalNumber",
                table: "PurchaseProposals",
                column: "ProposalNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseProposals_ProposedByUserId",
                table: "PurchaseProposals",
                column: "ProposedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_QRCodes_QRToken",
                table: "QRCodes",
                column: "QRToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QRCodes_UserId",
                table: "QRCodes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_Token",
                table: "RefreshTokens",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UserId",
                table: "RefreshTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportMetrics_BusinessReportSnapshotId",
                table: "ReportMetrics",
                column: "BusinessReportSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplementOrderDeliveries_SupplementOrderId",
                table: "SupplementOrderDeliveries",
                column: "SupplementOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplementOrderItems_SupplementOrderId",
                table: "SupplementOrderItems",
                column: "SupplementOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplementOrderItems_SupplementProductId",
                table: "SupplementOrderItems",
                column: "SupplementProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplementOrders_CustomerUserId",
                table: "SupplementOrders",
                column: "CustomerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplementOrders_OrderNumber",
                table: "SupplementOrders",
                column: "OrderNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrainerApplications_ApplicantUserId",
                table: "TrainerApplications",
                column: "ApplicantUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrainerApplications_ReviewedByUserId",
                table: "TrainerApplications",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainerProfiles_PTUserId",
                table: "TrainerProfiles",
                column: "PTUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrainerReviews_MemberUserId",
                table: "TrainerReviews",
                column: "MemberUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainerReviews_ReviewedByUserId",
                table: "TrainerReviews",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainerReviews_TrainerProfileId",
                table: "TrainerReviews",
                column: "TrainerProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainerReviews_TrainerUserId",
                table: "TrainerReviews",
                column: "TrainerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_VoucherRedemptions_OrderId",
                table: "VoucherRedemptions",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VoucherRedemptions_VoucherId",
                table: "VoucherRedemptions",
                column: "VoucherId");

            migrationBuilder.CreateIndex(
                name: "IX_Vouchers_Code",
                table: "Vouchers",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vouchers_CreatedByUserId",
                table: "Vouchers",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutPlans_MemberUserId",
                table: "WorkoutPlans",
                column: "MemberUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutPlans_PhysicalProfileId",
                table: "WorkoutPlans",
                column: "PhysicalProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_YogaAttendances_InstructorUserId",
                table: "YogaAttendances",
                column: "InstructorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_YogaAttendances_MemberUserId",
                table: "YogaAttendances",
                column: "MemberUserId");

            migrationBuilder.CreateIndex(
                name: "IX_YogaAttendances_YogaBookingId",
                table: "YogaAttendances",
                column: "YogaBookingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_YogaAttendances_YogaClassScheduleId",
                table: "YogaAttendances",
                column: "YogaClassScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_YogaBookings_MemberUserId",
                table: "YogaBookings",
                column: "MemberUserId");

            migrationBuilder.CreateIndex(
                name: "IX_YogaBookings_YogaClassScheduleId_MemberUserId",
                table: "YogaBookings",
                columns: new[] { "YogaClassScheduleId", "MemberUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_YogaClasses_InstructorUserId",
                table: "YogaClasses",
                column: "InstructorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_YogaClasses_YogaClassTypeId",
                table: "YogaClasses",
                column: "YogaClassTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_YogaClassSchedules_RescheduledFromId",
                table: "YogaClassSchedules",
                column: "RescheduledFromId");

            migrationBuilder.CreateIndex(
                name: "IX_YogaClassSchedules_YogaClassId",
                table: "YogaClassSchedules",
                column: "YogaClassId");

            migrationBuilder.CreateIndex(
                name: "IX_YogaClassSchedules_YogaStudioId",
                table: "YogaClassSchedules",
                column: "YogaStudioId");

            migrationBuilder.CreateIndex(
                name: "IX_YogaStudios_GymId_StudioName",
                table: "YogaStudios",
                columns: new[] { "GymId", "StudioName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_YogaWaitlists_MemberUserId",
                table: "YogaWaitlists",
                column: "MemberUserId");

            migrationBuilder.CreateIndex(
                name: "IX_YogaWaitlists_YogaClassScheduleId_MemberUserId",
                table: "YogaWaitlists",
                columns: new[] { "YogaClassScheduleId", "MemberUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_YogaWaitlists_YogaClassTypeId",
                table: "YogaWaitlists",
                column: "YogaClassTypeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApplicationUserRoles");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "BannedWordLists");

            migrationBuilder.DropTable(
                name: "CheckIns");

            migrationBuilder.DropTable(
                name: "CompetitionJoins");

            migrationBuilder.DropTable(
                name: "CompetitionPrizes");

            migrationBuilder.DropTable(
                name: "EquipmentSafetyConfirmations");

            migrationBuilder.DropTable(
                name: "FaceData");

            migrationBuilder.DropTable(
                name: "FreezePolicies");

            migrationBuilder.DropTable(
                name: "GymAnnouncements");

            migrationBuilder.DropTable(
                name: "Invoices");

            migrationBuilder.DropTable(
                name: "LockerIncidentLogs");

            migrationBuilder.DropTable(
                name: "LockerRentalContracts");

            migrationBuilder.DropTable(
                name: "MembershipFreezes");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "PlanItems");

            migrationBuilder.DropTable(
                name: "PTFreezes");

            migrationBuilder.DropTable(
                name: "PTSessions");

            migrationBuilder.DropTable(
                name: "PurchaseProposalItems");

            migrationBuilder.DropTable(
                name: "PurchaseProposalReviews");

            migrationBuilder.DropTable(
                name: "QRCodes");

            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropTable(
                name: "ReportMetrics");

            migrationBuilder.DropTable(
                name: "SupplementOrderDeliveries");

            migrationBuilder.DropTable(
                name: "SupplementOrderItems");

            migrationBuilder.DropTable(
                name: "TrainerApplications");

            migrationBuilder.DropTable(
                name: "TrainerReviews");

            migrationBuilder.DropTable(
                name: "VoucherRedemptions");

            migrationBuilder.DropTable(
                name: "YogaAttendances");

            migrationBuilder.DropTable(
                name: "YogaWaitlists");

            migrationBuilder.DropTable(
                name: "ApplicationRoles");

            migrationBuilder.DropTable(
                name: "Competitions");

            migrationBuilder.DropTable(
                name: "EquipmentIssueReports");

            migrationBuilder.DropTable(
                name: "PaymentTransactions");

            migrationBuilder.DropTable(
                name: "Lockers");

            migrationBuilder.DropTable(
                name: "Exercises");

            migrationBuilder.DropTable(
                name: "WorkoutPlans");

            migrationBuilder.DropTable(
                name: "PTAvailabilities");

            migrationBuilder.DropTable(
                name: "PTPurchases");

            migrationBuilder.DropTable(
                name: "PurchaseProposals");

            migrationBuilder.DropTable(
                name: "BusinessReportSnapshots");

            migrationBuilder.DropTable(
                name: "SupplementOrders");

            migrationBuilder.DropTable(
                name: "SupplementProducts");

            migrationBuilder.DropTable(
                name: "Vouchers");

            migrationBuilder.DropTable(
                name: "YogaBookings");

            migrationBuilder.DropTable(
                name: "Equipments");

            migrationBuilder.DropTable(
                name: "LockerPricingPolicies");

            migrationBuilder.DropTable(
                name: "LockerZones");

            migrationBuilder.DropTable(
                name: "PhysicalProfiles");

            migrationBuilder.DropTable(
                name: "MemberMemberships");

            migrationBuilder.DropTable(
                name: "YogaClassSchedules");

            migrationBuilder.DropTable(
                name: "CatalogCategories");

            migrationBuilder.DropTable(
                name: "OrderItems");

            migrationBuilder.DropTable(
                name: "PackageComponents");

            migrationBuilder.DropTable(
                name: "YogaClasses");

            migrationBuilder.DropTable(
                name: "YogaStudios");

            migrationBuilder.DropTable(
                name: "Orders");

            migrationBuilder.DropTable(
                name: "Packages");

            migrationBuilder.DropTable(
                name: "TrainerProfiles");

            migrationBuilder.DropTable(
                name: "YogaClassTypes");

            migrationBuilder.DropTable(
                name: "ApplicationUsers");

            migrationBuilder.DropTable(
                name: "Gyms");
        }
    }
}
