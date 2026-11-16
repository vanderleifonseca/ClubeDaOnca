using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClubeDaOnca.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Ocorrencias : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence<int>(
                name: "numero_ocorrencia");

            migrationBuilder.CreateTable(
                name: "Ocorrencias",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssociacaoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Numero = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "nextval('numero_ocorrencia')"),
                    Titulo = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Categoria = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Local = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Descricao = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RegistradaPorId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    RegistradaPorNome = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    RegistradaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResponsavelId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ResponsavelNome = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    AtualizadaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvidaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ocorrencias", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Ocorrencias_Associacoes_AssociacaoId",
                        column: x => x.AssociacaoId,
                        principalTable: "Associacoes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FotosOcorrencia",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OcorrenciaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Conteudo = table.Column<byte[]>(type: "bytea", nullable: false),
                    TipoConteudo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Tamanho = table.Column<int>(type: "integer", nullable: false),
                    EnviadaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FotosOcorrencia", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FotosOcorrencia_Ocorrencias_OcorrenciaId",
                        column: x => x.OcorrenciaId,
                        principalTable: "Ocorrencias",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MovimentosOcorrencia",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OcorrenciaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AutorId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    AutorNome = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Texto = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    StatusAnterior = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    StatusNovo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ParaId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ParaNome = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovimentosOcorrencia", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MovimentosOcorrencia_Ocorrencias_OcorrenciaId",
                        column: x => x.OcorrenciaId,
                        principalTable: "Ocorrencias",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FotosOcorrencia_OcorrenciaId",
                table: "FotosOcorrencia",
                column: "OcorrenciaId");

            migrationBuilder.CreateIndex(
                name: "IX_MovimentosOcorrencia_OcorrenciaId_Em",
                table: "MovimentosOcorrencia",
                columns: new[] { "OcorrenciaId", "Em" });

            migrationBuilder.CreateIndex(
                name: "IX_Ocorrencias_AssociacaoId",
                table: "Ocorrencias",
                column: "AssociacaoId");

            migrationBuilder.CreateIndex(
                name: "IX_Ocorrencias_AssociacaoId_Status",
                table: "Ocorrencias",
                columns: new[] { "AssociacaoId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Ocorrencias_RegistradaPorId",
                table: "Ocorrencias",
                column: "RegistradaPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Ocorrencias_ResponsavelId_Status",
                table: "Ocorrencias",
                columns: new[] { "ResponsavelId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FotosOcorrencia");

            migrationBuilder.DropTable(
                name: "MovimentosOcorrencia");

            migrationBuilder.DropTable(
                name: "Ocorrencias");

            migrationBuilder.DropSequence(
                name: "numero_ocorrencia");
        }
    }
}
