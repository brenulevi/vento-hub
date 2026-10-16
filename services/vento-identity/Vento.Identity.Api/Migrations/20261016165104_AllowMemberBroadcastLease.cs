using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vento.Identity.Api.Migrations
{
    /// <inheritdoc />
    public partial class AllowMemberBroadcastLease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPublishing",
                table: "RoomBroadcasts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "SessionId",
                table: "RoomBroadcasts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPublishing",
                table: "RoomBroadcasts");

            migrationBuilder.DropColumn(
                name: "SessionId",
                table: "RoomBroadcasts");
        }
    }
}
