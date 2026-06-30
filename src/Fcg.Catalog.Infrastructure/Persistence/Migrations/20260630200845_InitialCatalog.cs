using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fcg.Catalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "itens_biblioteca",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    jogo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    adicionado_em = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_itens_biblioteca", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "jogos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    titulo = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    descricao = table.Column<string>(type: "text", nullable: true),
                    preco = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    desenvolvedora = table.Column<string>(type: "text", nullable: true),
                    data_lancamento = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    atualizado_em = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_jogos", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "pedidos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    jogo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    valor = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    motivo_recusa = table.Column<string>(type: "text", nullable: true),
                    criado_em = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pedidos", x => x.id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "ux_itens_biblioteca_usuario_jogo",
                table: "itens_biblioteca",
                columns: new[] { "usuario_id", "jogo_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "ux_pedidos_usuario_jogo_pendente",
                table: "pedidos",
                columns: new[] { "usuario_id", "jogo_id" },
                unique: true,
                filter: "status = 0"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "itens_biblioteca");

            migrationBuilder.DropTable(name: "jogos");

            migrationBuilder.DropTable(name: "pedidos");
        }
    }
}
