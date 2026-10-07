using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MultiWarehouseParcels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "multi_warehouse",
                schema: "shop",
                table: "shops",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "package_no",
                schema: "logistics",
                table: "shipments",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "package_no",
                schema: "sales",
                table: "return_requests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "shipping_discount_back",
                schema: "sales",
                table: "return_requests",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "shipping_refund",
                schema: "sales",
                table: "return_requests",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<List<string>>(
                name: "carrier_codes",
                schema: "catalog",
                table: "products",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<Guid>(
                name: "warehouse_id",
                schema: "catalog",
                table: "products",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "package_no",
                schema: "sales",
                table: "order_items",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "order_packages",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    no = table.Column<int>(type: "integer", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    weight_g = table.Column<int>(type: "integer", nullable: false),
                    shipping_fee = table.Column<long>(type: "bigint", nullable: false),
                    shipping_discount = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_packages", x => x.id);
                    table.CheckConstraint("ck_order_packages_amounts", "no >= 1 AND weight_g >= 0 AND shipping_fee >= 0 AND shipping_discount >= 0 AND shipping_discount <= shipping_fee");
                    table.ForeignKey(
                        name: "fk_order_packages_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "sales",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shop_shipping_channels",
                schema: "shop",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    carrier_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    cod_enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shop_shipping_channels", x => x.id);
                    table.ForeignKey(
                        name: "fk_shop_shipping_channels_shops_shop_id",
                        column: x => x.shop_id,
                        principalSchema: "shop",
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_order_items_package",
                schema: "sales",
                table: "order_items",
                sql: "package_no >= 1");

            migrationBuilder.CreateIndex(
                name: "ux_order_packages_no",
                schema: "sales",
                table: "order_packages",
                columns: new[] { "order_id", "no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_shop_shipping_channels",
                schema: "shop",
                table: "shop_shipping_channels",
                columns: new[] { "shop_id", "carrier_code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_packages",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "shop_shipping_channels",
                schema: "shop");

            migrationBuilder.DropCheckConstraint(
                name: "ck_order_items_package",
                schema: "sales",
                table: "order_items");

            migrationBuilder.DropColumn(
                name: "multi_warehouse",
                schema: "shop",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "package_no",
                schema: "logistics",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "package_no",
                schema: "sales",
                table: "return_requests");

            migrationBuilder.DropColumn(
                name: "shipping_discount_back",
                schema: "sales",
                table: "return_requests");

            migrationBuilder.DropColumn(
                name: "shipping_refund",
                schema: "sales",
                table: "return_requests");

            migrationBuilder.DropColumn(
                name: "carrier_codes",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropColumn(
                name: "warehouse_id",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropColumn(
                name: "package_no",
                schema: "sales",
                table: "order_items");
        }
    }
}
